using System.Globalization;
using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core;

public static class OperationApplier
{
    public static JsonObject Apply(JsonNode canonical, JsonArray operations)
    {
        if (canonical is not JsonObject root)
        {
            throw new EngineException("INVALID_JSON", "Canonical map root must be an object.");
        }

        var working = root.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Canonical map could not be cloned.");
        var allChanges = new JsonArray();
        var applied = new JsonArray();
        foreach (var operationNode in operations)
        {
            if (operationNode is not JsonObject operation)
            {
                throw new EngineException("INVALID_ARGUMENT", "Each operation must be a JSON object.");
            }

            var operationId = RequiredString(operation, "operation_id");
            var before = working.DeepClone();
            ApplyOne(working, operation);
            var changes = SemanticDiff.Compare(before, working, operationId);
            foreach (var change in changes)
            {
                allChanges.Add(change!.DeepClone());
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

        switch (type)
        {
            case "set_map_metadata":
                SetMetadata(root, target, expected, value);
                break;
            case "update_region":
                UpdateRegion(root, target, expected, value);
                break;
            case "create_region":
                CreateRegion(root, expected, value);
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
            default:
                throw new EngineException("UNSUPPORTED_OPERATION", $"Operation type '{type}' is not supported by this engine release.");
        }
    }

    private static void SetMetadata(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var field = RequiredString(target, "field");
        var metadata = root["metadata"] as JsonArray ?? throw new EngineException("INVALID_JSON", "Canonical map has no metadata array.");
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

        entry["value"] = value.DeepClone();
        entry["provenance"] = "intended_design";
        entry["capability"] = "staged_typed_write";
    }

    private static void UpdateRegion(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var name = RequiredString(target, "name");
        var regions = root["regions"] as JsonArray ?? throw new EngineException("INVALID_JSON", "Canonical map has no regions array.");
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

        var newName = update["name"]?.GetValue<string>() ?? name;
        if (!string.Equals(newName, name, StringComparison.Ordinal))
        {
            throw new EngineException("REGION_RENAME_FORBIDDEN", "Existing region names are immutable; create a new region instead.");
        }

        foreach (var field in new[] { "min_x", "min_y", "max_x", "max_y" })
        {
            if (update[field] is not null)
            {
                region[field] = update[field]!.DeepClone();
            }
        }

        region["provenance"] = "intended_design";
        region["capability"] = "staged_typed_write";
    }

    private static void CreateRegion(JsonObject root, JsonNode? expected, JsonNode? value)
    {
        var regions = root["regions"] as JsonArray ?? throw new EngineException("INVALID_JSON", "Canonical map has no regions array.");
        if (value is not JsonObject region || string.IsNullOrWhiteSpace(region["name"]?.GetValue<string>()))
        {
            throw new EngineException("INVALID_ARGUMENT", "create_region requires a named region object.");
        }

        var name = region["name"]!.GetValue<string>();
        if (regions.OfType<JsonObject>().Any(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal)))
        {
            throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' already exists.");
        }

        regions.Add(new JsonObject
        {
            ["name"] = name,
            ["min_x"] = RequiredNumber(region, "min_x"),
            ["min_y"] = RequiredNumber(region, "min_y"),
            ["max_x"] = RequiredNumber(region, "max_x"),
            ["max_y"] = RequiredNumber(region, "max_y"),
            ["creation_number"] = region["creation_number"]?.DeepClone() ?? 0,
            ["weather"] = region["weather"]?.DeepClone() ?? "none",
            ["ambient_sound"] = region["ambient_sound"]?.DeepClone() ?? string.Empty,
            ["provenance"] = "intended_design",
            ["capability"] = "staged_typed_write"
        });

        _ = expected;
    }

    private static void DeleteRegion(JsonObject root, JsonObject target, JsonNode? expected)
    {
        var name = RequiredString(target, "name");
        var regions = root["regions"] as JsonArray ?? throw new EngineException("INVALID_JSON", "Canonical map has no regions array.");
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
        var id = target["id"]?.GetValue<int>() ?? throw new EngineException("INVALID_ARGUMENT", "set_player_slot requires a numeric player id.");
        var players = root["players"] as JsonArray ?? throw new EngineException("INVALID_JSON", "Canonical map has no players array.");
        var player = players.OfType<JsonObject>().FirstOrDefault(x => x["id"]?.GetValue<int>() == id);
        if (player is null)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Player {id} was not found.");
        }

        EnsureExpected(player, expected, $"players.{id}");
        if (value is not JsonObject update)
        {
            throw new EngineException("INVALID_ARGUMENT", "set_player_slot requires an object value.");
        }

        foreach (var field in new[] { "controller", "race", "flags", "start" })
        {
            if (update[field] is not null)
            {
                player[field] = update[field]!.DeepClone();
            }
        }

        player["provenance"] = "intended_design";
        player["capability"] = "staged_typed_write";
    }

    private static void SetForce(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var index = target["index"]?.GetValue<int>() ?? throw new EngineException("INVALID_ARGUMENT", "set_force requires a numeric force index.");
        var forces = root["forces"] as JsonArray ?? throw new EngineException("INVALID_JSON", "Canonical map has no forces array.");
        var force = forces.OfType<JsonObject>().FirstOrDefault(x => x["index"]?.GetValue<int>() == index);
        if (force is null)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Force {index} was not found.");
        }

        EnsureExpected(force, expected, $"forces.{index}");
        if (value is not JsonObject update)
        {
            throw new EngineException("INVALID_ARGUMENT", "set_force requires an object value.");
        }

        foreach (var field in new[] { "name", "flags", "player_mask", "player_ids" })
        {
            if (update[field] is not null)
            {
                force[field] = update[field]!.DeepClone();
            }
        }

        force["provenance"] = "intended_design";
        force["capability"] = "staged_typed_write";
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

    private static string RequiredString(JsonObject objectNode, string name) => objectNode[name]?.GetValue<string>() is { Length: > 0 } value
        ? value
        : throw new EngineException("INVALID_ARGUMENT", $"Missing required string property '{name}'.");

    private static JsonNode RequiredNumber(JsonObject objectNode, string name) => objectNode[name] is JsonValue value && value.TryGetValue<double>(out var number)
        ? JsonValue.Create(number)!
        : throw new EngineException("INVALID_ARGUMENT", $"Missing numeric property '{name}'.");
}
