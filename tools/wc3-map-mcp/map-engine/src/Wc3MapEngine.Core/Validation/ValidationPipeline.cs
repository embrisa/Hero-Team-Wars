using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Wc3MapEngine.Core.Gameplay;
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
                var sourceContext = context?.DeepClone() as JsonObject;
                sourceContext?.Remove("profile");
                ValidateInspection(sourceInspection, findings, sourceContext);
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
        ValidateObjectDefinitions(inspection, inspection["object_data"] as JsonArray, findings);
        ValidatePlacements(inspection, inspection["placed_objects"] as JsonArray, findings, bounds, context);
        ValidateObjectReferences(inspection, findings);
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
        ValidateObjectDefinitions(root, root["object_data"] as JsonArray, findings);
        ValidatePlacements(root, root["placed_objects"] as JsonArray, findings, bounds, context);
        ValidateObjectReferences(root, findings);
        ValidateTeams(root, findings, context);
        ValidateRawcodes(root, findings);
        ValidateImports(root, findings);
        ValidateScriptEntries(root["scripts"] as JsonArray, findings);
        ValidateGameplayModel(root, findings);

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
            if (field is not ("title" or "suggested_players" or "map_flags"))
            {
                Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "metadata", field, "This metadata field has no proven serializer in the Phase 3 writer.", "Use only title, suggested_players, or map_flags until another round-trip serializer is promoted.");
            }
        }

        var stagedRegions = Regions(staged);
        foreach (var after in stagedRegions)
        {
            foreach (var field in after.Select(x => x.Key))
            {
                if (field is not ("id" or "name" or "stored_name" or "min_x" or "min_y" or "max_x" or "max_y" or "creation_number" or "weather" or "ambient_sound" or "color_argb" or "references" or "codec_version" or "provenance" or "capability"))
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

        ValidateBuildableObjectChanges(source, staged, findings);
        ValidateBuildablePlacementChanges(source, staged, findings);
        ValidateBuildableScripts(source, staged, findings);
        ValidateGameplaySourceHashes(staged, findings);

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
        sourceClone.Remove("profile_spec");
        stagedClone.Remove("profile_spec");
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

    private static void ValidateBuildableObjectChanges(JsonObject source, JsonObject staged, JsonArray findings)
    {
        var before = EnumerateObjects(source["object_data"]).GroupBy(ObjectIdentity, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var after = EnumerateObjects(staged["object_data"]).GroupBy(ObjectIdentity, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var identity in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase))
        {
            before.TryGetValue(identity, out var left);
            after.TryGetValue(identity, out var right);
            var member = StringValue(right?["archive_path"]) ?? StringValue(left?["archive_path"])
                ?? (StringValue(right?["category"]) is { } category ? ObjectPlacementSupport.MemberForCategory(category) : null)
                ?? (StringValue(left?["category"]) is { } oldCategory ? ObjectPlacementSupport.MemberForCategory(oldCategory) : null);
            if (member is null || IsMemberBuildable(source, member))
            {
                if (left is null || right is null) continue;
                foreach (var field in left.Select(item => item.Key).Union(right.Select(item => item.Key), StringComparer.Ordinal))
                {
                    left.TryGetPropertyValue(field, out var oldValue);
                    right.TryGetPropertyValue(field, out var newValue);
                    if (JsonUtilities.Equal(oldValue, newValue) || field is "provenance" or "capability" or "codec_version") continue;
                    if (field == "unknown_ids") Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "object_data", identity, "Unknown object-data fields are preserved but are not mutable.", "Keep unknown_ids unchanged and edit only typed modifications.");
                    else if (field is not ("display_name" or "references" or "modifications")) Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "object_data", identity, $"Object field '{field}' changed without a typed object-data operation.", "Use create/update/delete_object_definition or set_object_reference for supported object fields.");
                }
                continue;
            }
            Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "object_data", member, "The staged object-data member is not round-trip writable for this map.", "Keep edits disabled until that specific category member has typed round-trip evidence.");
        }
    }

    private static void ValidateBuildablePlacementChanges(JsonObject source, JsonObject staged, JsonArray findings)
    {
        var before = EnumerateObjects(source["placed_objects"]).GroupBy(PlacementIdentity, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var after = EnumerateObjects(staged["placed_objects"]).GroupBy(PlacementIdentity, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var identity in before.Keys.Union(after.Keys, StringComparer.Ordinal))
        {
            before.TryGetValue(identity, out var left);
            after.TryGetValue(identity, out var right);
            var member = StringValue(right?["member"]) ?? StringValue(left?["member"]);
            if (member is not null && !IsMemberBuildable(source, member))
            {
                Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "placed_objects", identity, "The staged placement member is not round-trip writable for this map.", "Keep edits disabled until the placement member has typed round-trip evidence.");
                continue;
            }
            if (left is null || right is null) continue;
            foreach (var field in left.Select(item => item.Key).Union(right.Select(item => item.Key), StringComparer.Ordinal))
            {
                left.TryGetPropertyValue(field, out var oldValue);
                right.TryGetPropertyValue(field, out var newValue);
                if (JsonUtilities.Equal(oldValue, newValue) || field is "provenance" or "capability" or "codec_version") continue;
                if (field is not ("rawcode" or "skin_rawcode" or "owner_id" or "flags" or "hit_points" or "mana_points" or "hero_level" or "hero_strength" or "hero_agility" or "hero_intelligence" or "inventory" or "abilities" or "variation" or "position" or "facing" or "scale" or "map_region_role" or "waygate_destination_region_id" or "custom_player_color_id" or "life" or "state" or "member" or "kind"))
                {
                    Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "placed_objects", identity, $"Placement field '{field}' changed without a typed placement operation.", "Use place/move/update/remove_object operations for supported placement fields.");
                }
                if (field is "id" or "creation_number" or "member" or "kind") Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "placed_objects", identity, $"Placement identity field '{field}' is immutable.", "Preserve the native creation number, placement kind, and archive member identity.");
            }
        }
    }

    private static bool IsMemberBuildable(JsonObject source, string member)
    {
        var capability = (source["object_data_members"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals(StringValue(item["archive_path"]), member, StringComparison.OrdinalIgnoreCase))?["capability"];
        capability ??= (source["archive_members"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals(StringValue(item["path"]), member, StringComparison.OrdinalIgnoreCase))?["capability"];
        return capability is null || StringValue(capability) is "roundtrip_verified" or "typed_write_enabled" or "staged_typed_write";
    }

    private static string ObjectIdentity(JsonObject value)
        => StringValue(value["id"]) ?? $"{StringValue(value["category"])}:{StringValue(value["rawcode"])}";

    private static string PlacementIdentity(JsonObject value)
        => StringValue(value["id"]) ?? $"{StringValue(value["member"])}:{StringValue(value["creation_number"])}";

    private static string? SpecialDoodadId(JsonObject placement)
    {
        var rawcode = StringValue(placement["rawcode"]);
        var position = placement["position"] as JsonObject;
        var x = IntegerValue(position?["x"]);
        var y = IntegerValue(position?["y"]);
        return rawcode is not null && x is not null && y is not null
            ? FormattableString.Invariant($"special-doodad:{rawcode}:{x}:{y}")
            : null;
    }

    private static void ValidateGameplayModel(JsonObject root, JsonArray findings)
    {
        if (root["gameplay_modules"] is null && root["gameplay_triggers"] is null && root["gameplay_variables"] is null && root["gameplay_source"] is null) return;
        var hasGameplayEntries = (root["gameplay_modules"] as JsonArray)?.Count > 0 || (root["gameplay_triggers"] as JsonArray)?.Count > 0 || (root["gameplay_variables"] as JsonArray)?.Count > 0;
        if (hasGameplayEntries && root["gameplay_source"] is not JsonObject)
        {
            Add(findings, "error", "GAMEPLAY_SOURCE_MANIFEST_MISSING", "gameplay", "gameplay_source", "Source-owned gameplay entries require a generated gameplay source manifest.", "Recompose the MCP-native gameplay source before validating or building.");
        }
        try
        {
            GameplayModelValidator.ValidateCollections(root, requireModuleSources: true);
            ValidateGameplaySourceHashes(root, findings);
        }
        catch (EngineException exception)
        {
            Add(findings, "error", exception.Code, "gameplay", null, exception.Message, "Fix the typed gameplay source model before building.");
        }
    }

    private static void ValidateGameplaySourceHashes(JsonObject root, JsonArray findings)
    {
        if (root["gameplay_source"] is not JsonObject sourceRecord) return;
        var sourceHash = StringValue(sourceRecord["source_sha256"]);
        var script = (root["scripts"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(item => string.Equals(StringValue(item["archive_path"]), "war3map.j", StringComparison.OrdinalIgnoreCase));
        var scriptHash = StringValue(script?["source_sha256"]) ?? StringValue(script?["sha256"]);
        if (sourceHash is null || !Sha256.IsMatch(sourceHash) || scriptHash is null || !string.Equals(sourceHash, scriptHash, StringComparison.OrdinalIgnoreCase))
        {
            Add(findings, "error", "GAMEPLAY_SOURCE_STALE", "gameplay", "gameplay_source.source_sha256", "The gameplay source manifest hash does not match the staged war3map.j source hash.", "Recompose the MCP-native gameplay source and apply it in a fresh transaction.");
        }
        if (StringValue(sourceRecord["mode"]) is not (GameplayModelValidator.NativeMode or GameplayModelValidator.EditorMode))
        {
            Add(findings, "error", "TRIGGER_MODE_INVALID", "gameplay", "gameplay_source.mode", "The gameplay source manifest declares an unsupported trigger mode.", "Use mcp_native_jass until exact GUI fixtures are verified.");
        }
        if (sourceRecord["source_manifest"] is JsonNode sourceManifest)
        {
            var declaredManifestHash = StringValue(sourceRecord["source_manifest_sha256"]);
            var actualManifestHash = GameplayModelValidator.Hash(sourceManifest);
            if (!string.Equals(declaredManifestHash, actualManifestHash, StringComparison.OrdinalIgnoreCase))
            {
                Add(findings, "error", "GAMEPLAY_MANIFEST_STALE", "gameplay", "gameplay_source.source_manifest_sha256", "The generated source manifest hash does not match its manifest content.", "Recompose the MCP-native gameplay source before building.");
            }
        }
        if (root["trigger_mode"] is JsonValue triggerMode && triggerMode.TryGetValue<string>(out var selectedMode) && !string.Equals(selectedMode, StringValue(sourceRecord["mode"]), StringComparison.Ordinal))
        {
            Add(findings, "error", "TRIGGER_MODE_MISMATCH", "gameplay", "trigger_mode", "The canonical trigger mode does not match the source manifest mode.", "Keep trigger_mode and gameplay_source.mode aligned in the same transaction.");
        }
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

            if (field == "map_flags" && IntegerValue(entry["value"]) is not >= 0)
            {
                Add(findings, "error", "MAP_FLAGS_INVALID", "metadata", field, "Map flags must be a non-negative integer.", "Set the native map flags value through the typed metadata operation.");
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

            foreach (var field in new[] { "name", "controller", "race", "flags", "start", "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask" })
            {
                if (player[field] is null) Add(findings, "error", "PLAYER_FIELD_MISSING", "players", id?.ToString(CultureInfo.InvariantCulture), $"Player slot {id?.ToString(CultureInfo.InvariantCulture) ?? "?"} is missing required field '{field}'.", "Regenerate the slot from the proven map-info codec or provide the complete slot record.");
            }

            if (player["name"] is not null && StringValue(player["name"]) is null) Add(findings, "error", "PLAYER_NAME_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), "Player names must be exact strings.", "Use the decoded map-info player name.");

            var controller = StringValue(player["controller"]);
            if (controller is not null && controller is not ("None" or "User" or "Computer" or "Neutral" or "Rescuable")) Add(findings, "error", "PLAYER_CONTROLLER_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), $"Player controller '{controller}' is not a supported Warcraft III controller value.", "Use None, User, Computer, Neutral, or Rescuable.");
            var race = StringValue(player["race"]);
            if (race is not null && race is not ("Human" or "Orc" or "NightElf" or "Undead" or "Random" or "Selectable")) Add(findings, "error", "PLAYER_RACE_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), $"Player race '{race}' is not a supported Warcraft III race value.", "Use a value exposed by the pinned MapInfo reader.");
            if (IntegerValue(player["flags"]) is < 0) Add(findings, "error", "PLAYER_FLAGS_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), "Player flags must be a non-negative integer.", "Use the native PlayerFlags value.");
            foreach (var mask in new[] { "ally_low_priority_mask", "ally_high_priority_mask", "enemy_low_priority_mask", "enemy_high_priority_mask" })
            {
                if (IntegerValue(player[mask]) is < 0) Add(findings, "error", "PLAYER_MASK_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), $"Player {mask} must be a non-negative integer.", "Use a 32-bit player mask.");
            }
            if (!IsNullOrBoolean(player["fixed_start_position"])) Add(findings, "error", "PLAYER_FIXED_START_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), "Player fixed_start_position must be a boolean when present.", "Use the derived fixed_start_position value from the map-info codec.");
            if (!IsNullOrBoolean(player["observer"])) Add(findings, "error", "PLAYER_OBSERVER_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), "Player observer must be null or a boolean.", "Keep observer null until a native observer representation is proven.");

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

            foreach (var field in new[] { "name", "flags", "player_ids", "player_mask" })
            {
                if (force[field] is null) Add(findings, "error", "FORCE_FIELD_MISSING", "forces", index?.ToString(CultureInfo.InvariantCulture), $"Force {index?.ToString(CultureInfo.InvariantCulture) ?? "?"} is missing required field '{field}'.", "Regenerate the force from the proven map-info codec.");
            }
            if (string.IsNullOrWhiteSpace(StringValue(force["name"]))) Add(findings, "error", "FORCE_NAME_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), "Force names must be non-empty exact strings.", "Set an explicit force name.");
            if (IntegerValue(force["flags"]) is < 0) Add(findings, "error", "FORCE_FLAGS_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), "Force flags must be a non-negative integer.", "Use the native ForceFlags value.");
            foreach (var flag in new[] { "alliance", "shared_vision", "shared_unit_control" })
            {
                if (force[flag] is not null && (force[flag] is not JsonValue boolean || !boolean.TryGetValue<bool>(out _))) Add(findings, "error", "FORCE_FLAG_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), $"Force {flag} must be a boolean when present.", "Use the derived alliance/vision/control flags.");
            }
            var forceFlags = IntegerValue(force["flags"]);
            foreach (var (flag, bit) in new[] { ("alliance", 1), ("shared_vision", 8), ("shared_unit_control", 16) })
            {
                if (forceFlags is not null && force[flag] is JsonValue flagValue && flagValue.TryGetValue<bool>(out var enabled) && enabled != ((forceFlags.Value & bit) != 0)) Add(findings, "error", "FORCE_FLAG_CONTRADICTION", "forces", index?.ToString(CultureInfo.InvariantCulture), $"Force {flag} disagrees with the native force flags bitset.", "Update the named flag and flags together.");
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
                var knownMask = playerIds.Aggregate(0, (value, id) => value | (1 << (id - 1)));
                if (mask is null || mask.Value >= 0 && (mask.Value & knownMask) != expectedMask || mask.Value < 0 && (mask.Value & expectedMask) != expectedMask)
                {
                    Add(findings, "error", "FORCE_MASK_CONTRADICTION", "forces", index?.ToString(CultureInfo.InvariantCulture), "The explicit force player IDs disagree with player_mask.", "Regenerate the force assignment with matching explicit player IDs and mask.");
                }
            }
            else if (force["player_ids"] is not null)
            {
                Add(findings, "error", "FORCE_PLAYER_ID_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), "Force player_ids must be an array.", "Use explicit numeric player IDs.");
            }
        }

        foreach (var playerId in playerIds.OrderBy(id => id))
        {
            if (!assigned.Contains(playerId)) Add(findings, "error", "PLAYER_FORCE_MISSING", "forces", playerId.ToString(CultureInfo.InvariantCulture), $"Player {playerId} is declared but is not assigned to any force.", "Assign every declared player to exactly one force.");
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
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var creationNumbers = new HashSet<int>();
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

            var creation = IntegerValue(region["creation_number"]);
            var id = StringValue(region["id"]);
            var expectedId = creation is null ? null : RegionSupport.StableId(creation.Value);
            if (creation is null || creation < 0 || !creationNumbers.Add(creation.Value) || id is null || !ids.Add(id) || !string.Equals(id, expectedId, StringComparison.Ordinal))
            {
                Add(findings, "error", "REGION_ID_INVALID", "regions", name, "Region creation numbers and stable ids must be present, unique, and use the region:<creation_number> identity form.", "Preserve the native creation number and derived MCP region id.");
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

        foreach (var region in regions.OfType<JsonObject>())
        {
            if (region["references"] is not JsonObject references)
            {
                Add(findings, "error", "REGION_REFERENCES_MISSING", "regions", RegionName(region), "Every canonical region must carry its complete reference inventory.", "Reinspect the map with the versioned region codec.");
            }
            else
            {
                foreach (var bucket in new[] { "mcp_owned", "editor_trigger", "custom_text", "derived_roles" })
                {
                    if (references[bucket] is not JsonArray) Add(findings, "error", "REGION_REFERENCES_INVALID", "regions", RegionName(region), $"Region references.{bucket} must be an array.", "Regenerate the canonical reference inventory.");
                }
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
            var category = StringValue(item["category"])?.ToLowerInvariant() ?? "unknown";
            foreach (var key in new[] { "rawcode", "new_rawcode", "object_id", "new_id" })
            {
                var rawcode = StringValue(item[key]);
                if (rawcode is null) continue;
                if (!Rawcode.IsMatch(rawcode))
                {
                    Add(findings, "error", "RAWCODE_INVALID", "object_data", rawcode, "Object rawcodes must be exactly four printable ASCII characters.", "Use a category-valid four-character rawcode.");
                }
                else if (!definitions.Add($"{category}:{rawcode}") && duplicateDefinitions.Add($"{category}:{rawcode}"))
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

        if (root["object_data"] is JsonObject opaqueObject && StringValue(opaqueObject["capability"]) == "preserved_opaque")
        {
            Add(findings, "info", "RAWCODE_VALIDATION_LIMIT", "object_data", null, "Rawcode uniqueness and dangling-reference checks are incomplete because object data is opaque.", "Keep object-data edits disabled until a category-aware parser is proven.");
        }
    }

    private static void ValidateObjectDefinitions(JsonObject root, JsonArray? definitions, JsonArray findings)
    {
        if (definitions is null) return;
        var identities = definitions.OfType<JsonObject>()
            .Select(definition => $"{StringValue(definition["category"])}:{StringValue(definition["rawcode"])}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in definitions)
        {
            if (node is not JsonObject definition)
            {
                Add(findings, "error", "OBJECT_DEFINITION_INVALID", "object_data", null, "Every object definition must be an object.", "Regenerate the typed object-data section.");
                continue;
            }

            var category = StringValue(definition["category"]);
            var objectKind = StringValue(definition["object_kind"]);
            var archivePath = StringValue(definition["archive_path"]);
            var rawcode = StringValue(definition["rawcode"]);
            var key = $"{category}:{rawcode}";
            if (!ObjectPlacementSupport.IsSupportedCategory(category))
            {
                Add(findings, "error", "OBJECT_CATEGORY_INVALID", "object_data", category, "Object definitions must use a supported category.", "Use unit, ability, item, destructable, doodad, buff, or upgrade.");
            }
            if (objectKind is not ("base" or "custom"))
            {
                Add(findings, "error", "OBJECT_KIND_INVALID", "object_data", rawcode, "Object definitions must declare object_kind as base or custom.", "Preserve the native base/custom object kind.");
            }
            if (ObjectPlacementSupport.IsSupportedCategory(category) && archivePath is not null && !string.Equals(archivePath, ObjectPlacementSupport.MemberForCategory(category!), StringComparison.OrdinalIgnoreCase))
            {
                Add(findings, "error", "OBJECT_MEMBER_INVALID", "object_data", rawcode, "Object definition category and archive member must agree.", "Keep each object definition in its category-specific member.");
            }
            if (rawcode is null || !Rawcode.IsMatch(rawcode))
            {
                Add(findings, "error", "RAWCODE_INVALID", "object_data", rawcode, "Object definition rawcodes must be exactly four printable ASCII characters.", "Use a valid category-specific rawcode.");
            }
            else if (!seen.Add(key))
            {
                Add(findings, "error", "RAWCODE_DUPLICATE", "object_data", key, "Object rawcodes must be unique within their category.", "Choose a unique custom rawcode.");
            }

            foreach (var field in new[] { "base_rawcode", "custom_rawcode" })
            {
                var value = StringValue(definition[field]);
                if (value is not null && !Rawcode.IsMatch(value)) Add(findings, "error", "RAWCODE_INVALID", "object_data", value, $"Object definition {field} must be exactly four printable ASCII characters.", "Use a category-valid four-character rawcode.");
            }
            var baseRawcode = StringValue(definition["base_rawcode"]);
            var customRawcode = StringValue(definition["custom_rawcode"]);
            if (objectKind is "base" or "custom" && (baseRawcode is null || customRawcode is null))
            {
                Add(findings, "error", "OBJECT_IDENTITY_INVALID", "object_data", rawcode, "Object definitions require base_rawcode and custom_rawcode.", "Preserve both native object identity rawcodes.");
            }
            if (objectKind == "custom" && ObjectPlacementSupport.IsSupportedCategory(category) && baseRawcode is not null && customRawcode is not null
                && !ObjectPlacementSupport.IsKnownStandard(category!, baseRawcode)
                && !identities.Contains($"{category}:{baseRawcode}"))
            {
                Add(findings, "error", "BASE_REFERENCE_MISSING", "object_data", baseRawcode, "A custom object references a base object that is not known or staged in the same category.", "Use a valid standard base rawcode or stage the base definition first.");
            }
            if (objectKind is "base" or "custom" && baseRawcode is not null && customRawcode is not null && rawcode is not null)
            {
                var activeRawcode = objectKind == "custom" ? customRawcode : baseRawcode;
                if (rawcode != activeRawcode) Add(findings, "error", "OBJECT_IDENTITY_INVALID", "object_data", rawcode, "Object rawcode must match the active base/custom rawcode.", "Preserve the native object identity fields.");
                if (objectKind == "custom" && baseRawcode == customRawcode) Add(findings, "error", "OBJECT_IDENTITY_INVALID", "object_data", rawcode, "Custom objects must use distinct base and custom rawcodes.", "Choose a custom rawcode different from its base rawcode.");
            }
            if (definition.TryGetPropertyValue("display_name", out var displayName) && displayName is not null && StringValue(displayName) is null)
            {
                Add(findings, "error", "OBJECT_DISPLAY_NAME_INVALID", "object_data", rawcode, "Object display_name must be a string or null.", "Use the category name field through the typed object operation.");
            }

            if (definition["modifications"] is not null && definition["modifications"] is not JsonArray)
            {
                Add(findings, "error", "OBJECT_MODIFICATIONS_INVALID", "object_data", rawcode, "Object modifications must be an array.", "Use typed modification records.");
            }
            if (definition["dependencies"] is not null && definition["dependencies"] is not JsonArray)
            {
                Add(findings, "error", "OBJECT_DEPENDENCIES_INVALID", "object_data", rawcode, "Object dependencies must be an array of rawcodes.", "Use same-category dependency rawcodes.");
            }
            if (definition["references"] is not null && definition["references"] is not JsonObject)
            {
                Add(findings, "error", "OBJECT_REFERENCES_INVALID", "object_data", rawcode, "Object references must be a typed relation object.", "Use ability, item, upgrade, owner, or region references.");
            }
            if (definition["modifications"] is JsonArray modifications)
            {
                foreach (var nodeModification in modifications)
                {
                    if (nodeModification is not JsonObject modification)
                    {
                        Add(findings, "error", "OBJECT_MODIFICATION_INVALID", "object_data", rawcode, "Object modifications must be typed records.", "Use id, type, value, and the category-specific level or variation fields.");
                        continue;
                    }
                    var modificationId = StringValue(modification["id"]);
                    var modificationType = StringValue(modification["type"]);
                    if (modificationId is null || !Rawcode.IsMatch(modificationId) || modificationType is not ("Int" or "Real" or "Unreal" or "String" or "Bool" or "Char") || !ValidObjectModificationValue(modification["value"], modificationType))
                    {
                        Add(findings, "error", "OBJECT_MODIFICATION_INVALID", "object_data", rawcode, "Object modification id, type, and value must agree.", "Use the typed scalar value that matches the declared object-data type.");
                    }
                    if (category is "ability" or "upgrade" && (IntegerValue(modification["level"]) is null || IntegerValue(modification["pointer"]) is null))
                    {
                        Add(findings, "error", "OBJECT_MODIFICATION_SCOPE_INVALID", "object_data", rawcode, "Ability and upgrade modifications require level and pointer scope.", "Preserve the native level-based modification scope.");
                    }
                    if (category == "doodad" && (IntegerValue(modification["variation"]) is null || IntegerValue(modification["pointer"]) is null))
                    {
                        Add(findings, "error", "OBJECT_MODIFICATION_SCOPE_INVALID", "object_data", rawcode, "Doodad modifications require variation and pointer scope.", "Preserve the native variation-based modification scope.");
                    }
                    if (category is not ("ability" or "upgrade" or "doodad") && (modification["level"] is not null || modification["pointer"] is not null || modification["variation"] is not null))
                    {
                        Add(findings, "error", "OBJECT_MODIFICATION_SCOPE_INVALID", "object_data", rawcode, "Simple object-data modifications cannot carry level or variation scope.", "Remove unsupported scope fields from simple object categories.");
                    }
                }
            }
        }
    }

    private static bool ValidObjectModificationValue(JsonNode? value, string type)
    {
        if (value is not JsonValue scalar) return false;
        return type switch
        {
            "Int" => IntegerValue(value) is not null,
            "Real" or "Unreal" => NumericValue(value) is { } number && IsFinite(number),
            "String" => StringValue(value) is not null,
            "Bool" => scalar.TryGetValue<bool>(out _),
            "Char" => StringValue(value) is { Length: 1 },
            _ => false
        };
    }

    private static void ValidatePlacements(JsonObject root, JsonArray? placements, JsonArray findings, Bounds? bounds, JsonObject? context)
    {
        if (placements is null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var nativeIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            var kind = StringValue(placement["kind"])?.ToLowerInvariant();
            if (kind is not ("unit" or "building" or "item" or "doodad" or "destructable" or "special_doodad")) Add(findings, "error", "PLACEMENT_KIND_INVALID", "placed_objects", id, "Placed objects must declare a supported placement kind.", "Use unit, building, item, doodad, destructable, or special_doodad.");
            var expectedMember = kind is null ? null : PlacementMember(kind);
            var member = StringValue(placement["member"]);
            if (expectedMember is not null && !string.Equals(member, expectedMember, StringComparison.OrdinalIgnoreCase)) Add(findings, "error", "PLACEMENT_MEMBER_INVALID", "placed_objects", id, "Placement kind and archive member must agree.", "Keep units/buildings/items in war3mapUnits.doo and doodads/destructables in war3map.doo.");
            if (rawcode is null || !Rawcode.IsMatch(rawcode)) Add(findings, "error", "RAWCODE_INVALID", "placed_objects", rawcode, "Placed-object rawcodes must be exactly four printable ASCII characters.", "Use a valid unit, item, doodad, or destructable rawcode.");
            if (id is null || !ids.Add(id)) Add(findings, "error", "PLACEMENT_ID_INVALID", "placed_objects", id, "Placed objects require unique stable MCP IDs.", "Assign one stable id to every placement.");
            var creation = IntegerValue(placement["creation_number"]);
            var expectedIdKind = member?.Equals("war3map.doo", StringComparison.OrdinalIgnoreCase) == true ? "doodad" : "unit";
            if (kind == "special_doodad")
            {
                var expectedSpecialId = SpecialDoodadId(placement);
                if (creation is not null || id is null || expectedSpecialId is null || id != expectedSpecialId) Add(findings, "error", "PLACEMENT_ID_INVALID", "placed_objects", id, "Special doodad IDs must be derived from rawcode and integer position because the native record has no creation number.", "Use special-doodad:<rawcode>:<x>:<y>.");
            }
            else
            {
                if (creation is null || id is null || id != $"{expectedIdKind}:{creation}") Add(findings, "error", "PLACEMENT_ID_INVALID", "placed_objects", id, "Placed-object IDs must be derived from the native creation number and archive member.", "Use unit:<creation_number> or doodad:<creation_number>.");
                if (creation is not null && member is not null && !nativeIdentities.Add($"{member}:{creation}")) Add(findings, "error", "PLACEMENT_ID_INVALID", "placed_objects", id, "Native placement creation numbers must be unique within an archive member.", "Preserve one canonical placement per native creation number.");
            }
            var owner = IntegerValue(placement["owner_id"]);
            if (owner is not null && (owner is < 1 or > 24 || !PlayerExists(root, owner.Value))) Add(findings, "error", "PLACEMENT_OWNER_INVALID", "placed_objects", id, "Placement owner_id must reference a declared player slot from 1 through 24.", "Use an explicit valid player ID.");
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

            if (kind is "unit" or "building" or "item")
            {
                var category = kind == "item" ? "item" : "unit";
                if (!ObjectRawcodeExists(root, category, rawcode)) Add(findings, "error", "PLACEMENT_REFERENCE_MISSING", "placed_objects", id, "The placement rawcode is not known in its category.", "Use a standard or staged object rawcode.");
                ValidatePlacementArrays(root, placement, findings, id);
            }
            else if (kind is "doodad" or "special_doodad" or "destructable")
            {
                var category = kind == "destructable" ? "destructable" : "doodad";
                if (!ObjectRawcodeExists(root, category, rawcode)) Add(findings, "error", "PLACEMENT_REFERENCE_MISSING", "placed_objects", id, "The placement rawcode is not known in its category.", "Use a standard or staged object rawcode.");
            }

            if (context?["placement_limits"] is JsonObject limits && expectedMember is not null)
            {
                var limit = IntegerValue(limits[expectedMember]);
                if (limit is not null && placements.Count(item => item is JsonObject candidate && string.Equals(StringValue(candidate["member"]), expectedMember, StringComparison.OrdinalIgnoreCase)) > limit.Value)
                {
                    Add(findings, "error", "PLACEMENT_LIMIT_EXCEEDED", "placed_objects", expectedMember, $"The placement count exceeds the configured limit of {limit.Value} for {expectedMember}.", "Remove placements or raise the explicit project placement limit.");
                }
            }
        }
    }

    private static void ValidatePlacementArrays(JsonObject root, JsonObject placement, JsonArray findings, string? id)
    {
        if (placement["inventory"] is not JsonArray inventory) Add(findings, "error", "PLACEMENT_INVENTORY_INVALID", "placed_objects", id, "Unit-like placements require an inventory array.", "Use an empty inventory array when no items are carried.");
        else
        {
            var slots = new HashSet<int>();
            foreach (var item in inventory.OfType<JsonObject>())
            {
                var slot = IntegerValue(item["slot"]);
                var itemRawcode = StringValue(item["rawcode"]);
                if (slot is null or < 0 or > 5 || !slots.Add(slot.Value)) Add(findings, "error", "PLACEMENT_INVENTORY_INVALID", "placed_objects", id, "Inventory slots must be unique integers from 0 through 5.", "Normalize each carried item to one unique slot.");
                if (itemRawcode is null || !ObjectRawcodeExists(root, "item", itemRawcode)) Add(findings, "error", "PLACEMENT_REFERENCE_MISSING", "placed_objects", itemRawcode, "An inventory item rawcode is not known.", "Use a standard or staged item rawcode.");
            }
        }
        if (placement["abilities"] is not JsonArray abilities) Add(findings, "error", "PLACEMENT_ABILITIES_INVALID", "placed_objects", id, "Unit-like placements require an abilities array.", "Use an empty abilities array when no abilities are attached.");
        else
        {
            var abilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in abilities.OfType<JsonObject>())
            {
                var abilityRawcode = StringValue(item["rawcode"]);
                if (abilityRawcode is null || !abilityIds.Add(abilityRawcode)) Add(findings, "error", "PLACEMENT_ABILITIES_INVALID", "placed_objects", id, "Ability rawcodes must be valid and unique per placement.", "Normalize attached abilities by rawcode.");
                if (abilityRawcode is null || !ObjectRawcodeExists(root, "ability", abilityRawcode)) Add(findings, "error", "PLACEMENT_REFERENCE_MISSING", "placed_objects", abilityRawcode, "An attached ability rawcode is not known.", "Use a standard or staged ability rawcode.");
            }
        }
    }

    private static void ValidateObjectReferences(JsonObject root, JsonArray findings)
    {
        foreach (var definition in EnumerateObjects(root["object_data"]))
        {
            var category = StringValue(definition["category"]);
            if (!ObjectPlacementSupport.IsSupportedCategory(category)) continue;
            foreach (var dependency in (definition["dependencies"] as JsonArray ?? new JsonArray()))
            {
                var rawcode = StringValue(dependency);
                if (rawcode is null || !ObjectRawcodeExists(root, category!, rawcode)) Add(findings, "error", "REFERENCE_MISSING", "object_data", rawcode, "An object dependency references a missing same-category object.", "Use a standard or staged object rawcode.");
            }
            if (definition["references"] is not JsonObject references) continue;
            foreach (var reference in references)
            {
                if (reference.Key is "ability" or "item" or "upgrade")
                {
                    var referenceRawcode = ReferenceRawcode(reference.Value);
                    if (referenceRawcode is null || !ObjectRawcodeExists(root, reference.Key, referenceRawcode)) Add(findings, "error", "REFERENCE_MISSING", "object_data", reference.Key, "An object reference points to a missing object.", "Use a standard or staged referenced rawcode.");
                }
                else if (reference.Key == "owner")
                {
                    var owner = ReferenceInteger(reference.Value, "player_id");
                    if (owner is null || !PlayerExists(root, owner.Value)) Add(findings, "error", "REFERENCE_MISSING", "object_data", "owner", "An object owner reference points to a missing player.", "Use a declared player slot.");
                }
                else if (reference.Key == "region")
                {
                    var region = ReferenceInteger(reference.Value, "region_id");
                    if (region is null || !RegionExists(root, region.Value)) Add(findings, "error", "REFERENCE_MISSING", "object_data", "region", "An object region reference points to a missing region.", "Use a declared region creation number.");
                }
                else Add(findings, "error", "REFERENCE_RELATION_INVALID", "object_data", reference.Key, "Object reference relations must be ability, item, upgrade, owner, or region.", "Use a supported typed relation.");
            }
        }
    }

    private static string? ReferenceRawcode(JsonNode? value)
        => value is JsonObject reference ? StringValue(reference["rawcode"]) : StringValue(value);

    private static int? ReferenceInteger(JsonNode? value, string property)
        => value is JsonObject reference ? IntegerValue(reference[property]) : IntegerValue(value);

    private static bool ObjectRawcodeExists(JsonObject root, string category, string? rawcode)
        => rawcode is not null && (ObjectPlacementSupport.IsKnownStandard(category, rawcode)
            || EnumerateObjects(root["object_data"]).Any(item => string.Equals(StringValue(item["category"]), category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(StringValue(item["rawcode"]), rawcode, StringComparison.OrdinalIgnoreCase)));

    private static bool RegionExists(JsonObject root, int creationNumber)
        => Regions(root).Any(region => IntegerValue(region["creation_number"]) == creationNumber);

    private static bool PlayerExists(JsonObject root, int playerId)
        => (root["players"] as JsonArray)?.OfType<JsonObject>().Any(player => IntegerValue(player["id"]) == playerId) == true;

    private static string PlacementMember(string kind) => kind.ToLowerInvariant() switch
        {
            "unit" or "building" or "item" => "war3mapUnits.doo",
            "doodad" or "destructable" or "special_doodad" => "war3map.doo",
            _ => string.Empty
        };

    private static void ValidateTeams(JsonObject inspection, JsonArray findings, JsonObject? context)
    {
        var profile = StringValue(inspection["profile"]) ?? StringValue(context?["profile"]);
        if (inspection["teams"] is not JsonArray teams)
        {
            if (HtwProfileModel.IsKnown(profile)) Add(findings, "error", "TEAMS_MISSING", "teams", profile, "The active Hero Team Wars profile has no logical team registry.", "Compose the profile and provide explicit team records.");
            return;
        }
        var players = inspection["players"]?.AsArray().OfType<JsonObject>().Select(item => IntegerValue(item["id"])).Where(value => value.HasValue).Select(value => value!.Value).ToHashSet() ?? new HashSet<int>();
        var forces = inspection["forces"]?.AsArray().OfType<JsonObject>().GroupBy(item => IntegerValue(item["index"]) ?? -1).ToDictionary(group => group.Key, group => group.First()["player_ids"]?.AsArray().OfType<JsonValue>().Select(IntegerValue).Where(value => value.HasValue).Select(value => value!.Value).ToHashSet() ?? new HashSet<int>()) ?? new Dictionary<int, HashSet<int>>();
        var teamIds = new HashSet<string>(StringComparer.Ordinal);
        var assigned = new HashSet<int>();
        var teamById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in teams)
        {
            if (node is not JsonObject team)
            {
                Add(findings, "error", "TEAM_INVALID", "teams", null, "Every logical team must be an object.", "Regenerate the explicit team registry.");
                continue;
            }
            var id = StringValue(team["id"]) ?? StringValue(team["team_id"]);
            if (id is null || !teamIds.Add(id)) Add(findings, "error", "TEAM_ID_INVALID", "teams", id, "Logical team IDs must be unique and explicit.", "Use stable team IDs independent of player color.");
            else teamById[id] = team;
            foreach (var field in new[] { "name", "member_player_ids", "force_index", "arena_id", "hero_ids", "life_state", "routing_state" })
            {
                if (team[field] is null) Add(findings, "error", "TEAM_FIELD_MISSING", "teams", id, $"Team '{id ?? "?"}' is missing required field '{field}'.", "Provide the complete logical team record.");
            }
            if (string.IsNullOrWhiteSpace(StringValue(team["name"]))) Add(findings, "error", "TEAM_NAME_INVALID", "teams", id, "Logical team names must be non-empty.", "Set an exact team name.");
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
            var teamForceIndex = IntegerValue(team["force_index"]);
            if (teamForceIndex is null or < 0 or > 23) Add(findings, "error", "TEAM_FORCE_INVALID", "teams", id, "Every logical team must reference a valid force index.", "Set force_index to the force containing the same players.");
            if (string.IsNullOrWhiteSpace(StringValue(team["arena_id"]))) Add(findings, "error", "TEAM_ARENA_MISSING", "teams", id, "Every logical team must have an explicit arena assignment.", "Set a stable arena_id before building.");
            if (team["hero_ids"] is not JsonArray) Add(findings, "error", "TEAM_HERO_IDS_INVALID", "teams", id, "Team hero_ids must be an array.", "Provide the explicit hero identity list, even when empty.");
            if (string.IsNullOrWhiteSpace(StringValue(team["life_state"]))) Add(findings, "error", "TEAM_LIFE_STATE_INVALID", "teams", id, "Team life_state must be explicit.", "Use active or eliminated.");
            if (string.IsNullOrWhiteSpace(StringValue(team["routing_state"]))) Add(findings, "error", "TEAM_ROUTING_STATE_INVALID", "teams", id, "Team routing_state must be explicit.", "Use unassigned or a locked route state.");
            var forceIndex = IntegerValue(team["force_index"]);
            if (forceIndex is not null && !forces.ContainsKey(forceIndex.Value))
            {
                Add(findings, "error", "TEAM_FORCE_REFERENCE_INVALID", "teams", id, $"Logical team '{id}' references missing force {forceIndex.Value}.", "Create the referenced force or update the team to an existing force index.");
            }
            else if (forceIndex is not null && forces.TryGetValue(forceIndex.Value, out var forceMembers))
            {
                var teamMembers = members?.OfType<JsonValue>().Select(IntegerValue).Where(value => value.HasValue).Select(value => value!.Value).ToHashSet() ?? new HashSet<int>();
                if (!forceMembers.SetEquals(teamMembers)) Add(findings, "error", "TEAM_FORCE_MEMBERSHIP_CONTRADICTION", "teams", id, "Logical team membership differs from its referenced force membership.", "Update the team and force together with complete expected prior values.");
            }
        }

        if (profile == "mvp_2arena" && teams.Count != 2) Add(findings, "error", "PROFILE_TEAM_COUNT_INVALID", "teams", profile, "The mvp_2arena profile requires exactly two logical teams.", "Use two explicit teams of two players.");
        if (profile == "full_6team" && teams.Count != 6) Add(findings, "error", "PROFILE_TEAM_COUNT_INVALID", "teams", profile, "The full_6team profile requires exactly six logical teams.", "Use six explicit teams of two players.");

        if (HtwProfileModel.IsKnown(profile))
        {
            var expected = HtwProfileModel.DefaultTeams(profile!).OfType<JsonObject>().ToDictionary(team => team["id"]!.GetValue<string>(), StringComparer.Ordinal);
            foreach (var (expectedId, expectedTeam) in expected)
            {
                if (!teamById.TryGetValue(expectedId, out var actualTeam))
                {
                    Add(findings, "error", "PROFILE_TEAM_MISSING", "teams", expectedId, $"Profile '{profile}' is missing logical team '{expectedId}'.", "Use the profile's stable team IDs.");
                    continue;
                }
                if (!JsonUtilities.Equal(actualTeam["member_player_ids"], expectedTeam["member_player_ids"])) Add(findings, "error", "PROFILE_TEAM_MEMBERS_INVALID", "teams", expectedId, $"Profile '{profile}' assigns the wrong player slots to '{expectedId}'.", "Keep profile team membership explicit and color-independent.");
                if (IntegerValue(actualTeam["force_index"]) != IntegerValue(expectedTeam["force_index"])) Add(findings, "error", "PROFILE_TEAM_FORCE_INVALID", "teams", expectedId, $"Profile '{profile}' assigns '{expectedId}' to the wrong force.", "Use the profile force index.");
                if (!string.Equals(StringValue(actualTeam["arena_id"]), StringValue(expectedTeam["arena_id"]), StringComparison.Ordinal)) Add(findings, "error", "PROFILE_TEAM_ARENA_INVALID", "teams", expectedId, $"Profile '{profile}' assigns '{expectedId}' to the wrong arena.", "Use the profile's explicit arena ID.");
            }

            var expectedPlayers = expected.Values.SelectMany(team => team["member_player_ids"]!.AsArray().Select(value => value!.GetValue<int>())).ToHashSet();
            foreach (var playerId in expectedPlayers)
            {
                var player = inspection["players"]?.AsArray().OfType<JsonObject>().FirstOrDefault(item => IntegerValue(item["id"]) == playerId);
                var controller = StringValue(player?["controller"]);
                if (player is null || controller is not ("User" or "Computer")) Add(findings, "error", "PROFILE_ACTIVE_SLOT_INVALID", "players", playerId.ToString(CultureInfo.InvariantCulture), $"Profile '{profile}' requires active user or computer slot {playerId}.", "Declare every active profile slot with controller User or Computer.");
                if (!assigned.Contains(playerId)) Add(findings, "error", "PLAYER_TEAM_MISSING", "teams", playerId.ToString(CultureInfo.InvariantCulture), $"Active player {playerId} is not assigned to a logical team.", "Assign every active profile player exactly once.");
            }
            foreach (var player in inspection["players"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                var playerId = IntegerValue(player["id"]);
                var controller = StringValue(player["controller"]);
                if (playerId is not null && !expectedPlayers.Contains(playerId.Value) && controller is "User" or "Computer") Add(findings, "error", "PROFILE_ACTIVE_SLOT_EXTRA", "players", playerId.Value.ToString(CultureInfo.InvariantCulture), $"Profile '{profile}' contains active player slot {playerId.Value} outside its declared active slot set.", "Close or remove slots outside the active profile.");
            }
            if (forces.Count != expected.Count) Add(findings, "error", "PROFILE_FORCE_COUNT_INVALID", "forces", profile, $"Profile '{profile}' requires exactly {expected.Count} forces.", "Provide one explicit force for each logical team.");
            if (profile == HtwProfileModel.MvpProfile)
            {
                var regionNames = Regions(inspection).Select(RegionName).ToHashSet(StringComparer.Ordinal);
                foreach (var camp in new[] { "Camp_A_Player1", "Camp_A_Player2", "Camp_B_Player3", "Camp_B_Player4" })
                {
                    if (!regionNames.Contains(camp)) Add(findings, "error", "TEAM_CAMP_MISSING", "regions", camp, $"The MVP profile is missing required camp assignment region '{camp}'.", "Preserve the exact case-sensitive camp region names.");
                }
            }
        }
    }

    private static void ValidatePlayerFields(JsonArray? players, JsonArray findings)
    {
        foreach (var player in players?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            foreach (var field in player.Select(item => item.Key))
            {
                if (field is not ("id" or "name" or "stored_name" or "controller" or "race" or "flags" or "start" or "ally_low_priority_mask" or "ally_high_priority_mask" or "enemy_low_priority_mask" or "enemy_high_priority_mask" or "observer" or "fixed_start_position" or "slot_status" or "codec_version" or "provenance" or "capability")) Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "players", IntegerValue(player["id"])?.ToString(CultureInfo.InvariantCulture), $"Player field '{field}' has no proven typed serializer.", "Use only fields supported by the map-info codec.");
            }
        }
    }

    private static void ValidateForceFields(JsonArray? forces, JsonArray findings)
    {
        foreach (var force in forces?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            foreach (var field in force.Select(item => item.Key))
            {
                if (field is not ("index" or "name" or "stored_name" or "flags" or "player_ids" or "player_mask" or "alliance" or "shared_vision" or "shared_unit_control" or "codec_version" or "provenance" or "capability")) Add(findings, "error", "BUILD_COMPONENT_UNSUPPORTED", "forces", IntegerValue(force["index"])?.ToString(CultureInfo.InvariantCulture), $"Force field '{field}' has no proven typed serializer.", "Use only fields supported by the map-info codec.");
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
        var profile = StringValue(inspection["profile"]) ?? StringValue(context["profile"]);
        var protectedNames = context["protected_region_names"]?.AsArray().Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray()
            ?? (profile == HtwProfileModel.FullProfile ? Array.Empty<string>() : new[] { "Arena_A", "Camp_A_Player1" });
        var regionNames = Regions(inspection).Select(RegionName).ToHashSet(StringComparer.Ordinal);
        foreach (var name in protectedNames)
        {
            if (!regionNames.Contains(name))
            {
                Add(findings, "error", "PROTECTED_REGION_MISSING", "regions", name, "A protected Hero Team Wars region is missing or renamed.", "Preserve the exact case-sensitive region name.");
            }
        }

        var teams = HtwProfileModel.IsKnown(profile)
            ? HtwProfileModel.DefaultTeams(profile!).OfType<JsonObject>().Select(team => team["member_player_ids"]!.AsArray().Select(x => x!.GetValue<int>()).ToHashSet()).ToArray()
            : context["explicit_teams"]?.AsArray()
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

    private static bool IsNullOrBoolean(JsonNode? node)
        => node is null || node is JsonValue value && value.TryGetValue<bool>(out _);

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
