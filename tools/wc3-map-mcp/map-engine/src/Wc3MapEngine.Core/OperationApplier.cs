using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using War3Net.Build.Common;
using Wc3MapEngine.Core.Gameplay;
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
        "set_map_metadata", "create_player_slot", "set_player_slot", "delete_player_slot",
        "create_force", "set_force", "delete_force", "create_team", "set_team", "delete_team",
        "set_team_arena", "set_team_members", "create_region", "update_region", "rename_region", "delete_region", "set_region_role",
        "create_object_definition", "update_object_definition", "delete_object_definition", "set_object_reference",
        "place_object", "move_object", "update_placed_object", "remove_placed_object",
        "place_unit", "move_unit", "remove_placed_unit", "set_object_data",
        "set_script_source", "upsert_script_module", "remove_script_module", "set_trigger_mode",
        "create_trigger", "update_trigger", "move_trigger", "delete_trigger",
        "create_variable", "update_variable", "delete_variable"
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
        var operationTypes = operations.OfType<JsonObject>().Select(operation => RequiredString(operation, "type")).ToArray();
        if (operationTypes.Contains("set_trigger_mode", StringComparer.Ordinal) && operationTypes.Length != 1)
        {
            throw new EngineException("INVALID_ARGUMENT", "set_trigger_mode must be the only operation in a transaction batch.");
        }

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

        if (operationTypes.Any(IsGameplayModelOperation))
        {
            FinalizeGameplayModel(working);
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
            case "create_player_slot":
                CreatePlayer(root, target, expected, value);
                break;
            case "delete_player_slot":
                DeletePlayer(root, target, expected);
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
            case "rename_region":
                RenameRegion(root, target, expected, value);
                break;
            case "set_region_role":
                SetRegionRole(root, target, expected, value);
                break;
            case "set_player_slot":
                SetPlayer(root, target, expected, value);
                break;
            case "create_force":
                CreateForce(root, target, expected, value);
                break;
            case "delete_force":
                DeleteForce(root, target, expected);
                break;
            case "set_force":
                SetForce(root, target, expected, value);
                break;
            case "create_team":
                CreateTeam(root, target, expected, value);
                break;
            case "set_team":
                SetTeam(root, target, expected, value);
                break;
            case "delete_team":
                DeleteTeam(root, target, expected);
                break;
            case "set_team_arena":
                SetTeamField(root, target, expected, value, "arena_id");
                break;
            case "set_team_members":
                SetTeamField(root, target, expected, value, "member_player_ids");
                break;
            case "create_object_definition":
                CreateObjectDefinition(root, target, expected, value);
                break;
            case "update_object_definition":
                UpdateObjectDefinition(root, target, expected, value);
                break;
            case "delete_object_definition":
                DeleteObjectDefinition(root, target, expected);
                break;
            case "set_object_reference":
                SetObjectReference(root, target, expected, value);
                break;
            case "place_object":
            case "place_unit":
                PlaceObject(root, target, expected, value);
                break;
            case "move_object":
            case "move_unit":
                MoveObject(root, target, expected, value);
                break;
            case "update_placed_object":
                UpdatePlacedObject(root, target, expected, value);
                break;
            case "remove_placed_object":
            case "remove_placed_unit":
                RemovePlacedObject(root, target, expected);
                break;
            case "set_object_data":
                SetObjectData(root, target, expected, value);
                break;
            case "set_script_source":
                SetScriptSource(root, target, expected, value);
                break;
            case "upsert_script_module":
                UpsertScriptModule(root, target, expected, value);
                break;
            case "remove_script_module":
                RemoveScriptModule(root, target, expected);
                break;
            case "set_trigger_mode":
                SetTriggerMode(root, target, expected, value);
                break;
            case "create_trigger":
            case "update_trigger":
            case "move_trigger":
            case "delete_trigger":
                ApplyCollectionOperation(root, "triggers", type, target, expected, value);
                break;
            case "create_variable":
            case "update_variable":
            case "delete_variable":
                ApplyCollectionOperation(root, "variables", type, target, expected, value);
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
        EnsureAllowed(update, "name", "min_x", "min_y", "max_x", "max_y", "weather", "ambient_sound", "color_argb");

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
        if (update["weather"] is not null) region["weather"] = NormalizeWeather(update["weather"]!);
        if (update["ambient_sound"] is not null) { RequireStringValue(update["ambient_sound"]!, "ambient_sound"); region["ambient_sound"] = update["ambient_sound"]!.DeepClone(); }
        if (update["color_argb"] is not null) region["color_argb"] = RequiredIntValue(update["color_argb"]!, "color_argb", int.MinValue, int.MaxValue);
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
        EnsureAllowed(region, "id", "name", "min_x", "min_y", "max_x", "max_y", "creation_number", "weather", "ambient_sound", "color_argb");

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
            ["creation_number"] = region["creation_number"] is null ? NextCreationNumber(regions) : RequiredIntValue(region["creation_number"]!, "creation_number", 0, int.MaxValue),
            ["weather"] = region["weather"] is null ? WeatherType.None.ToString() : NormalizeWeather(region["weather"]!),
            ["ambient_sound"] = region["ambient_sound"]?.DeepClone() ?? string.Empty,
            ["color_argb"] = region["color_argb"]?.DeepClone() ?? 0,
            ["provenance"] = "intended_design",
            ["capability"] = "typed_write_enabled"
        };
        created["id"] = region["id"]?.DeepClone() ?? $"region:{created["creation_number"]!.GetValue<int>()}";
        if (regions.OfType<JsonObject>().Any(item => StringValue(item, "id") == StringValue(created, "id") || IntValue(item["creation_number"]) == IntValue(created["creation_number"])))
        {
            throw new EngineException("INVALID_ARGUMENT", "Region id and creation_number must be unique.");
        }
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
        EnsureNoRegionReferences(root, regions[index] as JsonObject ?? throw new EngineException("INVALID_JSON", "The region entry is not an object."));
        regions.RemoveAt(index);
    }

    private static void RenameRegion(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id", "name");
        if (value is not JsonObject update) throw new EngineException("INVALID_ARGUMENT", "rename_region requires an object value.");
        EnsureAllowed(update, "name");
        var region = FindRegion(root, target);
        EnsureExpected(region, expected, "regions.rename");
        var newName = RequiredString(update, "name");
        var oldName = RequiredString(region, "name");
        if (root["regions"]!.AsArray().OfType<JsonObject>().Any(item => !ReferenceEquals(item, region) && string.Equals(item["name"]?.GetValue<string>(), newName, StringComparison.Ordinal)))
        {
            throw new EngineException("INVALID_ARGUMENT", $"Region '{newName}' already exists.");
        }
        region["name"] = newName;
        RewriteKnownRegionReferences(root, oldName, newName, StringValue(region, "id"));
        region["provenance"] = "intended_design";
        region["capability"] = "typed_write_enabled";
    }

    private static void SetRegionRole(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "region_id", "name");
        if (value is not JsonObject roleValue) throw new EngineException("INVALID_ARGUMENT", "set_region_role requires { role }.");
        EnsureAllowed(roleValue, "role");
        var region = FindRegion(root, target);
        var regionId = StringValue(region, "id") ?? $"region:{IntValue(region["creation_number"])}";
        var roles = Collection(root, "region_roles");
        var role = roles.OfType<JsonObject>().FirstOrDefault(item => string.Equals(StringValue(item, "region_id"), regionId, StringComparison.Ordinal));
        EnsureExpectedOrAbsent(role, expected, $"region_roles.{regionId}");
        if (role is null)
        {
            role = new JsonObject { ["region_id"] = regionId };
            roles.Add(role);
        }
        role["role"] = RequiredString(roleValue, "role");
        role["provenance"] = "intended_design";
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
        EnsureAllowed(update, "name", "controller", "race", "flags", "start", "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask");
        if (update["name"] is not null) RequireStringValue(update["name"]!, "name");
        if (update["controller"] is not null) RequireStringValue(update["controller"]!, "controller");
        if (update["race"] is not null) RequireStringValue(update["race"]!, "race");
        if (update["flags"] is not null) _ = RequiredIntValue(update["flags"]!, "flags", 0, int.MaxValue);
        if (update["start"] is not null) ValidateStart(update["start"]!);
        foreach (var mask in new[] { "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask" })
        {
            if (update[mask] is not null) _ = RequiredIntValue(update[mask]!, mask, 0, int.MaxValue);
        }

        foreach (var field in new[] { "name", "controller", "race", "flags", "start", "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask" })
        {
            if (update[field] is not null)
            {
                player[field] = update[field]!.DeepClone();
                if (field == "name") player.Remove("stored_name");
            }
        }

        player["provenance"] = "intended_design";
        player["capability"] = "typed_write_enabled";
    }

    private static void CreatePlayer(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id");
        EnsureCreateExpected(expected, "create_player_slot");
        if (value is not JsonObject player) throw new EngineException("INVALID_ARGUMENT", "create_player_slot requires a player object.");
        var id = RequiredInt(target, "id", 1, 24);
        var players = RequiredArray(root, "players");
        if (players.OfType<JsonObject>().Any(item => IntValue(item["id"]) == id)) throw new EngineException("INVALID_ARGUMENT", $"Player {id} already exists.");
        EnsureAllowed(player, "id", "name", "controller", "race", "flags", "start", "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask");
        if (player["id"] is not null && RequiredIntValue(player["id"]!, "id", 1, 24) != id) throw new EngineException("INVALID_ARGUMENT", "Player target id and value id differ.");
        var created = player.DeepClone() as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Player object could not be cloned.");
        created["id"] = id;
        created["name"] = StringValue(created["name"]) ?? $"Player {id}";
        created["controller"] = StringValue(created["controller"]) ?? "User";
        created["race"] = StringValue(created["race"]) ?? "Selectable";
        created["flags"] = IntValue(created["flags"]) == int.MinValue ? 0 : created["flags"]!.DeepClone();
        created["start"] = created["start"]?.DeepClone() ?? new JsonObject { ["x"] = 0, ["y"] = 0 };
        foreach (var mask in new[] { "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask" }) created[mask] ??= 0;
        ValidateStart(created["start"]!);
        created["provenance"] = "intended_design";
        created["capability"] = "typed_write_enabled";
        players.Add(created);
    }

    private static void DeletePlayer(JsonObject root, JsonObject target, JsonNode? expected)
    {
        EnsureAllowed(target, "id");
        var id = RequiredInt(target, "id", 1, 24);
        var players = RequiredArray(root, "players");
        var player = players.OfType<JsonObject>().FirstOrDefault(item => IntValue(item["id"]) == id);
        if (player is null) throw new EngineException("INVALID_ARGUMENT", $"Player {id} was not found.");
        EnsureExpected(player, expected, $"players.{id}");
        if (root["forces"] is JsonArray forces && forces.OfType<JsonObject>().Any(force => (force["player_ids"] as JsonArray)?.Any(item => IntValue(item) == id) == true)) throw new EngineException("REFERENCE_IN_USE", $"Player {id} is still assigned to a force.");
        if (root["teams"] is JsonArray teams && teams.OfType<JsonObject>().Any(team => (team["member_player_ids"] as JsonArray)?.Any(item => IntValue(item) == id) == true)) throw new EngineException("REFERENCE_IN_USE", $"Player {id} is still assigned to a logical team.");
        players.Remove(player);
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
            if (update[field] is not null)
            {
                force[field] = update[field]!.DeepClone();
                if (field == "name") force.Remove("stored_name");
            }
        }

        if (update["player_ids"] is not null && update["player_mask"] is null) force["player_mask"] = PlayerMask(force["player_ids"]!);
        ValidateForceRecord(force);

        force["provenance"] = "intended_design";
        force["capability"] = "typed_write_enabled";
    }

    private static void CreateForce(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "index");
        EnsureCreateExpected(expected, "create_force");
        var index = RequiredInt(target, "index", 0, 23);
        var forces = RequiredArray(root, "forces");
        if (forces.OfType<JsonObject>().Any(item => IntValue(item["index"]) == index)) throw new EngineException("INVALID_ARGUMENT", $"Force {index} already exists.");
        if (value is not JsonObject force) throw new EngineException("INVALID_ARGUMENT", "create_force requires a force object.");
        EnsureAllowed(force, "index", "name", "stored_name", "flags", "player_ids", "player_mask");
        var created = force.DeepClone() as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Force object could not be cloned.");
        created["index"] = index;
        created["name"] = StringValue(created["name"]) ?? $"Force {index + 1}";
        created["flags"] ??= 0;
        created["player_ids"] ??= new JsonArray();
        ValidatePlayerIds(created["player_ids"]!);
        created["player_mask"] = created["player_mask"]?.DeepClone() ?? PlayerMask(created["player_ids"]!);
        ValidateForceRecord(created);
        created["provenance"] = "intended_design";
        created["capability"] = "typed_write_enabled";
        forces.Add(created);
    }

    private static void DeleteForce(JsonObject root, JsonObject target, JsonNode? expected)
    {
        EnsureAllowed(target, "index");
        var index = RequiredInt(target, "index", 0, 23);
        var forces = RequiredArray(root, "forces");
        var force = forces.OfType<JsonObject>().FirstOrDefault(item => IntValue(item["index"]) == index);
        if (force is null) throw new EngineException("INVALID_ARGUMENT", $"Force {index} was not found.");
        EnsureExpected(force, expected, $"forces.{index}");
        if (root["teams"] is JsonArray teams && teams.OfType<JsonObject>().Any(team => IntValue(team["force_index"]) == index)) throw new EngineException("REFERENCE_IN_USE", $"Force {index} is still assigned to a logical team.");
        forces.Remove(force);
    }

    private static void CreateTeam(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id", "team_id");
        EnsureCreateExpected(expected, "create_team");
        var teams = Collection(root, "teams");
        if (value is not JsonObject team) throw new EngineException("INVALID_ARGUMENT", "create_team requires a team object.");
        var id = StringValue(target["id"]) ?? StringValue(target["team_id"]) ?? StringValue(team["id"]) ?? throw new EngineException("INVALID_ARGUMENT", "A stable team id is required.");
        if (teams.OfType<JsonObject>().Any(item => TeamId(item) == id)) throw new EngineException("INVALID_ARGUMENT", $"Team '{id}' already exists.");
        var created = team.DeepClone() as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Team object could not be cloned.");
        created["id"] = id;
        created["member_player_ids"] ??= new JsonArray();
        EnsureAllowed(team, "id", "name", "member_player_ids", "force_index", "arena_id", "hero_ids", "life_state", "routing_state");
        ValidatePlayerIds(created["member_player_ids"]!);
        created["provenance"] = "intended_design";
        created["capability"] = "typed_write_enabled";
        teams.Add(created);
    }

    private static void SetTeam(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id", "team_id");
        var team = FindTeam(root, target);
        EnsureExpected(team, expected, "teams.set");
        if (value is not JsonObject update) throw new EngineException("INVALID_ARGUMENT", "set_team requires an object value.");
        EnsureAllowed(update, "name", "member_player_ids", "force_index", "arena_id", "hero_ids", "life_state", "routing_state");
        if (update["member_player_ids"] is not null) ValidatePlayerIds(update["member_player_ids"]!);
        foreach (var property in update) team[property.Key] = property.Value?.DeepClone();
        team["provenance"] = "intended_design";
        team["capability"] = "typed_write_enabled";
    }

    private static void DeleteTeam(JsonObject root, JsonObject target, JsonNode? expected)
    {
        EnsureAllowed(target, "id", "team_id");
        var teams = Collection(root, "teams");
        var team = FindTeam(root, target);
        EnsureExpected(team, expected, "teams.delete");
        teams.Remove(team);
    }

    private static void SetTeamField(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value, string field)
    {
        EnsureAllowed(target, "id", "team_id");
        var team = FindTeam(root, target);
        EnsureExpected(team, expected, $"teams.{field}");
        if (value is not JsonObject update) throw new EngineException("INVALID_ARGUMENT", $"The operation requires an object containing {field}.");
        EnsureAllowed(update, field);
        if (field == "member_player_ids") ValidatePlayerIds(update[field]!);
        team[field] = update[field]!.DeepClone();
        team["provenance"] = "intended_design";
        team["capability"] = "typed_write_enabled";
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

    private static void CreateObjectDefinition(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id", "category", "rawcode");
        EnsureCreateExpected(expected, "create_object_definition");
        if (value is not JsonObject definition) throw new EngineException("INVALID_ARGUMENT", "create_object_definition requires an object definition.");
        var candidate = definition.DeepClone() as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Object definition could not be cloned.");
        candidate["category"] ??= target["category"]?.DeepClone();
        candidate["rawcode"] ??= target["rawcode"]?.DeepClone();
        candidate["base_rawcode"] ??= candidate["rawcode"]?.DeepClone();
        candidate["custom_rawcode"] ??= candidate["rawcode"]?.DeepClone();
        candidate["object_kind"] ??= "custom";
        ValidateObjectDefinition(candidate);
        var definitions = Collection(root, "object_data");
        var category = StringValue(candidate, "category")!;
        var objectKind = StringValue(candidate, "object_kind") ?? "custom";
        var stableId = $"{MapComponentCodec.ObjectMemberForCategory(category)}:{(objectKind.Equals("custom", StringComparison.OrdinalIgnoreCase) ? "new" : "base")}:{StringValue(candidate, "base_rawcode")}:{StringValue(candidate, "custom_rawcode")}";
        var id = StringValue(target["id"]) ?? StringValue(candidate["id"]) ?? stableId;
        if (!string.Equals(id, stableId, StringComparison.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"Object definition id '{id}' must equal its native stable id '{stableId}' for binary round-trip.");
        if (definitions.OfType<JsonObject>().Any(item => ObjectId(item) == id || string.Equals(StringValue(item, "rawcode"), StringValue(candidate, "rawcode"), StringComparison.Ordinal))) throw new EngineException("INVALID_ARGUMENT", $"Object definition '{id}' already exists.");
        var created = candidate;
        created["id"] = id;
        created["archive_path"] = created["archive_path"]?.DeepClone() ?? MapComponentCodec.ObjectMemberForCategory(StringValue(created, "category")!);
        created["provenance"] = "intended_design";
        created["capability"] = "typed_write_enabled";
        definitions.Add(created);
    }

    private static void UpdateObjectDefinition(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var definition = FindObjectDefinition(root, target);
        EnsureExpected(definition, expected, "object_data.update");
        if (value is not JsonObject update) throw new EngineException("INVALID_ARGUMENT", "update_object_definition requires an object value.");
        EnsureAllowed(update, "unknown_ids", "modifications");
        var merged = definition.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Object definition could not be cloned.");
        foreach (var property in update) merged[property.Key] = property.Value?.DeepClone();
        ValidateObjectDefinition(merged);
        foreach (var property in update) definition[property.Key] = property.Value?.DeepClone();
        definition["provenance"] = "intended_design";
        definition["capability"] = "typed_write_enabled";
    }

    private static void DeleteObjectDefinition(JsonObject root, JsonObject target, JsonNode? expected)
    {
        var definitions = Collection(root, "object_data");
        var definition = FindObjectDefinition(root, target);
        EnsureExpected(definition, expected, "object_data.delete");
        var rawcode = StringValue(definition, "rawcode");
        if (rawcode is not null && (root["placed_objects"] as JsonArray)?.OfType<JsonObject>().Any(item => string.Equals(StringValue(item, "rawcode"), rawcode, StringComparison.Ordinal)) == true) throw new EngineException("REFERENCE_IN_USE", $"Object definition '{rawcode}' is still used by a placement.");
        definitions.Remove(definition);
    }

    private static void SetObjectReference(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id", "category", "rawcode", "relation");
        var definition = FindObjectDefinition(root, target);
        var relation = StringValue(target["relation"]) ?? throw new EngineException("INVALID_ARGUMENT", "set_object_reference requires a typed relation.");
        if (relation is not ("ability" or "item" or "upgrade" or "owner" or "region")) throw new EngineException("UNSUPPORTED_OPERATION", $"Object relation '{relation}' is not supported.");
        var references = definition["references"] as JsonObject ?? new JsonObject();
        EnsureExpectedOrAbsent(references[relation], expected, $"object_data.references.{relation}");
        if (value is null) throw new EngineException("INVALID_ARGUMENT", "set_object_reference requires a reference value.");
        references[relation] = value.DeepClone();
        definition["references"] = references;
        definition["provenance"] = "intended_design";
        definition["capability"] = "typed_write_enabled";
    }

    private static void PlaceObject(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id");
        EnsureCreateExpected(expected, "place_object");
        if (value is not JsonObject placement) throw new EngineException("INVALID_ARGUMENT", "place_object requires a placement object.");
        var placements = Collection(root, "placed_objects");
        var normalized = NormalizePlacement(root, target, placement);
        var id = RequiredString(normalized, "id");
        if (placements.OfType<JsonObject>().Any(item => string.Equals(StringValue(item, "id"), id, StringComparison.Ordinal))) throw new EngineException("INVALID_ARGUMENT", $"Placement '{id}' already exists.");
        ValidatePlacement(normalized);
        placements.Add(normalized);
    }

    private static void MoveObject(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var placement = FindPlacement(root, target);
        EnsureExpected(placement, expected, "placed_objects.move");
        if (value is not JsonObject position) throw new EngineException("INVALID_ARGUMENT", "move_object requires a position object.");
        EnsureAllowed(position, "position");
        ValidatePosition(position["position"] ?? throw new EngineException("INVALID_ARGUMENT", "move_object requires position."));
        placement["position"] = position["position"]!.DeepClone();
        placement["provenance"] = "intended_design";
    }

    private static void UpdatePlacedObject(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var placement = FindPlacement(root, target);
        EnsureExpected(placement, expected, "placed_objects.update");
        if (value is not JsonObject update) throw new EngineException("INVALID_ARGUMENT", "update_placed_object requires an object value.");
        EnsureAllowed(update, "rawcode", "skin_rawcode", "owner_id", "flags", "hit_points", "mana_points", "hero_level", "hero_strength", "hero_agility", "hero_intelligence", "inventory", "abilities", "variation", "position", "facing", "scale", "map_region_role", "waygate_destination_region_id", "custom_player_color_id", "life", "state");
        foreach (var property in update) placement[property.Key] = property.Value?.DeepClone();
        ValidatePlacement(placement);
        placement["provenance"] = "intended_design";
        placement["capability"] = "typed_write_enabled";
    }

    private static void RemovePlacedObject(JsonObject root, JsonObject target, JsonNode? expected)
    {
        var placements = Collection(root, "placed_objects");
        var placement = FindPlacement(root, target);
        EnsureExpected(placement, expected, "placed_objects.remove");
        placements.Remove(placement);
    }

    private static void SetObjectData(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
        => UpdateObjectDefinition(root, target, expected, value);

    private static void UpsertScriptModule(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id");
        if (value is not JsonObject module) throw new EngineException("INVALID_ARGUMENT", "upsert_script_module requires a module object.");
        EnsureAllowed(module, "id", "path", "dependencies", "source", "source_sha256", "enabled", "public_symbols", "provenance", "capability");
        var modules = Collection(root, "gameplay_modules");
        var id = StringValue(target["id"]) ?? StringValue(module["id"]) ?? throw new EngineException("INVALID_ARGUMENT", "A stable module id is required.");
        if (module["id"] is not null && !string.Equals(StringValue(module["id"]), id, StringComparison.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"Module value id '{StringValue(module["id"])}' does not match target id '{id}'.");
        var existing = modules.OfType<JsonObject>().FirstOrDefault(item => StringValue(item, "id") == id);
        EnsureModuleExpected(existing, expected, id);
        if (StringValue(module, "source") is not { Length: > 0 } source) throw new EngineException("INVALID_ARGUMENT", $"upsert_script_module '{id}' requires complete source text so the composed entry point remains reproducible.");
        var declaredSourceHash = StringValue(module, "source_sha256");
        module["source"] = NormalizeGameplaySource(source);
        var computedSourceHash = Hashing.Sha256(Encoding.UTF8.GetBytes(module["source"]!.GetValue<string>()));
        if (declaredSourceHash is not null && !string.Equals(declaredSourceHash, computedSourceHash, StringComparison.OrdinalIgnoreCase)) throw new EngineException("SOURCE_CHANGED", $"Gameplay module '{id}' source_sha256 does not match its source text.");
        module["source_sha256"] = computedSourceHash;
        GameplayModelValidator.ValidateModule(module, requireSource: true);
        if (existing is null)
        {
            var created = module.DeepClone() as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Module could not be cloned.");
            created["id"] = id;
            modules.Add(created);
        }
        else
        {
            foreach (var property in module) existing[property.Key] = property.Value?.DeepClone();
        }
    }

    private static void RemoveScriptModule(JsonObject root, JsonObject target, JsonNode? expected)
    {
        EnsureAllowed(target, "id");
        var modules = Collection(root, "gameplay_modules");
        var id = RequiredString(target, "id");
        var module = modules.OfType<JsonObject>().FirstOrDefault(item => StringValue(item, "id") == id) ?? throw new EngineException("INVALID_ARGUMENT", $"Script module '{id}' was not found.");
        EnsureModuleExpected(module, expected, id);
        modules.Remove(module);
    }

    private static void SetTriggerMode(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target);
        var current = root["trigger_mode"];
        EnsureExpected(current, expected, "trigger_mode");
        var mode = value is JsonValue text && text.TryGetValue<string>(out var direct) ? direct : value is JsonObject objectValue ? RequiredString(objectValue, "mode") : throw new EngineException("INVALID_ARGUMENT", "set_trigger_mode requires mcp_native or editor_compatible.");
        if (mode is not ("mcp_native" or "mcp_native_jass" or "editor_compatible")) throw new EngineException("INVALID_ARGUMENT", "Trigger mode must be mcp_native_jass or editor_compatible.");
        if (mode == "editor_compatible") throw new EngineException("CAPABILITY_GATED", "Exact-version GUI trigger round-trip and editor evidence are not available for this map.");
        root["trigger_mode"] = "mcp_native_jass";
    }

    private static void ApplyCollectionOperation(JsonObject root, string collectionName, string type, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, collectionName == "triggers" ? new[] { "id", "trigger_id" } : new[] { "id", "variable_id" });
        var ownedCollectionName = collectionName switch
        {
            "triggers" => "gameplay_triggers",
            "variables" => "gameplay_variables",
            _ => collectionName
        };
        var collection = Collection(root, ownedCollectionName);
        var identity = StringValue(target, "id") ?? StringValue(target, "variable_id") ?? StringValue(target, "trigger_id") ?? throw new EngineException("INVALID_ARGUMENT", $"{type} requires a stable id target.");
        var existing = collection.OfType<JsonObject>().FirstOrDefault(item => StringValue(item, "id") == identity);
        if (type.StartsWith("create", StringComparison.Ordinal))
        {
            EnsureCreateExpected(expected, type);
            if (existing is not null) throw new EngineException("INVALID_ARGUMENT", $"{collectionName} entry '{identity}' already exists.");
            if (value is not JsonObject created) throw new EngineException("INVALID_ARGUMENT", $"{type} requires an object value.");
            EnsureAllowed(created, collectionName == "triggers"
                ? new[] { "id", "name", "folder_path", "folder", "enabled", "initially_on", "events", "conditions", "actions", "references", "source_location", "handler_name", "dependencies", "editor_encoding", "provenance", "capability" }
                : new[] { "id", "name", "type", "initial", "default_value", "value", "dependencies", "provenance", "capability" });
            var clone = created.DeepClone() as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Collection value could not be cloned.");
            if (clone["id"] is not null && !string.Equals(StringValue(clone["id"]), identity, StringComparison.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"{collectionName} value id '{StringValue(clone["id"])}' does not match target id '{identity}'.");
            clone["id"] = identity;
            if (collectionName == "triggers") ValidateTrigger(clone);
            else ValidateVariable(clone);
            collection.Add(clone);
            return;
        }

        if (existing is null) throw new EngineException("INVALID_ARGUMENT", $"{collectionName} entry '{identity}' was not found.");
        EnsureExpected(existing, expected, $"{collectionName}.{identity}");
        if (type.StartsWith("delete", StringComparison.Ordinal))
        {
            collection.Remove(existing);
            return;
        }

        if (value is not JsonObject update) throw new EngineException("INVALID_ARGUMENT", $"{type} requires an object value.");
        if (type == "move_trigger")
        {
            var move = value as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "move_trigger requires an object value.");
            EnsureAllowed(move, "folder_path", "folder");
            if (move["folder_path"] is null && move["folder"] is not null) move["folder_path"] = move["folder"]!.DeepClone();
        }
        else
        {
            EnsureAllowed(update, "name", "folder_path", "folder", "enabled", "initially_on", "events", "conditions", "actions", "references", "source_location", "handler_name", "dependencies", "editor_encoding", "provenance", "capability", "type", "initial", "default_value", "value");
        }
        foreach (var property in update) existing[property.Key] = property.Value?.DeepClone();
        if (collectionName == "triggers") ValidateTrigger(existing);
        else ValidateVariable(existing);
    }

    private static bool IsGameplayModelOperation(string type)
        => type is "upsert_script_module" or "remove_script_module" or "create_trigger" or "update_trigger" or "move_trigger" or "delete_trigger" or "create_variable" or "update_variable" or "delete_variable";

    private static void FinalizeGameplayModel(JsonObject root)
    {
        GameplayModelValidator.ValidateCollections(root, requireModuleSources: true);
        var composition = GameplaySourceComposer.ComposeCanonical(root, StringValue(root["profile"]));
        var source = RequiredString(composition, "source");
        var scripts = RequiredArray(root, "scripts");
        var script = scripts.OfType<JsonObject>().FirstOrDefault(item => string.Equals(StringValue(item, "archive_path"), "war3map.j", StringComparison.OrdinalIgnoreCase))
            ?? throw new EngineException("UNSUPPORTED_COMPONENT", "Gameplay model changes require an existing war3map.j archive member.");
        var bytes = Encoding.UTF8.GetBytes(source);
        var hash = Hashing.Sha256(bytes);
        script["language"] = "Jass";
        script["source"] = source;
        script["source_sha256"] = hash;
        script["sha256"] = hash;
        script["size_bytes"] = bytes.Length;
        script["provenance"] = "intended_design";
        script["capability"] = "staged_typed_write";
        root["trigger_mode"] = GameplayModelValidator.NativeMode;
        root["gameplay_source"] = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["composer_version"] = composition["composer_version"]!.DeepClone(),
            ["mode"] = composition["mode"]!.DeepClone(),
            ["profile"] = composition["profile"]!.DeepClone(),
            ["source_sha256"] = hash,
            ["source_manifest_sha256"] = composition["source_manifest_sha256"]!.DeepClone(),
            ["source_manifest"] = composition["source_manifest"]!.DeepClone(),
            ["static_validation"] = composition["static_validation"]!.DeepClone(),
            ["provenance"] = "intended_design",
            ["capability"] = "staged_typed_write"
        };
    }

    private static void EnsureModuleExpected(JsonObject? actual, JsonNode? expected, string id)
    {
        if (actual is null)
        {
            EnsureCreateExpected(expected, $"gameplay_modules.{id}");
            return;
        }
        if (expected is JsonValue value && value.TryGetValue<string>(out var hash))
        {
            var actualHash = StringValue(actual, "source_sha256") ?? GameplayModelValidator.Hash(actual);
            if (!string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase)) throw new EngineException("PRECONDITION_FAILED", $"The expected source hash for gameplay module '{id}' does not match the staged value.");
            return;
        }
        if (expected is JsonObject expectedObject && expectedObject["source_sha256"] is JsonValue sourceHash && sourceHash.TryGetValue<string>(out var moduleHash))
        {
            var actualHash = StringValue(actual, "source_sha256") ?? GameplayModelValidator.Hash(actual);
            if (!string.Equals(actualHash, moduleHash, StringComparison.OrdinalIgnoreCase)) throw new EngineException("PRECONDITION_FAILED", $"The expected source hash for gameplay module '{id}' does not match the staged value.");
            return;
        }
        EnsureExpected(actual, expected, $"gameplay_modules.{id}");
    }

    private static string NormalizeGameplaySource(string source)
        => source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + "\n";

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

    private static JsonArray Collection(JsonObject root, string name)
    {
        if (root[name] is JsonArray values) return values;
        if (root[name] is not null) throw new EngineException("CAPABILITY_GATED", $"Canonical component '{name}' is opaque and cannot be changed through this typed operation.");
        var created = new JsonArray();
        root[name] = created;
        return created;
    }

    private static JsonObject FindRegion(JsonObject root, JsonObject target)
    {
        var regions = RequiredArray(root, "regions");
        var id = StringValue(target, "id") ?? StringValue(target, "region_id");
        var name = StringValue(target, "name");
        return regions.OfType<JsonObject>().FirstOrDefault(item => (id is not null && StringValue(item, "id") == id) || (name is not null && StringValue(item, "name") == name))
            ?? throw new EngineException("INVALID_ARGUMENT", "The target region was not found.");
    }

    private static JsonObject FindTeam(JsonObject root, JsonObject target)
    {
        var id = StringValue(target, "id") ?? StringValue(target, "team_id") ?? throw new EngineException("INVALID_ARGUMENT", "A stable team id is required.");
        return Collection(root, "teams").OfType<JsonObject>().FirstOrDefault(item => TeamId(item) == id)
            ?? throw new EngineException("INVALID_ARGUMENT", $"Team '{id}' was not found.");
    }

    private static JsonObject FindObjectDefinition(JsonObject root, JsonObject target)
    {
        var id = StringValue(target, "id");
        var rawcode = StringValue(target, "rawcode");
        var category = StringValue(target, "category");
        return Collection(root, "object_data").OfType<JsonObject>().FirstOrDefault(item =>
            (id is not null && ObjectId(item) == id)
            || (rawcode is not null && StringValue(item, "rawcode") == rawcode && (category is null || StringValue(item, "category") == category)))
            ?? throw new EngineException("INVALID_ARGUMENT", "The target object definition was not found.");
    }

    private static JsonObject FindPlacement(JsonObject root, JsonObject target)
    {
        EnsureAllowed(target, "id", "creation_number");
        var id = StringValue(target, "id");
        var creation = target["creation_number"] is JsonValue number && number.TryGetValue<int>(out var value) ? value : int.MinValue;
        return Collection(root, "placed_objects").OfType<JsonObject>().FirstOrDefault(item =>
            (id is not null && StringValue(item, "id") == id)
            || (creation != int.MinValue && IntValue(item["creation_number"]) == creation))
            ?? throw new EngineException("INVALID_ARGUMENT", "The target placement was not found.");
    }

    private static JsonObject NormalizePlacement(JsonObject root, JsonObject target, JsonObject value)
    {
        EnsureAllowed(value, "id", "member", "kind", "rawcode", "skin_rawcode", "owner_id", "flags", "unknown_1", "unknown_2", "hit_points", "mana_points", "gold_amount", "target_acquisition", "hero_level", "hero_strength", "hero_agility", "hero_intelligence", "inventory", "abilities", "random_data_mode", "random_data", "custom_player_color_id", "waygate_destination_region_id", "variation", "position", "facing", "scale", "map_item_table_id", "item_table_sets", "creation_number", "state", "life", "map_region_role");
        var result = value.DeepClone() as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Placement could not be cloned.");
        var kind = StringValue(result, "kind") ?? "unit";
        result["kind"] = kind;
        result["member"] = StringValue(result, "member") ?? (kind is "doodad" or "special_doodad" ? "war3map.doo" : "war3mapUnits.doo");
        if ((kind is "doodad" or "special_doodad") != result["member"]!.GetValue<string>().Equals("war3map.doo", StringComparison.OrdinalIgnoreCase)) throw new EngineException("INVALID_ARGUMENT", "Placement kind and archive member do not agree.");
        if (kind is not ("unit" or "item" or "building" or "doodad" or "special_doodad")) throw new EngineException("INVALID_ARGUMENT", $"Placement kind '{kind}' is not supported.");
        result["creation_number"] ??= NextCreationNumber(Collection(root, "placed_objects"));
        var derivedId = $"{kind}:{IntValue(result["creation_number"])}";
        var requestedId = StringValue(target, "id") ?? StringValue(result, "id");
        if (requestedId is not null && !string.Equals(requestedId, derivedId, StringComparison.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"Placement id '{requestedId}' must equal its native stable id '{derivedId}' for binary round-trip.");
        result["id"] = derivedId;
        result["rawcode"] = StringValue(result, "rawcode") ?? throw new EngineException("INVALID_ARGUMENT", "Placement rawcode is required.");
        result["skin_rawcode"] ??= result["rawcode"]!.DeepClone();
        result["owner_id"] ??= 1;
        result["flags"] ??= 0;
        result["unknown_1"] ??= 0;
        result["unknown_2"] ??= 0;
        result["hit_points"] ??= -1;
        result["mana_points"] ??= -1;
        result["gold_amount"] ??= 0;
        result["target_acquisition"] ??= 0;
        result["hero_level"] ??= 0;
        result["hero_strength"] ??= 0;
        result["hero_agility"] ??= 0;
        result["hero_intelligence"] ??= 0;
        result["inventory"] ??= new JsonArray();
        result["abilities"] ??= new JsonArray();
        result["custom_player_color_id"] ??= -1;
        result["waygate_destination_region_id"] ??= -1;
        result["variation"] ??= 0;
        result["position"] ??= new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 };
        result["facing"] ??= 0;
        result["scale"] ??= new JsonObject { ["x"] = 1, ["y"] = 1, ["z"] = 1 };
        result["map_item_table_id"] ??= -1;
        result["item_table_sets"] ??= new JsonArray();
        if (kind is "doodad" or "special_doodad")
        {
            result["state"] ??= "Normal";
            result["life"] ??= 100;
        }
        result["provenance"] = "intended_design";
        result["capability"] = "typed_write_enabled";
        return result;
    }

    private static void ValidatePlacement(JsonObject placement)
    {
        var rawcode = StringValue(placement, "rawcode");
        if (rawcode is null || rawcode.Length != 4 || rawcode.Any(character => character < 0x20 || character > 0x7E)) throw new EngineException("INVALID_ARGUMENT", "Placement rawcode must contain exactly four printable ASCII characters.");
        var owner = IntValue(placement["owner_id"]);
        if (owner is < 1 or > 24) throw new EngineException("INVALID_ARGUMENT", "Placement owner_id must be between 1 and 24.");
        ValidatePosition(placement["position"] ?? throw new EngineException("INVALID_ARGUMENT", "Placement position is required."));
        ValidatePosition(placement["scale"] ?? throw new EngineException("INVALID_ARGUMENT", "Placement scale is required."));
        if (!double.TryParse(placement["facing"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var facing) || !double.IsFinite(facing)) throw new EngineException("INVALID_ARGUMENT", "Placement facing must be finite.");
    }

    private static void ValidatePosition(JsonNode value)
    {
        if (value is not JsonObject position || !Finite(position["x"]) || !Finite(position["y"]) || !Finite(position["z"])) throw new EngineException("INVALID_ARGUMENT", "Position must contain finite x, y, and z values.");
    }

    private static bool Finite(JsonNode? value)
        => value is JsonValue node && (node.TryGetValue<double>(out var number) ? double.IsFinite(number) : node.TryGetValue<float>(out var single) && float.IsFinite(single));

    private static void ValidateObjectDefinition(JsonObject definition)
    {
        var category = StringValue(definition, "category");
        if (category is not ("unit" or "ability" or "item" or "destructable" or "doodad" or "buff" or "upgrade")) throw new EngineException("INVALID_ARGUMENT", "Object definition category is not supported.");
        var objectKind = StringValue(definition, "object_kind");
        if (objectKind is not ("base" or "custom")) throw new EngineException("INVALID_ARGUMENT", "Object definition object_kind must be base or custom.");
        foreach (var field in new[] { "rawcode", "base_rawcode", "custom_rawcode" })
        {
            if (StringValue(definition, field) is { } rawcode && (rawcode.Length != 4 || rawcode.Any(character => character < 0x20 || character > 0x7E))) throw new EngineException("INVALID_ARGUMENT", $"Object definition {field} must be exactly four printable ASCII characters.");
        }
        if (StringValue(definition, "rawcode") is null) throw new EngineException("INVALID_ARGUMENT", "Object definition rawcode is required.");
        if (StringValue(definition, "base_rawcode") is null || StringValue(definition, "custom_rawcode") is null) throw new EngineException("INVALID_ARGUMENT", "Object definition base_rawcode and custom_rawcode are required.");
        if (definition["unknown_ids"] is not null && definition["unknown_ids"] is not JsonArray) throw new EngineException("INVALID_ARGUMENT", "Object definition unknown_ids must be an array.");
        foreach (var unknownId in (definition["unknown_ids"] as JsonArray ?? new JsonArray())) RequiredRawcodeValue(unknownId ?? throw new EngineException("INVALID_ARGUMENT", "Object definition unknown_ids cannot contain null."));
        if (definition["modifications"] is not null && definition["modifications"] is not JsonArray) throw new EngineException("INVALID_ARGUMENT", "Object definition modifications must be an array.");
        if (definition["modifications"] is JsonArray modifications)
        {
            foreach (var node in modifications)
            {
                if (node is not JsonObject modification) throw new EngineException("INVALID_ARGUMENT", "Every object-data modification must be an object.");
                EnsureAllowed(modification, "id", "type", "value", "level", "pointer", "variation");
                if (StringValue(modification, "id") is not { Length: 4 } id || id.Any(character => character < 0x20 || character > 0x7E)) throw new EngineException("INVALID_ARGUMENT", "Object-data modification id must be four printable ASCII characters.");
                var type = StringValue(modification, "type");
                if (type is not ("Int" or "Real" or "Unreal" or "String" or "Bool" or "Char")) throw new EngineException("INVALID_ARGUMENT", "Object-data modification type is unsupported.");
                ValidateObjectModificationValue(modification["value"], type);
                if (category is "ability" or "upgrade")
                {
                    _ = RequiredIntValue(modification["level"] ?? throw new EngineException("INVALID_ARGUMENT", "Level-based object-data modifications require level."), "level", 0, int.MaxValue);
                    _ = RequiredIntValue(modification["pointer"] ?? throw new EngineException("INVALID_ARGUMENT", "Level-based object-data modifications require pointer."), "pointer", 0, int.MaxValue);
                    if (modification["variation"] is not null) throw new EngineException("INVALID_ARGUMENT", "Level-based object-data modifications cannot contain variation.");
                }
                else if (category == "doodad")
                {
                    _ = RequiredIntValue(modification["variation"] ?? throw new EngineException("INVALID_ARGUMENT", "Variation-based object-data modifications require variation."), "variation", 0, int.MaxValue);
                    _ = RequiredIntValue(modification["pointer"] ?? throw new EngineException("INVALID_ARGUMENT", "Variation-based object-data modifications require pointer."), "pointer", 0, int.MaxValue);
                    if (modification["level"] is not null) throw new EngineException("INVALID_ARGUMENT", "Variation-based object-data modifications cannot contain level.");
                }
                else if (modification["level"] is not null || modification["pointer"] is not null || modification["variation"] is not null)
                {
                    throw new EngineException("INVALID_ARGUMENT", "Simple object-data modifications cannot contain level, pointer, or variation.");
                }
            }
        }
    }

    private static void ValidateObjectModificationValue(JsonNode? value, string type)
    {
        if (value is not JsonValue json) throw new EngineException("INVALID_ARGUMENT", "Object-data modifications require a typed scalar value.");
        var valid = type switch
        {
            "Int" => json.TryGetValue<int>(out _),
            "Real" or "Unreal" => json.TryGetValue<double>(out var real) && double.IsFinite(real),
            "String" => json.TryGetValue<string>(out _),
            "Bool" => json.TryGetValue<bool>(out _),
            "Char" => json.TryGetValue<string>(out var character) && character.Length == 1,
            _ => false
        };
        if (!valid) throw new EngineException("INVALID_ARGUMENT", $"Object-data modification value does not match type {type}.");
    }

    private static void RequiredRawcodeValue(JsonNode value)
    {
        var rawcode = StringValue(value);
        if (rawcode is null || rawcode.Length != 4 || rawcode.Any(character => character < 0x20 || character > 0x7E)) throw new EngineException("INVALID_ARGUMENT", "Object-data unknown_ids must contain four printable ASCII rawcodes.");
    }

    private static void ValidateForceRecord(JsonObject force)
    {
        ValidatePlayerIds(force["player_ids"] ?? throw new EngineException("INVALID_ARGUMENT", "Force player_ids must be an array."));
        if (force["player_mask"] is JsonValue mask && mask.TryGetValue<int>(out var value) && value >= 0 && value != PlayerMask(force["player_ids"]!))
        {
            throw new EngineException("INVALID_ARGUMENT", "Force player_mask must match player_ids.");
        }
    }

    private static void ValidateTrigger(JsonObject trigger)
    {
        if (StringValue(trigger, "id") is null || StringValue(trigger, "name") is null) throw new EngineException("INVALID_ARGUMENT", "Triggers require stable id and exact name.");
        foreach (var field in new[] { "events", "conditions", "actions" })
        {
            if (trigger[field] is not null && trigger[field] is not JsonArray) throw new EngineException("INVALID_ARGUMENT", $"Trigger {field} must be an array.");
        }
    }

    private static void ValidateVariable(JsonObject variable)
    {
        if (StringValue(variable, "id") is null || StringValue(variable, "name") is null) throw new EngineException("INVALID_ARGUMENT", "Variables require stable id and exact name.");
        if (variable["type"] is not null && StringValue(variable["type"]) is null) throw new EngineException("INVALID_ARGUMENT", "Variable type must be a string.");
    }

    private static void RewriteKnownRegionReferences(JsonObject root, string oldName, string newName, string? regionId)
    {
        foreach (var section in new[] { "triggers", "variables", "gameplay_triggers", "gameplay_variables", "teams", "region_roles" })
        {
            if (root[section] is not JsonNode value) continue;
            Rewrite(value, oldName, newName, regionId);
        }
    }

    private static void Rewrite(JsonNode node, string oldName, string newName, string? regionId)
    {
        if (node is JsonObject objectValue)
        {
            foreach (var property in objectValue.ToList())
            {
                if (property.Key is "region_name" or "region" or "region_handle" && StringValue(property.Value) == oldName) objectValue[property.Key] = newName;
                else if (property.Key == "region_id" && regionId is not null && StringValue(property.Value) == regionId) objectValue[property.Key] = regionId;
                else if (property.Value is not null) Rewrite(property.Value, oldName, newName, regionId);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.Where(item => item is not null).Cast<JsonNode>()) Rewrite(item, oldName, newName, regionId);
        }
    }

    private static void EnsureNoRegionReferences(JsonObject root, JsonObject region)
    {
        var name = StringValue(region, "name");
        var id = StringValue(region, "id");
        foreach (var section in new[] { "triggers", "variables", "gameplay_triggers", "gameplay_variables", "teams", "region_roles" })
        {
            if (root[section] is not JsonNode value) continue;
            if (ContainsRegionReference(value, name, id)) throw new EngineException("REFERENCE_IN_USE", $"Region '{name}' still has a live MCP reference in {section}.");
        }
    }

    private static bool ContainsRegionReference(JsonNode node, string? name, string? id)
    {
        if (node is JsonObject objectValue)
        {
            foreach (var property in objectValue)
            {
                if (property.Key is "region_name" or "region" or "region_handle" && name is not null && StringValue(property.Value) == name) return true;
                if (property.Key == "region_id" && id is not null && StringValue(property.Value) == id) return true;
                if (property.Value is not null && ContainsRegionReference(property.Value, name, id)) return true;
            }
        }
        else if (node is JsonArray array)
        {
            return array.Where(item => item is not null).Cast<JsonNode>().Any(item => ContainsRegionReference(item, name, id));
        }
        return false;
    }

    private static void EnsureCreateExpected(JsonNode? expected, string operation)
    {
        if (expected is not null) throw new EngineException("INVALID_ARGUMENT", $"{operation} does not accept an expected prior value; a missing target is its precondition.");
    }

    private static int PlayerMask(JsonNode value)
        => (value as JsonArray ?? throw new EngineException("INVALID_ARGUMENT", "player_ids must be an array.")).Aggregate(0, (mask, item) => mask | (1 << (RequiredIntValue(item ?? throw new EngineException("INVALID_ARGUMENT", "player_ids cannot contain null."), "player_id", 1, 24) - 1)));

    private static int NextCreationNumber(JsonArray values)
        => values.OfType<JsonObject>().Select(item => IntValue(item["creation_number"])).Where(number => number >= 0).DefaultIfEmpty(-1).Max() + 1;

    private static string? StringValue(JsonObject value, string property)
        => value[property] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static string? StringValue(JsonNode? value)
        => value is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static string? TeamId(JsonObject value) => StringValue(value, "id") ?? StringValue(value, "team_id");
    private static string? ObjectId(JsonObject value) => StringValue(value, "id") ?? StringValue(value, "rawcode");

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

    private static void EnsureExpectedOrAbsent(JsonNode? actual, JsonNode? expected, string field)
    {
        if (actual is null && expected is null) return;
        EnsureExpected(actual, expected, field);
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

    private static string NormalizeWeather(JsonNode value)
    {
        var text = StringValue(value);
        if (text is null || !Enum.TryParse<WeatherType>(text, true, out var weather)) throw new EngineException("INVALID_ARGUMENT", "Region weather must be a known Warcraft III weather enum value.");
        return weather.ToString();
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
