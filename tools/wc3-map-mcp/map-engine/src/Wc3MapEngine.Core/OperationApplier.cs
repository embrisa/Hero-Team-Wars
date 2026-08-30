using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Wc3MapEngine.Core.Scripts;

namespace Wc3MapEngine.Core;

/// <summary>
/// Applies the Phase 2 typed operation vocabulary to a cloned canonical map.
/// The caller receives a new canonical value only after the entire batch has
/// succeeded; no operation in this class writes a caller-owned file.
/// </summary>
public static class OperationApplier
{
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.Ordinal)
    {
        "set_map_metadata", "set_player_slot", "set_force",
        "create_region", "update_region", "delete_region", "set_script_source"
    };

    private static readonly HashSet<string> MetadataFields = new(StringComparer.Ordinal)
    {
        // These are the metadata fields with a proven Phase 0/3 binary
        // representation. Other parsed fields remain read-only until their
        // serializer is proven.
        "title", "suggested_players"
    };

    public static JsonObject Apply(JsonNode canonical, JsonArray operations)
    {
        if (canonical is not JsonObject root)
        {
            throw new EngineException("INVALID_JSON", "Canonical map root must be an object.");
        }

        var working = root.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Canonical map could not be cloned.");
        var allChanges = new JsonArray();
        var applied = new JsonArray();
        var operationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var operationNode in operations)
        {
            if (operationNode is not JsonObject operation)
            {
                throw new EngineException("INVALID_ARGUMENT", "Each operation must be a JSON object.");
            }

            var operationId = RequiredOperationId(operation);
            if (!operationIds.Add(operationId))
            {
                throw new EngineException("INVALID_ARGUMENT", $"Operation ID '{operationId}' occurs more than once in the batch.");
            }

            var before = working.DeepClone();
            ApplyOne(working, operation);
            var changes = SemanticDiff.CompareCanonical(before, working, operationId);
            foreach (var change in changes.OfType<JsonObject>())
            {
                change["target"] = (operation["target"] as JsonObject)?.DeepClone();
                change["provenance"] = "intended_design";
                if (operation["design_reference"] is not null)
                {
                    change["design_reference"] = operation["design_reference"]!.DeepClone();
                }

                allChanges.Add(change.DeepClone());
            }

            applied.Add(operationId);
        }

        return new JsonObject
        {
            ["canonical_map"] = working,
            ["diff"] = new JsonObject
            {
                ["schema_version"] = "1.0",
                ["changes"] = allChanges
            },
            ["applied_operation_ids"] = applied
        };
    }

    private static void ApplyOne(JsonObject root, JsonObject operation)
    {
        var type = RequiredString(operation, "type");
        var target = operation["target"] as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", $"Operation '{type}' requires a target object.");
        var expected = operation["expected"];
        var value = operation["value"];

        if (!SupportedTypes.Contains(type))
        {
            throw new EngineException("UNSUPPORTED_OPERATION", $"Operation type '{type}' is not enabled for this map. The component needs a proven typed serializer first.");
        }

        switch (type)
        {
            case "set_map_metadata":
                SetMetadata(root, target, expected, value);
                break;
            case "update_region":
                UpdateRegion(root, target, expected, value);
                break;
            case "create_region":
                CreateRegion(root, target, expected, value);
                break;
            case "delete_region":
                DeleteRegion(root, target, expected);
                break;
            case "set_player_slot":
                SetPlayer(root, target, expected, value);
                break;
            case "set_force":
                SetForce(root, target, expected, value);
                break;
            case "set_script_source":
                SetScriptSource(root, target, expected, value);
                break;
        }
    }

    private static void SetMetadata(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "field");
        var field = RequiredString(target, "field");
        if (!MetadataFields.Contains(field))
        {
            throw new EngineException("UNSUPPORTED_OPERATION", $"Metadata field '{field}' has no proven typed writer in this release.");
        }

        var metadata = RequiredArray(root, "metadata");
        var entry = metadata.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["field"]?.GetValue<string>(), field, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new EngineException("UNSUPPORTED_COMPONENT", $"Metadata field '{field}' is not present in the canonical map.");
        }

        EnsureExpected(entry["value"], expected, $"metadata.{field}");
        if (value is null)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Metadata field '{field}' cannot be set to null.");
        }

        if (field == "title" && (value is not JsonValue text || !text.TryGetValue<string>(out var title) || string.IsNullOrWhiteSpace(title)))
        {
            throw new EngineException("INVALID_ARGUMENT", "Map title must be a non-empty string.");
        }
        if (field == "suggested_players")
        {
            ValidateSuggestedPlayers(value);
        }

        entry["value"] = value.DeepClone();
        entry["provenance"] = "intended_design";
        entry["capability"] = "typed_write_enabled";
    }

    private static void UpdateRegion(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "name");
        var name = RequiredString(target, "name");
        var regions = RequiredArray(root, "regions");
        var region = regions.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
        if (region is null)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' was not found.");
        }

        EnsureExpected(region, expected, $"regions.{name}");
        if (value is not JsonObject update)
        {
            throw new EngineException("INVALID_ARGUMENT", "update_region requires an object value.");
        }
        EnsureAllowed(update, "name", "min_x", "min_y", "max_x", "max_y");

        var newName = update["name"] is null ? name : RequiredString(update, "name");
        if (!string.Equals(newName, name, StringComparison.Ordinal))
        {
            throw new EngineException("REGION_RENAME_FORBIDDEN", "Existing region names are immutable; create a new region instead.");
        }

        foreach (var field in new[] { "min_x", "min_y", "max_x", "max_y" })
        {
            if (update[field] is not null)
            {
                region[field] = FiniteNumber(update[field]!, $"regions.{name}.{field}");
            }
        }
        ValidateRegionBounds(region, name);

        region["provenance"] = "intended_design";
        region["capability"] = "typed_write_enabled";
    }

    private static void CreateRegion(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target);
        if (expected is not null)
        {
            throw new EngineException("INVALID_ARGUMENT", "create_region does not accept an expected prior value; use update_region for an existing region.");
        }

        var regions = RequiredArray(root, "regions");
        if (value is not JsonObject region || string.IsNullOrWhiteSpace(region["name"]?.GetValue<string>()))
        {
            throw new EngineException("INVALID_ARGUMENT", "create_region requires a named region object.");
        }
        EnsureAllowed(region, "name", "min_x", "min_y", "max_x", "max_y", "creation_number", "weather", "ambient_sound");

        var name = RequiredString(region, "name");
        if (regions.OfType<JsonObject>().Any(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal)))
        {
            throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' already exists.");
        }

        var created = new JsonObject
        {
            ["name"] = name,
            ["min_x"] = FiniteNumber(region["min_x"] ?? throw new EngineException("INVALID_ARGUMENT", "create_region requires min_x."), "regions.min_x"),
            ["min_y"] = FiniteNumber(region["min_y"] ?? throw new EngineException("INVALID_ARGUMENT", "create_region requires min_y."), "regions.min_y"),
            ["max_x"] = FiniteNumber(region["max_x"] ?? throw new EngineException("INVALID_ARGUMENT", "create_region requires max_x."), "regions.max_x"),
            ["max_y"] = FiniteNumber(region["max_y"] ?? throw new EngineException("INVALID_ARGUMENT", "create_region requires max_y."), "regions.max_y"),
            ["creation_number"] = region["creation_number"] is null ? 0 : RequiredIntValue(region["creation_number"]!, "creation_number", 0, int.MaxValue),
            ["weather"] = region["weather"]?.DeepClone() ?? "none",
            ["ambient_sound"] = region["ambient_sound"]?.DeepClone() ?? string.Empty,
            ["provenance"] = "intended_design",
            ["capability"] = "typed_write_enabled"
        };
        if (region["weather"] is not null) RequireStringValue(region["weather"]!, "weather");
        if (region["ambient_sound"] is not null) RequireStringValue(region["ambient_sound"]!, "ambient_sound");
        ValidateRegionBounds(created, name);

        regions.Add(created);
    }

    private static void DeleteRegion(JsonObject root, JsonObject target, JsonNode? expected)
    {
        EnsureAllowed(target, "name");
        var name = RequiredString(target, "name");
        var regions = RequiredArray(root, "regions");
        var index = regions.IndexOf(regions.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal)));
        if (index < 0)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' was not found.");
        }

        EnsureExpected(regions[index], expected, $"regions.{name}");
        regions.RemoveAt(index);
    }

    private static void SetPlayer(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id");
        var id = RequiredInt(target, "id", 1, 24);
        var players = RequiredArray(root, "players");
        var player = players.OfType<JsonObject>().FirstOrDefault(x => IntValue(x["id"]) == id);
        if (player is null)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Player {id} was not found.");
        }

        EnsureExpected(player, expected, $"players.{id}");
        if (value is not JsonObject update)
        {
            throw new EngineException("INVALID_ARGUMENT", "set_player_slot requires an object value.");
        }
        EnsureAllowed(update, "controller", "race", "flags", "start");
        if (update["controller"] is not null) RequireStringValue(update["controller"]!, "controller");
        if (update["race"] is not null) RequireStringValue(update["race"]!, "race");
        if (update["flags"] is not null) _ = RequiredIntValue(update["flags"]!, "flags", 0, int.MaxValue);
        if (update["start"] is not null) ValidateStart(update["start"]!);

        foreach (var field in new[] { "controller", "race", "flags", "start" })
        {
            if (update[field] is not null) player[field] = update[field]!.DeepClone();
        }

        player["provenance"] = "intended_design";
        player["capability"] = "typed_write_enabled";
    }

    private static void SetForce(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "index");
        var index = RequiredInt(target, "index", 0, 23);
        var forces = RequiredArray(root, "forces");
        var force = forces.OfType<JsonObject>().FirstOrDefault(x => IntValue(x["index"]) == index);
        if (force is null)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Force {index} was not found.");
        }

        EnsureExpected(force, expected, $"forces.{index}");
        if (value is not JsonObject update)
        {
            throw new EngineException("INVALID_ARGUMENT", "set_force requires an object value.");
        }
        EnsureAllowed(update, "name", "flags", "player_ids", "player_mask");
        if (update["name"] is not null) RequireStringValue(update["name"]!, "name");
        if (update["flags"] is not null) _ = RequiredIntValue(update["flags"]!, "flags", 0, int.MaxValue);
        if (update["player_mask"] is not null) _ = RequiredIntValue(update["player_mask"]!, "player_mask", int.MinValue, int.MaxValue);
        if (update["player_ids"] is not null) ValidatePlayerIds(update["player_ids"]!);

        foreach (var field in new[] { "name", "flags", "player_ids", "player_mask" })
        {
            if (update[field] is not null) force[field] = update[field]!.DeepClone();
        }

        force["provenance"] = "intended_design";
        force["capability"] = "typed_write_enabled";
    }

    private static void SetScriptSource(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "archive_path");
        var archivePath = RequiredString(target, "archive_path");
        if (!archivePath.Equals("war3map.j", StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineException("UNSUPPORTED_OPERATION", "Only the map's JASS entry point war3map.j is enabled for MCP-owned gameplay source.");
        }

        var scripts = RequiredArray(root, "scripts");
        var script = scripts.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["archive_path"]?.GetValue<string>(), archivePath, StringComparison.OrdinalIgnoreCase));
        if (script is null)
        {
            throw new EngineException("UNSUPPORTED_COMPONENT", "The canonical map does not contain a war3map.j script entry point.");
        }

        var expectedHash = ExpectedScriptHash(expected);
        var actualHash = script["sha256"]?.GetValue<string>();
        if (expectedHash is null)
        {
            throw new EngineException("PRECONDITION_REQUIRED", "set_script_source must include the expected current war3map.j SHA-256.");
        }
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineException("PRECONDITION_FAILED", "The expected war3map.j SHA-256 does not match the staged script.");
        }

        if (value is not JsonObject sourceValue)
        {
            throw new EngineException("INVALID_ARGUMENT", "set_script_source requires an object value with language and source.");
        }
        EnsureAllowed(sourceValue, "language", "source");
        var language = RequiredString(sourceValue, "language");
        if (!language.Equals("jass", StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineException("UNSUPPORTED_OPERATION", "The first MCP-owned gameplay source strategy supports JASS only.");
        }

        var source = RequiredString(sourceValue, "source");
        try
        {
            ScriptOwnership.ValidateMcpOwnedJass(archivePath, source);
        }
        catch (InvalidDataException exception)
        {
            throw new EngineException("INVALID_ARGUMENT", exception.Message, false, exception, new JsonObject
            {
                ["component"] = "scripts",
                ["archive_path"] = archivePath
            });
        }

        var bytes = Encoding.UTF8.GetBytes(source);
        var sha256 = Hashing.Sha256(bytes);
        script["language"] = "Jass";
        script["source"] = source;
        script["source_sha256"] = sha256;
        script["size_bytes"] = bytes.Length;
        script["sha256"] = sha256;
        script["provenance"] = "intended_design";
        script["capability"] = "staged_typed_write";
    }

    private static string? ExpectedScriptHash(JsonNode? expected)
    {
        if (expected is JsonValue text && text.TryGetValue<string>(out var hash))
        {
            return hash;
        }

        if (expected is JsonObject expectedObject && expectedObject["sha256"] is JsonValue value && value.TryGetValue<string>(out var objectHash))
        {
            return objectHash;
        }

        return null;
    }

    private static void EnsureExpected(JsonNode? actual, JsonNode? expected, string field)
    {
        if (expected is null)
        {
            throw new EngineException("PRECONDITION_REQUIRED", $"Operation for '{field}' must include an expected prior value.");
        }

        if (!JsonUtilities.Equal(actual, expected))
        {
            throw new EngineException("PRECONDITION_FAILED", $"The expected value for '{field}' does not match the staged value.");
        }
    }

    private static void ValidateRegionBounds(JsonObject region, string name)
    {
        var minX = FiniteNumber(region["min_x"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires min_x."), $"regions.{name}.min_x").GetValue<double>();
        var minY = FiniteNumber(region["min_y"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires min_y."), $"regions.{name}.min_y").GetValue<double>();
        var maxX = FiniteNumber(region["max_x"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires max_x."), $"regions.{name}.max_x").GetValue<double>();
        var maxY = FiniteNumber(region["max_y"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires max_y."), $"regions.{name}.max_y").GetValue<double>();
        if (minX > maxX || minY > maxY)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' must have min coordinates no greater than max coordinates.");
        }
    }

    private static JsonArray RequiredArray(JsonObject root, string name) => root[name] as JsonArray
        ?? throw new EngineException("INVALID_JSON", $"Canonical map has no {name} array.");

    private static string RequiredOperationId(JsonObject operation)
    {
        var value = RequiredString(operation, "operation_id");
        if (!Guid.TryParse(value, out _)) throw new EngineException("INVALID_ARGUMENT", "operation_id must be a UUID.");
        return value;
    }

    private static string RequiredString(JsonObject objectNode, string name) => objectNode[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
        ? text
        : throw new EngineException("INVALID_ARGUMENT", $"Missing required non-empty string property '{name}'.");

    private static int RequiredInt(JsonObject objectNode, string name, int minimum, int maximum) => objectNode[name] is not null
        ? RequiredIntValue(objectNode[name]!, name, minimum, maximum)
        : throw new EngineException("INVALID_ARGUMENT", $"Missing required integer property '{name}'.");

    private static int RequiredIntValue(JsonNode value, string name, int minimum, int maximum)
    {
        if (value is JsonValue json && json.TryGetValue<int>(out var integer) && integer >= minimum && integer <= maximum) return integer;
        throw new EngineException("INVALID_ARGUMENT", $"Property '{name}' must be an integer between {minimum} and {maximum}.");
    }

    private static int IntValue(JsonNode? value) => value is JsonValue json && json.TryGetValue<int>(out var integer) ? integer : int.MinValue;

    private static JsonNode FiniteNumber(JsonNode value, string field)
    {
        if (value is JsonValue json)
        {
            if (json.TryGetValue<double>(out var number) && double.IsFinite(number)) return JsonValue.Create(number)!;
            if (json.TryGetValue<float>(out var single) && float.IsFinite(single)) return JsonValue.Create((double)single)!;
            if (json.TryGetValue<int>(out var integer)) return JsonValue.Create((double)integer)!;
            if (json.TryGetValue<long>(out var longValue)) return JsonValue.Create((double)longValue)!;
        }
        throw new EngineException("INVALID_ARGUMENT", $"Property '{field}' must be a finite number.");
    }

    private static void RequireStringValue(JsonNode value, string field)
    {
        if (value is not JsonValue json || !json.TryGetValue<string>(out _)) throw new EngineException("INVALID_ARGUMENT", $"Property '{field}' must be a string.");
    }

    private static void ValidateSuggestedPlayers(JsonNode value)
    {
        if (value is JsonValue json && json.TryGetValue<int>(out var integer) && integer is >= 1 and <= 24) return;
        if (value is JsonValue text && text.TryGetValue<string>(out var players) && !string.IsNullOrWhiteSpace(players)) return;
        throw new EngineException("INVALID_ARGUMENT", "suggested_players must be a non-empty string or an integer from 1 through 24.");
    }

    private static void ValidateStart(JsonNode value)
    {
        if (value is not JsonObject start) throw new EngineException("INVALID_ARGUMENT", "Player start must be an object.");
        EnsureAllowed(start, "x", "y");
        _ = FiniteNumber(start["x"] ?? throw new EngineException("INVALID_ARGUMENT", "Player start requires x."), "start.x");
        _ = FiniteNumber(start["y"] ?? throw new EngineException("INVALID_ARGUMENT", "Player start requires y."), "start.y");
    }

    private static void ValidatePlayerIds(JsonNode value)
    {
        if (value is not JsonArray ids) throw new EngineException("INVALID_ARGUMENT", "player_ids must be an array.");
        var parsed = ids.Select((node, index) => RequiredIntValue(node ?? throw new EngineException("INVALID_ARGUMENT", $"player_ids[{index}] is null."), $"player_ids[{index}]", 1, 24)).ToArray();
        if (parsed.Length != parsed.Distinct().Count()) throw new EngineException("INVALID_ARGUMENT", "player_ids must contain unique player IDs.");
    }

    private static void EnsureAllowed(JsonObject objectNode, params string[] allowed)
    {
        var permitted = new HashSet<string>(allowed, StringComparer.Ordinal);
        var unknown = objectNode.Select(property => property.Key).Where(key => !permitted.Contains(key)).ToArray();
        if (unknown.Length > 0) throw new EngineException("INVALID_ARGUMENT", $"Unsupported typed-operation field(s): {string.Join(", ", unknown)}.");
    }
}
