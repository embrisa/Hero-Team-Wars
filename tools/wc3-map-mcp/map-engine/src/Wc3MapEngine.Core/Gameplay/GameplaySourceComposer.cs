using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Wc3MapEngine.Core.Scripts;

namespace Wc3MapEngine.Core.Gameplay;

/// <summary>
/// Deterministically composes the MCP-owned JASS entry point from a project
/// manifest.  The composer is intentionally independent of map structure:
/// map components are consumed by generated registries, never mutated here.
/// </summary>
public static class GameplaySourceComposer
{
    public const string ComposerVersion = "mcp-jass-composer-1.0";
    private const int MaxManifestBytes = 2 * 1024 * 1024;
    private const int MaxModuleBytes = 2 * 1024 * 1024;
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ModuleIdentifier = new("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Function = new("(?im)^\\s*function\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s+takes\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Main = new("(?im)^\\s*function\\s+main\\s+takes\\s+nothing\\s+returns\\s+nothing\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> VariableTypes = new(StringComparer.OrdinalIgnoreCase) { "integer", "real", "boolean", "string", "handle" };

    public static JsonObject Compose(string manifestPath, string? requestedProfile = null)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath)) throw new EngineException("FILE_NOT_FOUND", $"Gameplay manifest does not exist: {fullManifestPath}");
        var manifestBytes = File.ReadAllBytes(fullManifestPath);
        if (manifestBytes.Length > MaxManifestBytes) throw new EngineException("INVALID_ARGUMENT", "Gameplay manifest exceeds the 2 MiB safety limit.");
        JsonObject manifest;
        try
        {
            manifest = JsonNode.Parse(Encoding.UTF8.GetString(manifestBytes)) as JsonObject
                ?? throw new EngineException("INVALID_JSON", "Gameplay manifest root must be an object.");
        }
        catch (JsonException exception)
        {
            throw new EngineException("INVALID_JSON", $"Gameplay manifest is not valid JSON: {exception.Message}", false, exception);
        }

        var profile = requestedProfile ?? manifest["profile"]?.GetValue<string>() ?? "mvp_2arena";
        if (profile is not ("mvp_2arena" or "full_6team" or "gui_compatible")) throw new EngineException("INVALID_ARGUMENT", $"Unsupported gameplay profile '{profile}'.");
        if (string.Equals(profile, "gui_compatible", StringComparison.OrdinalIgnoreCase)) throw new EngineException("UNSUPPORTED_OPERATION", "The GUI-compatible trigger path is gated pending exact WTG/WCT version fixtures and editor evidence; use mcp-native JASS.");
        if (manifest["schema_version"]?.GetValue<string>() is { } schema && schema != "1.0") throw new EngineException("INVALID_ARGUMENT", $"Unsupported gameplay manifest schema '{schema}'.");
        if (manifest["profiles"] is JsonObject profiles && profiles[profile] is not JsonObject) throw new EngineException("INVALID_ARGUMENT", $"Gameplay manifest has no definition for profile '{profile}'.");

        var modules = ReadModules(manifest, fullManifestPath);
        var orderedModules = TopologicalOrder(modules);
        var variables = ReadVariables(manifest);
        var triggers = ReadTriggers(manifest);
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var sourceParts = new List<string>();
        foreach (var module in orderedModules)
        {
            foreach (Match match in Function.Matches(module.Source))
            {
                var name = match.Groups["name"].Value;
                if (name.Equals("main", StringComparison.OrdinalIgnoreCase)) throw new EngineException("INVALID_ARGUMENT", $"Module '{module.Id}' defines main; only the generated entry point may define main.");
                if (!symbols.Add(name)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate JASS function symbol '{name}'.");
            }
            sourceParts.Add($"// MCP module: {module.Id} ({module.Path})\n{module.Source.TrimEnd()}\n");
        }

        if (!symbols.Contains("HTW_MCP_Bootstrap")) throw new EngineException("INVALID_ARGUMENT", "Gameplay modules must define HTW_MCP_Bootstrap as the one-time initialization entry point.");

        var generatedFunctions = new HashSet<string>(symbols, StringComparer.Ordinal);
        foreach (var trigger in triggers)
        {
            if (!generatedFunctions.Contains(trigger.Handler)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{trigger.Id}' references unresolved handler '{trigger.Handler}'.");
        }

        var source = ComposeSource(profile, variables, triggers, sourceParts);
        try
        {
            ScriptOwnership.ValidateMcpOwnedJass("war3map.j", source);
        }
        catch (InvalidDataException exception)
        {
            throw new EngineException("PARSE_FAILED", exception.Message, false, exception);
        }

        var moduleResults = new JsonArray(orderedModules.Select(module => (JsonNode)new JsonObject
        {
            ["id"] = module.Id,
            ["path"] = module.Path,
            ["enabled"] = module.Enabled,
            ["dependencies"] = new JsonArray(module.Dependencies.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["source_sha256"] = Hashing.Sha256(Encoding.UTF8.GetBytes(module.Source)),
            ["source_bytes"] = Encoding.UTF8.GetByteCount(module.Source)
        }).ToArray());
        var triggerManifest = new JsonArray(triggers.Select(trigger => (JsonNode)new JsonObject
        {
            ["id"] = trigger.Id,
            ["handler"] = trigger.Handler,
            ["enabled"] = trigger.Enabled,
            ["folder"] = trigger.Folder
        }).ToArray());
        var variableManifest = new JsonArray(variables.Select(variable => (JsonNode)new JsonObject
        {
            ["id"] = variable.Id,
            ["name"] = variable.Name,
            ["type"] = variable.Type,
            ["initial"] = variable.Initial
        }).ToArray());
        var manifestHash = Hashing.Sha256(manifestBytes);
        var triggerHash = Hashing.Sha256(Encoding.UTF8.GetBytes(triggerManifest.ToJsonString()));
        var variableHash = Hashing.Sha256(Encoding.UTF8.GetBytes(variableManifest.ToJsonString()));
        return new JsonObject
        {
            ["schema_version"] = "1.0",
            ["composer_version"] = ComposerVersion,
            ["mode"] = "mcp_native_jass",
            ["profile"] = profile,
            ["profile_spec"] = (manifest["profiles"] as JsonObject)?[profile]?.DeepClone(),
            ["manifest_path"] = fullManifestPath,
            ["manifest_sha256"] = manifestHash,
            ["module_order"] = new JsonArray(orderedModules.Select(x => (JsonNode?)JsonValue.Create(x.Id)).ToArray()),
            ["modules"] = moduleResults,
            ["triggers"] = triggerManifest,
            ["variables"] = variableManifest,
            ["trigger_manifest_sha256"] = triggerHash,
            ["variable_manifest_sha256"] = variableHash,
            ["function_count"] = Function.Matches(source).Count,
            ["main_count"] = Main.Matches(source).Count,
            ["source_bytes"] = Encoding.UTF8.GetByteCount(source),
            ["source_sha256"] = Hashing.Sha256(Encoding.UTF8.GetBytes(source)),
            ["static_validation"] = new JsonObject
            {
                ["status"] = "passed",
                ["evidence_level"] = "static_only",
                ["parser"] = "War3Net.CodeAnalysis.Jass",
                ["runtime_verified"] = false
            },
            ["source"] = source
        };
    }

    private static string ComposeSource(string profile, IReadOnlyList<Variable> variables, IReadOnlyList<Trigger> triggers, IReadOnlyList<string> sourceParts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// Generated by wc3-map-mcp; do not edit in World Editor.");
        builder.AppendLine($"// profile: {profile}");
        builder.AppendLine($"// composer: {ComposerVersion}");
        if (variables.Count > 0)
        {
            builder.AppendLine("globals");
            foreach (var variable in variables) builder.AppendLine($"    {variable.Type} {variable.Name}");
            builder.AppendLine("endglobals");
            builder.AppendLine();
        }

        foreach (var sourcePart in sourceParts) builder.Append(sourcePart).AppendLine();
        builder.AppendLine("function HTW_MCP_RunTriggers takes nothing returns nothing");
        foreach (var trigger in triggers.Where(x => x.Enabled)) builder.AppendLine($"    call {trigger.Handler}()");
        builder.AppendLine("endfunction");
        builder.AppendLine();
        builder.AppendLine("function main takes nothing returns nothing");
        builder.AppendLine("    call HTW_MCP_Bootstrap()");
        builder.AppendLine("    call HTW_MCP_RunTriggers()");
        builder.AppendLine("endfunction");
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static List<Module> ReadModules(JsonObject manifest, string manifestPath)
    {
        if (manifest["modules"] is not JsonArray values || values.Count == 0) throw new EngineException("INVALID_ARGUMENT", "Gameplay manifest requires a non-empty modules array.");
        var modules = new List<Module>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in values)
        {
            if (node is not JsonObject value) throw new EngineException("INVALID_ARGUMENT", "Every gameplay module must be an object.");
            var id = RequiredModuleIdentifier(value, "id");
            if (!ids.Add(id)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate gameplay module '{id}'.");
            var enabled = value["enabled"]?.GetValue<bool>() ?? true;
            var dependencies = ReadModuleStringArray(value["dependencies"], $"module {id} dependencies");
            if (dependencies.Contains(id, StringComparer.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"Module '{id}' cannot depend on itself.");
            var path = value["path"]?.GetValue<string>();
            var source = value["source"]?.GetValue<string>();
            if (enabled && string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(path)) throw new EngineException("INVALID_ARGUMENT", $"Enabled module '{id}' requires path or source.");
            if (!enabled) source ??= string.Empty;
            if (string.IsNullOrWhiteSpace(source))
            {
                var fullPath = SafeRelativePath(Path.GetDirectoryName(manifestPath)!, path!, $"module {id}");
                var bytes = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : throw new EngineException("FILE_NOT_FOUND", $"Gameplay module does not exist: {fullPath}");
                if (bytes.Length > MaxModuleBytes) throw new EngineException("INVALID_ARGUMENT", $"Gameplay module '{id}' exceeds the 2 MiB safety limit.");
                try { source = new UTF8Encoding(false, true).GetString(bytes); }
                catch (Exception exception) { throw new EngineException("PARSE_FAILED", $"Gameplay module '{id}' is not valid UTF-8.", false, exception); }
            }
            source = NormalizeSource(source!);
            if (enabled && source.Length == 0) throw new EngineException("INVALID_ARGUMENT", $"Enabled module '{id}' is empty.");
            modules.Add(new Module(id, path ?? $"<inline:{id}>", enabled, dependencies, enabled ? source : string.Empty));
        }

        var available = modules.Where(x => x.Enabled).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var module in modules.Where(x => x.Enabled))
        {
            foreach (var dependency in module.Dependencies)
            {
                if (!available.Contains(dependency)) throw new EngineException("INVALID_ARGUMENT", $"Module '{module.Id}' depends on missing or disabled module '{dependency}'.");
            }
        }
        return modules.Where(x => x.Enabled).ToList();
    }

    private static List<Module> TopologicalOrder(IReadOnlyList<Module> modules)
    {
        var byId = modules.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var output = new List<Module>();
        void Visit(string id)
        {
            state.TryGetValue(id, out var current);
            if (current == 1) throw new EngineException("INVALID_ARGUMENT", $"Gameplay module dependency cycle includes '{id}'.");
            if (current == 2) return;
            state[id] = 1;
            foreach (var dependency in byId[id].Dependencies.OrderBy(x => x, StringComparer.Ordinal)) Visit(dependency);
            state[id] = 2;
            output.Add(byId[id]);
        }
        foreach (var id in byId.Keys.OrderBy(x => x, StringComparer.Ordinal)) Visit(id);
        return output;
    }

    private static List<Variable> ReadVariables(JsonObject manifest)
    {
        if (manifest["variables"] is not JsonArray values) return new List<Variable>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Variable>();
        foreach (var node in values)
        {
            if (node is not JsonObject value) throw new EngineException("INVALID_ARGUMENT", "Every gameplay variable must be an object.");
            var id = RequiredIdentifier(value, "id");
            var name = RequiredIdentifier(value, "name");
            var type = value["type"]?.GetValue<string>() ?? "integer";
            if (!VariableTypes.Contains(type)) throw new EngineException("INVALID_ARGUMENT", $"Variable '{id}' uses unsupported JASS type '{type}'.");
            if (!ids.Add(id) || !names.Add(name)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate gameplay variable identity '{id}' or name '{name}'.");
            result.Add(new Variable(id, name, type.ToLowerInvariant(), value["initial"]?.DeepClone()));
        }
        return result.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    }

    private static List<Trigger> ReadTriggers(JsonObject manifest)
    {
        if (manifest["triggers"] is not JsonArray values) return new List<Trigger>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Trigger>();
        foreach (var node in values)
        {
            if (node is not JsonObject value) throw new EngineException("INVALID_ARGUMENT", "Every gameplay trigger must be an object.");
            var id = RequiredIdentifier(value, "id");
            var handler = RequiredIdentifier(value, "handler");
            if (!ids.Add(id)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate gameplay trigger '{id}'.");
            result.Add(new Trigger(id, handler, value["enabled"]?.GetValue<bool>() ?? true, value["folder"]?.GetValue<string>() ?? string.Empty));
        }
        return result.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    }

    private static string RequiredIdentifier(JsonObject value, string property)
    {
        var text = value[property]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text) || !Identifier.IsMatch(text)) throw new EngineException("INVALID_ARGUMENT", $"Property '{property}' must be a valid JASS/MCP identifier.");
        return text;
    }

    private static string RequiredModuleIdentifier(JsonObject value, string property)
    {
        var text = value[property]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text) || !ModuleIdentifier.IsMatch(text)) throw new EngineException("INVALID_ARGUMENT", $"Property '{property}' must be a valid module identifier.");
        return text;
    }

    private static List<string> ReadStringArray(JsonNode? node, string context)
    {
        if (node is null) return new List<string>();
        if (node is not JsonArray values) throw new EngineException("INVALID_ARGUMENT", $"{context} must be an array.");
        var result = values.Select(value => value?.GetValue<string>() ?? throw new EngineException("INVALID_ARGUMENT", $"{context} contains a non-string value.")).ToList();
        if (result.Any(x => !Identifier.IsMatch(x))) throw new EngineException("INVALID_ARGUMENT", $"{context} contains an invalid identifier.");
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    private static List<string> ReadModuleStringArray(JsonNode? node, string context)
    {
        if (node is null) return new List<string>();
        if (node is not JsonArray values) throw new EngineException("INVALID_ARGUMENT", $"{context} must be an array.");
        var result = values.Select(value => value?.GetValue<string>() ?? throw new EngineException("INVALID_ARGUMENT", $"{context} contains a non-string value.")).ToList();
        if (result.Any(x => !ModuleIdentifier.IsMatch(x))) throw new EngineException("INVALID_ARGUMENT", $"{context} contains an invalid module identifier.");
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string SafeRelativePath(string root, string relative, string context)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal) || relative.Contains(':')) throw new EngineException("INVALID_ARGUMENT", $"{context} path must be project-relative and traversal-free.");
        return Path.GetFullPath(Path.Combine(root, relative));
    }

    private static string NormalizeSource(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + "\n";

    private sealed record Module(string Id, string Path, bool Enabled, IReadOnlyList<string> Dependencies, string Source);
    private sealed record Variable(string Id, string Name, string Type, JsonNode? Initial);
    private sealed record Trigger(string Id, string Handler, bool Enabled, string Folder);
}
