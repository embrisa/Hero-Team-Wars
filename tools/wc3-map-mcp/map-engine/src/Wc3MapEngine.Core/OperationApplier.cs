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
        "set_team_arena", "set_team_members", "create_region", "update_region", "rename_region", "delete_region", "reorder_regions", "set_region_role",
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
        "title", "suggested_players", "map_flags"
    };

    public static JsonObject Apply(JsonNode canonical, JsonArray operations)
    {
        if (canonical is not JsonObject root)
        {
            throw new EngineException("INVALID_JSON", "Canonical map root must be an object.");
        }

        var working = root.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Canonical map could not be cloned.");
        RefreshRegionReferences(working);
        var allChanges = new JsonArray();
        var referenceRewrites = new JsonArray();
        var applied = new JsonArray();
        var operationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operationTypes = operations.OfType<JsonObject>().Select(operation => RequiredString(operation, "type")).ToArray();
        if (operationTypes.Contains("set_trigger_mode", StringComparer.Ordinal) && operationTypes.Length != 1)
        {
            throw new EngineException("INVALID_ARGUMENT", "set_trigger_mode must be the only operation in a transaction batch.");
        }

        var orderedOperations = OrderOperations(operations);
        foreach (var operationNode in orderedOperations)
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
            RefreshRegionReferences(working);
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
                if (string.Equals(operation["type"]?.GetValue<string>(), "rename_region", StringComparison.Ordinal)
                    && IsReferenceRewritePath(change["path"]?.GetValue<string>()))
                {
                    referenceRewrites.Add(new JsonObject
                    {
                        ["operation_id"] = operationId,
                        ["component"] = change["component"]?.DeepClone(),
                        ["path"] = change["path"]?.DeepClone(),
                        ["before"] = change["before"]?.DeepClone(),
                        ["after"] = change["after"]?.DeepClone(),
                        ["kind"] = "generated_reference_rewrite",
                        ["provenance"] = "derived"
                    });
                }
            }

            applied.Add(operationId);
        }

        if (operationTypes.Any(IsTeamStructureOperation) && working["teams"] is JsonArray teamRecords)
        {
            working["team_registry"] = HtwProfileModel.BuildTeamRegistry(teamRecords);
        }

        // A direct source replacement is authoritative for war3map.j. If it
        // was not produced by GameplayService.prepare, discard the generated
        // source manifest so validation cannot silently pair unrelated JASS
        // text with the old module graph. The profile/team metadata remains
        // staged and can still be validated independently.
        var directScriptReplacement = operationTypes.Contains("set_script_source", StringComparer.Ordinal)
            && !operations.OfType<JsonObject>().Any(operation =>
                operation["type"]?.GetValue<string>() == "set_script_source"
                && operation["value"] is JsonObject value
                && value["source_strategy"]?.GetValue<string>() == "composed");
        if (directScriptReplacement && HasGameplayModel(working))
        {
            foreach (var field in new[] { "trigger_mode", "gameplay_source", "gameplay_modules", "gameplay_triggers", "gameplay_variables" })
            {
                working.Remove(field);
            }
        }

        if (operationTypes.Any(type => type != "rename_region" && IsGeneratedGameplayOperation(type))
            || operationTypes.Contains("rename_region", StringComparer.Ordinal) && HasGameplayModel(working)
            || operationTypes.Any(IsTeamStructureOperation) && HasGameplayModel(working))
        {
            FinalizeGameplayModel(working);
        }

        return new JsonObject
        {
            ["canonical_map"] = working,
            ["diff"] = new JsonObject
            {
                ["schema_version"] = "1.0",
                ["changes"] = allChanges,
                ["groups"] = GroupChanges(allChanges),
                ["reference_rewrites"] = referenceRewrites,
                ["dependency_order"] = new JsonArray(orderedOperations.Select(operation => (JsonNode?)new JsonObject
                {
                    ["operation_id"] = operation["operation_id"]?.DeepClone(),
                    ["type"] = operation["type"]?.DeepClone(),
                    ["phase"] = OperationPhase(operation["type"]?.GetValue<string>() ?? string.Empty)
                }).ToArray())
            },
            ["applied_operation_ids"] = applied
        };
    }

    private static IReadOnlyList<JsonObject> OrderOperations(JsonArray operations)
    {
        return operations.OfType<JsonObject>()
            .Select((operation, index) => (operation, index))
            .OrderBy(item => OperationPhase(RequiredString(item.operation, "type")))
            .ThenBy(item => item.index)
            .Select(item => item.operation)
            .ToArray();
    }

    private static int OperationPhase(string type) => type switch
    {
        "set_map_metadata" => 0,
        "create_player_slot" or "set_player_slot" => 10,
        "create_force" or "set_force" or "create_team" or "set_team" or "set_team_arena" or "set_team_members" or "delete_team" => 20,
        "delete_player_slot" or "delete_force" => 25,
        "create_region" or "update_region" or "rename_region" or "reorder_regions" or "set_region_role" or "delete_region" => 30,
        "create_object_definition" or "update_object_definition" or "set_object_data" => 40,
        "place_object" or "place_unit" or "move_object" or "move_unit" or "update_placed_object" or "remove_placed_object" or "remove_placed_unit" => 50,
        "set_object_reference" => 55,
        "delete_object_definition" => 70,
        "upsert_script_module" or "remove_script_module" or "create_trigger" or "update_trigger" or "move_trigger" or "delete_trigger" or "create_variable" or "update_variable" or "delete_variable" => 80,
        "set_script_source" => 85,
        "set_trigger_mode" => 90,
        _ => 100
    };

    private static bool IsReferenceRewritePath(string? path)
        => path is not null && (path.Contains("references", StringComparison.OrdinalIgnoreCase)
            || path.Contains("region_name", StringComparison.OrdinalIgnoreCase)
            || path.Contains("region_roles", StringComparison.OrdinalIgnoreCase));

    private static JsonArray GroupChanges(JsonArray changes)
    {
        var groups = new JsonArray();
        foreach (var group in changes.OfType<JsonObject>().GroupBy(change => change["component"]?.GetValue<string>() ?? "unknown", StringComparer.Ordinal))
        {
            groups.Add(new JsonObject
            {
                ["component"] = group.Key,
                ["change_count"] = group.Count(),
                ["changes"] = new JsonArray(group.Select(change => (JsonNode?)change.DeepClone()).ToArray())
            });
        }

        return groups;
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
            case "reorder_regions":
                ReorderRegions(root, target, expected, value);
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
        if (field == "map_flags")
        {
            _ = RequiredIntValue(value, "map_flags", 0, int.MaxValue);
        }

        entry["value"] = value.DeepClone();
        entry["provenance"] = "intended_design";
        entry["capability"] = "typed_write_enabled";
    }

    private static void UpdateRegion(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id", "region_id", "name", "creation_number");
        var regions = RequiredArray(root, "regions");
        var region = FindRegion(root, target);
        var name = RequiredString(region, "name");

        EnsureExpected(region, expected, $"regions.{name}");
        if (value is not JsonObject update)
        {
            throw new EngineException("INVALID_ARGUMENT", "update_region requires an object value.");
        }
        EnsureAllowed(update, "min_x", "min_y", "max_x", "max_y", "weather", "ambient_sound", "color_argb");

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
        ValidateRegionBounds(root, region, name);

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
            ["color_argb"] = region["color_argb"] is null ? 0 : RequiredIntValue(region["color_argb"]!, "color_argb", int.MinValue, int.MaxValue),
            ["references"] = RegionSupport.EmptyReferences(),
            ["provenance"] = "intended_design",
            ["capability"] = "typed_write_enabled",
            ["codec_version"] = RegionSupport.CodecVersion
        };
        RegionSupport.ValidateIdentity(created, "create_region");
        if (region["id"] is not null && !string.Equals(StringValue(region, "id"), StringValue(created, "id"), StringComparison.Ordinal))
        {
            throw new EngineException("INVALID_ARGUMENT", "create_region id must match its creation_number.");
        }
        if (regions.OfType<JsonObject>().Any(item => StringValue(item, "id") == StringValue(created, "id") || IntValue(item["creation_number"]) == IntValue(created["creation_number"])))
        {
            throw new EngineException("INVALID_ARGUMENT", "Region id and creation_number must be unique.");
        }
        if (region["weather"] is not null) RequireStringValue(region["weather"]!, "weather");
        if (region["ambient_sound"] is not null) RequireStringValue(region["ambient_sound"]!, "ambient_sound");
        ValidateRegionBounds(root, created, name);

        regions.Add(created);
    }

    private static void DeleteRegion(JsonObject root, JsonObject target, JsonNode? expected)
    {
        EnsureAllowed(target, "id", "region_id", "name", "creation_number");
        var regions = RequiredArray(root, "regions");
        var region = FindRegion(root, target);
        var name = RequiredString(region, "name");
        if (RegionSupport.IsProtectedName(name))
        {
            throw new EngineException("REGION_PROTECTED", $"Protected region '{name}' must be explicitly renamed before it can be deleted.");
        }
        var index = regions.IndexOf(region);
        if (index < 0)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' was not found.");
        }

        EnsureExpected(regions[index], expected, $"regions.{name}");
        EnsureNoRegionReferences(root, region);
        regions.RemoveAt(index);
    }

    private static void RenameRegion(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id", "region_id", "name", "creation_number");
        if (value is not JsonObject update) throw new EngineException("INVALID_ARGUMENT", "rename_region requires an object value.");
        EnsureAllowed(update, "name", "reference_rewrite_plan");
        var region = FindRegion(root, target);
        EnsureExpected(region, expected, "regions.rename");
        var newName = RequiredString(update, "name");
        var oldName = RequiredString(region, "name");
        if (root["regions"]!.AsArray().OfType<JsonObject>().Any(item => !ReferenceEquals(item, region) && string.Equals(item["name"]?.GetValue<string>(), newName, StringComparison.Ordinal)))
        {
            throw new EngineException("INVALID_ARGUMENT", $"Region '{newName}' already exists.");
        }
        ValidateReferenceRewritePlan(root, region, update["reference_rewrite_plan"]);
        region["name"] = newName;
        region.Remove("stored_name");
        RewriteKnownRegionReferences(root, oldName, newName, StringValue(region, "id"));
        region["provenance"] = "intended_design";
        region["capability"] = "typed_write_enabled";
    }

    private static void SetRegionRole(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id", "region_id", "name", "creation_number");
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
        role["role"] = RegionSupport.RequiredRole(roleValue);
        role["provenance"] = "intended_design";
    }

    private static void ReorderRegions(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target);
        if (expected is not JsonArray expectedIds) throw new EngineException("PRECONDITION_REQUIRED", "reorder_regions requires the complete expected prior region-id order.");
        if (value is not JsonObject update) throw new EngineException("INVALID_ARGUMENT", "reorder_regions requires an object value.");
        EnsureAllowed(update, "region_ids");
        if (update["region_ids"] is not JsonArray requestedIds) throw new EngineException("INVALID_ARGUMENT", "reorder_regions requires region_ids as an array.");

        var regions = RequiredArray(root, "regions");
        var currentIds = regions.OfType<JsonObject>().Select(RegionId).ToArray();
        var priorIds = expectedIds.Select(node => StringValue(node)).ToArray();
        var newIds = requestedIds.Select(node => StringValue(node)).ToArray();
        if (priorIds.Any(id => id is null) || newIds.Any(id => id is null) || !currentIds.SequenceEqual(priorIds!, StringComparer.Ordinal))
        {
            throw new EngineException("PRECONDITION_FAILED", "The expected region-id order does not match the staged region order.");
        }
        if (newIds.Length != currentIds.Length || newIds.Distinct(StringComparer.Ordinal).Count() != newIds.Length || currentIds.Except(newIds, StringComparer.Ordinal).Any())
        {
            throw new EngineException("INVALID_ARGUMENT", "reorder_regions must provide every region id exactly once.");
        }
        var byId = regions.OfType<JsonObject>().ToDictionary(RegionId, StringComparer.Ordinal);
        regions.Clear();
        foreach (var id in newIds) regions.Add(byId[id!]);
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
        EnsureAllowed(update, "name", "controller", "race", "flags", "start", "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask", "observer", "fixed_start_position", "slot_status");
        if (update["name"] is not null) RequireStringValue(update["name"]!, "name");
        if (update["controller"] is not null) RequireStringValue(update["controller"]!, "controller");
        if (update["race"] is not null) RequireStringValue(update["race"]!, "race");
        if (update["observer"] is not null) throw new EngineException("UNSUPPORTED_OPERATION", "war3map.w3i has no proven observer-slot representation.");
        if (update["slot_status"] is not null) throw new EngineException("UNSUPPORTED_OPERATION", "slot_status is derived from the native controller field and cannot be set directly.");
        if (update["flags"] is not null) _ = RequiredIntValue(update["flags"]!, "flags", 0, int.MaxValue);
        if (update["fixed_start_position"] is JsonValue fixedStartNode && !fixedStartNode.TryGetValue<bool>(out _)) throw new EngineException("INVALID_ARGUMENT", "Player fixed_start_position must be a boolean.");
        if (update["start"] is not null) ValidateStart(update["start"]!);
        foreach (var mask in new[] { "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask" })
        {
            if (update[mask] is not null) _ = RequiredIntValue(update[mask]!, mask, 0, int.MaxValue);
        }
        if (update["fixed_start_position"] is JsonValue fixedStart && fixedStart.TryGetValue<bool>(out var useFixedStart))
        {
            var currentFlags = RequiredIntValue(player["flags"] ?? JsonValue.Create(0), "flags", 0, int.MaxValue);
            update["flags"] = useFixedStart ? currentFlags | 1 : currentFlags & ~1;
        }

        foreach (var field in new[] { "name", "controller", "race", "flags", "start", "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask", "fixed_start_position" })
        {
            if (update[field] is not null)
            {
                player[field] = update[field]!.DeepClone();
                if (field == "name") player.Remove("stored_name");
            }
        }

        player["provenance"] = "intended_design";
        player["capability"] = "typed_write_enabled";
        UpdatePlayerDerivedFields(player);
    }

    private static void CreatePlayer(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id");
        EnsureCreateExpected(expected, "create_player_slot");
        if (value is not JsonObject player) throw new EngineException("INVALID_ARGUMENT", "create_player_slot requires a player object.");
        var id = RequiredInt(target, "id", 1, 24);
        var players = RequiredArray(root, "players");
        if (players.OfType<JsonObject>().Any(item => IntValue(item["id"]) == id)) throw new EngineException("INVALID_ARGUMENT", $"Player {id} already exists.");
        EnsureAllowed(player, "id", "name", "controller", "race", "flags", "start", "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask", "observer", "fixed_start_position", "slot_status");
        if (player["observer"] is not null) throw new EngineException("UNSUPPORTED_OPERATION", "war3map.w3i has no proven observer-slot representation.");
        if (player["slot_status"] is not null) throw new EngineException("UNSUPPORTED_OPERATION", "slot_status is derived from the native controller field and cannot be set directly.");
        if (player["id"] is not null && RequiredIntValue(player["id"]!, "id", 1, 24) != id) throw new EngineException("INVALID_ARGUMENT", "Player target id and value id differ.");
        var created = player.DeepClone() as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Player object could not be cloned.");
        created["id"] = id;
        created["name"] = StringValue(created["name"]) ?? $"Player {id}";
        created["controller"] = StringValue(created["controller"]) ?? "User";
        created["race"] = StringValue(created["race"]) ?? "Selectable";
        created["flags"] ??= 0;
        if (created["flags"] is not JsonValue flagsValue || !flagsValue.TryGetValue<int>(out var flags) || flags < 0) throw new EngineException("INVALID_ARGUMENT", "Player flags must be a non-negative integer.");
        if (created["fixed_start_position"] is JsonValue fixedStart && fixedStart.TryGetValue<bool>(out var useFixedStart)) created["flags"] = useFixedStart ? flags | 1 : flags & ~1;
        created["start"] = created["start"]?.DeepClone() ?? new JsonObject { ["x"] = 0, ["y"] = 0 };
        foreach (var mask in new[] { "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask" }) created[mask] ??= 0;
        ValidateStart(created["start"]!);
        created["provenance"] = "intended_design";
        created["capability"] = "typed_write_enabled";
        UpdatePlayerDerivedFields(created);
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
        EnsureAllowed(update, "name", "flags", "player_ids", "player_mask", "alliance", "shared_vision", "shared_unit_control");
        if (update["name"] is not null) RequireStringValue(update["name"]!, "name");
        if (update["flags"] is not null) _ = RequiredIntValue(update["flags"]!, "flags", 0, int.MaxValue);
        if (update["player_mask"] is not null) _ = RequiredIntValue(update["player_mask"]!, "player_mask", int.MinValue, int.MaxValue);
        if (update["player_ids"] is not null) ValidatePlayerIds(update["player_ids"]!);
        MergeForceFlags(update, force["flags"]);

        foreach (var field in new[] { "name", "flags", "player_ids", "player_mask", "alliance", "shared_vision", "shared_unit_control" })
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
        UpdateForceDerivedFields(force);
    }

    private static void CreateForce(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "index");
        EnsureCreateExpected(expected, "create_force");
        var index = RequiredInt(target, "index", 0, 23);
        var forces = RequiredArray(root, "forces");
        if (forces.OfType<JsonObject>().Any(item => IntValue(item["index"]) == index)) throw new EngineException("INVALID_ARGUMENT", $"Force {index} already exists.");
        if (value is not JsonObject force) throw new EngineException("INVALID_ARGUMENT", "create_force requires a force object.");
        EnsureAllowed(force, "index", "name", "stored_name", "flags", "player_ids", "player_mask", "alliance", "shared_vision", "shared_unit_control");
        var created = force.DeepClone() as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Force object could not be cloned.");
        created["index"] = index;
        created["name"] = StringValue(created["name"]) ?? $"Force {index + 1}";
        created["flags"] ??= 0;
        MergeForceFlags(created, created["flags"]);
        created["player_ids"] ??= new JsonArray();
        ValidatePlayerIds(created["player_ids"]!);
        created["player_mask"] = created["player_mask"]?.DeepClone() ?? PlayerMask(created["player_ids"]!);
        ValidateForceRecord(created);
        created["provenance"] = "intended_design";
        created["capability"] = "typed_write_enabled";
        UpdateForceDerivedFields(created);
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
        ValidateTeamRecord(created);
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
        EnsureAllowed(sourceValue, "language", "source", "source_strategy");
        var language = RequiredString(sourceValue, "language");
        if (!language.Equals("jass", StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineException("UNSUPPORTED_OPERATION", "The first MCP-owned gameplay source strategy supports JASS only.");
        }

        if (sourceValue["source_strategy"] is JsonValue strategy && strategy.TryGetValue<string>(out var selectedStrategy) && selectedStrategy != "composed")
        {
            throw new EngineException("INVALID_ARGUMENT", "set_script_source source_strategy must be 'composed' when supplied.");
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
        EnsureAllowed(candidate, "id", "archive_path", "category", "object_kind", "base_rawcode", "custom_rawcode", "rawcode", "display_name", "dependencies", "references", "unknown_ids", "modifications", "codec_version", "provenance", "capability");
        candidate["category"] ??= target["category"]?.DeepClone();
        candidate["rawcode"] ??= target["rawcode"]?.DeepClone();
        candidate["base_rawcode"] ??= candidate["rawcode"]?.DeepClone();
        candidate["custom_rawcode"] ??= candidate["rawcode"]?.DeepClone();
        candidate["object_kind"] ??= "custom";
        ValidateObjectDefinition(root, candidate);
        var definitions = Collection(root, "object_data");
        var category = StringValue(candidate, "category")!;
        var objectKind = StringValue(candidate, "object_kind") ?? "custom";
        var stableId = $"{MapComponentCodec.ObjectMemberForCategory(category)}:{(objectKind.Equals("custom", StringComparison.OrdinalIgnoreCase) ? "new" : "base")}:{StringValue(candidate, "base_rawcode")}:{StringValue(candidate, "custom_rawcode")}";
        var id = StringValue(target["id"]) ?? StringValue(candidate["id"]) ?? stableId;
        if (!string.Equals(id, stableId, StringComparison.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"Object definition id '{id}' must equal its native stable id '{stableId}' for binary round-trip.");
        if (definitions.OfType<JsonObject>().Any(item => ObjectId(item) == id ||
            (string.Equals(StringValue(item, "category"), category, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(StringValue(item, "rawcode"), StringValue(candidate, "rawcode"), StringComparison.Ordinal)))) throw new EngineException("INVALID_ARGUMENT", $"Object definition '{id}' already exists.");
        if (objectKind.Equals("custom", StringComparison.OrdinalIgnoreCase) && ObjectPlacementSupport.IsKnownStandard(category, StringValue(candidate, "custom_rawcode")!))
        {
            throw new EngineException("INVALID_ARGUMENT", $"Custom object rawcode '{StringValue(candidate, "custom_rawcode")}' collides with a known standard {category} object.");
        }
        var created = candidate;
        created["id"] = id;
        created["archive_path"] = created["archive_path"]?.DeepClone() ?? MapComponentCodec.ObjectMemberForCategory(StringValue(created, "category")!);
        if (!created.ContainsKey("display_name")) created.Add("display_name", null);
        created["dependencies"] ??= new JsonArray();
        created["references"] ??= new JsonObject();
        created["codec_version"] ??= MapComponentCodec.CodecVersion;
        created["provenance"] = "intended_design";
        created["capability"] = "typed_write_enabled";
        definitions.Add(created);
    }

    private static void UpdateObjectDefinition(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        EnsureAllowed(target, "id", "category", "rawcode");
        var definition = FindObjectDefinition(root, target);
        EnsureExpected(definition, expected, "object_data.update");
        if (value is not JsonObject update) throw new EngineException("INVALID_ARGUMENT", "update_object_definition requires an object value.");
        EnsureAllowed(update, "display_name", "modifications");
        var merged = definition.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Object definition could not be cloned.");
        if (update.ContainsKey("display_name"))
        {
            SetObjectDisplayName(merged, update["display_name"]);
            update = update.DeepClone()!.AsObject();
            update.Remove("display_name");
        }
        foreach (var property in update) merged[property.Key] = property.Value?.DeepClone();
        ValidateObjectDefinition(root, merged);
        foreach (var property in update) definition[property.Key] = property.Value?.DeepClone();
        if (merged["modifications"] is JsonNode modifications) definition["modifications"] = modifications.DeepClone();
        if (merged.TryGetPropertyValue("display_name", out var display)) definition["display_name"] = display?.DeepClone();
        if (update.ContainsKey("modifications")) definition["display_name"] = DisplayNameFromModifications(merged);
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
        EnsureAllowed(target, "id", "category", "rawcode", "creation_number", "relation");
        var relation = StringValue(target["relation"]) ?? throw new EngineException("INVALID_ARGUMENT", "set_object_reference requires a typed relation.");
        if (relation is not ("ability" or "item" or "upgrade" or "owner" or "region")) throw new EngineException("UNSUPPORTED_OPERATION", $"Object relation '{relation}' is not supported.");
        if (TryFindPlacement(root, target, out var placement))
        {
            SetPlacementReference(root, placement, relation, expected, value);
            return;
        }

        var definition = FindObjectDefinition(root, target);
        var references = definition["references"] as JsonObject ?? new JsonObject();
        EnsureExpectedOrAbsent(references[relation], expected, $"object_data.references.{relation}");
        var normalized = NormalizeDefinitionReference(root, definition, relation, value);
        references[relation] = normalized;
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
        ValidatePlacement(root, normalized);
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
        ValidatePlacement(root, placement);
        placement["provenance"] = "intended_design";
    }

    private static void UpdatePlacedObject(JsonObject root, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var placement = FindPlacement(root, target);
        EnsureExpected(placement, expected, "placed_objects.update");
        if (value is not JsonObject update) throw new EngineException("INVALID_ARGUMENT", "update_placed_object requires an object value.");
        EnsureAllowed(update, "rawcode", "skin_rawcode", "owner_id", "flags", "hit_points", "mana_points", "hero_level", "hero_strength", "hero_agility", "hero_intelligence", "inventory", "abilities", "variation", "position", "facing", "scale", "map_region_role", "waygate_destination_region_id", "custom_player_color_id", "life", "state");
        foreach (var property in update) placement[property.Key] = property.Value?.DeepClone();
        ValidatePlacement(root, placement);
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

    private static bool IsGeneratedGameplayOperation(string type)
        => type is "upsert_script_module" or "remove_script_module" or "create_trigger" or "update_trigger" or "move_trigger" or "delete_trigger" or "create_variable" or "update_variable" or "delete_variable" or "rename_region";

    private static bool IsTeamStructureOperation(string type)
        => type is "create_player_slot" or "set_player_slot" or "delete_player_slot" or "create_force" or "set_force" or "delete_force" or "create_team" or "set_team" or "delete_team" or "set_team_arena" or "set_team_members";

    private static bool HasGameplayModel(JsonObject root)
        => root["gameplay_source"] is JsonObject
            || root["gameplay_modules"] is JsonArray modules && modules.Count > 0;

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
        root["profile"] = composition["profile"]!.DeepClone();
        root["profile_spec"] = composition["profile_spec"]!.DeepClone();
        root["teams"] = composition["teams"]!.DeepClone();
        root["team_registry"] = composition["team_registry"]!.DeepClone();
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
        var creation = target["creation_number"] is JsonValue number && number.TryGetValue<int>(out var value) ? value : int.MinValue;
        if (id is null && name is null && creation == int.MinValue) throw new EngineException("INVALID_ARGUMENT", "A region target requires id, name, region_id, or creation_number.");
        if (id is not null && (!id.StartsWith("region:", StringComparison.Ordinal) || !int.TryParse(id[7..], out var parsedId) || parsedId < 0)) throw new EngineException("INVALID_ARGUMENT", "Region target ids must use the stable region:<creation_number> form.");
        var matches = regions.OfType<JsonObject>().Where(item =>
            (id is null || StringValue(item, "id") == id) &&
            (name is null || StringValue(item, "name") == name) &&
            (creation == int.MinValue || IntValue(item["creation_number"]) == creation)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new EngineException("INVALID_ARGUMENT", "The target region was not found or its identity selectors disagree."),
            _ => throw new EngineException("INVALID_ARGUMENT", "The region target is ambiguous.")
        };
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
        return TryFindPlacement(root, target, out var placement)
            ? placement
            : throw new EngineException("INVALID_ARGUMENT", "The target placement was not found.");
    }

    private static bool TryFindPlacement(JsonObject root, JsonObject target, out JsonObject placement)
    {
        var id = StringValue(target, "id");
        var creation = target["creation_number"] is JsonValue number && number.TryGetValue<int>(out var value) ? value : int.MinValue;
        placement = Collection(root, "placed_objects").OfType<JsonObject>().FirstOrDefault(item =>
            (id is not null && StringValue(item, "id") == id)
            || (creation != int.MinValue && IntValue(item["creation_number"]) == creation))!;
        return placement is not null;
    }

    private static JsonObject NormalizePlacement(JsonObject root, JsonObject target, JsonObject value)
    {
        EnsureAllowed(value, "id", "member", "kind", "rawcode", "skin_rawcode", "owner_id", "flags", "unknown_1", "unknown_2", "hit_points", "mana_points", "gold_amount", "target_acquisition", "hero_level", "hero_strength", "hero_agility", "hero_intelligence", "inventory", "abilities", "random_data_mode", "random_data", "custom_player_color_id", "waygate_destination_region_id", "variation", "position", "facing", "scale", "map_item_table_id", "item_table_sets", "creation_number", "state", "life", "map_region_role");
        var result = value.DeepClone() as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Placement could not be cloned.");
        var kind = (StringValue(result, "kind") ?? "unit").ToLowerInvariant();
        result["kind"] = kind;
        result["member"] = StringValue(result, "member") ?? ObjectPlacementSupport.MemberForPlacementKind(kind);
        if (kind is not ("unit" or "item" or "building" or "doodad" or "destructable" or "special_doodad")) throw new EngineException("INVALID_ARGUMENT", $"Placement kind '{kind}' is not supported.");
        var expectedMember = ObjectPlacementSupport.MemberForPlacementKind(kind);
        if (!string.Equals(result["member"]!.GetValue<string>(), expectedMember, StringComparison.OrdinalIgnoreCase)) throw new EngineException("INVALID_ARGUMENT", "Placement kind and archive member do not agree.");
        var identityKind = ObjectPlacementSupport.IdentityKindForMember(expectedMember);
        var requestedId = StringValue(target, "id") ?? StringValue(result, "id");
        if (kind == "special_doodad")
        {
            var specialId = requestedId ?? SpecialDoodadId(result);
            if (!string.Equals(specialId, SpecialDoodadId(result), StringComparison.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"Special doodad id '{specialId}' must be derived from its rawcode and integer position.");
            result["id"] = specialId;
            result.Remove("creation_number");
        }
        else
        {
            result["creation_number"] ??= NextPlacementCreationNumber(root, expectedMember);
            var derivedId = $"{identityKind}:{IntValue(result["creation_number"])}";
            if (requestedId is not null && !string.Equals(requestedId, derivedId, StringComparison.Ordinal)
                && !string.Equals(requestedId, $"{kind}:{IntValue(result["creation_number"])}", StringComparison.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"Placement id '{requestedId}' must equal its native stable id '{derivedId}' for binary round-trip.");
            result["id"] = derivedId;
        }
        result["rawcode"] = StringValue(result, "rawcode") ?? throw new EngineException("INVALID_ARGUMENT", "Placement rawcode is required.");
        result["skin_rawcode"] ??= result["rawcode"]!.DeepClone();
        result["variation"] ??= 0;
        result["position"] ??= new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 };
        result["facing"] ??= 0;
        result["scale"] ??= new JsonObject { ["x"] = 1, ["y"] = 1, ["z"] = 1 };
        result["map_item_table_id"] ??= -1;
        result["item_table_sets"] ??= new JsonArray();
        if (kind is "unit" or "building" or "item")
        {
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
            result["random_data_mode"] ??= "None";
            result["custom_player_color_id"] ??= -1;
            result["waygate_destination_region_id"] ??= -1;
        }
        if (kind is "doodad" or "destructable" or "special_doodad")
        {
            result["state"] ??= "Normal";
            result["life"] ??= 100;
        }
        result["provenance"] = "intended_design";
        result["capability"] = "typed_write_enabled";
        return result;
    }

    private static string SpecialDoodadId(JsonObject placement)
    {
        var position = placement["position"] as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", "Special doodad placement requires position.");
        var rawcode = StringValue(placement, "rawcode") ?? throw new EngineException("INVALID_ARGUMENT", "Special doodad placement requires rawcode.");
        var x = RequiredIntValue(position["x"] ?? throw new EngineException("INVALID_ARGUMENT", "Special doodad position requires integer x."), "position.x", int.MinValue, int.MaxValue);
        var y = RequiredIntValue(position["y"] ?? throw new EngineException("INVALID_ARGUMENT", "Special doodad position requires integer y."), "position.y", int.MinValue, int.MaxValue);
        return FormattableString.Invariant($"special-doodad:{rawcode}:{x}:{y}");
    }

    private static void ValidatePlacement(JsonObject root, JsonObject placement)
    {
        var rawcode = StringValue(placement, "rawcode");
        if (rawcode is null || rawcode.Length != 4 || rawcode.Any(character => character < 0x20 || character > 0x7E)) throw new EngineException("INVALID_ARGUMENT", "Placement rawcode must contain exactly four printable ASCII characters.");
        ValidatePosition(placement["position"] ?? throw new EngineException("INVALID_ARGUMENT", "Placement position is required."));
        ValidatePosition(placement["scale"] ?? throw new EngineException("INVALID_ARGUMENT", "Placement scale is required."));
        if (!double.TryParse(placement["facing"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var facing) || !double.IsFinite(facing)) throw new EngineException("INVALID_ARGUMENT", "Placement facing must be finite.");
        var kind = StringValue(placement, "kind") ?? "unit";
        if (kind is "unit" or "building" or "item")
        {
            var owner = IntValue(placement["owner_id"]);
            if (owner is < 1 or > 24 || !PlayerExists(root, owner)) throw new EngineException("INVALID_ARGUMENT", "Placement owner_id must reference a declared player slot from 1 through 24.");
        }
        ValidatePlacementEnvelope(root, placement);
        if (kind is "unit" or "building" or "item") ValidatePlacementArrays(root, placement);
    }

    private static void ValidatePosition(JsonNode value)
    {
        if (value is not JsonObject position || !Finite(position["x"]) || !Finite(position["y"]) || !Finite(position["z"])) throw new EngineException("INVALID_ARGUMENT", "Position must contain finite x, y, and z values.");
    }

    private static bool Finite(JsonNode? value)
        => value is JsonValue node && (node.TryGetValue<double>(out var number) ? double.IsFinite(number)
            : node.TryGetValue<float>(out var single) ? float.IsFinite(single)
            : node.TryGetValue<int>(out _) || node.TryGetValue<long>(out _));

    private static void ValidatePlacementEnvelope(JsonObject root, JsonObject placement)
    {
        var bounds = CameraBounds(root);
        if (bounds is null) return;
        var position = placement["position"]!.AsObject();
        var x = Number(position["x"]!);
        var y = Number(position["y"]!);
        if (x < bounds.Value.Left || x > bounds.Value.Right || y < bounds.Value.Bottom || y > bounds.Value.Top)
        {
            throw new EngineException("COORDINATE_OUT_OF_BOUNDS", "Placement coordinates fall outside the map envelope.");
        }
    }

    private static void ValidatePlacementArrays(JsonObject root, JsonObject placement)
    {
        var kind = StringValue(placement, "kind") ?? "unit";
        var category = kind switch
        {
            "item" => "item",
            "doodad" or "special_doodad" => "doodad",
            "destructable" => "destructable",
            _ => "unit"
        };
        var rawcode = StringValue(placement, "rawcode")!;
        if (!ObjectRawcodeExists(root, category, rawcode)) throw new EngineException("REFERENCE_MISSING", $"Placement rawcode '{rawcode}' is not a known {category} definition.");

        if (placement["inventory"] is not JsonArray inventory) throw new EngineException("INVALID_ARGUMENT", "Placement inventory must be an array.");
        var slots = new HashSet<int>();
        foreach (var item in inventory)
        {
            if (item is not JsonObject record) throw new EngineException("INVALID_ARGUMENT", "Placement inventory entries must be objects.");
            var slot = RequiredInt(record, "slot", 0, 5);
            if (!slots.Add(slot)) throw new EngineException("INVALID_ARGUMENT", $"Placement inventory slot {slot} occurs more than once.");
            var itemRawcode = RequiredRawcodeValue(record["rawcode"] ?? throw new EngineException("INVALID_ARGUMENT", "Placement inventory entries require rawcode."));
            if (!ObjectRawcodeExists(root, "item", itemRawcode)) throw new EngineException("REFERENCE_MISSING", $"Inventory item rawcode '{itemRawcode}' is not a known item definition.");
        }

        if (placement["abilities"] is not JsonArray abilities) throw new EngineException("INVALID_ARGUMENT", "Placement abilities must be an array.");
        var abilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in abilities)
        {
            if (item is not JsonObject record) throw new EngineException("INVALID_ARGUMENT", "Placement ability entries must be objects.");
            var abilityRawcode = RequiredRawcodeValue(record["rawcode"] ?? throw new EngineException("INVALID_ARGUMENT", "Placement ability entries require rawcode."));
            if (!abilityIds.Add(abilityRawcode)) throw new EngineException("INVALID_ARGUMENT", $"Placement ability '{abilityRawcode}' occurs more than once.");
            if (!ObjectRawcodeExists(root, "ability", abilityRawcode)) throw new EngineException("REFERENCE_MISSING", $"Ability rawcode '{abilityRawcode}' is not a known ability definition.");
        }

        var regionNumber = IntValue(placement["waygate_destination_region_id"]);
        if (regionNumber is >= 0 && !RegionExists(root, regionNumber)) throw new EngineException("REFERENCE_MISSING", $"Placement waygate region '{regionNumber}' does not exist.");
        if (placement["map_region_role"] is JsonNode role && role is not JsonValue && role is not JsonObject) throw new EngineException("INVALID_ARGUMENT", "map_region_role must be a scalar or object value.");
    }

    private static void ValidateObjectDefinition(JsonObject root, JsonObject definition)
    {
        var category = StringValue(definition, "category")?.ToLowerInvariant();
        if (!ObjectPlacementSupport.IsSupportedCategory(category)) throw new EngineException("INVALID_ARGUMENT", "Object definition category is not supported.");
        definition["category"] = category;
        var objectKind = StringValue(definition, "object_kind")?.ToLowerInvariant();
        if (objectKind is not ("base" or "custom")) throw new EngineException("INVALID_ARGUMENT", "Object definition object_kind must be base or custom.");
        definition["object_kind"] = objectKind;
        foreach (var field in new[] { "rawcode", "base_rawcode", "custom_rawcode" })
        {
            if (StringValue(definition, field) is { } rawcode && !ObjectPlacementSupport.IsValidRawcode(rawcode)) throw new EngineException("INVALID_ARGUMENT", $"Object definition {field} must be exactly four printable ASCII characters.");
        }
        if (StringValue(definition, "rawcode") is null) throw new EngineException("INVALID_ARGUMENT", "Object definition rawcode is required.");
        if (StringValue(definition, "base_rawcode") is null || StringValue(definition, "custom_rawcode") is null) throw new EngineException("INVALID_ARGUMENT", "Object definition base_rawcode and custom_rawcode are required.");
        var activeRawcode = objectKind == "custom" ? StringValue(definition, "custom_rawcode")! : StringValue(definition, "base_rawcode")!;
        if (StringValue(definition, "rawcode") != activeRawcode) throw new EngineException("INVALID_ARGUMENT", "Object definition rawcode must match its active base/custom rawcode.");
        if (objectKind == "custom" && StringValue(definition, "base_rawcode") == StringValue(definition, "custom_rawcode")) throw new EngineException("INVALID_ARGUMENT", "A custom object must use a rawcode distinct from its base object.");
        if (objectKind == "custom" && !ObjectPlacementSupport.IsKnownStandard(category!, StringValue(definition, "base_rawcode")!) && !DefinitionExists(root, category!, StringValue(definition, "base_rawcode")!))
        {
            throw new EngineException("REFERENCE_MISSING", $"Base {category} rawcode '{StringValue(definition, "base_rawcode")}' is not present in the known standard set or staged object definitions.");
        }
        if (definition.TryGetPropertyValue("display_name", out var displayName) && displayName is not null
            && (displayName is not JsonValue displayValue || !displayValue.TryGetValue<string>(out _))) throw new EngineException("INVALID_ARGUMENT", "Object definition display_name must be a string or null.");
        if (definition["dependencies"] is not null && definition["dependencies"] is not JsonArray) throw new EngineException("INVALID_ARGUMENT", "Object definition dependencies must be an array.");
        foreach (var dependency in (definition["dependencies"] as JsonArray ?? new JsonArray()))
        {
            var dependencyRawcode = RequiredRawcodeValue(dependency ?? throw new EngineException("INVALID_ARGUMENT", "Object definition dependencies cannot contain null."));
            if (!ObjectRawcodeExists(root, category!, dependencyRawcode)) throw new EngineException("REFERENCE_MISSING", $"Object dependency rawcode '{dependencyRawcode}' is not a known {category} definition.");
        }
        ValidateDefinitionReferences(root, definition);
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

    private static string RequiredRawcodeValue(JsonNode value)
    {
        var rawcode = StringValue(value);
        if (!ObjectPlacementSupport.IsValidRawcode(rawcode)) throw new EngineException("INVALID_ARGUMENT", "Rawcodes must contain exactly four printable ASCII characters.");
        return rawcode!;
    }

    private static bool ObjectRawcodeExists(JsonObject root, string category, string rawcode)
        => ObjectPlacementSupport.IsKnownStandard(category, rawcode)
            || Collection(root, "object_data").OfType<JsonObject>().Any(item => string.Equals(StringValue(item, "category"), category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(StringValue(item, "rawcode"), rawcode, StringComparison.OrdinalIgnoreCase));

    private static bool DefinitionExists(JsonObject root, string category, string rawcode)
        => Collection(root, "object_data").OfType<JsonObject>().Any(item => string.Equals(StringValue(item, "category"), category, StringComparison.OrdinalIgnoreCase)
            && string.Equals(StringValue(item, "rawcode"), rawcode, StringComparison.OrdinalIgnoreCase));

    private static bool RegionExists(JsonObject root, int creationNumber)
        => Collection(root, "regions").OfType<JsonObject>().Any(region => IntValue(region["creation_number"]) == creationNumber);

    private static void ValidateDefinitionReferences(JsonObject root, JsonObject definition)
    {
        if (definition["references"] is not JsonObject references) return;
        foreach (var relation in references)
        {
            if (relation.Key is not ("ability" or "item" or "upgrade" or "owner" or "region")) throw new EngineException("INVALID_ARGUMENT", $"Object reference relation '{relation.Key}' is unsupported.");
            if (relation.Value is null) throw new EngineException("INVALID_ARGUMENT", $"Object reference relation '{relation.Key}' cannot be null.");
            switch (relation.Key)
            {
                case "ability":
                    var ability = ReferenceRawcode(relation.Value, "ability");
                    if (!ObjectRawcodeExists(root, "ability", ability)) throw new EngineException("REFERENCE_MISSING", $"Ability rawcode '{ability}' is not a known ability definition.");
                    break;
                case "item":
                    var item = ReferenceRawcode(relation.Value, "item");
                    if (!ObjectRawcodeExists(root, "item", item)) throw new EngineException("REFERENCE_MISSING", $"Item rawcode '{item}' is not a known item definition.");
                    break;
                case "upgrade":
                    var upgrade = ReferenceRawcode(relation.Value, "upgrade");
                    if (!ObjectRawcodeExists(root, "upgrade", upgrade)) throw new EngineException("REFERENCE_MISSING", $"Upgrade rawcode '{upgrade}' is not a known upgrade definition.");
                    break;
                case "owner":
                    var owner = ReferencePlayerId(relation.Value);
                    if (!PlayerExists(root, owner)) throw new EngineException("REFERENCE_MISSING", $"Owner player '{owner}' is not declared in the map.");
                    break;
                case "region":
                    var region = ReferenceRegionId(relation.Value);
                    if (!RegionExists(root, region)) throw new EngineException("REFERENCE_MISSING", $"Region '{region}' does not exist.");
                    break;
            }
        }
    }

    private static JsonNode NormalizeDefinitionReference(JsonObject root, JsonObject definition, string relation, JsonNode? value)
    {
        if (value is null) throw new EngineException("INVALID_ARGUMENT", "set_object_reference requires a reference value.");
        return relation switch
        {
            "ability" => JsonValue.Create(RequireKnownReference(root, "ability", ReferenceRawcode(value, "ability")))!,
            "item" => JsonValue.Create(RequireKnownReference(root, "item", ReferenceRawcode(value, "item")))!,
            "upgrade" => JsonValue.Create(RequireKnownReference(root, "upgrade", ReferenceRawcode(value, "upgrade")))!,
            "owner" => JsonValue.Create(RequirePlayer(root, ReferencePlayerId(value)))!,
            "region" => JsonValue.Create(RequireRegion(root, ReferenceRegionId(value)))!,
            _ => throw new EngineException("UNSUPPORTED_OPERATION", $"Object relation '{relation}' is not supported.")
        };
    }

    private static void SetPlacementReference(JsonObject root, JsonObject placement, string relation, JsonNode? expected, JsonNode? value)
    {
        switch (relation)
        {
            case "ability":
                EnsureExpected(placement["abilities"], expected, "placed_objects.references.ability");
                var abilityRawcode = RequireKnownReference(root, "ability", ReferenceRawcode(value ?? throw new EngineException("INVALID_ARGUMENT", "Ability reference is required.")));
                var ability = value is JsonObject abilityValue ? abilityValue.DeepClone()!.AsObject() : new JsonObject();
                ability["rawcode"] = abilityRawcode;
                ability["autocast_active"] ??= false;
                ability["hero_ability_level"] ??= 0;
                (placement["abilities"] as JsonArray ?? throw new EngineException("INVALID_ARGUMENT", "Placement abilities must be an array.")).Add(ability);
                break;
            case "item":
                EnsureExpected(placement["inventory"], expected, "placed_objects.references.item");
                var itemValue = value as JsonObject;
                var itemRawcode = RequireKnownReference(root, "item", ReferenceRawcode(value ?? throw new EngineException("INVALID_ARGUMENT", "Item reference is required.")));
                var inventory = placement["inventory"] as JsonArray ?? throw new EngineException("INVALID_ARGUMENT", "Placement inventory must be an array.");
                var slot = itemValue is null ? Enumerable.Range(0, 6).FirstOrDefault(candidate => !inventory.OfType<JsonObject>().Any(item => IntValue(item["slot"]) == candidate), -1) : RequiredInt(itemValue, "slot", 0, 5);
                if (slot < 0) throw new EngineException("INVALID_ARGUMENT", "The placement has no free inventory slot.");
                var existing = inventory.OfType<JsonObject>().FirstOrDefault(item => IntValue(item["slot"]) == slot);
                if (existing is not null) existing["rawcode"] = itemRawcode;
                else inventory.Add(new JsonObject { ["slot"] = slot, ["rawcode"] = itemRawcode });
                break;
            case "owner":
                EnsureExpected(placement["owner_id"], expected, "placed_objects.references.owner");
                placement["owner_id"] = RequirePlayer(root, ReferencePlayerId(value ?? throw new EngineException("INVALID_ARGUMENT", "Owner reference is required.")));
                break;
            case "region":
                EnsureExpected(placement["waygate_destination_region_id"], expected, "placed_objects.references.region");
                placement["waygate_destination_region_id"] = RequireRegion(root, ReferenceRegionId(value ?? throw new EngineException("INVALID_ARGUMENT", "Region reference is required.")));
                break;
            case "upgrade":
                throw new EngineException("UNSUPPORTED_OPERATION", "Placed objects have no native upgrade reference field; set upgrade references on an object definition.");
        }
        ValidatePlacement(root, placement);
        placement["provenance"] = "intended_design";
        placement["capability"] = "typed_write_enabled";
    }

    private static string RequireKnownReference(JsonObject root, string category, string rawcode)
    {
        if (!ObjectRawcodeExists(root, category, rawcode)) throw new EngineException("REFERENCE_MISSING", $"{category} rawcode '{rawcode}' is not a known object definition.");
        return rawcode;
    }

    private static int RequirePlayer(JsonObject root, int playerId)
    {
        if (!PlayerExists(root, playerId)) throw new EngineException("REFERENCE_MISSING", $"Owner player '{playerId}' is not declared in the map.");
        return playerId;
    }

    private static int RequireRegion(JsonObject root, int regionId)
    {
        if (!RegionExists(root, regionId)) throw new EngineException("REFERENCE_MISSING", $"Region '{regionId}' does not exist.");
        return regionId;
    }

    private static string ReferenceRawcode(JsonNode value, string relation = "object")
    {
        var rawcode = value is JsonObject reference ? StringValue(reference, "rawcode") : StringValue(value);
        if (!ObjectPlacementSupport.IsValidRawcode(rawcode)) throw new EngineException("INVALID_ARGUMENT", $"The {relation} reference must contain a four-character rawcode.");
        return rawcode!;
    }

    private static int ReferencePlayerId(JsonNode value)
    {
        var node = value is JsonObject reference ? reference["player_id"] : value;
        return RequiredIntValue(node ?? throw new EngineException("INVALID_ARGUMENT", "The owner reference requires player_id."), "player_id", 1, 24);
    }

    private static int ReferenceRegionId(JsonNode value)
    {
        var node = value is JsonObject reference ? reference["region_id"] : value;
        var text = StringValue(node);
        if (text is not null && text.StartsWith("region:", StringComparison.Ordinal) && int.TryParse(text[7..], out var parsed) && parsed >= 0) return parsed;
        return RequiredIntValue(node ?? throw new EngineException("INVALID_ARGUMENT", "The region reference requires region_id."), "region_id", 0, int.MaxValue);
    }

    private static bool PlayerExists(JsonObject root, int playerId)
        => Collection(root, "players").OfType<JsonObject>().Any(player => IntValue(player["id"]) == playerId);

    private static (double Left, double Bottom, double Right, double Top)? CameraBounds(JsonObject root)
    {
        if (root["metadata"] is not JsonArray metadata) return null;
        var entry = metadata.OfType<JsonObject>().FirstOrDefault(item => StringValue(item, "field") == "camera_bounds");
        var bounds = entry?["value"] as JsonObject;
        if (bounds is null) return null;
        var values = new[] { bounds["left"], bounds["bottom"], bounds["right"], bounds["top"] };
        if (values.Any(item => item is null || !Finite(item))) return null;
        return (Number(values[0]!), Number(values[1]!), Number(values[2]!), Number(values[3]!));
    }

    private static double Number(JsonNode value)
    {
        if (value is JsonValue json && json.TryGetValue<double>(out var number) && double.IsFinite(number)) return number;
        if (value is JsonValue single && single.TryGetValue<float>(out var floatValue) && float.IsFinite(floatValue)) return floatValue;
        if (value is JsonValue integer && integer.TryGetValue<int>(out var intValue)) return intValue;
        throw new EngineException("INVALID_ARGUMENT", "A finite numeric value is required.");
    }

    private static void SetObjectDisplayName(JsonObject definition, JsonNode? displayName)
    {
        var category = StringValue(definition, "category") ?? throw new EngineException("INVALID_ARGUMENT", "Object definition category is required.");
        var nameId = ObjectNameField(category);
        var modifications = definition["modifications"] as JsonArray ?? new JsonArray();
        definition["modifications"] = modifications;
        var existing = modifications.OfType<JsonObject>().FirstOrDefault(item => StringValue(item, "id") == nameId);
        if (displayName is null)
        {
            if (existing is not null) modifications.Remove(existing);
            definition["display_name"] = null;
            return;
        }
        if (displayName is not JsonValue textValue || !textValue.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) throw new EngineException("INVALID_ARGUMENT", "Object definition display_name must be a non-empty string or null.");
        if (existing is null)
        {
            existing = new JsonObject { ["id"] = nameId, ["type"] = "String", ["value"] = text };
            if (category is "ability" or "upgrade") { existing["level"] = 0; existing["pointer"] = 0; }
            else if (category == "doodad") { existing["variation"] = 0; existing["pointer"] = 0; }
            modifications.Add(existing);
        }
        else
        {
            existing["type"] = "String";
            existing["value"] = text;
        }
        definition["display_name"] = text;
    }

    private static string? DisplayNameFromModifications(JsonObject definition)
    {
        if (definition["modifications"] is not JsonArray modifications) return null;
        var nameId = ObjectNameField(StringValue(definition, "category") ?? string.Empty);
        return modifications.OfType<JsonObject>().FirstOrDefault(item => StringValue(item, "id") == nameId && StringValue(item, "type") == "String") is { } name
            ? StringValue(name, "value")
            : null;
    }

    private static string ObjectNameField(string category) => category switch
    {
        "unit" or "item" => "unam",
        "ability" => "anam",
        "destructable" => "bnam",
        "doodad" => "dnam",
        "buff" => "fnam",
        "upgrade" => "gnam",
        _ => throw new EngineException("INVALID_ARGUMENT", $"Unsupported object category '{category}'.")
    };

    private static void ValidateForceRecord(JsonObject force)
    {
        ValidatePlayerIds(force["player_ids"] ?? throw new EngineException("INVALID_ARGUMENT", "Force player_ids must be an array."));
        if (force["player_mask"] is JsonValue mask && mask.TryGetValue<int>(out var value))
        {
            var expected = PlayerMask(force["player_ids"]!);
            if (value >= 0 && value != expected || value < 0 && (value & expected) != expected) throw new EngineException("INVALID_ARGUMENT", "Force player_mask must match player_ids.");
        }
    }

    private static void MergeForceFlags(JsonObject force, JsonNode? existingFlags)
    {
        var flags = existingFlags is JsonValue value && value.TryGetValue<int>(out var parsed) && parsed >= 0 ? parsed : 0;
        foreach (var (field, bit) in new[] { ("alliance", 1), ("shared_vision", 8), ("shared_unit_control", 16) })
        {
            if (force[field] is not JsonValue boolean || !boolean.TryGetValue<bool>(out var enabled)) continue;
            flags = enabled ? flags | bit : flags & ~bit;
        }
        if (force["flags"] is null || force.Any(property => property.Key is "alliance" or "shared_vision" or "shared_unit_control")) force["flags"] = flags;
    }

    private static void UpdatePlayerDerivedFields(JsonObject player)
    {
        var flags = IntValue(player["flags"]);
        player["observer"] = null;
        player["fixed_start_position"] = flags != int.MinValue && (flags & 1) != 0;
        player["slot_status"] = StringValue(player["controller"]) switch
        {
            "None" => "closed",
            "Neutral" => "neutral",
            "Rescuable" => "rescuable",
            _ => "active"
        };
        player["codec_version"] = MapComponentCodec.CodecVersion;
    }

    private static void UpdateForceDerivedFields(JsonObject force)
    {
        var flags = IntValue(force["flags"]);
        if (flags == int.MinValue) flags = 0;
        force["alliance"] = (flags & 1) != 0;
        force["shared_vision"] = (flags & 8) != 0;
        force["shared_unit_control"] = (flags & 16) != 0;
        force["codec_version"] = MapComponentCodec.CodecVersion;
    }

    private static void ValidateTeamRecord(JsonObject team)
    {
        foreach (var field in new[] { "id", "name", "member_player_ids", "force_index", "arena_id", "hero_ids", "life_state", "routing_state" })
        {
            if (team[field] is null) throw new EngineException("INVALID_ARGUMENT", $"Team records require '{field}'.");
        }
        if (string.IsNullOrWhiteSpace(StringValue(team, "id")) || string.IsNullOrWhiteSpace(StringValue(team, "name"))) throw new EngineException("INVALID_ARGUMENT", "Team id and name must be non-empty strings.");
        ValidatePlayerIds(team["member_player_ids"]!);
        if (RequiredIntValue(team["force_index"]!, "force_index", 0, 23) < 0) throw new EngineException("INVALID_ARGUMENT", "Team force_index must be a valid force index.");
        if (string.IsNullOrWhiteSpace(StringValue(team, "arena_id"))) throw new EngineException("INVALID_ARGUMENT", "Team arena_id must be a non-empty stable arena ID.");
        if (team["hero_ids"] is not JsonArray) throw new EngineException("INVALID_ARGUMENT", "Team hero_ids must be an array.");
        if (string.IsNullOrWhiteSpace(StringValue(team, "life_state")) || string.IsNullOrWhiteSpace(StringValue(team, "routing_state"))) throw new EngineException("INVALID_ARGUMENT", "Team life_state and routing_state must be non-empty strings.");
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

    private static void ValidateReferenceRewritePlan(JsonObject root, JsonObject region, JsonNode? value)
    {
        if (value is not JsonObject plan) throw new EngineException("INVALID_ARGUMENT", "rename_region requires a complete reference_rewrite_plan.");
        EnsureAllowed(plan, "mcp_owned", "editor_trigger", "custom_text", "unresolved");
        foreach (var (field, bucket) in new[]
        {
            ("mcp_owned", "mcp_owned"),
            ("editor_trigger", "editor_trigger"),
            ("custom_text", "custom_text")
        })
        {
            if (plan[field] is null) throw new EngineException("INVALID_ARGUMENT", $"reference_rewrite_plan is missing '{field}'.");
            if (plan[field] is JsonValue status && status.TryGetValue<string>(out var text))
            {
                if (text is not ("rewrite" or "unchanged" or "not_applicable"))
                {
                    throw new EngineException("INVALID_ARGUMENT", $"reference_rewrite_plan.{field} must be rewrite, unchanged, or not_applicable.");
                }

                var references = (region["references"] as JsonObject)?[bucket] as JsonArray;
                if (text is "unchanged" or "not_applicable" && references is { Count: > 0 })
                {
                    throw new EngineException("REGION_REFERENCES_INCOMPLETE", $"reference_rewrite_plan.{field} declares {text}, but the inspected region has {references.Count} live reference(s) in that bucket.");
                }
            }
            else if (plan[field] is JsonArray references)
            {
                var actual = (region["references"] as JsonObject)?[bucket] as JsonArray ?? new JsonArray();
                if (!JsonUtilities.Equal(references, actual))
                {
                    throw new EngineException("REGION_REFERENCES_INCOMPLETE", $"reference_rewrite_plan.{field} must enumerate the complete inspected reference set.");
                }
            }
            else
            {
                throw new EngineException("INVALID_ARGUMENT", $"reference_rewrite_plan.{field} must be a status or reference array.");
            }
        }
        if (plan["unresolved"] is JsonArray unresolved && unresolved.Count > 0)
        {
            throw new EngineException("REGION_REFERENCES_UNRESOLVED", "rename_region cannot proceed while reference_rewrite_plan.unresolved is non-empty.");
        }
        if (StringValue(root, "trigger_mode") == GameplayModelValidator.EditorMode && plan["editor_trigger"] is JsonValue editor && editor.TryGetValue<string>(out var editorStatus) && editorStatus != "rewrite")
        {
            throw new EngineException("CAPABILITY_GATED", "Editor-compatible region rename requires an editor trigger serializer; unresolved editor references must be rewritten in World Editor first.");
        }
    }

    private static void RewriteKnownRegionReferences(JsonObject root, string oldName, string newName, string? regionId)
    {
        foreach (var section in new[] { "triggers", "variables", "gameplay_triggers", "gameplay_variables", "teams", "region_roles", "placed_objects" })
        {
            if (root[section] is not JsonNode sectionValue) continue;
            Rewrite(sectionValue, oldName, newName, regionId);
        }
    }

    private static void Rewrite(JsonNode node, string oldName, string newName, string? regionId)
    {
        if (node is JsonObject objectValue)
        {
            foreach (var property in objectValue.ToList())
            {
                if (property.Key is "region_name" or "region" or "region_handle" && StringValue(property.Value) == oldName) objectValue[property.Key] = newName;
                else if (property.Key == "regions" && property.Value is JsonArray names)
                {
                    for (var index = 0; index < names.Count; index++) if (StringValue(names[index]) == oldName) names[index] = newName;
                    Rewrite(names, oldName, newName, regionId);
                }
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
        foreach (var (section, value) in new[] { "triggers", "variables", "gameplay_triggers", "gameplay_variables", "teams", "region_roles", "placed_objects" }
            .Where(section => root[section] is not null)
            .Select(section => (section, value: root[section]!)))
        {
            if (ContainsRegionReference(value, name, id)) throw new EngineException("REFERENCE_IN_USE", $"Region '{name}' still has a live reference in {section}; remove or rewrite it before deleting the region.");
        }
        var handle = id is null ? null : RegionHandle(id);
        if (handle is not null && root["scripts"] is JsonArray scripts && scripts.OfType<JsonObject>().Any(script => StringValue(script, "source")?.Contains(handle, StringComparison.Ordinal) == true))
        {
            throw new EngineException("REFERENCE_IN_USE", $"Region '{name}' is referenced by generated MCP JASS handle '{handle}'.");
        }
    }

    private static string RegionHandle(string id)
        => "HTW_Region_" + new string(id.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());

    private static string RegionId(JsonObject region)
        => StringValue(region, "id") ?? RegionSupport.StableId(IntValue(region["creation_number"]));

    private static void RefreshRegionReferences(JsonObject root)
    {
        if (root["regions"] is not JsonArray regions) return;
        foreach (var region in regions.OfType<JsonObject>())
        {
            // Hand-authored unit tests and legacy canonical snapshots can
            // contain a name/bounds-only region. Preserve those snapshots for
            // unrelated operations; inspected/buildable maps still fail the
            // full identity check in ValidationPipeline.
            if (region["creation_number"] is not null || region["id"] is not null)
            {
                RegionSupport.ValidateIdentity(region, "regions");
            }
            region["references"] = CollectRegionReferences(root, region);
            if (region["creation_number"] is not null) region["codec_version"] ??= RegionSupport.CodecVersion;
        }
    }

    private static JsonObject CollectRegionReferences(JsonObject root, JsonObject region)
    {
        var name = StringValue(region, "name");
        var id = StringValue(region, "id") ?? string.Empty;
        var references = RegionSupport.EmptyReferences();
        foreach (var (section, bucket) in new[]
        {
            ("triggers", "editor_trigger"),
            ("variables", "editor_trigger"),
            ("gameplay_triggers", "mcp_owned"),
            ("gameplay_variables", "mcp_owned"),
            ("teams", "mcp_owned"),
            ("region_roles", "derived_roles"),
            ("placed_objects", "mcp_owned"),
            ("custom_text", "custom_text")
        })
        {
            if (root[section] is JsonNode value && references[bucket] is JsonArray entries)
            {
                CollectRegionReferences(value, section, "$", name, id, entries);
            }
        }
        return references;
    }

    private static void CollectRegionReferences(JsonNode node, string section, string path, string? name, string id, JsonArray output)
    {
        if (node is JsonObject objectValue)
        {
            foreach (var property in objectValue)
            {
                var propertyPath = $"{path}.{property.Key}";
                if (property.Key is "region_name" or "region" or "region_handle" && name is not null && StringValue(property.Value) == name)
                {
                    output.Add(new JsonObject { ["section"] = section, ["path"] = propertyPath, ["kind"] = "name" });
                }
                else if (property.Key == "region_id" && StringValue(property.Value) == id)
                {
                    output.Add(new JsonObject { ["section"] = section, ["path"] = propertyPath, ["kind"] = "id" });
                }
                else if (property.Key == "waygate_destination_region_id"
                    && RegionCreationNumber(id) is int creationNumber
                    && IntValue(property.Value) == creationNumber)
                {
                    output.Add(new JsonObject { ["section"] = section, ["path"] = propertyPath, ["kind"] = "creation_number" });
                }
                else if (property.Key == "regions" && property.Value is JsonArray regionNames)
                {
                    for (var index = 0; index < regionNames.Count; index++)
                    {
                        var reference = StringValue(regionNames[index]);
                        if (reference == name || reference == id)
                        {
                            output.Add(new JsonObject { ["section"] = section, ["path"] = $"{propertyPath}[{index}]", ["kind"] = reference == id ? "id" : "name" });
                        }
                    }
                }
                if (property.Value is not null) CollectRegionReferences(property.Value, section, propertyPath, name, id, output);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is not null) CollectRegionReferences(array[index]!, section, $"{path}[{index}]", name, id, output);
            }
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
                if (property.Key == "waygate_destination_region_id"
                    && RegionCreationNumber(id) is int creationNumber
                    && IntValue(property.Value) == creationNumber) return true;
                if (property.Key == "regions" && property.Value is JsonArray names && names.Any(item => StringValue(item) == name)) return true;
                if (property.Value is not null && ContainsRegionReference(property.Value, name, id)) return true;
            }
        }
        else if (node is JsonArray array)
        {
            return array.Where(item => item is not null).Cast<JsonNode>().Any(item => ContainsRegionReference(item, name, id));
        }
        return false;
    }

    private static int? RegionCreationNumber(string? id)
        => id is not null && id.StartsWith("region:", StringComparison.Ordinal)
            && int.TryParse(id["region:".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var creationNumber)
            ? creationNumber
            : null;

    private static void EnsureCreateExpected(JsonNode? expected, string operation)
    {
        if (expected is not null) throw new EngineException("INVALID_ARGUMENT", $"{operation} does not accept an expected prior value; a missing target is its precondition.");
    }

    private static int PlayerMask(JsonNode value)
        => (value as JsonArray ?? throw new EngineException("INVALID_ARGUMENT", "player_ids must be an array.")).Aggregate(0, (mask, item) => mask | (1 << (RequiredIntValue(item ?? throw new EngineException("INVALID_ARGUMENT", "player_ids cannot contain null."), "player_id", 1, 24) - 1)));

    private static int NextCreationNumber(JsonArray values)
        => values.OfType<JsonObject>().Select(item => IntValue(item["creation_number"])).Where(number => number >= 0).DefaultIfEmpty(-1).Max() + 1;

    private static int NextPlacementCreationNumber(JsonObject root, string member)
        => Collection(root, "placed_objects").OfType<JsonObject>()
            .Where(item => string.Equals(StringValue(item, "member"), member, StringComparison.OrdinalIgnoreCase))
            .Select(item => IntValue(item["creation_number"]))
            .Where(number => number >= 0)
            .DefaultIfEmpty(-1)
            .Max() + 1;

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

    private static void ValidateRegionBounds(JsonObject root, JsonObject region, string name)
    {
        var minX = FiniteNumber(region["min_x"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires min_x."), $"regions.{name}.min_x").GetValue<double>();
        var minY = FiniteNumber(region["min_y"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires min_y."), $"regions.{name}.min_y").GetValue<double>();
        var maxX = FiniteNumber(region["max_x"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires max_x."), $"regions.{name}.max_x").GetValue<double>();
        var maxY = FiniteNumber(region["max_y"] ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires max_y."), $"regions.{name}.max_y").GetValue<double>();
        if (minX > maxX || minY > maxY)
        {
            throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' must have min coordinates no greater than max coordinates.");
        }
        RegionSupport.ValidateEnvelope(root, region, name);
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
