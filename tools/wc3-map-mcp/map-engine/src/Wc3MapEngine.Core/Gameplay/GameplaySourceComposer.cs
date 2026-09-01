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
    public const string ComposerVersion = "mcp-jass-composer-2.4";
    private const int MaxManifestBytes = 2 * 1024 * 1024;
    private const int MaxModuleBytes = 2 * 1024 * 1024;
    private static readonly Regex Function = new("(?im)^\\s*function\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s+takes\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FunctionSignature = new(
        "(?im)^\\s*function\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s+takes\\s+(?<parameters>.*?)\\s+returns\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FunctionBlock = new(
        "^[ \\t]*function[ \\t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \\t]+takes\\b.*?^[ \\t]*endfunction[ \\t]*(?:\\r?\\n|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex FunctionReference = new(
        "\\bfunction[ \\t]+(?<callback>[A-Za-z_][A-Za-z0-9_]*)\\b|(?<![A-Za-z0-9_])(?<call>[A-Za-z_][A-Za-z0-9_]*)[ \\t]*\\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        // Map-info records are intentionally carried only by canonical
        // composition. A standalone gameplay manifest has no map archive
        // context, so ComposeManifest supplies deterministic profile defaults.
        foreach (var field in new[] { "metadata", "players", "forces" })
        {
            if (canonical[field] is JsonNode value) manifest[field] = value.DeepClone();
        }
        var bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString(EngineProtocol.JsonOptions));
        return ComposeManifest(manifest, "<canonical-gameplay-model>", bytes, requestedProfile);
    }

    private static JsonObject ComposeManifest(JsonObject manifest, string manifestPath, byte[] manifestBytes, string? requestedProfile)
    {
        var profile = requestedProfile ?? StringValue(manifest, "profile") ?? "mvp_2arena";
        if (profile is not ("mvp_2arena" or "full_6team" or "gui_compatible")) throw new EngineException("INVALID_ARGUMENT", $"Unsupported gameplay profile '{profile}'.");
        if (profile == "gui_compatible") throw new EngineException("CAPABILITY_GATED", "GUI-compatible trigger composition is gated pending exact WTG/WCT/WTS fixtures and World Editor evidence.");
        if (StringValue(manifest, "schema_version") is { } schema && schema != "1.0") throw new EngineException("INVALID_ARGUMENT", $"Unsupported gameplay manifest schema '{schema}'.");
        if (StringValue(manifest, "native_catalogue_version") is { } nativeCatalogue
            && nativeCatalogue != JassNativeCatalogue.Version
            && nativeCatalogue != JassNativeCatalogue.LegacyManifestVersion)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Gameplay manifest native catalogue '{nativeCatalogue}' does not match pinned engine catalogue '{JassNativeCatalogue.Version}'.");
        }
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
        var orderedFunctions = OrderFunctions(modules);
        var source = ComposeSource(
            profile,
            orderedFunctions,
            variables,
            triggers,
            handlers,
            regions,
            regionBindings,
            teams,
            manifest["metadata"] as JsonArray,
            manifest["players"] as JsonArray,
            manifest["forces"] as JsonArray);
        ValidateFunctionDeclarationOrder(source);
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
            ["function_order"] = new JsonArray(orderedFunctions.Select(x => (JsonNode?)new JsonObject
            {
                ["name"] = x.Name,
                ["module_id"] = x.ModuleId,
                ["path"] = x.ModulePath
            }).ToArray()),
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
            ["function_order"] = sourceManifest["function_order"]!.DeepClone(),
            ["trigger_manifest_sha256"] = GameplayModelValidator.Hash(triggerManifest),
            ["variable_manifest_sha256"] = GameplayModelValidator.Hash(variableManifest),
            ["function_count"] = Function.Matches(source).Count,
            ["main_count"] = Regex.Matches(source, "(?im)^\\s*function\\s+main\\s+takes\\s+nothing\\s+returns\\s+nothing\\b").Count,
            ["source_bytes"] = Encoding.UTF8.GetByteCount(source),
            ["source_sha256"] = Hashing.Sha256(Encoding.UTF8.GetBytes(source)),
            ["static_validation"] = new JsonObject { ["status"] = "passed", ["evidence_level"] = "static_only", ["validation_scope"] = "syntax_symbols_native_catalogue_declaration_order_and_mcp_call_arity", ["parser"] = "War3Net.CodeAnalysis.Jass", ["native_catalogue_version"] = JassNativeCatalogue.Version, ["runtime_verified"] = false },
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

    private static List<ComposedFunction> OrderFunctions(IReadOnlyList<JsonObject> modules)
    {
        var functions = new List<ComposedFunction>();
        foreach (var module in modules)
        {
            var moduleId = GameplayModelValidator.RequiredString(module, "id");
            var modulePath = StringValue(module, "path") ?? "<inline>";
            var moduleSource = GameplayModelValidator.RequiredString(module, "source");
            var matches = FunctionBlock.Matches(moduleSource);
            if (matches.Count == 0) throw new EngineException("INVALID_ARGUMENT", $"Gameplay module '{moduleId}' contains no complete JASS function blocks.");
            foreach (Match match in matches)
            {
                functions.Add(new ComposedFunction(
                    match.Groups["name"].Value,
                    moduleId,
                    modulePath,
                    match.Value.Trim()));
            }
        }

        var byName = functions.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var originalIndex = functions.Select((function, index) => (function.Name, index)).ToDictionary(x => x.Name, x => x.index, StringComparer.OrdinalIgnoreCase);
        var dependencies = functions.ToDictionary(x => x.Name, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var dependents = functions.ToDictionary(x => x.Name, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var function in functions)
        {
            var code = Regex.Replace(function.Source, "(?m)^[ \\t]*//.*$", string.Empty);
            foreach (Match reference in FunctionReference.Matches(code))
            {
                var dependency = reference.Groups["callback"].Success
                    ? reference.Groups["callback"].Value
                    : reference.Groups["call"].Value;
                if (!byName.ContainsKey(dependency) || string.Equals(dependency, function.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (dependencies[function.Name].Add(dependency)) dependents[dependency].Add(function.Name);
            }
        }

        var indegree = dependencies.ToDictionary(x => x.Key, x => x.Value.Count, StringComparer.OrdinalIgnoreCase);
        var ready = new SortedSet<int>();
        foreach (var function in functions.Where(x => indegree[x.Name] == 0)) ready.Add(originalIndex[function.Name]);

        var ordered = new List<ComposedFunction>(functions.Count);
        while (ready.Count > 0)
        {
            var index = ready.Min;
            ready.Remove(index);
            var function = functions[index];
            ordered.Add(function);
            foreach (var dependent in dependents[function.Name].OrderBy(name => originalIndex[name]))
            {
                indegree[dependent]--;
                if (indegree[dependent] == 0) ready.Add(originalIndex[dependent]);
            }
        }

        if (ordered.Count != functions.Count)
        {
            var cycle = functions.Where(function => indegree[function.Name] > 0).Select(function => function.Name);
            throw new EngineException("INVALID_ARGUMENT", $"Gameplay JASS function dependency cycle includes: {string.Join(", ", cycle)}.");
        }

        return ordered;
    }

    private static void ValidateFunctionDeclarationOrder(string source)
    {
        var lines = source.Split('\n');
        var declarations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < lines.Length; index++)
        {
            var match = Function.Match(lines[index]);
            if (match.Success) declarations[match.Groups["name"].Value] = index + 1;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var code = Regex.Replace(lines[index], "//.*$", string.Empty);
            foreach (Match reference in FunctionReference.Matches(code))
            {
                var name = reference.Groups["callback"].Success
                    ? reference.Groups["callback"].Value
                    : reference.Groups["call"].Value;
                if (declarations.TryGetValue(name, out var declarationLine) && index + 1 < declarationLine)
                {
                    throw new EngineException("INVALID_ARGUMENT", $"Generated JASS uses function '{name}' on line {index + 1} before its declaration on line {declarationLine}.");
                }
            }
        }

        ValidateFunctionCallArity(source);
    }

    private static void ValidateFunctionCallArity(string source)
    {
        // Function markers are comments and may contain parenthesized module
        // metadata, so remove whole-line comments before scanning calls while
        // preserving newlines for accurate diagnostics.
        var code = Regex.Replace(source, "(?m)^[ \\t]*//[^\\r\\n]*", string.Empty);
        var signatures = FunctionSignature.Matches(code)
            .Cast<Match>()
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => ParameterCount(match.Groups["parameters"].Value),
                StringComparer.OrdinalIgnoreCase);

        foreach (Match reference in FunctionReference.Matches(code))
        {
            if (!reference.Groups["call"].Success) continue;
            var name = reference.Groups["call"].Value;
            if (!signatures.TryGetValue(name, out var expected)) continue;

            var openParen = reference.Index + reference.Length - 1;
            var closeParen = FindMatchingParenthesis(code, openParen);
            if (closeParen < 0) throw new EngineException("INVALID_ARGUMENT", $"Generated JASS has an unterminated call to function '{name}' on line {LineNumber(source, reference.Index)}.");

            var actual = ArgumentCount(code.AsSpan(openParen + 1, closeParen - openParen - 1));
            if (actual != expected)
            {
                throw new EngineException("INVALID_ARGUMENT", $"Generated JASS calls function '{name}' with {actual} argument(s) on line {LineNumber(source, reference.Index)}, but its declaration takes {expected}.");
            }
        }
    }

    private static int ParameterCount(string parameters)
    {
        var trimmed = parameters.Trim();
        if (trimmed.Equals("nothing", StringComparison.OrdinalIgnoreCase)) return 0;
        return trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static int ArgumentCount(ReadOnlySpan<char> arguments)
    {
        if (arguments.Trim().IsEmpty) return 0;

        var count = 1;
        var depth = 0;
        var inString = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var character = arguments[index];
            if (character == '"' && (index == 0 || arguments[index - 1] != '\\'))
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;
            if (character == '(') depth++;
            else if (character == ')' && depth > 0) depth--;
            else if (character == ',' && depth == 0) count++;
        }

        return count;
    }

    private static int FindMatchingParenthesis(string source, int openParen)
    {
        var depth = 0;
        var inString = false;
        for (var index = openParen; index < source.Length; index++)
        {
            var character = source[index];
            if (character == '"' && (index == 0 || source[index - 1] != '\\'))
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;
            if (character == '(') depth++;
            else if (character == ')' && --depth == 0) return index;
        }

        return -1;
    }

    private static int LineNumber(string source, int index)
        => 1 + source[..index].Count(character => character == '\n');

    private static string ComposeSource(
        string profile,
        IReadOnlyList<ComposedFunction> functions,
        IReadOnlyList<JsonObject> variables,
        IReadOnlyList<JsonObject> triggers,
        IReadOnlyDictionary<string, string> handlers,
        IReadOnlyList<JsonObject> regions,
        IReadOnlyDictionary<string, JsonObject> regionBindings,
        JsonArray teams,
        JsonArray? metadata,
        JsonArray? players,
        JsonArray? forces)
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
        foreach (var function in functions)
        {
            builder.AppendLine($"// MCP function: {function.Name} (module: {function.ModuleId}; {function.ModulePath})");
            builder.AppendLine(function.Source);
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
        RenderCameraBounds(builder, metadata);
        builder.AppendLine("    call HTW_MCP_InitializeVariables()");
        builder.AppendLine("    call HTW_MCP_Bootstrap()");
        builder.AppendLine("    call HTW_MCP_RegisterTriggers()");
        builder.AppendLine("    call HTW_MCP_RunInitializationTriggers()");
        builder.AppendLine("endfunction");
        builder.AppendLine();
        RenderMapConfig(builder, profile, metadata, players, forces, teams);
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void RenderCameraBounds(StringBuilder builder, JsonArray? metadata)
    {
        var bounds = ConfigMetadataObject(metadata, "camera_bounds");
        if (bounds is null) return;

        var playableArea = ConfigMetadataObject(metadata, "playable_map_area");
        var left = ConfigNumber(bounds, "left", "camera_bounds.left");
        var bottom = ConfigNumber(bounds, "bottom", "camera_bounds.bottom");
        var right = ConfigNumber(bounds, "right", "camera_bounds.right");
        var top = ConfigNumber(bounds, "top", "camera_bounds.top");
        if (left >= right || bottom >= top) throw new EngineException("INVALID_ARGUMENT", "Canonical camera_bounds must describe a positive area.");

        const double tileSize = 128.0;
        var terrainWidth = playableArea is null ? right - left : ConfigNumber(playableArea, "width", "playable_map_area.width") * tileSize;
        var terrainHeight = playableArea is null ? top - bottom : ConfigNumber(playableArea, "height", "playable_map_area.height") * tileSize;
        if (terrainWidth <= 0 || terrainHeight <= 0) throw new EngineException("INVALID_ARGUMENT", "Canonical playable_map_area must have positive dimensions.");
        var centerX = (left + right) / 2.0;
        var centerY = (bottom + top) / 2.0;
        var terrainLeft = centerX - terrainWidth / 2.0;
        var terrainBottom = centerY - terrainHeight / 2.0;
        var terrainRight = centerX + terrainWidth / 2.0;
        var terrainTop = centerY + terrainHeight / 2.0;

        builder.AppendLine(
            $"    call SetCameraBounds({JassReal(terrainLeft)} + GetCameraMargin(CAMERA_MARGIN_LEFT), {JassReal(terrainBottom)} + GetCameraMargin(CAMERA_MARGIN_BOTTOM), {JassReal(terrainRight)} - GetCameraMargin(CAMERA_MARGIN_RIGHT), {JassReal(terrainTop)} - GetCameraMargin(CAMERA_MARGIN_TOP), {JassReal(terrainLeft)} + GetCameraMargin(CAMERA_MARGIN_LEFT), {JassReal(terrainTop)} - GetCameraMargin(CAMERA_MARGIN_TOP), {JassReal(terrainRight)} - GetCameraMargin(CAMERA_MARGIN_RIGHT), {JassReal(terrainBottom)} + GetCameraMargin(CAMERA_MARGIN_BOTTOM))");
        if (string.Equals(ConfigMetadataValue(metadata, "tileset"), "IcecrownGlacier", StringComparison.Ordinal))
        {
            builder.AppendLine("    call SetDayNightModels(\"Environment\\\\DNC\\\\DNCLordaeron\\\\DNCLordaeronTerrain\\\\DNCLordaeronTerrain.mdl\", \"Environment\\\\DNC\\\\DNCLordaeron\\\\DNCLordaeronUnit\\\\DNCLordaeronUnit.mdl\")");
            builder.AppendLine("    call NewSoundEnvironment(\"Default\")");
            builder.AppendLine("    call SetAmbientDaySound(\"IceCrownDay\")");
            builder.AppendLine("    call SetAmbientNightSound(\"IceCrownNight\")");
            builder.AppendLine("    call SetMapMusic(\"Music\", true, 0)");
        }
        builder.AppendLine("    call InitBlizzard()");
    }

    private static void RenderMapConfig(StringBuilder builder, string profile, JsonArray? metadata, JsonArray? players, JsonArray? forces, JsonArray teams)
    {
        var configPlayers = ReadConfigPlayers(profile, players);
        var configForces = ReadConfigForces(forces, teams, configPlayers);
        var teamByPlayer = configForces
            .SelectMany(force => force.PlayerIds.Select(playerId => (playerId, force.Index)))
            .ToDictionary(value => value.playerId, value => value.Index);
        var title = ConfigMetadataValue(metadata, "title");
        var description = ConfigMetadataValue(metadata, "description");

        builder.AppendLine("// Map Configuration");
        builder.AppendLine("function config takes nothing returns nothing");
        if (title is not null) builder.AppendLine($"    call SetMapName({Quote(title)})");
        if (description is not null) builder.AppendLine($"    call SetMapDescription({Quote(description)})");
        builder.AppendLine($"    call SetPlayers({configPlayers.Count})");
        // Warcraft III's generated map scripts use the player count as the
        // maximum team count, even when custom forces group those players.
        builder.AppendLine($"    call SetTeams({configPlayers.Count})");
        builder.AppendLine("    call SetGamePlacement(MAP_PLACEMENT_TEAMS_TOGETHER)");
        builder.AppendLine();

        foreach (var player in configPlayers)
        {
            var startIndex = player.Id - 1;
            builder.AppendLine($"    // Player {player.Id}");
            builder.AppendLine($"    call DefineStartLocation({startIndex}, {JassReal(player.StartX)}, {JassReal(player.StartY)})");
        }

        builder.AppendLine();
        builder.AppendLine("    // Player setup");
        foreach (var player in configPlayers)
        {
            var playerIndex = player.Id - 1;
            builder.AppendLine($"    call SetPlayerStartLocation(Player({playerIndex}), {playerIndex})");
            if (player.FixedStartPosition) builder.AppendLine($"    call ForcePlayerStartLocation(Player({playerIndex}), {playerIndex})");
            builder.AppendLine($"    call SetPlayerColor(Player({playerIndex}), ConvertPlayerColor({playerIndex}))");
            builder.AppendLine($"    call SetPlayerRacePreference(Player({playerIndex}), {player.RacePreference})");
            builder.AppendLine($"    call SetPlayerRaceSelectable(Player({playerIndex}), {(player.RaceSelectable ? "true" : "false")})");
            builder.AppendLine($"    call SetPlayerController(Player({playerIndex}), {player.Controller})");
        }

        builder.AppendLine();
        builder.AppendLine("    // Team setup");
        foreach (var player in configPlayers)
        {
            var playerIndex = player.Id - 1;
            builder.AppendLine($"    call SetPlayerTeam(Player({playerIndex}), {teamByPlayer[player.Id]})");
        }

        foreach (var force in configForces)
        {
            foreach (var sourcePlayer in force.PlayerIds)
            {
                foreach (var targetPlayer in force.PlayerIds.Where(id => id != sourcePlayer))
                {
                    var sourceIndex = sourcePlayer - 1;
                    var targetIndex = targetPlayer - 1;
                    if (force.Alliance) builder.AppendLine($"    call SetPlayerAllianceStateAllyBJ(Player({sourceIndex}), Player({targetIndex}), true)");
                    if (force.SharedVision) builder.AppendLine($"    call SetPlayerAllianceStateVisionBJ(Player({sourceIndex}), Player({targetIndex}), true)");
                    if (force.SharedUnitControl) builder.AppendLine($"    call SetPlayerAllianceStateControlBJ(Player({sourceIndex}), Player({targetIndex}), true)");
                    if (force.SharedAdvancedControl) builder.AppendLine($"    call SetPlayerAllianceStateFullControlBJ(Player({sourceIndex}), Player({targetIndex}), true)");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("    // Ally priorities");
        foreach (var player in configPlayers)
        {
            var priorities = player.Priorities;
            builder.AppendLine($"    call SetStartLocPrioCount({player.Id - 1}, {priorities.Count})");
            for (var index = 0; index < priorities.Count; index++)
            {
                var priority = priorities[index];
                builder.AppendLine($"    call SetStartLocPrio({player.Id - 1}, {index}, {priority.TargetPlayerId - 1}, {priority.Constant})");
            }
        }

        builder.AppendLine("endfunction");
    }

    private static IReadOnlyList<ConfigPlayer> ReadConfigPlayers(string profile, JsonArray? players)
    {
        if (players is null) return DefaultConfigPlayers(profile);
        if (players.Count == 0) throw new EngineException("INVALID_ARGUMENT", "Canonical map players must not be empty when composing map-bound config.");

        var result = new List<ConfigPlayer>();
        var ids = new HashSet<int>();
        foreach (var node in players)
        {
            if (node is not JsonObject player) throw new EngineException("INVALID_ARGUMENT", "Canonical map players must contain objects.");
            var id = ConfigInteger(player, "id", "player id", 1, 24);
            if (!ids.Add(id)) throw new EngineException("INVALID_ARGUMENT", $"Canonical map contains duplicate player id {id}.");
            var name = ConfigString(player, "name", $"player {id} name") ?? StringValue(player, "stored_name") ?? $"Player {id}";
            var controller = ConfigController(ConfigString(player, "controller", $"player {id} controller")!);
            var race = ConfigRace(ConfigString(player, "race", $"player {id} race")!);
            var flags = ConfigInteger(player, "flags", $"player {id} flags", 0, int.MaxValue);
            if (player["observer"] is JsonValue observer && observer.TryGetValue<bool>(out var isObserver) && isObserver)
            {
                throw new EngineException("BUILD_UNSUPPORTED", $"Canonical player {id} is marked observer; no safe war3map.j lobby representation is available.");
            }

            var start = player["start"] as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", $"Canonical player {id} requires a start object.");
            var startX = ConfigNumber(start, "x", $"player {id} start.x");
            var startY = ConfigNumber(start, "y", $"player {id} start.y");
            var fixedStart = ConfigBoolean(player, "fixed_start_position", $"player {id} fixed_start_position", (flags & 1) != 0);
            var raceSelectable = (flags & 2) != 0 || race is "Selectable" or "Random";
            var priorities = ReadConfigPriorities(player, id);
            result.Add(new ConfigPlayer(id, name, controller, RacePreference(race), raceSelectable, fixedStart, startX, startY, priorities));
        }

        result.Sort((left, right) => left.Id.CompareTo(right.Id));
        for (var index = 0; index < result.Count; index++)
        {
            if (result[index].Id != index + 1) throw new EngineException("INVALID_ARGUMENT", "Canonical map player ids must be contiguous starting at 1 for deterministic lobby setup.");
        }
        foreach (var player in result)
        {
            if (player.Priorities.Any(priority => priority.TargetPlayerId > result.Count))
            {
                throw new EngineException("INVALID_ARGUMENT", $"Canonical player {player.Id} has an ally priority for an undeclared start location.");
            }
        }
        return result;
    }

    private static IReadOnlyList<ConfigPlayer> DefaultConfigPlayers(string profile)
    {
        var count = profile switch
        {
            HtwProfileModel.MvpProfile => 4,
            HtwProfileModel.FullProfile => 12,
            _ => throw new EngineException("INVALID_ARGUMENT", $"Unknown Hero Team Wars profile '{profile}'.")
        };
        var result = new List<ConfigPlayer>(count);
        for (var id = 1; id <= count; id++)
        {
            var column = (id - 1) % 4;
            var row = (id - 1) / 4;
            var priorities = new List<ConfigPriority>();
            var partner = id % 2 == 1 ? id + 1 : id - 1;
            priorities.Add(new ConfigPriority(partner, "MAP_LOC_PRIO_HIGH"));
            result.Add(new ConfigPlayer(id, $"Player {id}", "MAP_CONTROL_USER", "RACE_PREF_NIGHTELF", false, true, column * 256, row * 256, priorities));
        }
        return result;
    }

    private static IReadOnlyList<ConfigForce> ReadConfigForces(JsonArray? forces, JsonArray teams, IReadOnlyList<ConfigPlayer> players)
    {
        var result = forces is null ? DefaultConfigForces(teams) : ParseConfigForces(forces);
        if (result.Count == 0) throw new EngineException("INVALID_ARGUMENT", "Canonical map forces must not be empty when composing map-bound config.");

        var playerIds = players.Select(player => player.Id).ToHashSet();
        var assigned = new Dictionary<int, int>();
        foreach (var force in result)
        {
            foreach (var playerId in force.PlayerIds)
            {
                if (!playerIds.Contains(playerId)) throw new EngineException("INVALID_ARGUMENT", $"Force {force.Index} references unknown player {playerId}.");
                if (!assigned.TryAdd(playerId, force.Index)) throw new EngineException("INVALID_ARGUMENT", $"Player {playerId} is assigned to multiple map forces.");
            }
        }
        foreach (var playerId in playerIds)
        {
            if (!assigned.ContainsKey(playerId)) throw new EngineException("INVALID_ARGUMENT", $"Player {playerId} is not assigned to a map force.");
        }
        return result.OrderBy(force => force.Index).ToArray();
    }

    private static IReadOnlyList<ConfigForce> ParseConfigForces(JsonArray forces)
    {
        var result = new List<ConfigForce>();
        var indexes = new HashSet<int>();
        foreach (var node in forces)
        {
            if (node is not JsonObject force) throw new EngineException("INVALID_ARGUMENT", "Canonical map forces must contain objects.");
            var index = ConfigInteger(force, "index", "force index", 0, 23);
            if (!indexes.Add(index)) throw new EngineException("INVALID_ARGUMENT", $"Canonical map contains duplicate force index {index}.");
            var playerIdsNode = force["player_ids"] as JsonArray ?? throw new EngineException("INVALID_ARGUMENT", $"Force {index} requires a player_ids array.");
            var playerIds = playerIdsNode.Select(value => value is JsonValue number && number.TryGetValue<int>(out var id) ? id : throw new EngineException("INVALID_ARGUMENT", $"Force {index} player_ids must contain integers.")).ToArray();
            if (playerIds.Length == 0 || playerIds.Distinct().Count() != playerIds.Length) throw new EngineException("INVALID_ARGUMENT", $"Force {index} must contain unique player ids.");
            var flags = ConfigInteger(force, "flags", $"force {index} flags", 0, int.MaxValue);
            var alliance = ConfigBoolean(force, "alliance", $"force {index} alliance", (flags & 1) != 0);
            var sharedVision = ConfigBoolean(force, "shared_vision", $"force {index} shared_vision", (flags & 8) != 0);
            var sharedControl = ConfigBoolean(force, "shared_unit_control", $"force {index} shared_unit_control", (flags & 16) != 0);
            var sharedAdvancedControl = (flags & 32) != 0;
            result.Add(new ConfigForce(index, playerIds.OrderBy(id => id).ToArray(), alliance, sharedVision, sharedControl, sharedAdvancedControl));
        }
        return result;
    }

    private static IReadOnlyList<ConfigForce> DefaultConfigForces(JsonArray teams)
    {
        var result = new List<ConfigForce>();
        foreach (var node in teams.OfType<JsonObject>().OrderBy(team => ConfigString(team, "id", "team id"), StringComparer.Ordinal))
        {
            var index = ConfigInteger(node, "force_index", "team force_index", 0, 23);
            var members = node["member_player_ids"] as JsonArray ?? throw new EngineException("INVALID_ARGUMENT", $"Team force {index} requires member_player_ids.");
            var playerIds = members.Select(value => value is JsonValue number && number.TryGetValue<int>(out var id) ? id : throw new EngineException("INVALID_ARGUMENT", $"Team force {index} member_player_ids must contain integers.")).Distinct().OrderBy(id => id).ToArray();
            if (playerIds.Length == 0) throw new EngineException("INVALID_ARGUMENT", $"Team force {index} must contain at least one player.");
            result.Add(new ConfigForce(index, playerIds, true, true, false, false));
        }
        return result;
    }

    private static IReadOnlyList<ConfigPriority> ReadConfigPriorities(JsonObject player, int playerId)
    {
        var priorities = new List<ConfigPriority>();
        AddConfigPriorities(priorities, playerId, player["ally_high_priority_mask"], "MAP_LOC_PRIO_HIGH", "ally_high_priority_mask");
        AddConfigPriorities(priorities, playerId, player["ally_low_priority_mask"], "MAP_LOC_PRIO_LOW", "ally_low_priority_mask");
        AddConfigPriorities(priorities, playerId, player["enemy_high_priority_mask"], "MAP_LOC_PRIO_HIGH", "enemy_high_priority_mask");
        AddConfigPriorities(priorities, playerId, player["enemy_low_priority_mask"], "MAP_LOC_PRIO_LOW", "enemy_low_priority_mask");
        return priorities;
    }

    private static void AddConfigPriorities(List<ConfigPriority> priorities, int playerId, JsonNode? node, string constant, string field)
    {
        if (node is null) return;
        if (node is not JsonValue value || !value.TryGetValue<int>(out var mask) || mask < 0) throw new EngineException("INVALID_ARGUMENT", $"Player {playerId} {field} must be a non-negative integer.");
        for (var bit = 0; bit < 31; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;
            var targetPlayerId = bit + 1;
            priorities.Add(new ConfigPriority(targetPlayerId, constant));
        }
    }

    private static string? ConfigMetadataValue(JsonArray? metadata, string field)
    {
        foreach (var node in metadata?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            if (!string.Equals(StringValue(node, "field"), field, StringComparison.Ordinal)) continue;
            return node["value"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }
        return null;
    }

    private static JsonObject? ConfigMetadataObject(JsonArray? metadata, string field)
        => metadata?.OfType<JsonObject>()
            .FirstOrDefault(node => string.Equals(StringValue(node, "field"), field, StringComparison.Ordinal))?["value"] as JsonObject;

    private static string ConfigController(string value) => value switch
    {
        "None" => "MAP_CONTROL_NONE",
        "User" => "MAP_CONTROL_USER",
        "Computer" => "MAP_CONTROL_COMPUTER",
        "Neutral" => "MAP_CONTROL_NEUTRAL",
        "Rescuable" => "MAP_CONTROL_RESCUABLE",
        _ => throw new EngineException("INVALID_ARGUMENT", $"Unsupported map player controller '{value}'.")
    };

    private static string ConfigRace(string value) => value switch
    {
        "Selectable" or "Random" => value,
        "Human" or "Orc" or "Undead" or "NightElf" => value,
        _ => throw new EngineException("INVALID_ARGUMENT", $"Unsupported map player race '{value}'.")
    };

    private static string RacePreference(string race) => race switch
    {
        "Selectable" or "Random" => "RACE_PREF_RANDOM",
        "Human" => "RACE_PREF_HUMAN",
        "Orc" => "RACE_PREF_ORC",
        "Undead" => "RACE_PREF_UNDEAD",
        "NightElf" => "RACE_PREF_NIGHTELF",
        _ => throw new EngineException("INVALID_ARGUMENT", $"Unsupported map player race '{race}'.")
    };

    private static string? ConfigString(JsonObject value, string property, string context)
    {
        if (value[property] is null) return null;
        if (value[property] is JsonValue node && node.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) return text;
        throw new EngineException("INVALID_ARGUMENT", $"{context} must be a non-empty string.");
    }

    private static int ConfigInteger(JsonObject value, string property, string context, int minimum, int maximum)
    {
        if (value[property] is JsonValue node && node.TryGetValue<int>(out var number) && number >= minimum && number <= maximum) return number;
        throw new EngineException("INVALID_ARGUMENT", $"{context} must be an integer between {minimum} and {maximum}.");
    }

    private static double ConfigNumber(JsonObject value, string property, string context)
    {
        if (value[property] is JsonValue node && node.TryGetValue<double>(out var number) && double.IsFinite(number)) return number;
        if (value[property] is JsonValue integer && integer.TryGetValue<int>(out var intNumber)) return intNumber;
        if (value[property] is JsonValue single && single.TryGetValue<float>(out var floatNumber) && float.IsFinite(floatNumber)) return floatNumber;
        throw new EngineException("INVALID_ARGUMENT", $"{context} must be a finite number.");
    }

    private static bool ConfigBoolean(JsonObject value, string property, string context, bool fallback)
    {
        if (value[property] is null) return fallback;
        if (value[property] is JsonValue node && node.TryGetValue<bool>(out var result)) return result;
        throw new EngineException("INVALID_ARGUMENT", $"{context} must be a boolean.");
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

    private sealed record ComposedFunction(string Name, string ModuleId, string ModulePath, string Source);
    private sealed record ConfigPlayer(
        int Id,
        string Name,
        string Controller,
        string RacePreference,
        bool RaceSelectable,
        bool FixedStartPosition,
        double StartX,
        double StartY,
        IReadOnlyList<ConfigPriority> Priorities);
    private sealed record ConfigForce(
        int Index,
        IReadOnlyList<int> PlayerIds,
        bool Alliance,
        bool SharedVision,
        bool SharedUnitControl,
        bool SharedAdvancedControl);
    private sealed record ConfigPriority(int TargetPlayerId, string Constant);
}
