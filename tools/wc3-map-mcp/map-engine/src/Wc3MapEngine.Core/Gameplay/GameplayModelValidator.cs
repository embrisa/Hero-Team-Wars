using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Wc3MapEngine.Contracts;

namespace Wc3MapEngine.Core.Gameplay;

/// <summary>
/// Validates the source-owned gameplay model shared by the composer and typed
/// transaction operations.  This deliberately does not parse GUI trigger
/// members; those remain an explicit compatibility-gated mode.
/// </summary>
public static class GameplayModelValidator
{
    public const string NativeMode = "mcp_native_jass";
    public const string EditorMode = "editor_compatible";

    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ModuleIdentifier = new("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Rawcode = new("^[\\x20-\\x7E]{4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Function = new("(?im)^\\s*function\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s+takes\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> VariableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "integer", "real", "boolean", "string", "handle", "timer", "trigger", "unit", "group", "region", "rect", "player", "force"
    };
    private static readonly HashSet<string> EventTypes = new(StringComparer.Ordinal)
    {
        "map_initialization", "periodic_timer", "elapsed_time", "player_chat", "unit_death", "region_entry", "player_state_change", "custom_event"
    };
    private static readonly HashSet<string> ConditionTypes = new(StringComparer.Ordinal)
    {
        "always", "boolean_variable", "integer_compare", "real_compare", "function"
    };
    private static readonly HashSet<string> ActionTypes = new(StringComparer.Ordinal)
    {
        "set_variable", "branch", "create_timer", "unit_operation", "group_operation", "message", "phase_transition", "call_function", "return"
    };
    private static readonly HashSet<string> ComparisonOperators = new(StringComparer.Ordinal)
    {
        "equal", "not_equal", "less", "less_equal", "greater", "greater_equal"
    };

    public static string Hash(JsonNode? value)
        => Wc3MapEngine.Core.Hashing.Sha256(Encoding.UTF8.GetBytes(value?.ToJsonString(EngineProtocol.JsonOptions) ?? "null"));

    public static void ValidateCollections(JsonObject root, bool requireModuleSources = false)
    {
        var modules = Collection(root, "gameplay_modules");
        var variables = Collection(root, "gameplay_variables");
        var triggers = Collection(root, "gameplay_triggers");

        if (modules.Any(item => item is not JsonObject) || variables.Any(item => item is not JsonObject) || triggers.Any(item => item is not JsonObject)) throw new EngineException("INVALID_ARGUMENT", "Gameplay module, variable, and trigger collections may contain only objects.");
        foreach (var module in modules.OfType<JsonObject>())
        {
            ValidateModule(module, requireModuleSources);
        }

        var moduleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in modules.OfType<JsonObject>())
        {
            if (!moduleIds.Add(RequiredString(module, "id"))) throw new EngineException("INVALID_ARGUMENT", "Duplicate gameplay module identity.");
        }
        ValidateModuleDependencies(modules, moduleIds);
        var functionOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules.OfType<JsonObject>())
        {
            if (StringValue(module, "source") is not { } source) continue;
            foreach (Match match in Function.Matches(source))
            {
                var function = match.Groups["name"].Value;
                if (!functionOwners.TryAdd(function, RequiredString(module, "id"))) throw new EngineException("INVALID_ARGUMENT", $"Duplicate gameplay function identity '{function}' in modules '{functionOwners[function]}' and '{RequiredString(module, "id")}'.");
            }
        }

        var variableIds = new HashSet<string>(StringComparer.Ordinal);
        var variableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in variables.OfType<JsonObject>())
        {
            ValidateVariable(variable);
            var id = RequiredString(variable, "id");
            var name = RequiredString(variable, "name");
            if (!variableIds.Add(id) || !variableNames.Add(name)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate gameplay variable identity '{id}' or name '{name}'.");
        }

        var triggerIds = new HashSet<string>(StringComparer.Ordinal);
        var triggerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var handlers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trigger in triggers.OfType<JsonObject>())
        {
            ValidateTrigger(trigger);
            var id = RequiredString(trigger, "id");
            var name = RequiredString(trigger, "name");
            if (!triggerIds.Add(id) || !triggerNames.Add(name)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate gameplay trigger identity '{id}' or name '{name}'.");
            var handler = trigger["handler_name"] is null ? $"HTW_Trigger_{id}" : RequiredIdentifier(trigger, "handler_name");
            if (!handlers.Add(handler)) throw new EngineException("INVALID_ARGUMENT", $"Duplicate generated trigger handler '{handler}'.");
            if (trigger["handler_name"] is null && functionOwners.ContainsKey(handler)) throw new EngineException("INVALID_ARGUMENT", $"Generated trigger handler '{handler}' shadows module function '{functionOwners[handler]}'.");
        }

        ValidateReferences(root, modules, variables, triggers);
        ValidateRegionRoles(root);
        ValidateTypedArguments(variables, triggers);
    }

    public static void ValidateModule(JsonObject module, bool requireSource)
    {
        EnsureAllowed(module, "id", "path", "source", "source_sha256", "enabled", "dependencies", "public_symbols", "provenance", "capability");
        var id = RequiredModuleIdentifier(module, "id");
        var enabled = module["enabled"] is null || BooleanValue(module["enabled"]!, "enabled");
        if (module["path"] is not null) ValidateRelativePath(module["path"]!, $"module {id} path");
        if (module["source"] is not null) RequireString(module["source"]!, "source");
        if (module["source_sha256"] is not null) RequireHash(module["source_sha256"]!, $"module {id} source_sha256");
        if (module["dependencies"] is not null) ValidateStringArray(module["dependencies"]!, $"module {id} dependencies", ModuleIdentifier);
        if (module["public_symbols"] is not null) ValidateStringArray(module["public_symbols"]!, $"module {id} public_symbols", Identifier);
        if (enabled && requireSource && string.IsNullOrWhiteSpace(StringValue(module, "source"))) throw new EngineException("INVALID_ARGUMENT", $"Enabled gameplay module '{id}' must carry source text in the canonical transaction model.");
        if (enabled && string.IsNullOrWhiteSpace(StringValue(module, "source")) && string.IsNullOrWhiteSpace(StringValue(module, "path"))) throw new EngineException("INVALID_ARGUMENT", $"Enabled gameplay module '{id}' requires path or source.");

        if (StringValue(module, "source") is { } source)
        {
            var symbols = Function.Matches(source).Select(x => x.Groups["name"].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (symbols.Contains("main")) throw new EngineException("INVALID_ARGUMENT", $"Gameplay module '{id}' may not define main; only the generated entry point may define main.");
            foreach (var publicSymbol in Strings(module["public_symbols"]))
            {
                if (!symbols.Contains(publicSymbol)) throw new EngineException("INVALID_ARGUMENT", $"Module '{id}' declares public symbol '{publicSymbol}' but its source does not define it.");
            }
            if (module["source_sha256"] is not null && !string.Equals(StringValue(module, "source_sha256"), Wc3MapEngine.Core.Hashing.Sha256(Encoding.UTF8.GetBytes(source)), StringComparison.OrdinalIgnoreCase))
            {
                throw new EngineException("SOURCE_CHANGED", $"Gameplay module '{id}' source_sha256 does not match its source text.");
            }
        }
    }

    public static void ValidateVariable(JsonObject variable)
    {
        EnsureAllowed(variable, "id", "name", "type", "array", "array_size", "initial", "default_value", "value", "dependencies", "provenance", "capability");
        _ = RequiredIdentifier(variable, "id");
        _ = RequiredIdentifier(variable, "name");
        var type = StringValue(variable, "type") ?? "integer";
        if (!VariableTypes.Contains(type)) throw new EngineException("INVALID_ARGUMENT", $"Gameplay variable '{RequiredString(variable, "id")}' uses unsupported JASS type '{type}'.");
        if (variable["array"] is not null && (variable["array"] is not JsonValue arrayNode || !arrayNode.TryGetValue<bool>(out _))) throw new EngineException("INVALID_ARGUMENT", $"Gameplay variable '{RequiredString(variable, "id")}' array must be boolean.");
        if (variable["array"]?.GetValue<bool>() == true)
        {
            if (variable["array_size"] is null) throw new EngineException("INVALID_ARGUMENT", $"Gameplay array variable '{RequiredString(variable, "id")}' requires array_size.");
            var arraySize = Integer(variable["array_size"]!, $"variable {RequiredString(variable, "id")} array_size");
            if (arraySize is < 1 or > 8191) throw new EngineException("INVALID_ARGUMENT", $"Gameplay variable '{RequiredString(variable, "id")}' array_size must be between 1 and 8191.");
            if (variable["initial"] is not null || variable["default_value"] is not null || variable["value"] is not null) throw new EngineException("INVALID_ARGUMENT", $"Gameplay array variable '{RequiredString(variable, "id")}' cannot declare a scalar initial value.");
        }
        if (variable["dependencies"] is not null) ValidateStringArray(variable["dependencies"]!, "variable dependencies", Identifier);
        foreach (var field in new[] { "initial", "default_value", "value" })
        {
            if (variable[field] is not null) ValidateTypedValue(variable[field]!, type, $"variable {RequiredString(variable, "id")} {field}");
        }
    }

    public static void ValidateTrigger(JsonObject trigger)
    {
        EnsureAllowed(trigger, "id", "name", "folder_path", "folder", "enabled", "initially_on", "events", "conditions", "actions", "references", "source_location", "handler_name", "dependencies", "editor_encoding", "provenance", "capability");
        _ = RequiredIdentifier(trigger, "id");
        if (string.IsNullOrWhiteSpace(StringValue(trigger, "name"))) throw new EngineException("INVALID_ARGUMENT", "Gameplay triggers require a non-empty exact display name.");
        var folder = StringValue(trigger, "folder_path") ?? StringValue(trigger, "folder");
        if (string.IsNullOrWhiteSpace(folder)) throw new EngineException("INVALID_ARGUMENT", $"Gameplay trigger '{RequiredString(trigger, "id")}' requires folder_path.");
        if (trigger["enabled"] is not null) _ = BooleanValue(trigger["enabled"]!, "enabled");
        if (trigger["initially_on"] is not null) _ = BooleanValue(trigger["initially_on"]!, "initially_on");
        var events = RequiredArray(trigger, "events", $"trigger {RequiredString(trigger, "id")} events");
        if (events.Count == 0) throw new EngineException("INVALID_ARGUMENT", $"Gameplay trigger '{RequiredString(trigger, "id")}' must declare at least one event.");
        foreach (var node in events)
        {
            if (node is not JsonObject) throw new EngineException("INVALID_ARGUMENT", "Every gameplay trigger event must be an object.");
            ValidateEvent(node);
        }
        foreach (var node in OptionalArray(trigger, "conditions"))
        {
            if (node is not JsonObject) throw new EngineException("INVALID_ARGUMENT", "Every gameplay condition must be an object.");
            ValidateCondition(node);
        }
        foreach (var node in OptionalArray(trigger, "actions"))
        {
            if (node is not JsonObject) throw new EngineException("INVALID_ARGUMENT", "Every gameplay action must be an object.");
            ValidateAction(node);
        }
        if (trigger["references"] is not null) ValidateReferencesObject(trigger["references"]!);
        if (trigger["source_location"] is not null) ValidateSourceLocation(trigger["source_location"]!);
        if (trigger["handler_name"] is not null) _ = RequiredIdentifier(trigger, "handler_name");
        if (trigger["editor_encoding"] is JsonObject encoding)
        {
            EnsureAllowed(encoding, "status", "version", "reason");
            if (StringValue(encoding, "status") is not ("unsupported" or "available" or "not_applicable")) throw new EngineException("INVALID_ARGUMENT", "Trigger editor_encoding.status must be unsupported, available, or not_applicable.");
        }
        if (trigger["handler_name"] is not null && OptionalArray(trigger, "actions").Count > 0)
        {
            throw new EngineException("INVALID_ARGUMENT", "A trigger with an existing handler_name may not also declare generated actions.");
        }
    }

    public static string RequiredIdentifier(JsonObject value, string property)
    {
        var text = RequiredString(value, property);
        if (!Identifier.IsMatch(text)) throw new EngineException("INVALID_ARGUMENT", $"Property '{property}' must be a valid JASS/MCP identifier.");
        return text;
    }

    public static string RequiredModuleIdentifier(JsonObject value, string property)
    {
        var text = RequiredString(value, property);
        if (!ModuleIdentifier.IsMatch(text)) throw new EngineException("INVALID_ARGUMENT", $"Property '{property}' must be a valid module identifier.");
        return text;
    }

    public static string RequiredString(JsonObject value, string property)
        => value[property] is JsonValue node && node.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new EngineException("INVALID_ARGUMENT", $"Missing required non-empty string property '{property}'.");

    public static IReadOnlyList<string> Strings(JsonNode? node)
        => node is JsonArray array
            ? array.Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw new EngineException("INVALID_ARGUMENT", "Expected an array of strings.")).ToArray()
            : Array.Empty<string>();

    public static JsonArray Collection(JsonObject root, string name)
    {
        if (root[name] is JsonArray array) return array;
        if (root[name] is not null) throw new EngineException("CAPABILITY_GATED", $"Canonical component '{name}' is not a typed gameplay collection.");
        var created = new JsonArray();
        root[name] = created;
        return created;
    }

    private static void ValidateModuleDependencies(JsonArray modules, HashSet<string> moduleIds)
    {
        var graph = modules.OfType<JsonObject>().ToDictionary(x => RequiredString(x, "id"), x => Strings(x["dependencies"]), StringComparer.Ordinal);
        foreach (var (id, dependencies) in graph)
        {
            foreach (var dependency in dependencies)
            {
                if (!moduleIds.Contains(dependency)) throw new EngineException("INVALID_ARGUMENT", $"Gameplay module '{id}' depends on missing module '{dependency}'.");
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visiting.Contains(id)) throw new EngineException("INVALID_ARGUMENT", $"Gameplay module dependency cycle includes '{id}'.");
            if (!visited.Add(id)) return;
            visiting.Add(id);
            foreach (var dependency in graph[id]) Visit(dependency);
            visiting.Remove(id);
        }
        foreach (var id in graph.Keys.OrderBy(x => x, StringComparer.Ordinal)) Visit(id);
    }

    private static void ValidateReferences(JsonObject root, JsonArray modules, JsonArray variables, JsonArray triggers)
    {
        var variableIds = variables.OfType<JsonObject>().Select(x => RequiredString(x, "id")).ToHashSet(StringComparer.Ordinal);
        var variableNames = variables.OfType<JsonObject>().Select(x => RequiredString(x, "name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasRegions = root["regions"] is JsonArray;
        var regionIds = (root["regions"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(x => StringValue(x, "id")).Where(x => x is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        var regionNames = (root["regions"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(x => StringValue(x, "name")).Where(x => x is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        var hasObjects = root["object_data"] is JsonArray;
        var objectIds = (root["object_data"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(x => StringValue(x, "id") ?? StringValue(x, "rawcode")).Where(x => x is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        var players = (root["players"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(x => IntValue(x["id"])).Where(x => x > 0).ToHashSet();
        var forces = (root["forces"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(x => StringValue(x, "id") ?? (IntValue(x["index"]) >= 0 ? $"force:{IntValue(x["index"])}" : null)).Where(x => x is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        var functions = modules.OfType<JsonObject>().SelectMany(module =>
            StringValue(module, "source") is { } source
                ? Function.Matches(source).Select(match => match.Groups["name"].Value)
                : Strings(module["public_symbols"])).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var trigger in triggers.OfType<JsonObject>())
        {
            foreach (var reference in ReferenceObjects(trigger))
            {
                foreach (var variable in Strings(reference["variables"]).Concat(Strings(reference["variable_ids"])))
                {
                    if (!variableIds.Contains(variable) && !variableNames.Contains(variable)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown variable '{variable}'.");
                }
                foreach (var region in Strings(reference["regions"]))
                {
                    if (hasRegions && !regionIds.Contains(region) && !regionNames.Contains(region)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown region '{region}'.");
                }
                foreach (var obj in Strings(reference["objects"]).Concat(Strings(reference["object_ids"])) .Concat(Strings(reference["rawcodes"])))
                {
                    if (!objectIds.Contains(obj) && !Rawcode.IsMatch(obj)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references invalid or unknown object '{obj}'.");
                    if (hasObjects && !objectIds.Contains(obj) && Rawcode.IsMatch(obj)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown object/rawcode '{obj}'.");
                }
                foreach (var player in Strings(reference["players"]))
                {
                    if (!int.TryParse(player, out var id) || id is < 1 or > 24 || (players.Count > 0 && !players.Contains(id))) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown player '{player}'.");
                }
                foreach (var force in Strings(reference["forces"]))
                {
                    if (forces.Count > 0 && !forces.Contains(force)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown force '{force}'.");
                }
                foreach (var function in Strings(reference["functions"]))
                {
                    if (!functions.Contains(function)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown gameplay function '{function}'.");
                }
            }

            ValidateTriggerNestedReferences(root, trigger, variableIds, variableNames, regionIds, regionNames, functions);
        }
    }

    private static void ValidateRegionRoles(JsonObject root)
    {
        if (root["region_roles"] is not JsonArray roles) return;
        var regions = (root["regions"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToArray();
        var ids = regions.Select(region => StringValue(region, "id")).Where(id => id is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        var names = regions.Select(region => StringValue(region, "name")).Where(name => name is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in roles)
        {
            if (node is not JsonObject role) throw new EngineException("INVALID_ARGUMENT", "Every region role must be an object.");
            EnsureAllowed(role, "region_id", "region_name", "role", "provenance", "capability");
            var reference = StringValue(role, "region_id") ?? StringValue(role, "region_name");
            if (reference is null || (ids.Count > 0 && !ids.Contains(reference) && !names.Contains(reference))) throw new EngineException("INVALID_ARGUMENT", $"Region role references unknown region '{reference}'.");
            var roleName = StringValue(role, "role");
            if (roleName is null || !RegionSupport.Roles.Contains(roleName)) throw new EngineException("INVALID_ARGUMENT", "Region role is not supported.");
            if (!seen.Add($"{reference}:{roleName}")) throw new EngineException("INVALID_ARGUMENT", $"Duplicate region role '{roleName}' for '{reference}'.");
        }
    }

    private static void ValidateTriggerNestedReferences(JsonObject root, JsonObject trigger, HashSet<string> variableIds, HashSet<string> variableNames, HashSet<string> regionIds, HashSet<string> regionNames, HashSet<string> functions)
    {
        var types = (root["gameplay_variables"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToDictionary(x => RequiredString(x, "id"), x => StringValue(x, "type") ?? "integer", StringComparer.Ordinal);
        void Variable(string id, string context, string? expectedType = null)
        {
            if (!variableIds.Contains(id) && !variableNames.Contains(id)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown variable '{id}' in {context}.");
            if (expectedType is not null && variableIds.Contains(id) && !string.Equals(types[id], expectedType, StringComparison.OrdinalIgnoreCase)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' uses variable '{id}' as {expectedType}, but it is {types[id]}.");
        }
        void Function(string name, string context)
        {
            if (!functions.Contains(name)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown gameplay function '{name}' in {context}.");
        }
        foreach (var eventNode in RequiredArray(trigger, "events", "events"))
        {
            if (eventNode is not JsonObject eventObject) throw new EngineException("INVALID_ARGUMENT", "Every gameplay trigger event must be an object.");
            if (root["regions"] is JsonArray && eventObject["region_id"] is JsonValue region && region.TryGetValue<string>(out var regionId) && !regionIds.Contains(regionId) && !regionNames.Contains(regionId)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown region '{regionId}'.");
            if (root["regions"] is JsonArray && eventObject["region_name"] is JsonValue regionName && regionName.TryGetValue<string>(out var name) && !regionIds.Contains(name) && !regionNames.Contains(name)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown region '{name}'.");
            ValidatePlayerReference(root, trigger, eventObject["player_id"]);
        }
        foreach (var condition in OptionalArray(trigger, "conditions"))
        {
            if (condition is not JsonObject conditionObject) throw new EngineException("INVALID_ARGUMENT", "Every gameplay condition must be an object.");
            var type = RequiredString(conditionObject, "type");
            if (type == "boolean_variable") Variable(RequiredString(conditionObject, "variable_id"), "condition", "boolean");
            if (type == "integer_compare") Variable(RequiredString(conditionObject, "variable_id"), "condition", "integer");
            if (type == "real_compare") Variable(RequiredString(conditionObject, "variable_id"), "condition", "real");
            if (type == "function") Function(RequiredIdentifier(conditionObject, "function"), "condition");
        }
        foreach (var action in OptionalArray(trigger, "actions"))
        {
            if (action is not JsonObject actionObject) throw new EngineException("INVALID_ARGUMENT", "Every gameplay action must be an object.");
            ValidateActionReferences(actionObject, Variable, Function);
        }
        if (trigger["handler_name"] is not null) Function(RequiredIdentifier(trigger, "handler_name"), "handler_name");
    }

    private static void ValidateActionReferences(JsonObject action, Action<string, string, string?> variable, Action<string, string> function)
    {
        var type = RequiredString(action, "type");
        switch (type)
        {
            case "set_variable": variable(RequiredString(action, "variable_id"), "set_variable", null); break;
            case "create_timer":
                variable(RequiredString(action, "variable_id"), "create_timer", "timer");
                if (action["callback"] is not null) function(RequiredIdentifier(action, "callback"), "create_timer callback");
                break;
            case "group_operation": variable(RequiredString(action, "group_variable_id"), "group_operation", "group"); break;
            case "call_function": function(RequiredIdentifier(action, "function"), "call_function"); break;
            case "branch":
                if (action["condition"] is not JsonObject condition) throw new EngineException("INVALID_ARGUMENT", "branch requires a condition object.");
                ValidateCondition(condition);
                if (condition["type"]?.GetValue<string>() is "boolean_variable" or "integer_compare" or "real_compare") variable(RequiredString(condition, "variable_id"), "branch condition", null);
                if (condition["type"]?.GetValue<string>() == "function") function(RequiredIdentifier(condition, "function"), "branch condition");
                foreach (var child in RequiredArray(action, "then", "branch then"))
                {
                    if (child is not JsonObject childObject) throw new EngineException("INVALID_ARGUMENT", "branch then must contain action objects.");
                    ValidateActionReferences(childObject, variable, function);
                }
                foreach (var child in OptionalArray(action, "else"))
                {
                    if (child is not JsonObject childObject) throw new EngineException("INVALID_ARGUMENT", "branch else must contain action objects.");
                    ValidateActionReferences(childObject, variable, function);
                }
                break;
        }
    }

    private static void ValidateTypedArguments(JsonArray variables, JsonArray triggers)
    {
        var typesById = variables.OfType<JsonObject>().ToDictionary(x => RequiredString(x, "id"), x => StringValue(x, "type") ?? "integer", StringComparer.Ordinal);
        var typesByName = variables.OfType<JsonObject>().ToDictionary(x => RequiredString(x, "name"), x => StringValue(x, "type") ?? "integer", StringComparer.OrdinalIgnoreCase);
        string? VariableType(JsonNode? node)
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out var id)) return null;
            return typesById.TryGetValue(id, out var byId) ? byId : typesByName.TryGetValue(id, out var byName) ? byName : null;
        }
        string? ExpressionType(JsonNode? node)
        {
            if (node is JsonObject expression && expression["variable_id"] is not null) return VariableType(expression["variable_id"]);
            if (node is JsonObject literal && literal["literal"] is not null) return ExpressionType(literal["literal"]);
            if (node is JsonValue value)
            {
                if (value.TryGetValue<bool>(out _)) return "boolean";
                if (value.TryGetValue<int>(out _)) return "integer";
                if (value.TryGetValue<double>(out _)) return "real";
                if (value.TryGetValue<string>(out _)) return "string";
            }
            return null;
        }
        void RequireType(string? actual, string expected, string context)
        {
            if (actual is null || expected == "unknown" || (expected == "real" && actual == "integer")) return;
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new EngineException("INVALID_ARGUMENT", context + " expects " + expected + ", but received " + actual + ".");
        }
        void Condition(JsonObject condition, string context)
        {
            var type = RequiredString(condition, "type");
            if (type == "boolean_variable") RequireType(VariableType(condition["variable_id"]), "boolean", context);
            if (type == "integer_compare") RequireType(VariableType(condition["variable_id"]), "integer", context);
            if (type == "real_compare") RequireType(VariableType(condition["variable_id"]), "real", context);
        }
        void Action(JsonObject action, string context)
        {
            var type = RequiredString(action, "type");
            if (type == "set_variable") RequireType(ExpressionType(action["value"]), VariableType(action["variable_id"]) ?? "unknown", context);
            if (type == "create_timer") RequireType(VariableType(action["variable_id"]), "timer", context);
            if (type == "group_operation") RequireType(VariableType(action["group_variable_id"]), "group", context);
            if (type == "branch")
            {
                if (action["condition"] is JsonObject condition) Condition(condition, context + ".condition");
                foreach (var child in RequiredArray(action, "then", "branch then").OfType<JsonObject>()) Action(child, context + ".then");
                foreach (var child in OptionalArray(action, "else").OfType<JsonObject>()) Action(child, context + ".else");
            }
        }
        foreach (var trigger in triggers.OfType<JsonObject>())
        {
            var id = RequiredString(trigger, "id");
            foreach (var condition in OptionalArray(trigger, "conditions").OfType<JsonObject>()) Condition(condition, "trigger " + id + " condition");
            foreach (var action in OptionalArray(trigger, "actions").OfType<JsonObject>()) Action(action, "trigger " + id + " action");
        }
    }

    private static void ValidateEvent(JsonNode node)
    {
        if (node is not JsonObject value) throw new EngineException("INVALID_ARGUMENT", "Every gameplay trigger event must be an object.");
        var type = RequiredString(value, "type");
        if (!EventTypes.Contains(type)) throw new EngineException("UNSUPPORTED_OPERATION", $"Gameplay trigger event type '{type}' is not supported.");
        switch (type)
        {
            case "periodic_timer": PositiveNumber(value, "period"); break;
            case "elapsed_time": NonNegativeNumber(value, "seconds"); break;
            case "player_chat":
                PlayerId(value, "player_id");
                if (string.IsNullOrWhiteSpace(StringValue(value, "message"))) throw new EngineException("INVALID_ARGUMENT", "player_chat requires a non-empty message.");
                if (value["exact"] is not null) _ = BooleanValue(value["exact"]!, "exact");
                break;
            case "unit_death":
                if (value["player_id"] is not null) PlayerId(value, "player_id");
                if (value["unit_rawcode"] is not null) RawcodeValue(value["unit_rawcode"]!, "unit_rawcode");
                break;
            case "region_entry":
                if (value["region_id"] is null && value["region_name"] is null) throw new EngineException("INVALID_ARGUMENT", "region_entry requires region_id or region_name.");
                break;
            case "player_state_change":
            {
                PlayerId(value, "player_id");
                var state = RequiredIdentifier(value, "state");
                if (!JassNativeCatalogue.IsPlayerState(state)) throw new EngineException("INVALID_ARGUMENT", $"player_state_change state '{state}' is not in the pinned JASS native catalogue '{JassNativeCatalogue.Version}'.");
                if (!ComparisonOperators.Contains(RequiredString(value, "operator"))) throw new EngineException("INVALID_ARGUMENT", "player_state_change operator is unsupported.");
                Number(value, "value");
                break;
            }
            case "custom_event": _ = RequiredIdentifier(value, "name"); break;
        }
    }

    private static void ValidateCondition(JsonNode node)
    {
        if (node is not JsonObject value) throw new EngineException("INVALID_ARGUMENT", "Every gameplay condition must be an object.");
        var type = RequiredString(value, "type");
        if (!ConditionTypes.Contains(type)) throw new EngineException("UNSUPPORTED_OPERATION", $"Gameplay condition type '{type}' is not supported.");
        if (type == "always") return;
        if (type == "function") { _ = RequiredIdentifier(value, "function"); return; }
        _ = RequiredString(value, "variable_id");
        if (type is "integer_compare" or "real_compare")
        {
            if (!ComparisonOperators.Contains(RequiredString(value, "operator"))) throw new EngineException("INVALID_ARGUMENT", "Condition comparison operator is unsupported.");
            if (type == "integer_compare") _ = Integer(value["value"] ?? throw new EngineException("INVALID_ARGUMENT", "Condition value is required."), "value");
            else Number(value, "value");
        }
        else if (value["value"] is not null) _ = BooleanValue(value["value"]!, "value");
    }

    private static void ValidateAction(JsonNode node)
    {
        if (node is not JsonObject value) throw new EngineException("INVALID_ARGUMENT", "Every gameplay action must be an object.");
        var type = RequiredString(value, "type");
        if (!ActionTypes.Contains(type)) throw new EngineException("UNSUPPORTED_OPERATION", $"Gameplay action type '{type}' is not supported.");
        switch (type)
        {
            case "set_variable":
                _ = RequiredString(value, "variable_id");
                if (value["value"] is null) throw new EngineException("INVALID_ARGUMENT", "set_variable requires value.");
                break;
            case "branch":
                if (value["condition"] is not JsonObject condition) throw new EngineException("INVALID_ARGUMENT", "branch requires condition.");
                ValidateCondition(condition);
                foreach (var child in RequiredArray(value, "then", "branch then"))
                {
                    if (child is not JsonObject childObject) throw new EngineException("INVALID_ARGUMENT", "branch then must contain action objects.");
                    ValidateAction(childObject);
                }
                foreach (var child in OptionalArray(value, "else"))
                {
                    if (child is not JsonObject childObject) throw new EngineException("INVALID_ARGUMENT", "branch else must contain action objects.");
                    ValidateAction(childObject);
                }
                break;
            case "create_timer":
                _ = RequiredString(value, "variable_id"); PositiveNumber(value, "period");
                if (value["repeating"] is not null) _ = BooleanValue(value["repeating"]!, "repeating");
                if (value["callback"] is not null) _ = RequiredIdentifier(value, "callback");
                break;
            case "unit_operation":
                if (RequiredString(value, "operation") is not ("kill_trigger_unit" or "remove_trigger_unit")) throw new EngineException("UNSUPPORTED_OPERATION", "unit_operation operation is unsupported.");
                break;
            case "group_operation":
                if (RequiredString(value, "operation") is not ("add_trigger_unit" or "remove_trigger_unit" or "destroy")) throw new EngineException("UNSUPPORTED_OPERATION", "group_operation operation is unsupported.");
                _ = RequiredString(value, "group_variable_id");
                break;
            case "message": if (string.IsNullOrWhiteSpace(StringValue(value, "text"))) throw new EngineException("INVALID_ARGUMENT", "message requires non-empty text."); break;
            case "phase_transition": if (value["phase"] is not null) _ = Integer(value["phase"]!, "phase"); break;
            case "call_function": _ = RequiredIdentifier(value, "function"); break;
        }
    }

    private static void ValidateReferencesObject(JsonNode node)
    {
        if (node is not JsonObject references) throw new EngineException("INVALID_ARGUMENT", "Trigger references must be an object.");
        EnsureAllowed(references, "variables", "variable_ids", "regions", "objects", "object_ids", "rawcodes", "players", "forces", "functions");
        foreach (var key in references.Select(x => x.Key)) ValidateStringArray(references[key]!, $"references.{key}", key is "rawcodes" ? Rawcode : Identifier);
    }

    private static IEnumerable<JsonObject> ReferenceObjects(JsonObject trigger)
    {
        if (trigger["references"] is JsonObject references) yield return references;
        foreach (var node in RequiredArray(trigger, "events", "events")) if (node is JsonObject value) yield return value;
        foreach (var node in OptionalArray(trigger, "conditions")) if (node is JsonObject value) yield return value;
        foreach (var node in OptionalArray(trigger, "actions")) if (node is JsonObject value) yield return value;
    }

    private static void ValidateSourceLocation(JsonNode node)
    {
        if (node is not JsonObject location) throw new EngineException("INVALID_ARGUMENT", "source_location must be an object.");
        EnsureAllowed(location, "path", "line", "column");
        ValidateRelativePath(location["path"] ?? throw new EngineException("INVALID_ARGUMENT", "source_location.path is required."), "source_location.path");
        if (location["line"] is not null && Integer(location["line"]!, "line") < 1) throw new EngineException("INVALID_ARGUMENT", "source_location.line must be positive.");
        if (location["column"] is not null && Integer(location["column"]!, "column") < 1) throw new EngineException("INVALID_ARGUMENT", "source_location.column must be positive.");
    }

    private static void ValidateTypedValue(JsonNode value, string type, string context)
    {
        if (value is JsonObject expression && expression["variable_id"] is JsonValue) return;
        switch (type.ToLowerInvariant())
        {
            case "integer": _ = IntegerValue(value, context); break;
            case "real": _ = NumberValue(value, context); break;
            case "boolean": _ = BooleanValue(value, context); break;
            case "string": RequireString(value, context); break;
            default: if (value is not JsonValue || !value.ToJsonString().Equals("null", StringComparison.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"{context} must be null for handle type."); break;
        }
    }

    private static void ValidateStringArray(JsonNode node, string context, Regex pattern)
    {
        if (node is not JsonArray values) throw new EngineException("INVALID_ARGUMENT", $"{context} must be an array.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in values)
        {
            var value = item is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : throw new EngineException("INVALID_ARGUMENT", $"{context} must contain strings.");
            if (!pattern.IsMatch(value) || !seen.Add(value)) throw new EngineException("INVALID_ARGUMENT", $"{context} contains an invalid or duplicate value '{value}'.");
        }
    }

    private static void ValidateRelativePath(JsonNode node, string context)
    {
        var path = RequireString(node, context);
        if (Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal) || path.Contains(':')) throw new EngineException("INVALID_ARGUMENT", $"{context} must be relative and traversal-free.");
    }

    private static void PlayerId(JsonObject value, string property)
    {
        var id = Integer(value[property] ?? throw new EngineException("INVALID_ARGUMENT", $"{property} is required."), property);
        if (id is < 1 or > 24) throw new EngineException("INVALID_ARGUMENT", $"{property} must be between 1 and 24.");
    }

    private static void ValidatePlayerReference(JsonObject root, JsonObject trigger, JsonNode? node)
    {
        if (node is null) return;
        var id = Integer(node, "player_id");
        var players = root["players"] as JsonArray;
        if (players is not null && players.Count > 0 && !players.OfType<JsonObject>().Any(x => IntValue(x["id"]) == id)) throw new EngineException("INVALID_ARGUMENT", $"Trigger '{RequiredString(trigger, "id")}' references unknown player '{id}'.");
    }

    private static JsonArray RequiredArray(JsonObject value, string property, string context)
        => value[property] as JsonArray ?? throw new EngineException("INVALID_ARGUMENT", $"{context} must be an array.");

    private static JsonArray OptionalArray(JsonObject value, string property)
        => value[property] is null ? new JsonArray() : RequiredArray(value, property, property);

    private static string? StringValue(JsonObject value, string property)
        => value[property] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static string RequireString(JsonNode node, string context)
        => node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new EngineException("INVALID_ARGUMENT", $"{context} must be a non-empty string.");

    private static bool BooleanValue(JsonNode node, string context)
        => node is JsonValue value && value.TryGetValue<bool>(out var result) ? result : throw new EngineException("INVALID_ARGUMENT", $"{context} must be boolean.");

    private static int Integer(JsonNode node, string context)
        => node is JsonValue value && value.TryGetValue<int>(out var result) ? result : throw new EngineException("INVALID_ARGUMENT", $"{context} must be an integer.");

    private static int IntegerValue(JsonNode node, string context) => Integer(node, context);

    private static double Number(JsonObject value, string property)
        => NumberValue(value[property] ?? throw new EngineException("INVALID_ARGUMENT", $"{property} is required."), property);

    private static double NumberValue(JsonNode node, string context)
    {
        if (node is JsonValue value && value.TryGetValue<double>(out var result) && double.IsFinite(result)) return result;
        if (node is JsonValue integer && integer.TryGetValue<int>(out var intResult)) return intResult;
        throw new EngineException("INVALID_ARGUMENT", $"{context} must be a finite number.");
    }

    private static void PositiveNumber(JsonObject value, string property) { if (Number(value, property) <= 0) throw new EngineException("INVALID_ARGUMENT", $"{property} must be greater than zero."); }
    private static void NonNegativeNumber(JsonObject value, string property) { if (Number(value, property) < 0) throw new EngineException("INVALID_ARGUMENT", $"{property} must not be negative."); }

    private static void RawcodeValue(JsonNode node, string context)
    {
        var value = RequireString(node, context);
        if (!Rawcode.IsMatch(value)) throw new EngineException("INVALID_ARGUMENT", $"{context} must be a four-character rawcode.");
    }

    private static void RequireHash(JsonNode node, string context)
    {
        var value = RequireString(node, context);
        if (!Regex.IsMatch(value, "^[0-9A-F]{64}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) throw new EngineException("INVALID_ARGUMENT", $"{context} must be a SHA-256 hash.");
    }

    private static int IntValue(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var result) ? result : -1;

    private static void EnsureAllowed(JsonObject value, params string[] allowed)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        var unknown = value.Select(x => x.Key).Where(x => !set.Contains(x)).ToArray();
        if (unknown.Length > 0) throw new EngineException("INVALID_ARGUMENT", $"Unsupported gameplay field(s): {string.Join(", ", unknown)}.");
    }
}
