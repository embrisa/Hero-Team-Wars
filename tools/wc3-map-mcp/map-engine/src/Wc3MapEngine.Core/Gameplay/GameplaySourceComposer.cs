using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Wc3MapEngine.Contracts;
using Wc3MapEngine.Core.Scripts;

namespace Wc3MapEngine.Core.Gameplay;

/// <summary>
/// Composes the complete MCP-owned JASS entry point from deterministic module,
/// trigger, and variable source manifests. The same canonical model is used
/// by typed transaction operations through <see cref="ComposeCanonical"/>.
/// </summary>
public static class GameplaySourceComposer
{
    public const string ComposerVersion = "mcp-jass-composer-2.0";
    private const int MaxManifestBytes = 2 * 1024 * 1024;
    private const int MaxModuleBytes = 2 * 1024 * 1024;
    private static readonly Regex Function = new("(?im)^\\s*function\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s+takes\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static JsonObject Compose(string manifestPath, string? requestedProfile = null)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath)) throw new EngineException("FILE_NOT_FOUND", $"Gameplay manifest does not exist: {fullManifestPath}");
        var manifestBytes = File.ReadAllBytes(fullManifestPath);
        if (manifestBytes.Length > MaxManifestBytes) throw new EngineException("INVALID_ARGUMENT", "Gameplay manifest exceeds the 2 MiB safety limit.");
        return ComposeManifest(ParseObject(manifestBytes, $"Gameplay manifest '{fullManifestPath}'"), fullManifestPath, manifestBytes, requestedProfile);
    }

    /// <summary>Compose an entry point from the transaction's source-owned model.</summary>
    public static JsonObject ComposeCanonical(JsonObject canonical, string? requestedProfile = null)
    {
        var profile = requestedProfile ?? canonical["profile"]?.GetValue<string>() ?? HtwProfileModel.MvpProfile;
        var manifest = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["profile"] = profile,
            ["modules"] = (canonical["gameplay_modules"] as JsonArray)?.DeepClone() ?? new JsonArray(),
            ["triggers"] = (canonical["gameplay_triggers"] as JsonArray)?.DeepClone() ?? new JsonArray(),
            ["variables"] = (canonical["gameplay_variables"] as JsonArray)?.DeepClone() ?? new JsonArray(),
            ["regions"] = (canonical["regions"] as JsonArray)?.DeepClone() ?? new JsonArray(),
            ["region_roles"] = (canonical["region_roles"] as JsonArray)?.DeepClone() ?? new JsonArray(),
            ["profiles"] = (canonical["profiles"] as JsonObject)?.DeepClone() ?? HtwProfileModel.ProfilesDocument(),
            ["teams"] = (canonical["teams"] as JsonArray)?.DeepClone() ?? HtwProfileModel.DefaultTeams(profile)
        };
        var bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString(EngineProtocol.JsonOptions));
        return ComposeManifest(manifest, "<canonical-gameplay-model>", bytes, requestedProfile);
    }

    private static JsonObject ComposeManifest(JsonObject manifest, string manifestPath, byte[] manifestBytes, string? requestedProfile)
    {
        var profile = requestedProfile ?? StringValue(manifest, "profile") ?? "mvp_2arena";
        if (profile is not ("mvp_2arena" or "full_6team" or "gui_compatible")) throw new EngineException("INVALID_ARGUMENT", $"Unsupported gameplay profile '{profile}'.");
        if (profile == "gui_compatible") throw new EngineException("CAPABILITY_GATED", "GUI-compatible trigger composition is gated pending exact WTG/WCT/WTS fixtures and World Editor evidence.");
        if (StringValue(manifest, "schema_version") is { } schema && schema != "1.0") throw new EngineException("INVALID_ARGUMENT", $"Unsupported gameplay manifest schema '{schema}'.");
        if (StringValue(manifest, "native_catalogue_version") is { } nativeCatalogue && nativeCatalogue != JassNativeCatalogue.Version) throw new EngineException("INVALID_ARGUMENT", $"Gameplay manifest native catalogue '{nativeCatalogue}' does not match pinned engine catalogue '{JassNativeCatalogue.Version}'.");
        if (manifest["profiles"] is JsonObject profiles && profiles[profile] is not JsonObject) throw new EngineException("INVALID_ARGUMENT", $"Gameplay manifest has no definition for profile '{profile}'.");

        var profileSpec = (manifest["profiles"] as JsonObject)?[profile] as JsonObject ?? HtwProfileModel.ProfileSpec(profile);
        profileSpec["team_definitions"] ??= HtwProfileModel.DefaultTeams(profile);
        var teams = HtwProfileModel.NormalizeTeams(profile, manifest["teams"] ?? profileSpec["team_definitions"]);
        var teamRegistry = HtwProfileModel.BuildTeamRegistry(teams);

        var modules = ReadModules(manifest, manifestPath);
        var variables = ReadVariables(manifest, manifestPath);
        var triggers = ReadTriggers(manifest, manifestPath);
        var regions = ReadRegions(profileSpec["regions"] ?? manifest["regions"]);
        var regionRoles = ReadRegionRoles(manifest, profile, regions);
        var model = new JsonObject
        {
            ["gameplay_modules"] = new JsonArray(modules.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
            ["gameplay_variables"] = new JsonArray(variables.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
            ["gameplay_triggers"] = new JsonArray(triggers.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
            ["regions"] = new JsonArray(regions.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
            ["region_roles"] = new JsonArray(regionRoles.Select(x => (JsonNode?)x.DeepClone()).ToArray())
        };
        // Source composition has no map archive context. It validates typed
        // syntax and function references; map-bound refs are checked again
        // when this model is staged against an inspected map.
        GameplayModelValidator.ValidateCollections(model, requireModuleSources: true);

        var symbolOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var symbols = new List<JsonObject>();
        foreach (var module in modules)
        {
            var moduleId = GameplayModelValidator.RequiredString(module, "id");
            var moduleSource = GameplayModelValidator.RequiredString(module, "source");
            foreach (Match match in Function.Matches(moduleSource))
            {
                var symbol = match.Groups["name"].Value;
                if (!symbolOwners.TryAdd(symbol, moduleId)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate JASS function symbol '{symbol}' in modules '{symbolOwners[symbol]}' and '{moduleId}'.");
                symbols.Add(new JsonObject { ["name"] = symbol, ["module_id"] = moduleId, ["public"] = IsPublic(module, symbol) });
            }
        }
        symbols.Add(new JsonObject { ["name"] = "HTW_Teams_ConfigureProfile", ["module_id"] = "generated.team_registry", ["public"] = true });
        if (!symbolOwners.ContainsKey("HTW_MCP_Bootstrap")) throw new EngineException("INVALID_ARGUMENT", "Gameplay modules must define HTW_MCP_Bootstrap as the one-time initialization entry point.");

        var handlers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var trigger in triggers)
        {
            var id = GameplayModelValidator.RequiredString(trigger, "id");
            var handler = StringValue(trigger, "handler_name") ?? $"HTW_Trigger_{id}";
            if (trigger["handler_name"] is null && symbolOwners.ContainsKey(handler)) throw new EngineException("INVALID_ARGUMENT", $"Generated trigger handler '{handler}' shadows a module function.");
            if (trigger["handler_name"] is not null && !symbolOwners.ContainsKey(handler)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{id}' references unresolved handler '{handler}'.");
            handlers[id] = handler;
        }

        var regionBindings = BuildRegionBindings(regions);
        var source = ComposeSource(profile, modules, variables, triggers, handlers, regions, regionBindings, teams);
        try { ScriptOwnership.ValidateMcpOwnedJass("war3map.j", source); }
        catch (InvalidDataException exception) { throw new EngineException("PARSE_FAILED", exception.Message, false, exception); }

        var moduleResults = new JsonArray(modules.Select(module =>
        {
            var sourceText = GameplayModelValidator.RequiredString(module, "source");
            var result = module.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Gameplay module could not be cloned.");
            result.Remove("source");
            result["source_sha256"] = Hashing.Sha256(Encoding.UTF8.GetBytes(sourceText));
            result["source_bytes"] = Encoding.UTF8.GetByteCount(sourceText);
            return (JsonNode)result;
        }).ToArray());
        var triggerManifest = new JsonArray(triggers.Select(trigger =>
        {
            var result = trigger.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Gameplay trigger could not be cloned.");
            var id = GameplayModelValidator.RequiredString(result, "id");
            result["folder_path"] = StringValue(result, "folder_path") ?? StringValue(result, "folder") ?? string.Empty;
            result.Remove("folder");
            result["handler"] = handlers[id];
            return (JsonNode)result;
        }).ToArray());
        var variableManifest = new JsonArray(variables.Select(variable => (JsonNode)(variable.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Gameplay variable could not be cloned."))).ToArray());
        var sourceManifest = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["composer_version"] = ComposerVersion,
            ["mode"] = GameplayModelValidator.NativeMode,
            ["native_catalogue_version"] = JassNativeCatalogue.Version,
            ["profile"] = profile,
            ["modules"] = moduleResults.DeepClone(),
            ["triggers"] = triggerManifest.DeepClone(),
            ["variables"] = variableManifest.DeepClone(),
            ["regions"] = new JsonArray(regions.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
            ["region_roles"] = new JsonArray(regionRoles.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
            ["profile_spec"] = profileSpec.DeepClone(),
            ["teams"] = teams.DeepClone(),
            ["team_registry"] = teamRegistry.DeepClone(),
            ["module_order"] = new JsonArray(modules.Select(x => (JsonNode?)JsonValue.Create(GameplayModelValidator.RequiredString(x, "id"))).ToArray()),
            ["symbols"] = new JsonArray(symbols.Select(x => (JsonNode?)x.DeepClone()).ToArray())
        };
        return new JsonObject
        {
            ["schema_version"] = "1.0",
            ["composer_version"] = ComposerVersion,
            ["mode"] = GameplayModelValidator.NativeMode,
            ["native_catalogue_version"] = JassNativeCatalogue.Version,
            ["profile"] = profile,
            ["profile_spec"] = profileSpec.DeepClone(),
            ["manifest_path"] = manifestPath,
            ["manifest_sha256"] = Hashing.Sha256(manifestBytes),
            ["source_manifest_sha256"] = GameplayModelValidator.Hash(sourceManifest),
            ["module_order"] = sourceManifest["module_order"]!.DeepClone(),
            ["modules"] = moduleResults,
            ["triggers"] = triggerManifest,
            ["variables"] = variableManifest,
            ["regions"] = new JsonArray(regions.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
            ["region_roles"] = new JsonArray(regionRoles.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
            ["teams"] = teams.DeepClone(),
            ["team_registry"] = teamRegistry.DeepClone(),
            ["region_handles"] = new JsonObject(regionBindings.Keys.OrderBy(x => x, StringComparer.Ordinal).ToDictionary(id => id, id => (JsonNode?)RegionHandle(id))),
            ["symbols"] = new JsonArray(symbols.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
            ["trigger_manifest_sha256"] = GameplayModelValidator.Hash(triggerManifest),
            ["variable_manifest_sha256"] = GameplayModelValidator.Hash(variableManifest),
            ["function_count"] = Function.Matches(source).Count,
            ["main_count"] = Regex.Matches(source, "(?im)^\\s*function\\s+main\\s+takes\\s+nothing\\s+returns\\s+nothing\\b").Count,
            ["source_bytes"] = Encoding.UTF8.GetByteCount(source),
            ["source_sha256"] = Hashing.Sha256(Encoding.UTF8.GetBytes(source)),
            ["static_validation"] = new JsonObject { ["status"] = "passed", ["evidence_level"] = "static_only", ["validation_scope"] = "syntax_symbols_and_native_catalogue", ["parser"] = "War3Net.CodeAnalysis.Jass", ["native_catalogue_version"] = JassNativeCatalogue.Version, ["runtime_verified"] = false },
            ["source_manifest"] = sourceManifest,
            ["canonical_model"] = new JsonObject
            {
                ["profile"] = profile,
                ["profiles"] = new JsonObject { [profile] = profileSpec.DeepClone() },
                ["gameplay_modules"] = new JsonArray(modules.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
                ["gameplay_triggers"] = new JsonArray(triggers.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
                ["gameplay_variables"] = new JsonArray(variables.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
                ["regions"] = new JsonArray(regions.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
                ["region_roles"] = new JsonArray(regionRoles.Select(x => (JsonNode?)x.DeepClone()).ToArray()),
                ["teams"] = teams.DeepClone(),
                ["team_registry"] = teamRegistry.DeepClone()
            },
            ["source"] = source
        };
    }

    private static List<JsonObject> ReadModules(JsonObject manifest, string manifestPath)
    {
        if (manifest["modules"] is not JsonArray values || values.Count == 0) throw new EngineException("INVALID_ARGUMENT", "Gameplay manifest requires a non-empty modules array.");
        var modules = new List<JsonObject>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in values)
        {
            if (node is not JsonObject value) throw new EngineException("INVALID_ARGUMENT", "Every gameplay module must be an object.");
            var module = value.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Gameplay module could not be cloned.");
            var id = GameplayModelValidator.RequiredModuleIdentifier(module, "id");
            if (!ids.Add(id)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate gameplay module '{id}'.");
            var source = StringValue(module, "source");
            var declaredSourceHash = StringValue(module, "source_sha256");
            if (string.IsNullOrWhiteSpace(source))
            {
                var relative = GameplayModelValidator.RequiredString(module, "path");
                var fullPath = SafeRelativePath(Path.GetDirectoryName(manifestPath)!, relative, $"module {id}");
                var bytes = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : throw new EngineException("FILE_NOT_FOUND", $"Gameplay module does not exist: {fullPath}");
                if (bytes.Length > MaxModuleBytes) throw new EngineException("INVALID_ARGUMENT", $"Gameplay module '{id}' exceeds the 2 MiB safety limit.");
                try { source = new UTF8Encoding(false, true).GetString(bytes); }
                catch (Exception exception) { throw new EngineException("PARSE_FAILED", $"Gameplay module '{id}' is not valid UTF-8.", false, exception); }
            }
            module["source"] = NormalizeSource(source!);
            var computedSourceHash = Hashing.Sha256(Encoding.UTF8.GetBytes(module["source"]!.GetValue<string>()));
            if (declaredSourceHash is not null && !string.Equals(declaredSourceHash, computedSourceHash, StringComparison.OrdinalIgnoreCase)) throw new EngineException("SOURCE_CHANGED", $"Gameplay module '{id}' source_sha256 does not match its source text.");
            module["source_sha256"] = computedSourceHash;
            GameplayModelValidator.ValidateModule(module, requireSource: true);
            modules.Add(module);
        }
        var available = modules.Select(x => GameplayModelValidator.RequiredString(x, "id")).ToHashSet(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            foreach (var dependency in GameplayModelValidator.Strings(module["dependencies"])) if (!available.Contains(dependency)) throw new EngineException("INVALID_ARGUMENT", $"Gameplay module '{GameplayModelValidator.RequiredString(module, "id")}' depends on missing module '{dependency}'.");
        }
        return TopologicalOrder(modules);
    }

    private static List<JsonObject> ReadVariables(JsonObject manifest, string manifestPath)
    {
        var values = new List<JsonObject>();
        values.AddRange(ReadInlineObjects(manifest["variables"], "variables"));
        values.AddRange(ReadObjectFiles(manifest["variable_files"], manifestPath, "variable"));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in values)
        {
            GameplayModelValidator.ValidateVariable(variable);
            var id = GameplayModelValidator.RequiredString(variable, "id");
            var name = GameplayModelValidator.RequiredString(variable, "name");
            if (!ids.Add(id) || !names.Add(name)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate gameplay variable identity '{id}' or name '{name}'.");
        }
        return values.OrderBy(x => GameplayModelValidator.RequiredString(x, "id"), StringComparer.Ordinal).ToList();
    }

    private static List<JsonObject> ReadTriggers(JsonObject manifest, string manifestPath)
    {
        var values = new List<JsonObject>();
        values.AddRange(ReadInlineObjects(manifest["triggers"], "triggers"));
        values.AddRange(ReadObjectFiles(manifest["trigger_files"], manifestPath, "trigger"));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trigger in values)
        {
            if (trigger["folder_path"] is null && trigger["folder"] is not null) trigger["folder_path"] = trigger["folder"]!.DeepClone();
            GameplayModelValidator.ValidateTrigger(trigger);
            var id = GameplayModelValidator.RequiredString(trigger, "id");
            var name = GameplayModelValidator.RequiredString(trigger, "name");
            if (!ids.Add(id) || !names.Add(name)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate gameplay trigger identity '{id}' or name '{name}'.");
        }
        return values.OrderBy(x => GameplayModelValidator.RequiredString(x, "id"), StringComparer.Ordinal).ToList();
    }

    private static List<JsonObject> ReadRegions(JsonNode? node)
    {
        if (node is null) return new List<JsonObject>();
        if (node is not JsonArray values) throw new EngineException("INVALID_ARGUMENT", "Gameplay manifest field 'regions' must be an array.");
        var result = new List<JsonObject>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var creationNumbers = new HashSet<int>();
        foreach (var value in values)
        {
            if (value is not JsonObject region) throw new EngineException("INVALID_ARGUMENT", "Every gameplay region binding must be an object.");
            var clone = region.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Gameplay region binding could not be cloned.");
            var name = GameplayModelValidator.RequiredString(clone, "name");
            var id = StringValue(clone, "id") ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires a stable id.");
            var creation = clone["creation_number"] is JsonValue number && number.TryGetValue<int>(out var valueNumber) ? valueNumber : -1;
            if (!ids.Add(id) || !names.Add(name) || creation < 0 || !creationNumbers.Add(creation) || !string.Equals(id, RegionSupport.StableId(creation), StringComparison.Ordinal))
            {
                throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' has duplicate or non-native stable identity.");
            }
            var minX = Number(clone["min_x"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires min_x."), "min_x");
            var minY = Number(clone["min_y"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires min_y."), "min_y");
            var maxX = Number(clone["max_x"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires max_x."), "max_x");
            var maxY = Number(clone["max_y"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires max_y."), "max_y");
            if (minX > maxX || minY > maxY) throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' has inverted bounds.");
            clone["weather"] ??= "None";
            clone["ambient_sound"] ??= string.Empty;
            clone["color_argb"] ??= 0;
            clone["references"] ??= RegionSupport.EmptyReferences();
            clone["codec_version"] ??= RegionSupport.CodecVersion;
            clone["provenance"] ??= "intended_design";
            clone["capability"] ??= "typed_write_enabled";
            result.Add(clone);
        }
        return result;
    }

    private static Dictionary<string, JsonObject> BuildRegionBindings(IReadOnlyList<JsonObject> regions)
        => regions.ToDictionary(
            region => GameplayModelValidator.RequiredString(region, "id"),
            region => region,
            StringComparer.Ordinal);

    private static bool IsArenaRegion(JsonObject region)
    {
        var name = StringValue(region, "name") ?? string.Empty;
        return name.StartsWith("Arena_", StringComparison.Ordinal)
            && !name.Contains("_Entrance", StringComparison.Ordinal)
            && !name.Contains("_Backline", StringComparison.Ordinal);
    }

    private static List<JsonObject> ReadRegionRoles(JsonObject manifest, string profile, IReadOnlyList<JsonObject> regions)
    {
        var byId = regions.ToDictionary(region => GameplayModelValidator.RequiredString(region, "id"), StringComparer.Ordinal);
        var byName = regions.ToDictionary(region => GameplayModelValidator.RequiredString(region, "name"), StringComparer.Ordinal);
        var configured = manifest["region_roles"] as JsonArray;
        if ((configured is null || configured.Count == 0) && manifest["profiles"] is JsonObject profiles && profiles[profile] is JsonObject profileObject) configured = profileObject["region_roles"] as JsonArray;
        if (configured is null) return new List<JsonObject>();

        var result = new List<JsonObject>();
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in configured)
        {
            if (node is not JsonObject role) throw new EngineException("INVALID_ARGUMENT", "Every region role binding must be an object.");
            var roleName = RegionSupport.RequiredRole(role);
            var reference = StringValue(role, "region_id") ?? StringValue(role, "region_name") ?? throw new EngineException("INVALID_ARGUMENT", "Region role bindings require region_id or region_name.");
            var region = byId.TryGetValue(reference, out var byIdValue) ? byIdValue : byName.TryGetValue(reference, out var byNameValue) ? byNameValue : null;
            if (region is null) throw new EngineException("INVALID_ARGUMENT", $"Region role binding references unknown region '{reference}'.");
            var regionId = GameplayModelValidator.RequiredString(region, "id");
            if (!roles.Add($"{regionId}:{roleName}")) throw new EngineException("INVALID_ARGUMENT", $"Duplicate region role '{roleName}' for '{regionId}'.");
            result.Add(new JsonObject
            {
                ["region_id"] = regionId,
                ["role"] = roleName,
                ["provenance"] = "intended_design"
            });
        }
        return result;
    }

    private static List<JsonObject> ReadInlineObjects(JsonNode? node, string field)
    {
        if (node is null) return new List<JsonObject>();
        if (node is not JsonArray values) throw new EngineException("INVALID_ARGUMENT", $"Gameplay manifest field '{field}' must be an array.");
        return values.Select(value => value is JsonObject item ? item.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", $"Could not clone {field} entry.") : throw new EngineException("INVALID_ARGUMENT", $"Every {field} entry must be an object.")).ToList();
    }

    private static List<JsonObject> ReadObjectFiles(JsonNode? node, string manifestPath, string kind)
    {
        if (node is null) return new List<JsonObject>();
        if (node is not JsonArray paths) throw new EngineException("INVALID_ARGUMENT", $"{kind}_files must be an array.");
        var result = new List<JsonObject>();
        foreach (var pathNode in paths)
        {
            var relative = pathNode is JsonValue value && value.TryGetValue<string>(out var path) ? path : throw new EngineException("INVALID_ARGUMENT", $"{kind}_files must contain path strings.");
            var fullPath = SafeRelativePath(Path.GetDirectoryName(manifestPath)!, relative, $"{kind} manifest");
            if (!File.Exists(fullPath)) throw new EngineException("FILE_NOT_FOUND", $"Gameplay {kind} manifest does not exist: {fullPath}");
            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length > MaxManifestBytes) throw new EngineException("INVALID_ARGUMENT", $"Gameplay {kind} manifest exceeds the 2 MiB safety limit: {fullPath}");
            var parsed = ParseNode(bytes, $"Gameplay {kind} manifest '{fullPath}'");
            if (parsed is JsonObject item)
            {
                result.Add(item);
            }
            else if (parsed is JsonArray items)
            {
                foreach (var child in items)
                {
                    if (child is not JsonObject childObject) throw new EngineException("INVALID_ARGUMENT", $"Gameplay {kind} manifest array '{fullPath}' must contain objects.");
                    result.Add(childObject.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", $"Could not clone {kind} manifest entry."));
                }
            }
            else
            {
                throw new EngineException("INVALID_JSON", $"Gameplay {kind} manifest '{fullPath}' must contain an object or an array of objects.");
            }
        }
        return result;
    }

    private static List<JsonObject> TopologicalOrder(IReadOnlyList<JsonObject> modules)
    {
        var byId = modules.ToDictionary(x => GameplayModelValidator.RequiredString(x, "id"), StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var output = new List<JsonObject>();
        void Visit(string id)
        {
            state.TryGetValue(id, out var current);
            if (current == 1) throw new EngineException("INVALID_ARGUMENT", $"Gameplay module dependency cycle includes '{id}'.");
            if (current == 2) return;
            state[id] = 1;
            foreach (var dependency in GameplayModelValidator.Strings(byId[id]["dependencies"]).OrderBy(x => x, StringComparer.Ordinal)) Visit(dependency);
            state[id] = 2;
            output.Add(byId[id]);
        }
        foreach (var id in byId.Keys.OrderBy(x => x, StringComparer.Ordinal)) Visit(id);
        return output;
    }

    private static string ComposeSource(string profile, IReadOnlyList<JsonObject> modules, IReadOnlyList<JsonObject> variables, IReadOnlyList<JsonObject> triggers, IReadOnlyDictionary<string, string> handlers, IReadOnlyList<JsonObject> regions, IReadOnlyDictionary<string, JsonObject> regionBindings, JsonArray teams)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// Generated by wc3-map-mcp; do not edit in World Editor.");
        builder.AppendLine($"// profile: {profile}");
        builder.AppendLine($"// composer: {ComposerVersion}");
        var regionHandles = triggers.SelectMany(x => (x["events"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()).Where(x => StringValue(x, "type") == "region_entry").Select(x => ResolveRegionReference(x, regionBindings)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var customEvents = triggers.SelectMany(x => (x["events"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()).Where(x => StringValue(x, "type") == "custom_event").Select(x => StringValue(x, "name")).Where(x => x is not null).Cast<string>().Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var variableNames = variables.Select(variable => GameplayModelValidator.RequiredString(variable, "name")).ToHashSet(StringComparer.Ordinal);
        var arenaRegions = regions.Where(IsArenaRegion).OrderBy(region => GameplayModelValidator.RequiredString(region, "id"), StringComparer.Ordinal).ToArray();
        if (arenaRegions.Length < teams.Count) throw new EngineException("INVALID_ARGUMENT", $"Profile '{profile}' requires at least {teams.Count} arena regions, but the gameplay manifest defines {arenaRegions.Length}.");
        builder.AppendLine("globals");
        var declaredGlobals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void DeclareGlobal(string type, string name, bool array = false)
        {
            if (declaredGlobals.Add(name)) builder.AppendLine($"    {type}{(array ? " array" : string.Empty)} {name}");
        }
        DeclareGlobal("integer", "HTW_Round");
        DeclareGlobal("integer", "HTW_Wave");
        DeclareGlobal("integer", "HTW_Phase");
        DeclareGlobal("integer", "HTW_TeamCount");
        DeclareGlobal("integer", "HTW_TeamMemberA", true);
        DeclareGlobal("integer", "HTW_TeamMemberB", true);
        DeclareGlobal("integer", "HTW_TeamForce", true);
        DeclareGlobal("string", "HTW_TeamStableId", true);
        DeclareGlobal("string", "HTW_TeamArena", true);
        DeclareGlobal("boolean", "HTW_TeamLiving", true);
        DeclareGlobal("integer", "HTW_TeamDestination", true);
        DeclareGlobal("integer", "HTW_ActivePlayerCount");
        DeclareGlobal("integer", "HTW_ArenaCount");
        DeclareGlobal("rect", "HTW_ArenaRect", true);
        DeclareGlobal("rect", "HTW_ArenaRectA");
        DeclareGlobal("rect", "HTW_ArenaRectB");
        DeclareGlobal("integer", "HTW_LivingTeamCount");
        DeclareGlobal("integer", "HTW_LivingTeamIds", true);
        DeclareGlobal("integer", "HTW_RouteOffset");
        DeclareGlobal("integer", "HTW_RouteDestinationTeam");
        DeclareGlobal("boolean", "HTW_RoutingLocked");
        foreach (var variable in variables)
        {
            var type = GameplayModelValidator.RequiredString(variable, "type").ToLowerInvariant();
            var array = variable["array"]?.GetValue<bool>() == true;
            DeclareGlobal(type, GameplayModelValidator.RequiredString(variable, "name"), array);
        }
        foreach (var region in regions) builder.AppendLine($"    region {RegionHandle(GameplayModelValidator.RequiredString(region, "id"))}");
        foreach (var eventName in customEvents) builder.AppendLine($"    real {EventHandle(eventName)}");
        builder.AppendLine("endglobals");
        builder.AppendLine();

        builder.AppendLine("function HTW_Regions_InitializeProfile takes nothing returns nothing");
        builder.AppendLine($"    set HTW_ArenaCount = {arenaRegions.Length}");
        for (var index = 0; index < arenaRegions.Length; index++)
        {
            var region = arenaRegions[index];
            builder.AppendLine($"    set HTW_ArenaRect[{index + 1}] = Rect({JassReal(Number(region["min_x"]!, "min_x"))}, {JassReal(Number(region["min_y"]!, "min_y"))}, {JassReal(Number(region["max_x"]!, "max_x"))}, {JassReal(Number(region["max_y"]!, "max_y"))})");
        }
        builder.AppendLine("    set HTW_ArenaRectA = HTW_ArenaRect[1]");
        if (arenaRegions.Length > 1) builder.AppendLine("    set HTW_ArenaRectB = HTW_ArenaRect[2]");
        builder.AppendLine("endfunction");
        builder.AppendLine();

        builder.AppendLine("function HTW_Teams_ConfigureProfile takes nothing returns nothing");
        var orderedTeams = teams.OfType<JsonObject>().OrderBy(team => team["id"]?.GetValue<string>(), StringComparer.Ordinal).ToArray();
        var livingTeams = orderedTeams.Where(team => !string.Equals(team["life_state"]?.GetValue<string>(), "eliminated", StringComparison.OrdinalIgnoreCase)).ToArray();
        builder.AppendLine($"    set HTW_TeamCount = {orderedTeams.Length}");
        builder.AppendLine($"    set HTW_LivingTeamCount = {livingTeams.Length}");
        var teamIndex = 0;
        var livingIndex = 0;
        var maxPlayerId = 0;
        foreach (var team in orderedTeams) foreach (var member in team["member_player_ids"]?.AsArray() ?? new JsonArray()) maxPlayerId = Math.Max(maxPlayerId, member?.GetValue<int>() ?? 0);
        builder.AppendLine($"    set HTW_ActivePlayerCount = {maxPlayerId}");
        foreach (var team in orderedTeams)
        {
            teamIndex++;
            var members = team["member_player_ids"]?.AsArray().Select(value => value?.GetValue<int>() ?? 0).ToArray() ?? Array.Empty<int>();
            var force = team["force_index"]?.GetValue<int>() ?? -1;
            var stableId = GameplayModelValidator.RequiredString(team, "id");
            var arenaId = team["arena_id"]?.GetValue<string>() ?? string.Empty;
            var living = !string.Equals(team["life_state"]?.GetValue<string>(), "eliminated", StringComparison.OrdinalIgnoreCase);
            builder.AppendLine($"    set HTW_TeamStableId[{teamIndex}] = {Quote(stableId)}");
            builder.AppendLine($"    set HTW_TeamMemberA[{teamIndex}] = {members.ElementAtOrDefault(0)}");
            builder.AppendLine($"    set HTW_TeamMemberB[{teamIndex}] = {members.ElementAtOrDefault(1)}");
            builder.AppendLine($"    set HTW_TeamForce[{teamIndex}] = {force + 1}");
            builder.AppendLine($"    set HTW_TeamArena[{teamIndex}] = {Quote(arenaId)}");
            builder.AppendLine($"    set HTW_TeamLiving[{teamIndex}] = {(living ? "true" : "false")}");
            if (living)
            {
                livingIndex++;
                builder.AppendLine($"    set HTW_LivingTeamIds[{livingIndex}] = {teamIndex}");
            }
        }
        builder.AppendLine("endfunction");
        builder.AppendLine();
        foreach (var module in modules)
        {
            builder.AppendLine($"// MCP module: {GameplayModelValidator.RequiredString(module, "id")} ({StringValue(module, "path") ?? "<inline>"})");
            builder.AppendLine(GameplayModelValidator.RequiredString(module, "source").TrimEnd());
            builder.AppendLine();
        }
        builder.AppendLine("function HTW_MCP_InitializeVariables takes nothing returns nothing");
        foreach (var variable in variables.Where(x => x["initial"] is not null))
        {
            var type = GameplayModelValidator.RequiredString(variable, "type").ToLowerInvariant();
            if (type is not ("handle" or "timer" or "trigger" or "unit" or "group" or "region" or "rect" or "player" or "force")) builder.AppendLine($"    set {GameplayModelValidator.RequiredString(variable, "name")} = {Literal(variable["initial"]!, type)}");
        }
        foreach (var regionId in regions.Select(region => GameplayModelValidator.RequiredString(region, "id")))
        {
            var region = regionBindings[regionId];
            builder.AppendLine($"    set {RegionHandle(regionId)} = CreateRegion()");
            builder.AppendLine($"    call RegionAddRect({RegionHandle(regionId)}, Rect({JassReal(Number(region["min_x"]!, "min_x"))}, {JassReal(Number(region["min_y"]!, "min_y"))}, {JassReal(Number(region["max_x"]!, "max_x"))}, {JassReal(Number(region["max_y"]!, "max_y"))}))");
        }
        builder.AppendLine("endfunction");
        builder.AppendLine();
        foreach (var trigger in triggers.Where(x => x["handler_name"] is null && (x["enabled"] is null || x["enabled"]!.GetValue<bool>())))
        {
            var id = GameplayModelValidator.RequiredString(trigger, "id");
            builder.AppendLine($"function {handlers[id]} takes nothing returns nothing");
            RenderConditions(builder, trigger["conditions"] as JsonArray, variables, 1);
            RenderActions(builder, trigger["actions"] as JsonArray, variables, 1);
            builder.AppendLine("endfunction");
            builder.AppendLine();
        }
        builder.AppendLine("function HTW_MCP_RegisterTriggers takes nothing returns nothing");
        var hasRegisteredEvents = triggers
            .Where(x => (x["enabled"] is null || x["enabled"]!.GetValue<bool>()) && (x["initially_on"] is null || x["initially_on"]!.GetValue<bool>()))
            .SelectMany(x => (x["events"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            .Any(x => StringValue(x, "type") is not ("map_initialization" or "periodic_timer"));
        if (hasRegisteredEvents) builder.AppendLine("    local trigger htw_trigger");
        foreach (var trigger in triggers.Where(x => (x["enabled"] is null || x["enabled"]!.GetValue<bool>()) && (x["initially_on"] is null || x["initially_on"]!.GetValue<bool>())))
        {
            var id = GameplayModelValidator.RequiredString(trigger, "id");
            var handler = handlers[id];
            var events = (trigger["events"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToArray();
            foreach (var timer in events.Where(x => StringValue(x, "type") == "periodic_timer")) builder.AppendLine($"    call TimerStart(CreateTimer(), {JassReal(Number(timer["period"]!, "period"))}, {(timer["repeat"] is null || timer["repeat"]!.GetValue<bool>() ? "true" : "false")}, function {handler})");
            var registrations = events.Where(x => StringValue(x, "type") != "map_initialization" && StringValue(x, "type") != "periodic_timer").ToArray();
            if (registrations.Length > 0)
            {
                builder.AppendLine("    set htw_trigger = CreateTrigger()");
                builder.AppendLine($"    call TriggerAddAction(htw_trigger, function {handler})");
            foreach (var eventNode in registrations) RenderEventRegistration(builder, eventNode, regionBindings);
            }
        }
        builder.AppendLine("endfunction");
        builder.AppendLine();
        builder.AppendLine("function HTW_MCP_RunInitializationTriggers takes nothing returns nothing");
        foreach (var trigger in triggers.Where(x => (x["enabled"] is null || x["enabled"]!.GetValue<bool>()) && (x["initially_on"] is null || x["initially_on"]!.GetValue<bool>()) && (x["events"] as JsonArray)?.OfType<JsonObject>().Any(e => StringValue(e, "type") == "map_initialization") == true)) builder.AppendLine($"    call {handlers[GameplayModelValidator.RequiredString(trigger, "id")]}()");
        builder.AppendLine("endfunction");
        builder.AppendLine();
        builder.AppendLine("function main takes nothing returns nothing");
        builder.AppendLine("    call HTW_MCP_InitializeVariables()");
        builder.AppendLine("    call HTW_MCP_Bootstrap()");
        builder.AppendLine("    call HTW_MCP_RegisterTriggers()");
        builder.AppendLine("    call HTW_MCP_RunInitializationTriggers()");
        builder.AppendLine("endfunction");
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void RenderConditions(StringBuilder builder, JsonArray? conditions, IReadOnlyList<JsonObject> variables, int indent)
    {
        foreach (var node in conditions?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            builder.AppendLine($"{Indent(indent)}if not ({RenderCondition(node, variables)}) then");
            builder.AppendLine($"{Indent(indent + 1)}return");
            builder.AppendLine($"{Indent(indent)}endif");
        }
    }

    private static void RenderActions(StringBuilder builder, JsonArray? actions, IReadOnlyList<JsonObject> variables, int indent)
    {
        foreach (var node in actions?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var type = GameplayModelValidator.RequiredString(node, "type");
            switch (type)
            {
                case "set_variable": builder.AppendLine($"{Indent(indent)}set {VariableName(node["variable_id"]!, variables)} = {Expression(node["value"] ?? throw new EngineException("INVALID_ARGUMENT", "set_variable requires value."), variables)}"); break;
                case "branch":
                    builder.AppendLine($"{Indent(indent)}if {RenderCondition(node["condition"]!.AsObject(), variables)} then");
                    RenderActions(builder, node["then"]!.AsArray(), variables, indent + 1);
                    if (node["else"] is JsonArray otherwise) { builder.AppendLine($"{Indent(indent)}else"); RenderActions(builder, otherwise, variables, indent + 1); }
                    builder.AppendLine($"{Indent(indent)}endif");
                    break;
                case "create_timer":
                    var variable = VariableName(node["variable_id"]!, variables);
                    var callback = node["callback"] is null ? "null" : $"function {GameplayModelValidator.RequiredIdentifier(node, "callback")}";
                    builder.AppendLine($"{Indent(indent)}set {variable} = CreateTimer()");
                    builder.AppendLine($"{Indent(indent)}call TimerStart({variable}, {JassReal(Number(node["period"]!, "period"))}, {(node["repeating"] is null || node["repeating"]!.GetValue<bool>() ? "true" : "false")}, {callback})");
                    break;
                case "unit_operation": builder.AppendLine($"{Indent(indent)}call {node["operation"]!.GetValue<string>() switch { "kill_trigger_unit" => "KillUnit", "remove_trigger_unit" => "RemoveUnit", _ => throw new EngineException("UNSUPPORTED_OPERATION", "unit_operation operation is unsupported.") }}(GetTriggerUnit())"); break;
                case "group_operation":
                    var group = VariableName(node["group_variable_id"]!, variables);
                    var operation = node["operation"]!.GetValue<string>();
                    builder.AppendLine($"{Indent(indent)}call {operation switch { "add_trigger_unit" => "GroupAddUnit", "remove_trigger_unit" => "GroupRemoveUnit", _ => "DestroyGroup" }}({group}{(operation == "destroy" ? string.Empty : ", GetTriggerUnit()")})");
                    break;
                case "message": builder.AppendLine($"{Indent(indent)}call DisplayTextToForce(GetPlayersAll(), {Quote(node["text"]!.GetValue<string>())})"); break;
                case "phase_transition": builder.AppendLine(node["phase"] is null ? $"{Indent(indent)}call HTW_Phases_Advance()" : $"{Indent(indent)}set HTW_Phase = {node["phase"]!.GetValue<int>()}"); break;
                case "call_function": builder.AppendLine($"{Indent(indent)}call {GameplayModelValidator.RequiredIdentifier(node, "function")}()"); break;
                case "return": builder.AppendLine($"{Indent(indent)}return"); break;
            }
        }
    }

    private static string RenderCondition(JsonObject condition, IReadOnlyList<JsonObject> variables)
    {
        var type = GameplayModelValidator.RequiredString(condition, "type");
        if (type == "always") return "true";
        if (type == "function") return $"{GameplayModelValidator.RequiredIdentifier(condition, "function")}()";
        var variable = VariableName(condition["variable_id"]!, variables);
        if (type == "boolean_variable") return condition["value"] is null ? variable : $"{variable} == {(condition["value"]!.GetValue<bool>() ? "true" : "false")}";
        return $"{variable} {Operator(condition["operator"]!.GetValue<string>())} {Expression(condition["value"]!, variables)}";
    }

    private static void RenderEventRegistration(StringBuilder builder, JsonObject eventNode, IReadOnlyDictionary<string, JsonObject> regionBindings)
    {
        var type = GameplayModelValidator.RequiredString(eventNode, "type");
        switch (type)
        {
            case "elapsed_time": builder.AppendLine($"    call TriggerRegisterTimerEvent(htw_trigger, {JassReal(Number(eventNode["seconds"]!, "seconds"))}, false)"); break;
            case "player_chat": builder.AppendLine($"    call TriggerRegisterPlayerChatEvent(htw_trigger, Player({eventNode["player_id"]!.GetValue<int>() - 1}), {Quote(eventNode["message"]!.GetValue<string>())}, {(eventNode["exact"] is null || eventNode["exact"]!.GetValue<bool>() ? "true" : "false")})"); break;
            case "unit_death":
                if (eventNode["player_id"] is null) builder.AppendLine("    call TriggerRegisterAnyUnitEventBJ(htw_trigger, EVENT_PLAYER_UNIT_DEATH)");
                else builder.AppendLine($"    call TriggerRegisterPlayerUnitEvent(htw_trigger, Player({eventNode["player_id"]!.GetValue<int>() - 1}), EVENT_PLAYER_UNIT_DEATH, null)");
                break;
            case "region_entry": builder.AppendLine($"    call TriggerRegisterEnterRegion(htw_trigger, {RegionHandle(ResolveRegionReference(eventNode, regionBindings))}, null)"); break;
            case "player_state_change": builder.AppendLine($"    call TriggerRegisterPlayerStateEvent(htw_trigger, Player({eventNode["player_id"]!.GetValue<int>() - 1}), {GameplayModelValidator.RequiredIdentifier(eventNode, "state")}, {OperatorConstant(eventNode["operator"]!.GetValue<string>())}, {JassReal(Number(eventNode["value"]!, "value"))})"); break;
            case "custom_event": builder.AppendLine($"    call TriggerRegisterVariableEvent(htw_trigger, {Quote(EventHandle(GameplayModelValidator.RequiredIdentifier(eventNode, "name")))}, EQUAL, 1.0)"); break;
        }
    }

    private static string VariableName(JsonNode node, IReadOnlyList<JsonObject> variables)
    {
        var id = node is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw new EngineException("INVALID_ARGUMENT", "Variable reference must be a variable id.");
        var variable = variables.FirstOrDefault(x => GameplayModelValidator.RequiredString(x, "id") == id || string.Equals(GameplayModelValidator.RequiredString(x, "name"), id, StringComparison.OrdinalIgnoreCase)) ?? throw new EngineException("INVALID_ARGUMENT", $"Unknown gameplay variable '{id}'.");
        return GameplayModelValidator.RequiredString(variable, "name");
    }

    private static string Expression(JsonNode node, IReadOnlyList<JsonObject> variables)
    {
        if (node is JsonObject objectValue && objectValue["variable_id"] is not null) return VariableName(objectValue["variable_id"]!, variables);
        if (node is JsonObject literal && literal["literal"] is not null) return Literal(literal["literal"]!, "auto");
        return Literal(node, "auto");
    }

    private static string Literal(JsonNode node, string type)
    {
        if (node is JsonValue value && value.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
        if (node is JsonValue integer && integer.TryGetValue<int>(out var intValue)) return intValue.ToString(CultureInfo.InvariantCulture);
        if (node is JsonValue number && number.TryGetValue<double>(out var real)) return JassReal(real);
        if (node is JsonValue text && text.TryGetValue<string>(out var stringValue)) return Quote(stringValue);
        throw new EngineException("INVALID_ARGUMENT", $"Unsupported literal for JASS type '{type}'.");
    }

    private static string Operator(string value) => value switch { "equal" => "==", "not_equal" => "!=", "less" => "<", "less_equal" => "<=", "greater" => ">", "greater_equal" => ">=", _ => throw new EngineException("INVALID_ARGUMENT", $"Unsupported comparison operator '{value}'.") };
    private static string OperatorConstant(string value) => value switch { "equal" => "EQUAL", "not_equal" => "NOT_EQUAL", "less" => "LESS_THAN", "less_equal" => "LESS_THAN_OR_EQUAL", "greater" => "GREATER_THAN", "greater_equal" => "GREATER_THAN_OR_EQUAL", _ => throw new EngineException("INVALID_ARGUMENT", $"Unsupported comparison operator '{value}'.") };
    private static string ResolveRegionReference(JsonObject eventNode, IReadOnlyDictionary<string, JsonObject> regionBindings)
    {
        var reference = StringValue(eventNode, "region_id") ?? StringValue(eventNode, "region_name") ?? throw new EngineException("INVALID_ARGUMENT", "region_entry requires region_id or region_name.");
        if (regionBindings.ContainsKey(reference)) return reference;
        var byName = regionBindings.Values.FirstOrDefault(region => string.Equals(StringValue(region, "name"), reference, StringComparison.Ordinal));
        return byName is null
            ? throw new EngineException("INVALID_ARGUMENT", $"region_entry references unknown region '{reference}'.")
            : GameplayModelValidator.RequiredString(byName, "id");
    }

    private static string RegionHandle(string logicalId) => $"HTW_Region_{Regex.Replace(logicalId, "[^A-Za-z0-9_]", "_")}";
    private static string EventHandle(string logicalId) => $"HTW_Event_{Regex.Replace(logicalId, "[^A-Za-z0-9_]", "_")}";
    private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"";
    private static string Indent(int count) => new(' ', count * 4);
    private static string JassReal(double value) => value.ToString("0.########", CultureInfo.InvariantCulture) + ".";
    private static double Number(JsonNode node, string field)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out var real) && double.IsFinite(real)) return real;
            if (value.TryGetValue<int>(out var integer)) return integer;
        }
        throw new EngineException("INVALID_ARGUMENT", $"{field} must be numeric.");
    }
    private static bool IsPublic(JsonObject module, string symbol) => GameplayModelValidator.Strings(module["public_symbols"]).Contains(symbol, StringComparer.OrdinalIgnoreCase);
    private static string? StringValue(JsonObject value, string property) => value[property] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;
    private static JsonObject ParseObject(byte[] bytes, string context)
    {
        try { return ParseNode(bytes, context) as JsonObject ?? throw new EngineException("INVALID_JSON", $"{context} root must be an object."); }
        catch (JsonException exception) { throw new EngineException("INVALID_JSON", $"{context} is not valid JSON: {exception.Message}", false, exception); }
    }
    private static JsonNode ParseNode(byte[] bytes, string context)
    {
        try { return JsonNode.Parse(Encoding.UTF8.GetString(bytes)) ?? throw new EngineException("INVALID_JSON", $"{context} is empty."); }
        catch (JsonException exception) { throw new EngineException("INVALID_JSON", $"{context} is not valid JSON: {exception.Message}", false, exception); }
    }
    private static string SafeRelativePath(string root, string relative, string context)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal) || relative.Contains(':')) throw new EngineException("INVALID_ARGUMENT", $"{context} path must be project-relative and traversal-free.");
        return Path.GetFullPath(Path.Combine(root, relative));
    }
    private static string NormalizeSource(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + "\n";
}
