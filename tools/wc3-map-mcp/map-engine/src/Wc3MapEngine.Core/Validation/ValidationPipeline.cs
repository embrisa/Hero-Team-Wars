using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Wc3MapEngine.Core.Scripts;

namespace Wc3MapEngine.Core.Validation;

public static class ValidationPipeline
{
    public const string ValidatorVersion = "phase3-1.0";

    private static readonly Regex Sha256 = new("^[0-9A-F]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SafeImport = new("^[^\\/]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Rawcode = new("^[\\x20-\\x7E]{4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static JsonObject ValidateMap(string path, JsonObject? context = null)
    {
        var findings = new JsonArray();
        JsonObject? inspection = null;
        try
        {
            if (!path.EndsWith(".w3m", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".w3x", StringComparison.OrdinalIgnoreCase))
            {
                Add(findings, "error", "MAP_EXTENSION_INVALID", "archive", path, "The map must use a .w3m or .w3x extension.", "Use a Warcraft III map extension.");
            }

            inspection = MapInspector.Inspect(path);
        }
        catch (EngineException exception)
        {
            Add(findings, "error", "ARCHIVE_UNREADABLE", "archive", null, exception.Message, "Repair or replace the map copy before continuing.");
        }

        if (inspection is not null)
        {
            ValidateInspection(inspection, findings, context);
        }

        var source = inspection?["source"] as JsonObject;
        return Report(findings, source, requestedPath: path, target: "map");
    }

    public static JsonObject ValidateCanonical(string canonicalPath, string? sourcePath = null, JsonObject? context = null)
    {
        var findings = new JsonArray();
        var root = JsonUtilities.Read(canonicalPath) as JsonObject
            ?? throw new EngineException("INVALID_JSON", "Canonical map root must be a JSON object.");

        if (!string.Equals(root["schema_version"]?.GetValue<string>(), "1.0", StringComparison.Ordinal))
        {
            Add(findings, "error", "SCHEMA_VERSION_UNSUPPORTED", "canonical_map", null, "Expected canonical map schema version 1.0.", "Regenerate the canonical map with the current engine.");
        }

        ValidateCanonicalShape(root, findings, context);
        if (context?["project_id"]?.GetValue<string>() == "hero-team-wars")
        {
            ValidateHtwInvariants(root, findings, context);
        }

        JsonObject? sourceInspection = null;
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            try
            {
                sourceInspection = MapInspector.Inspect(sourcePath);
                ValidateInspection(sourceInspection, findings, context);
                ValidateBuildableChanges(sourceInspection, root, findings, context);
            }
            catch (EngineException exception)
            {
                Add(findings, "error", "SOURCE_UNREADABLE", "source_map", sourcePath, exception.Message, "Restore a readable, hash-checked transaction source copy.");
            }
        }

        var source = sourceInspection?["source"] as JsonObject;
        return Report(
            findings,
            source,
            requestedPath: canonicalPath,
            target: sourcePath is null ? "canonical_map" : "transaction_build");
    }

    private static void ValidateInspection(JsonObject inspection, JsonArray findings, JsonObject? context)
    {
        var members = inspection["archive_members"] as JsonArray ?? new JsonArray();
        var memberEntries = members.OfType<JsonObject>().ToList();
        var memberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in memberEntries)
        {
            var path = StringValue(member["path"]);
            if (string.IsNullOrWhiteSpace(path) || !memberNames.Add(path))
            {
                Add(findings, "error", "DUPLICATE_ARCHIVE_MEMBER", "archive", path, "Archive member names must be unique.", "Remove the duplicate member through a verified archive writer.");
            }

            var hash = StringValue(member["sha256"]);
            if (hash is null || !Sha256.IsMatch(hash))
            {
                Add(findings, "error", "ARCHIVE_MEMBER_HASH_INVALID", "archive", path, "Every archive member must have a valid content hash.", "Regenerate the archive inventory.");
            }

            var size = NumericValue(member["size_bytes"]);
            if (size is null || size < 0)
            {
                Add(findings, "error", "ARCHIVE_MEMBER_SIZE_INVALID", "archive", path, "Archive member sizes must be non-negative numbers.", "Regenerate the archive inventory.");
            }
        }

        var infoMember = memberEntries.FirstOrDefault(x => string.Equals(StringValue(x["path"]), "war3map.w3i", StringComparison.OrdinalIgnoreCase));
        if (infoMember is null)
        {
            Add(findings, "error", "REQUIRED_MEMBER_MISSING", "metadata", "war3map.w3i", "The map has no war3map.w3i member.", "Use a complete Warcraft III map archive.");
        }
        else if (!IsParsedCapability(infoMember["capability"]))
        {
            Add(findings, "error", "REQUIRED_MEMBER_UNPARSED", "metadata", "war3map.w3i", "war3map.w3i is present but was not parsed safely.", "Use a map version supported by the pinned War3Net reader.");
        }

        ValidateMapInfoContract(inspection, findings);

        foreach (var member in memberEntries)
        {
            var capability = StringValue(member["capability"]);
            if (string.Equals(capability, "unsupported_blocking", StringComparison.Ordinal))
            {
                Add(findings, "error", "MEMBER_PARSE_FAILED", "archive", StringValue(member["path"]), "An archive member could not be parsed or safely preserved.", "Repair the member or keep the build blocked until a compatible reader is available.");
            }
            else if (string.Equals(capability, "preserved_opaque", StringComparison.Ordinal))
            {
                Add(findings, "info", "OPAQUE_MEMBER_PRESERVED", "archive", StringValue(member["path"]), "Archive member is preserved byte-for-byte but not semantically decoded.", "Do not edit this member through the semantic writer.");
            }
        }

        ValidateMetadata(inspection["metadata"] as JsonArray, findings);
        var bounds = CameraBounds(inspection["metadata"] as JsonArray);
        ValidatePlayers(inspection["players"] as JsonArray, findings, bounds);
        ValidateForces(inspection["players"] as JsonArray, inspection["forces"] as JsonArray, findings, context);
        ValidateRegions(inspection["regions"] as JsonArray, findings, bounds, context);
        ValidateObjectDefinitions(inspection["object_data"] as JsonArray, findings);
        ValidatePlacements(inspection["placed_objects"] as JsonArray, findings, bounds);
        ValidateTeams(inspection, findings, context);
        ValidateRawcodes(inspection, findings);
        ValidateImports(inspection, findings);

        try
        {
            var archive = MapArchive.Read(inspection["source"]!["path"]!.GetValue<string>());
            ValidateScriptsAndWts(archive, inspection, findings);
        }
        catch (Exception exception)
        {
            Add(findings, "warning", "OPAQUE_VALIDATION_LIMIT", "archive", null, $"Script/WTS checks could not be completed: {exception.Message}", "Keep MCP-owned source unchanged until the script and trigger members can be inspected safely.");
        }

        if (context?["project_id"]?.GetValue<string>() == "hero-team-wars")
        {
            ValidateHtwInvariants(inspection, findings, context);
        }
    }

    private static void ValidateCanonicalShape(JsonObject root, JsonArray findings, JsonObject? context)
    {
        ValidateMetadata(root["metadata"] as JsonArray, findings);
        var bounds = CameraBounds(root["metadata"] as JsonArray);
        ValidatePlayers(root["players"] as JsonArray, findings, bounds);
        ValidateForces(root["players"] as JsonArray, root["forces"] as JsonArray, findings, context);
        ValidateRegions(root["regions"] as JsonArray, findings, bounds, context);
        ValidateObjectDefinitions(root["object_data"] as JsonArray, findings);
        ValidatePlacements(root["placed_objects"] as JsonArray, findings, bounds);
        ValidateTeams(root, findings, context);
        ValidateRawcodes(root, findings);
        ValidateImports(root, findings);
        ValidateScriptEntries(root["scripts"] as JsonArray, findings);

        if (root["opaque_members"] is not JsonArray && root["archive_members"] is not JsonArray)
        {
            Add(findings, "error", "PRESERVATION_PLAN_MISSING", "archive", null, "The canonical map does not include an archive preservation plan.", "Regenerate the canonical map from the source archive.");
        }
    }

    private static void ValidateBuildableChanges(JsonObject source, JsonObject staged, JsonArray findings, JsonObject? context)
    {
        var sourceMetadata = Metadata(source);
        var stagedMetadata = Metadata(staged);
        foreach (var field in sourceMetadata.Keys.Union(stagedMetadata.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            sourceMetadata.TryGetValue(field, out var before);
            stagedMetadata.TryGetValue(field, out var after);
            if (JsonUtilities.Equal(before, after)) continue;
            if (field is not ("title" or "suggested_players"))
            {
                Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "metadata", field, "This metadata field has no proven serializer in the Phase 3 writer.", "Use only title or suggested_players until a round-trip serializer is promoted.");
            }
        }

        var sourceRegions = Regions(source);
        var stagedRegions = Regions(staged);
        foreach (var after in stagedRegions)
        {
            foreach (var field in after.Select(x => x.Key))
            {
                if (field is not ("id" or "name" or "min_x" or "min_y" or "max_x" or "max_y" or "creation_number" or "weather" or "ambient_sound" or "color_argb" or "provenance" or "capability"))
                {
                    Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "regions", RegionName(after), $"Region field '{field}' has no proven typed serializer.", "Use only fields supported by the typed region codec.");
                }
            }
        }

        if (!JsonUtilities.Equal(source["players"], staged["players"]) || !JsonUtilities.Equal(source["forces"], staged["forces"]))
        {
            ValidatePlayerFields(staged["players"] as JsonArray, findings);
            ValidateForceFields(staged["forces"] as JsonArray, findings);
        }

        ValidateBuildableScripts(source, staged, findings);

        var sourceClone = source.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Could not clone source canonical map.");
        var stagedClone = staged.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Could not clone staged canonical map.");
        sourceClone.Remove("source");
        stagedClone.Remove("source");
        sourceClone.Remove("metadata");
        stagedClone.Remove("metadata");
        sourceClone.Remove("regions");
        stagedClone.Remove("regions");
        sourceClone.Remove("players");
        stagedClone.Remove("players");
        sourceClone.Remove("forces");
        stagedClone.Remove("forces");
        sourceClone.Remove("object_data");
        stagedClone.Remove("object_data");
        sourceClone.Remove("object_data_members");
        stagedClone.Remove("object_data_members");
        sourceClone.Remove("placed_objects");
        stagedClone.Remove("placed_objects");
        sourceClone.Remove("scripts");
        stagedClone.Remove("scripts");
        sourceClone.Remove("archive_members");
        stagedClone.Remove("archive_members");
        sourceClone.Remove("capabilities");
        stagedClone.Remove("capabilities");
        sourceClone.Remove("component_status");
        stagedClone.Remove("component_status");
        sourceClone.Remove("opaque_members");
        stagedClone.Remove("opaque_members");
        sourceClone.Remove("parse_warnings");
        stagedClone.Remove("parse_warnings");
        sourceClone.Remove("profile");
        stagedClone.Remove("profile");
        sourceClone.Remove("profiles");
        stagedClone.Remove("profiles");
        sourceClone.Remove("teams");
        stagedClone.Remove("teams");
        sourceClone.Remove("team_registry");
        stagedClone.Remove("team_registry");
        sourceClone.Remove("region_roles");
        stagedClone.Remove("region_roles");
        sourceClone.Remove("gameplay_source");
        stagedClone.Remove("gameplay_source");
        sourceClone.Remove("gameplay_triggers");
        stagedClone.Remove("gameplay_triggers");
        sourceClone.Remove("gameplay_variables");
        stagedClone.Remove("gameplay_variables");
        sourceClone.Remove("gameplay_modules");
        stagedClone.Remove("gameplay_modules");
        sourceClone.Remove("trigger_mode");
        stagedClone.Remove("trigger_mode");
        if (!JsonUtilities.Equal(sourceClone, stagedClone))
        {
            Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "canonical_map", null, "The staged transaction changes a component without a proven binary serializer.", "Use a typed operation backed by a Phase 3 round-trip test.");
        }

        _ = context;
    }

    private static void ValidateMetadata(JsonArray? metadata, JsonArray findings)
    {
        if (metadata is null)
        {
            Add(findings, "error", "METADATA_MISSING", "metadata", null, "Canonical map metadata is unavailable.", "Regenerate the canonical map from the source archive.");
            return;
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in metadata)
        {
            if (node is not JsonObject entry)
            {
                Add(findings, "error", "METADATA_INVALID", "metadata", null, "Every metadata entry must be an object.", "Regenerate the canonical map from the source archive.");
                continue;
            }

            var field = StringValue(entry["field"]);
            if (string.IsNullOrWhiteSpace(field) || !fields.Add(field))
            {
                Add(findings, "error", "METADATA_FIELD_INVALID", "metadata", field, "Metadata fields must be non-empty and unique.", "Regenerate the canonical map from the source archive.");
                continue;
            }

            if (field == "title" && string.IsNullOrWhiteSpace(StringValue(entry["value"])))
            {
                Add(findings, "error", "MAP_TITLE_INVALID", "metadata", field, "The map title must be a non-empty string.", "Set a non-empty map title through the typed metadata operation.");
            }

            if (field == "suggested_players" && !ValidSuggestedPlayers(entry["value"]))
            {
                Add(findings, "error", "SUGGESTED_PLAYERS_INVALID", "metadata", field, "Suggested players must be a non-empty string or an integer from 1 through 24.", "Set a valid suggested-player value through the typed metadata operation.");
            }
        }
    }

    private static void ValidateMapInfoContract(JsonObject inspection, JsonArray findings)
    {
        var sourcePath = StringValue(inspection["source"]?["path"]);
        var extension = sourcePath is null ? null : Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not ".w3m" and not ".w3x")
        {
            Add(findings, "error", "MAP_EXTENSION_INVALID", "archive", sourcePath, "The map extension must be .w3m or .w3x and must agree with the parsed map-info contract.", "Use a Warcraft III map extension.");
        }

        var metadata = inspection["metadata"] as JsonArray;
        var fields = metadata?.OfType<JsonObject>()
            .Select(entry => StringValue(entry["field"]))
            .Where(field => field is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in new[] { "format_version", "map_version", "editor_version", "script_language", "camera_bounds", "playable_map_area" })
        {
            if (!fields.Contains(field))
            {
                Add(findings, "error", "MAP_INFO_FIELD_MISSING", "metadata", field, $"Parsed war3map.w3i is missing required map-info field '{field}'.", "Regenerate the canonical map with the pinned map-info reader.");
            }
        }

        if (extension is ".w3m" or ".w3x")
        {
            Add(findings, "info", "MAP_INFO_EXTENSION_AGREEMENT", "metadata", extension, $"The {extension} archive has a readable war3map.w3i map-info contract.", "Keep the map extension and map-info member together when producing a build.");
        }
    }

    private static void ValidatePlayers(JsonArray? players, JsonArray findings, Bounds? bounds)
    {
        if (players is null)
        {
            Add(findings, "error", "PLAYERS_MISSING", "players", null, "Canonical map player slots are unavailable.", "Regenerate the canonical map from the source archive.");
            return;
        }

        var ids = new HashSet<int>();
        foreach (var node in players)
        {
            if (node is not JsonObject player)
            {
                Add(findings, "error", "PLAYER_INVALID", "players", null, "Every player entry must be an object.", "Regenerate the canonical map from the source archive.");
                continue;
            }

            var id = IntegerValue(player["id"]);
            if (id is null || id is < 1 or > 24 || !ids.Add(id.Value))
            {
                Add(findings, "error", "PLAYER_ID_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), "Player IDs must be unique values from 1 through 24.", "Use explicit numeric player slots.");
            }

            foreach (var field in new[] { "controller", "race" })
            {
                if (player[field] is not null && StringValue(player[field]) is null)
                {
                    Add(findings, "error", "PLAYER_FIELD_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), $"Player field '{field}' must be a string.", "Regenerate the canonical map from the source archive.");
                }
            }

            if (player["start"] is JsonObject start)
            {
                var x = NumericValue(start["x"]);
                var y = NumericValue(start["y"]);
                if (x is null || y is null || !IsFinite(x.Value) || !IsFinite(y.Value))
                {
                    Add(findings, "error", "PLAYER_START_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), "Player start coordinates must be finite numbers.", "Supply finite player start coordinates.");
                }
                else if (bounds is not null && !bounds.Value.Contains(x.Value, y.Value))
                {
                    Add(findings, "error", "COORDINATE_OUT_OF_BOUNDS", "players", id?.ToString(CultureInfo.InvariantCulture), "Player start coordinates fall outside the map camera bounds.", "Move the player start inside the playable map bounds.");
                }
            }
            else if (player["start"] is not null)
            {
                Add(findings, "error", "PLAYER_START_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), "Player start must be an object.", "Supply finite player start coordinates.");
            }
        }
    }

    private static void ValidateForces(JsonArray? players, JsonArray? forces, JsonArray findings, JsonObject? context)
    {
        if (forces is null)
        {
            Add(findings, "error", "FORCES_MISSING", "forces", null, "Canonical map force assignments are unavailable.", "Regenerate the canonical map from the source archive.");
            return;
        }

        var playerIds = players?.OfType<JsonObject>().Select(x => IntegerValue(x["id"])).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet() ?? new HashSet<int>();
        var indices = new HashSet<int>();
        var assigned = new HashSet<int>();
        foreach (var node in forces)
        {
            if (node is not JsonObject force)
            {
                Add(findings, "error", "FORCE_INVALID", "forces", null, "Every force entry must be an object.", "Regenerate the canonical map from the source archive.");
                continue;
            }

            var index = IntegerValue(force["index"]);
            if (index is null || index is < 0 or > 23 || !indices.Add(index.Value))
            {
                Add(findings, "error", "FORCE_INDEX_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), "Force indices must be unique values from 0 through 23.", "Use explicit force indices.");
            }

            if (force["player_ids"] is JsonArray playerIdsInForce)
            {
                var seenPlayers = new HashSet<int>();
                foreach (var playerIdNode in playerIdsInForce)
                {
                    var playerId = IntegerValue(playerIdNode);
                    if (playerId is null || playerId is < 1 or > 24 || !seenPlayers.Add(playerId.Value))
                    {
                        Add(findings, "error", "FORCE_PLAYER_ID_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), "Force player IDs must be unique values from 1 through 24.", "Use explicit numeric player IDs.");
                    }
                    else
                    {
                        if (!playerIds.Contains(playerId.Value))
                        {
                            Add(findings, "error", "FORCE_PLAYER_REFERENCE_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), $"Force references missing player {playerId.Value}.", "Assign only declared player slots to forces.");
                        }

                        if (!assigned.Add(playerId.Value))
                        {
                            Add(findings, "error", "PLAYER_FORCE_CONTRADICTION", "forces", playerId.Value.ToString(CultureInfo.InvariantCulture), "A player is assigned to more than one force.", "Give each player one explicit team assignment.");
                        }
                    }
                }

                var mask = IntegerValue(force["player_mask"]);
                var expectedMask = seenPlayers.Where(x => x is >= 1 and <= 24).Aggregate(0, (value, id) => value | (1 << (id - 1)));
                if (mask is >= 0 && mask.Value != expectedMask)
                {
                    Add(findings, "error", "FORCE_MASK_CONTRADICTION", "forces", index?.ToString(CultureInfo.InvariantCulture), "The explicit force player IDs disagree with player_mask.", "Regenerate the force assignment with matching explicit player IDs and mask.");
                }
            }
            else if (force["player_ids"] is not null)
            {
                Add(findings, "error", "FORCE_PLAYER_ID_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), "Force player_ids must be an array.", "Use explicit numeric player IDs.");
            }
        }

        _ = context;
    }

    private static void ValidateRegions(JsonArray? regions, JsonArray findings, Bounds? bounds, JsonObject? context)
    {
        if (regions is null)
        {
            Add(findings, "warning", "REGIONS_UNKNOWN", "regions", null, "Region inventory is unavailable.", "Inspect the map with a compatible region parser.");
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in regions)
        {
            if (node is not JsonObject region)
            {
                Add(findings, "error", "REGION_INVALID", "regions", null, "Every region entry must be an object.", "Regenerate the canonical map from the source archive.");
                continue;
            }

            var name = RegionName(region);
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
            {
                Add(findings, "error", "REGION_NAME_INVALID", "regions", name, "Region names must be non-empty and unique with case-sensitive identity.", "Rename the duplicate through an explicit typed operation.");
                continue;
            }

            var values = new[] { "min_x", "min_y", "max_x", "max_y" }.Select(key => NumericValue(region[key])).ToArray();
            if (values.Any(x => x is null || !IsFinite(x.Value)) || values[0] > values[2] || values[1] > values[3])
            {
                Add(findings, "error", "REGION_BOUNDS_INVALID", "regions", name, "Region bounds must be finite and min values cannot exceed max values.", "Supply finite coordinates in world space.");
            }
            else if (bounds is not null && !bounds.Value.Contains(values[0]!.Value, values[1]!.Value, values[2]!.Value, values[3]!.Value))
            {
                Add(findings, "error", "COORDINATE_OUT_OF_BOUNDS", "regions", name, "Region coordinates fall outside the map camera bounds.", "Keep the region rectangle inside the map bounds.");
            }
        }

        _ = context;
    }

    private static void ValidateRawcodes(JsonObject root, JsonArray findings)
    {
        var definitions = new HashSet<string>(StringComparer.Ordinal);
        var duplicateDefinitions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in EnumerateObjects(root["object_data"]))
        {
            foreach (var key in new[] { "rawcode", "new_rawcode", "object_id", "new_id" })
            {
                var rawcode = StringValue(item[key]);
                if (rawcode is null) continue;
                if (!Rawcode.IsMatch(rawcode))
                {
                    Add(findings, "error", "RAWCODE_INVALID", "object_data", rawcode, "Object rawcodes must be exactly four printable ASCII characters.", "Use a category-valid four-character rawcode.");
                }
                else if (!definitions.Add(rawcode) && duplicateDefinitions.Add(rawcode))
                {
                    Add(findings, "error", "RAWCODE_DUPLICATE", "object_data", rawcode, "Object rawcodes must be unique within their declared category.", "Choose a unique custom rawcode.");
                }
            }
        }

        foreach (var item in EnumerateObjects(root["placed_objects"]))
        {
            var rawcode = StringValue(item["rawcode"]);
            if (rawcode is not null && !Rawcode.IsMatch(rawcode))
            {
                Add(findings, "error", "RAWCODE_INVALID", "placed_objects", rawcode, "Placed-object rawcodes must be exactly four printable ASCII characters.", "Use a valid unit/item rawcode.");
            }
        }

        var objectData = root["object_data"];
        var opaqueObject = objectData as JsonObject;
        if (opaqueObject is not null || objectData is JsonArray { Count: 0 })
        {
            var capability = opaqueObject is null ? null : StringValue(opaqueObject["capability"]);
            if (capability == "preserved_opaque" || objectData is JsonArray)
            {
                Add(findings, "info", "RAWCODE_VALIDATION_LIMIT", "object_data", null, "Rawcode uniqueness and dangling-reference checks are incomplete because object data is opaque.", "Keep object-data edits disabled until a category-aware parser is proven.");
            }
        }
    }

    private static void ValidateObjectDefinitions(JsonArray? definitions, JsonArray findings)
    {
        if (definitions is null) return;
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in definitions)
        {
            if (node is not JsonObject definition)
            {
                Add(findings, "error", "OBJECT_DEFINITION_INVALID", "object_data", null, "Every object definition must be an object.", "Regenerate the typed object-data section.");
                continue;
            }

            var category = StringValue(definition["category"]);
            var rawcode = StringValue(definition["rawcode"]);
            var key = $"{category}:{rawcode}";
            if (category is not ("unit" or "ability" or "item" or "destructable" or "doodad" or "buff" or "upgrade"))
            {
                Add(findings, "error", "OBJECT_CATEGORY_INVALID", "object_data", category, "Object definitions must use a supported category.", "Use unit, ability, item, destructable, doodad, buff, or upgrade.");
            }
            if (rawcode is null || !Rawcode.IsMatch(rawcode))
            {
                Add(findings, "error", "RAWCODE_INVALID", "object_data", rawcode, "Object definition rawcodes must be exactly four printable ASCII characters.", "Use a valid category-specific rawcode.");
            }
            else if (!identities.Add(key))
            {
                Add(findings, "error", "RAWCODE_DUPLICATE", "object_data", key, "Object rawcodes must be unique within their category.", "Choose a unique custom rawcode.");
            }

            if (definition["modifications"] is not null && definition["modifications"] is not JsonArray)
            {
                Add(findings, "error", "OBJECT_MODIFICATIONS_INVALID", "object_data", rawcode, "Object modifications must be an array.", "Use typed modification records.");
            }
        }
    }

    private static void ValidatePlacements(JsonArray? placements, JsonArray findings, Bounds? bounds)
    {
        if (placements is null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in placements)
        {
            if (node is not JsonObject placement)
            {
                Add(findings, "error", "PLACEMENT_INVALID", "placed_objects", null, "Every placement must be an object.", "Regenerate the typed placement section.");
                continue;
            }

            var rawcode = StringValue(placement["rawcode"]);
            var id = StringValue(placement["id"]);
            if (rawcode is null && StringValue(placement["capability"]) == "preserved_opaque") continue;
            if (rawcode is null || !Rawcode.IsMatch(rawcode)) Add(findings, "error", "RAWCODE_INVALID", "placed_objects", rawcode, "Placed-object rawcodes must be exactly four printable ASCII characters.", "Use a valid unit, item, doodad, or destructable rawcode.");
            if (id is null || !ids.Add(id)) Add(findings, "error", "PLACEMENT_ID_INVALID", "placed_objects", id, "Placed objects require unique stable MCP IDs.", "Assign one stable id to every placement.");
            var owner = IntegerValue(placement["owner_id"]);
            if (owner is not null and (< 1 or > 24)) Add(findings, "error", "PLACEMENT_OWNER_INVALID", "placed_objects", id, "Placement owner_id must reference a player slot from 1 through 24.", "Use an explicit valid player ID.");
            if (placement["position"] is JsonObject position)
            {
                var x = NumericValue(position["x"]);
                var y = NumericValue(position["y"]);
                var z = NumericValue(position["z"]);
                if (x is null || y is null || z is null || !IsFinite(x.Value) || !IsFinite(y.Value) || !IsFinite(z.Value)) Add(findings, "error", "PLACEMENT_POSITION_INVALID", "placed_objects", id, "Placement coordinates must be finite x/y/z values.", "Supply finite world coordinates.");
                else if (bounds is not null && !bounds.Value.Contains(x.Value, y.Value)) Add(findings, "error", "COORDINATE_OUT_OF_BOUNDS", "placed_objects", id, "Placement coordinates fall outside the map envelope.", "Move the placement inside the inspected map envelope.");
            }
            else if (placement["position"] is not null)
            {
                Add(findings, "error", "PLACEMENT_POSITION_INVALID", "placed_objects", id, "Placement position must be an object with x/y/z.", "Supply finite world coordinates.");
            }
        }
    }

    private static void ValidateTeams(JsonObject inspection, JsonArray findings, JsonObject? context)
    {
        if (inspection["teams"] is not JsonArray teams) return;
        var players = inspection["players"]?.AsArray().OfType<JsonObject>().Select(item => IntegerValue(item["id"])).Where(value => value.HasValue).Select(value => value!.Value).ToHashSet() ?? new HashSet<int>();
        var teamIds = new HashSet<string>(StringComparer.Ordinal);
        var assigned = new HashSet<int>();
        foreach (var node in teams)
        {
            if (node is not JsonObject team)
            {
                Add(findings, "error", "TEAM_INVALID", "teams", null, "Every logical team must be an object.", "Regenerate the explicit team registry.");
                continue;
            }
            var id = StringValue(team["id"]) ?? StringValue(team["team_id"]);
            if (id is null || !teamIds.Add(id)) Add(findings, "error", "TEAM_ID_INVALID", "teams", id, "Logical team IDs must be unique and explicit.", "Use stable team IDs independent of player color.");
            if (team["member_player_ids"] is not JsonArray members)
            {
                Add(findings, "error", "TEAM_MEMBERS_MISSING", "teams", id, "Every team must declare member_player_ids.", "Assign explicit player slots to the team.");
                continue;
            }
            foreach (var member in members)
            {
                var playerId = IntegerValue(member);
                if (playerId is null || !players.Contains(playerId.Value)) Add(findings, "error", "TEAM_PLAYER_REFERENCE_INVALID", "teams", id, "A logical team references a missing player slot.", "Use declared player IDs only.");
                else if (!assigned.Add(playerId.Value)) Add(findings, "error", "PLAYER_TEAM_CONTRADICTION", "teams", playerId.Value.ToString(CultureInfo.InvariantCulture), "A player is assigned to multiple logical teams.", "Assign each player to exactly one explicit team.");
            }
        }

        var profile = StringValue(inspection["profile"]) ?? StringValue(context?["profile"]);
        if (profile == "mvp_2arena" && teams.Count != 2) Add(findings, "error", "PROFILE_TEAM_COUNT_INVALID", "teams", profile, "The mvp_2arena profile requires exactly two logical teams.", "Use two explicit teams of two players.");
        if (profile == "full_6team" && teams.Count != 6) Add(findings, "error", "PROFILE_TEAM_COUNT_INVALID", "teams", profile, "The full_6team profile requires exactly six logical teams.", "Use six explicit teams of two players.");
    }

    private static void ValidatePlayerFields(JsonArray? players, JsonArray findings)
    {
        foreach (var player in players?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            foreach (var field in player.Select(item => item.Key))
            {
                if (field is not ("id" or "name" or "stored_name" or "controller" or "race" or "flags" or "start" or "ally_low_priority_mask" or "ally_high_priority_mask" or "enemy_low_priority_mask" or "enemy_high_priority_mask" or "provenance" or "capability")) Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "players", IntegerValue(player["id"])?.ToString(CultureInfo.InvariantCulture), $"Player field '{field}' has no proven typed serializer.", "Use only fields supported by the map-info codec.");
            }
        }
    }

    private static void ValidateForceFields(JsonArray? forces, JsonArray findings)
    {
        foreach (var force in forces?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            foreach (var field in force.Select(item => item.Key))
            {
                if (field is not ("index" or "name" or "stored_name" or "flags" or "player_ids" or "player_mask" or "provenance" or "capability")) Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "forces", IntegerValue(force["index"])?.ToString(CultureInfo.InvariantCulture), $"Force field '{field}' has no proven typed serializer.", "Use only fields supported by the map-info codec.");
            }
        }
    }

    private static void ValidateImports(JsonObject root, JsonArray findings)
    {
        if (root["imports"] is not JsonArray imports)
        {
            Add(findings, "info", "IMPORT_VALIDATION_LIMIT", "imports", null, "Import details are unavailable because the import member is opaque.", "Preserve imports byte-for-byte until import parsing is proven.");
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in imports.OfType<JsonObject>())
        {
            var path = StringValue(item["path"]) ?? StringValue(item["archive_path"]);
            var size = NumericValue(item["size_bytes"]);
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal) || path.Contains(':') || !SafeImport.IsMatch(path))
            {
                Add(findings, "error", "IMPORT_PATH_INVALID", "imports", path, "Import paths must be relative, normalized, and free of traversal syntax.", "Use a project-relative import path.");
            }
            else if (!names.Add(path))
            {
                Add(findings, "error", "IMPORT_DUPLICATE", "imports", path, "Import paths must be unique case-insensitively.", "Remove the duplicate import.");
            }

            if (size is < 0 or > 536870912)
            {
                Add(findings, "error", "IMPORT_SIZE_LIMIT", "imports", path, "Import size exceeds the 512 MiB safety limit.", "Reduce the import size or split the asset.");
            }
        }
    }

    private static void ValidateScriptsAndWts(MapArchiveSnapshot archive, JsonObject inspection, JsonArray findings)
    {
        var scripts = archive.Members.Where(x => x.Path.Equals("war3map.j", StringComparison.OrdinalIgnoreCase) || x.Path.Equals("war3map.lua", StringComparison.OrdinalIgnoreCase)).ToList();
        if (scripts.Count > 1)
        {
            Add(findings, "error", "SCRIPT_ENTRY_AMBIGUOUS", "scripts", null, "The archive contains both JASS and Lua entry-point members.", "Keep exactly one map script language and use the existing war3map.j JASS entry point.");
        }
        else if (scripts.Count == 0 && archive.Find("war3map.wtg") is not null)
        {
            Add(findings, "warning", "SCRIPT_ENTRY_MISSING", "scripts", "war3map.j", "GUI trigger data exists but no generated script entry was found.", "Provide a generated war3map.j entry point before enabling MCP-owned gameplay source.");
        }

        foreach (var script in scripts)
        {
            if (!ScriptOwnership.HasEntryPoint(script.Path, script.Bytes))
            {
                Add(findings, "warning", "SCRIPT_ENTRY_MISSING", "scripts", script.Path, "The script member does not expose a recognizable main entry point.", "Add a valid main entry point before enabling MCP-owned gameplay source.");
            }
        }

        if (archive.Find("war3map.wtg") is not null && archive.Find("war3map.wct") is not null && scripts.Count > 0)
        {
            Add(findings, "info", "SCRIPT_CONNECTION_OPAQUE", "scripts", scripts[0].Path, "GUI/custom-text members are preserved but are not synchronized with MCP-owned JASS source.", "Treat war3map.j as authoritative and avoid saving the generated trigger source over MCP-owned gameplay code.");
        }
        else if ((archive.Find("war3map.wtg") is not null || archive.Find("war3map.wct") is not null) && scripts.Count == 0)
        {
            Add(findings, "warning", "SCRIPT_SOURCE_DISCONNECTED", "scripts", null, "Trigger source members exist without a generated script entry.", "Provide war3map.j or keep MCP-owned script mutation disabled.");
        }

        var wts = archive.Find("war3map.wts");
        if (wts is null)
        {
            Add(findings, "info", "WTS_ABSENT", "strings", "war3map.wts", "No trigger-string table is present.", "Keep literal strings or add WTS through a proven editor-owned workflow.");
            return;
        }

        IReadOnlyDictionary<string, string> strings;
        try
        {
            strings = ScriptOwnership.ParseTriggerStrings(wts.Bytes);
        }
        catch (Exception exception)
        {
            Add(findings, "error", "WTS_INVALID", "strings", "war3map.wts", exception.Message, "Repair duplicate or malformed trigger-string blocks.");
            return;
        }

        foreach (var token in (inspection["metadata"]?.AsArray().OfType<JsonObject>().Select(x => StringValue(x["stored_value"])).Where(x => x is not null).Select(x => x!) ?? Enumerable.Empty<string>()))
        {
            if (!strings.ContainsKey(token))
            {
                Add(findings, "error", "WTS_REFERENCE_MISSING", "strings", token, "A decoded metadata field references a missing trigger string.", "Restore the referenced WTS entry or write a literal value through a typed serializer.");
            }
        }
    }

    private static void ValidateScriptEntries(JsonArray? scripts, JsonArray findings)
    {
        if (scripts is null)
        {
            Add(findings, "error", "SCRIPTS_MISSING", "scripts", null, "Canonical map scripts are unavailable.", "Regenerate the canonical map from the source archive.");
            return;
        }

        foreach (var node in scripts)
        {
            if (node is not JsonObject script)
            {
                Add(findings, "error", "SCRIPT_INVALID", "scripts", null, "Every script entry must be an object.", "Regenerate the canonical map from the source archive.");
                continue;
            }

            var archivePath = StringValue(script["archive_path"]);
            var sourceText = StringValue(script["source"]);
            if (sourceText is null)
            {
                continue;
            }

            if (!string.Equals(archivePath, "war3map.j", StringComparison.OrdinalIgnoreCase))
            {
                Add(findings, "error", "SCRIPT_MEMBER_UNSUPPORTED", "scripts", archivePath, "Only the existing war3map.j member can be MCP-owned gameplay source.", "Use a JASS war3map.j entry point.");
                continue;
            }

            try
            {
                ScriptOwnership.ValidateMcpOwnedJass(archivePath ?? string.Empty, sourceText);
            }
            catch (InvalidDataException exception)
            {
                Add(findings, "error", "SCRIPT_PARSE_FAILED", "scripts", archivePath, exception.Message, "Fix the JASS source and rerun transaction validation.");
                continue;
            }

            var hash = Hashing.Sha256(Encoding.UTF8.GetBytes(sourceText));
            if (!string.Equals(StringValue(script["source_sha256"]), hash, StringComparison.OrdinalIgnoreCase))
            {
                Add(findings, "error", "SCRIPT_HASH_MISMATCH", "scripts", archivePath, "The staged source hash does not match its source text.", "Use the exact UTF-8 source hash produced by the MCP operation.");
            }
        }
    }

    private static void ValidateBuildableScripts(JsonObject source, JsonObject staged, JsonArray findings)
    {
        var sourceScripts = Scripts(source);
        var stagedScripts = Scripts(staged);
        if (sourceScripts.Count != stagedScripts.Count)
        {
            Add(findings, "error", "BUILD_SCRIPT_IDENTITY_CHANGED", "scripts", null, "Adding or removing script entry points is not supported.", "Replace the existing war3map.j member only.");
            return;
        }

        for (var index = 0; index < sourceScripts.Count; index++)
        {
            var before = sourceScripts[index];
            var after = stagedScripts[index];
            var archivePath = StringValue(before["archive_path"]) ?? string.Empty;
            if (!string.Equals(archivePath, StringValue(after["archive_path"]), StringComparison.OrdinalIgnoreCase))
            {
                Add(findings, "error", "BUILD_SCRIPT_IDENTITY_CHANGED", "scripts", archivePath, "Script entry-point identity is immutable.", "Keep the existing war3map.j archive path.");
                continue;
            }

            if (!ScriptIdentityEqual(before, after))
            {
                Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "scripts", archivePath, "Script metadata changed without a supported source replacement.", "Change gameplay through set_script_source with an exact expected hash.");
            }

            var stagedSource = StringValue(after["source"]);
            if (stagedSource is null)
            {
                if (!string.Equals(StringValue(before["source_sha256"]), StringValue(after["source_sha256"]), StringComparison.OrdinalIgnoreCase))
                {
                    Add(findings, "error", "BUILD_SCRIPT_SOURCE_MISSING", "scripts", archivePath, "The staged script hash changed without carrying the staged source text.", "Use set_script_source to provide the complete JASS source.");
                }

                continue;
            }

            if (!archivePath.Equals("war3map.j", StringComparison.OrdinalIgnoreCase))
            {
                Add(findings, "error", "BUILD_SCRIPT_MEMBER_UNSUPPORTED", "scripts", archivePath, "Only war3map.j is enabled for MCP-owned gameplay source.", "Use the map's JASS entry point.");
            }
        }
    }

    private static bool ScriptIdentityEqual(JsonObject before, JsonObject after)
    {
        var left = before.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Script entry could not be cloned.");
        var right = after.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Script entry could not be cloned.");
        foreach (var property in new[] { "source", "source_sha256", "sha256", "size_bytes", "capability", "provenance" })
        {
            left.Remove(property);
            right.Remove(property);
        }

        return JsonUtilities.Equal(left, right);
    }

    private static void ValidateHtwInvariants(JsonObject inspection, JsonArray findings, JsonObject context)
    {
        var protectedNames = context["protected_region_names"]?.AsArray().Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray()
            ?? new[] { "Arena_A", "Camp_A_Player1" };
        var regionNames = Regions(inspection).Select(RegionName).ToHashSet(StringComparer.Ordinal);
        foreach (var name in protectedNames)
        {
            if (!regionNames.Contains(name))
            {
                Add(findings, "error", "PROTECTED_REGION_MISSING", "regions", name, "A protected Hero Team Wars region is missing or renamed.", "Preserve the exact case-sensitive region name.");
            }
        }

        var teams = context["explicit_teams"]?.AsArray()
            .Select(team => team?.AsArray().Select(x => x?.GetValue<int>()).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet() ?? new HashSet<int>())
            .Where(team => team.Count > 0).ToArray()
            ?? new[] { new HashSet<int> { 1, 2 }, new HashSet<int> { 3, 4 } };
        var forces = inspection["forces"]?.AsArray().OfType<JsonObject>().Select(x => x["player_ids"]?.AsArray().Select(y => y?.GetValue<int>()).Where(y => y.HasValue).Select(y => y!.Value).ToHashSet() ?? new HashSet<int>()).ToArray() ?? Array.Empty<HashSet<int>>();
        foreach (var team in teams)
        {
            if (!forces.Any(force => force.SetEquals(team)))
            {
                Add(findings, "error", "EXPLICIT_TEAM_ASSIGNMENT_MISSING", "forces", string.Join(",", team.OrderBy(x => x)), "The map does not contain the required explicit Hero Team Wars player team.", "Use explicit player IDs 1-2 and 3-4 in separate forces.");
            }
        }
    }

    private static Dictionary<string, JsonNode?> Metadata(JsonObject root)
        => root["metadata"] is JsonArray values
            ? values.OfType<JsonObject>().Where(x => StringValue(x["field"]) is not null).ToDictionary(x => StringValue(x["field"])!, x => x["value"], StringComparer.Ordinal)
            : throw new EngineException("INVALID_JSON", "Canonical map has no metadata array.");

    private static List<JsonObject> Regions(JsonObject root)
        => root["regions"] is JsonArray values ? values.OfType<JsonObject>().ToList() : new List<JsonObject>();

    private static List<JsonObject> Scripts(JsonObject root)
        => root["scripts"] is JsonArray values ? values.OfType<JsonObject>().ToList() : new List<JsonObject>();

    private static string RegionName(JsonObject region) => StringValue(region["name"]) ?? string.Empty;

    private static Bounds? CameraBounds(JsonArray? metadata)
    {
        var value = metadata?.OfType<JsonObject>().FirstOrDefault(x => x["field"]?.GetValue<string>() == "camera_bounds")?["value"] as JsonObject;
        var left = NumericValue(value?["left"]);
        var bottom = NumericValue(value?["bottom"]);
        var right = NumericValue(value?["right"]);
        var top = NumericValue(value?["top"]);
        return left is null || bottom is null || right is null || top is null || !IsFinite(left.Value) || !IsFinite(bottom.Value) || !IsFinite(right.Value) || !IsFinite(top.Value)
            ? null
            : new Bounds(left.Value, bottom.Value, right.Value, top.Value);
    }

    private static IEnumerable<JsonObject> EnumerateObjects(JsonNode? value)
    {
        if (value is JsonArray array)
        {
            foreach (var child in array.OfType<JsonObject>()) yield return child;
        }
        else if (value is JsonObject item)
        {
            yield return item;
        }
    }

    private static bool IsParsedCapability(JsonNode? value)
        => StringValue(value) is "parsed_read_only" or "roundtrip_verified" or "typed_write_enabled";

    private static bool ValidSuggestedPlayers(JsonNode? value)
    {
        var integer = IntegerValue(value);
        return integer is >= 1 and <= 24 || !string.IsNullOrWhiteSpace(StringValue(value));
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static double? NumericValue(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var doubleValue)) return doubleValue;
        if (value.TryGetValue<float>(out var floatValue)) return floatValue;
        if (value.TryGetValue<long>(out var longValue)) return longValue;
        if (value.TryGetValue<int>(out var integerValue)) return integerValue;
        return null;
    }

    private static int? IntegerValue(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var integer) ? integer : null;

    private static string? StringValue(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonObject Report(JsonArray findings, JsonObject? source = null, string? requestedPath = null, string target = "map")
    {
        var errors = findings.OfType<JsonObject>().Count(x => x["severity"]?.GetValue<string>() == "error");
        var warnings = findings.OfType<JsonObject>().Count(x => x["severity"]?.GetValue<string>() == "warning");
        var info = findings.OfType<JsonObject>().Count(x => x["severity"]?.GetValue<string>() == "info");
        var report = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["validator_version"] = ValidatorVersion,
            ["target"] = target,
            ["buildable"] = errors == 0,
            ["findings"] = findings,
            ["summary"] = new JsonObject { ["errors"] = errors, ["warnings"] = warnings, ["info"] = info }
        };

        if (source is not null)
        {
            report["map_path"] = source["path"]?.DeepClone() ?? requestedPath;
            report["map_sha256"] = source["sha256"]?.DeepClone();
            report["source_sha256"] = source["sha256"]?.DeepClone();
            report["map_hash"] = source["sha256"]?.DeepClone();
            report["size_bytes"] = source["size_bytes"]?.DeepClone();
            report["modified_utc"] = source["modified_utc"]?.DeepClone();
        }
        else if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            report["map_path"] = requestedPath;
        }

        return report;
    }

    private static void Add(JsonArray findings, string severity, string code, string component, string? target, string message, string suggestedAction)
    {
        findings.Add(new JsonObject
        {
            ["severity"] = severity,
            ["code"] = code,
            ["component"] = component,
            ["target"] = target,
            ["provenance"] = "derived",
            ["message"] = message,
            ["suggested_action"] = suggestedAction,
            ["suggested_remediation"] = suggestedAction
        });
    }

    private readonly record struct Bounds(double Left, double Bottom, double Right, double Top)
    {
        // Region rectangles in the editor may extend one or two tiles beyond
        // the camera rectangle. Treat that documented editor margin as part of
        // the map envelope while still rejecting clearly out-of-map values.
        private const double EditorMargin = 256.0;

        public bool Contains(double x, double y) => x >= Left - EditorMargin && x <= Right + EditorMargin && y >= Bottom - EditorMargin && y <= Top + EditorMargin;

        public bool Contains(double minX, double minY, double maxX, double maxY)
            => minX >= Left - EditorMargin && minY >= Bottom - EditorMargin && maxX <= Right + EditorMargin && maxY <= Top + EditorMargin;
    }
}
