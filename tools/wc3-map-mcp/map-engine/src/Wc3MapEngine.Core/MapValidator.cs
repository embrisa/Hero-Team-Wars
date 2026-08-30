using System.Globalization;
using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core;

public static class MapValidator
{
    public static JsonObject ValidateMap(string path)
    {
        var findings = new JsonArray();
        JsonObject? inspection = null;
        try
        {
            inspection = MapInspector.Inspect(path);
        }
        catch (EngineException exception)
        {
            findings.Add(Finding("error", "ARCHIVE_UNREADABLE", "archive", null, exception.Message, "Repair or replace the map copy before continuing."));
        }

        if (inspection is not null)
        {
            var members = inspection["archive_members"] as JsonArray ?? new JsonArray();
            if (!members.OfType<JsonObject>().Any(x => string.Equals(x["path"]?.GetValue<string>(), "war3map.w3i", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(Finding("error", "REQUIRED_MEMBER_MISSING", "metadata", "war3map.w3i", "The map has no war3map.w3i member.", "Use a complete Warcraft III map archive."));
            }

            foreach (var warning in inspection["parse_warnings"] as JsonArray ?? new JsonArray())
            {
                if (warning is JsonObject item)
                {
                    findings.Add(Finding("warning", "COMPONENT_PARSE_WARNING", item["component"]?.GetValue<string>() ?? "unknown", null, item["message"]?.GetValue<string>() ?? "Component parse warning.", "Keep the component opaque until a compatible parser is proven."));
                }
            }

            foreach (var opaque in inspection["opaque_members"] as JsonArray ?? new JsonArray())
            {
                if (opaque is JsonObject member)
                {
                    findings.Add(Finding("info", "OPAQUE_MEMBER_PRESERVED", "archive", member["path"]?.GetValue<string>(), "Archive member is preserved byte-for-byte but not semantically decoded.", "Do not edit this member through the semantic writer."));
                }
            }

            ValidateRegions(inspection["regions"] as JsonArray, findings);
        }

        // MapInspector hashes the file before opening the archive. Echo that
        // exact identity in the validation report so callers can bind the
        // findings to the map they actually inspected.
        var source = inspection?["source"] as JsonObject;
        return Report(findings, source, path);
    }

    public static JsonObject ValidateCanonical(string path)
    {
        var findings = new JsonArray();
        var root = JsonUtilities.Read(path) as JsonObject ?? throw new EngineException("INVALID_JSON", "Canonical map root must be a JSON object.");
        if (!string.Equals(root["schema_version"]?.GetValue<string>(), "1.0", StringComparison.Ordinal))
        {
            findings.Add(Finding("error", "SCHEMA_VERSION_UNSUPPORTED", "canonical_map", null, "Expected canonical map schema version 1.0.", "Regenerate the canonical map with the current engine."));
        }

        ValidateMetadata(root["metadata"] as JsonArray, findings);
        ValidatePlayers(root["players"] as JsonArray, findings);
        ValidateForces(root["forces"] as JsonArray, findings);
        ValidateRegions(root["regions"] as JsonArray, findings);

        return Report(findings);
    }

    private static void ValidateRegions(JsonArray? regions, JsonArray findings)
    {
        if (regions is null)
        {
            findings.Add(Finding("warning", "REGIONS_UNKNOWN", "regions", null, "Region inventory is unavailable.", "Inspect the map with a compatible region parser."));
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in regions)
        {
            if (node is not JsonObject region)
            {
                findings.Add(Finding("error", "REGION_INVALID", "regions", null, "Every region entry must be an object.", "Regenerate the canonical map from the source archive."));
                continue;
            }

            var name = StringValue(region["name"]);
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
            {
                findings.Add(Finding("error", "REGION_NAME_INVALID", "regions", name, "Region names must be non-empty and unique with case-sensitive identity.", "Rename the duplicate through an explicit typed operation."));
                continue;
            }

            var values = new[] { "min_x", "min_y", "max_x", "max_y" }
                .Select(key => NumericValue(region[key]))
                .ToArray();
            if (values.Any(x => x is null || double.IsNaN(x.Value) || double.IsInfinity(x.Value)) || values[0] > values[2] || values[1] > values[3])
            {
                findings.Add(Finding("error", "REGION_BOUNDS_INVALID", "regions", name, "Region bounds must be finite and min values cannot exceed max values.", "Supply finite coordinates in world space."));
            }
        }
    }

    private static void ValidateMetadata(JsonArray? metadata, JsonArray findings)
    {
        if (metadata is null)
        {
            findings.Add(Finding("error", "METADATA_MISSING", "metadata", null, "Canonical map metadata is unavailable.", "Regenerate the canonical map from the source archive."));
            return;
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in metadata)
        {
            if (node is not JsonObject entry)
            {
                findings.Add(Finding("error", "METADATA_INVALID", "metadata", null, "Every metadata entry must be an object.", "Regenerate the canonical map from the source archive."));
                continue;
            }

            var field = StringValue(entry["field"]);
            if (string.IsNullOrWhiteSpace(field) || !fields.Add(field))
            {
                findings.Add(Finding("error", "METADATA_FIELD_INVALID", "metadata", field, "Metadata fields must be non-empty and unique.", "Regenerate the canonical map from the source archive."));
                continue;
            }

            if (field == "title" && string.IsNullOrWhiteSpace(StringValue(entry["value"])))
            {
                findings.Add(Finding("error", "MAP_TITLE_INVALID", "metadata", field, "The map title must be a non-empty string.", "Set a non-empty map title through the typed metadata operation."));
            }

            if (field == "suggested_players" && !ValidSuggestedPlayers(entry["value"]))
            {
                findings.Add(Finding("error", "SUGGESTED_PLAYERS_INVALID", "metadata", field, "Suggested players must be a non-empty string or an integer from 1 through 24.", "Set a valid suggested-player value through the typed metadata operation."));
            }
        }
    }

    private static void ValidatePlayers(JsonArray? players, JsonArray findings)
    {
        if (players is null)
        {
            findings.Add(Finding("error", "PLAYERS_MISSING", "players", null, "Canonical map player slots are unavailable.", "Regenerate the canonical map from the source archive."));
            return;
        }

        var ids = new HashSet<int>();
        foreach (var node in players)
        {
            if (node is not JsonObject player)
            {
                findings.Add(Finding("error", "PLAYER_INVALID", "players", null, "Every player entry must be an object.", "Regenerate the canonical map from the source archive."));
                continue;
            }

            var id = IntegerValue(player["id"]);
            if (id is null || id is < 1 or > 24 || !ids.Add(id.Value))
            {
                findings.Add(Finding("error", "PLAYER_ID_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), "Player IDs must be unique values from 1 through 24.", "Use explicit numeric player slots."));
            }

            foreach (var field in new[] { "controller", "race" })
            {
                if (player[field] is not null && StringValue(player[field]) is null)
                {
                    findings.Add(Finding("error", "PLAYER_FIELD_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), $"Player field '{field}' must be a string.", "Regenerate the canonical map from the source archive."));
                }
            }

            if (player["start"] is JsonObject start)
            {
                if (NumericValue(start["x"]) is not { } x || double.IsNaN(x) || double.IsInfinity(x)
                    || NumericValue(start["y"]) is not { } y || double.IsNaN(y) || double.IsInfinity(y))
                {
                    findings.Add(Finding("error", "PLAYER_START_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), "Player start coordinates must be finite numbers.", "Supply finite player start coordinates."));
                }
            }
            else if (player["start"] is not null)
            {
                findings.Add(Finding("error", "PLAYER_START_INVALID", "players", id?.ToString(CultureInfo.InvariantCulture), "Player start must be an object.", "Supply finite player start coordinates."));
            }
        }
    }

    private static void ValidateForces(JsonArray? forces, JsonArray findings)
    {
        if (forces is null)
        {
            findings.Add(Finding("error", "FORCES_MISSING", "forces", null, "Canonical map force assignments are unavailable.", "Regenerate the canonical map from the source archive."));
            return;
        }

        var indices = new HashSet<int>();
        foreach (var node in forces)
        {
            if (node is not JsonObject force)
            {
                findings.Add(Finding("error", "FORCE_INVALID", "forces", null, "Every force entry must be an object.", "Regenerate the canonical map from the source archive."));
                continue;
            }

            var index = IntegerValue(force["index"]);
            if (index is null || index is < 0 or > 23 || !indices.Add(index.Value))
            {
                findings.Add(Finding("error", "FORCE_INDEX_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), "Force indices must be unique values from 0 through 23.", "Use explicit force indices."));
            }

            if (force["player_ids"] is JsonArray playerIds)
            {
                var seenPlayers = new HashSet<int>();
                foreach (var playerIdNode in playerIds)
                {
                    var playerId = IntegerValue(playerIdNode);
                    if (playerId is null || playerId is < 1 or > 24 || !seenPlayers.Add(playerId.Value))
                    {
                        findings.Add(Finding("error", "FORCE_PLAYER_ID_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), "Force player IDs must be unique values from 1 through 24.", "Use explicit numeric player IDs."));
                    }
                }
            }
            else if (force["player_ids"] is not null)
            {
                findings.Add(Finding("error", "FORCE_PLAYER_ID_INVALID", "forces", index?.ToString(CultureInfo.InvariantCulture), "Force player_ids must be an array.", "Use explicit numeric player IDs."));
            }
        }
    }

    private static JsonObject Report(JsonArray findings, JsonObject? source = null, string? requestedPath = null)
    {
        var hasErrors = findings.OfType<JsonObject>().Any(x => string.Equals(x["severity"]?.GetValue<string>(), "error", StringComparison.Ordinal));
        var report = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["buildable"] = !hasErrors,
            ["findings"] = findings,
            ["summary"] = new JsonObject
            {
                ["errors"] = findings.OfType<JsonObject>().Count(x => x["severity"]?.GetValue<string>() == "error"),
                ["warnings"] = findings.OfType<JsonObject>().Count(x => x["severity"]?.GetValue<string>() == "warning"),
                ["info"] = findings.OfType<JsonObject>().Count(x => x["severity"]?.GetValue<string>() == "info")
            }
        };

        if (source is not null)
        {
            report["map_path"] = source["path"]?.DeepClone() ?? requestedPath;
            report["map_sha256"] = source["sha256"]?.DeepClone();
            // Keep the source_sha256/map_hash aliases for existing worker
            // clients while map_sha256 is the canonical validation field.
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

    private static JsonObject Finding(string severity, string code, string component, string? target, string message, string remediation) => new()
    {
        ["severity"] = severity,
        ["code"] = code,
        ["component"] = component,
        ["target"] = target,
        ["provenance"] = "derived",
        ["message"] = message,
        ["suggested_remediation"] = remediation
    };

    private static double? NumericValue(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var doubleValue)) return doubleValue;
        if (value.TryGetValue<float>(out var floatValue)) return floatValue;
        if (value.TryGetValue<int>(out var integerValue)) return integerValue;
        if (value.TryGetValue<long>(out var longValue)) return longValue;
        return null;
    }

    private static int? IntegerValue(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var integer) ? integer : null;

    private static string? StringValue(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool ValidSuggestedPlayers(JsonNode? value)
    {
        var integer = IntegerValue(value);
        return integer is >= 1 and <= 24 || !string.IsNullOrWhiteSpace(StringValue(value));
    }
}
