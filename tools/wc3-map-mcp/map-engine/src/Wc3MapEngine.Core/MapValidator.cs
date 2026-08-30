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

        return Report(findings);
    }

    public static JsonObject ValidateCanonical(string path)
    {
        var findings = new JsonArray();
        var root = JsonUtilities.Read(path) as JsonObject ?? throw new EngineException("INVALID_JSON", "Canonical map root must be a JSON object.");
        if (!string.Equals(root["schema_version"]?.GetValue<string>(), "1.0", StringComparison.Ordinal))
        {
            findings.Add(Finding("error", "SCHEMA_VERSION_UNSUPPORTED", "canonical_map", null, "Expected canonical map schema version 1.0.", "Regenerate the canonical map with the current engine."));
        }

        ValidateRegions(root["regions"] as JsonArray, findings);
        var players = root["players"] as JsonArray;
        if (players is not null)
        {
            var ids = players.OfType<JsonObject>().Select(x => x["id"]?.GetValue<int>() ?? -1).ToList();
            if (ids.Any(x => x is < 1 or > 24) || ids.Count != ids.Distinct().Count())
            {
                findings.Add(Finding("error", "PLAYER_ID_INVALID", "players", null, "Player IDs must be unique values from 1 through 24.", "Use explicit numeric player slots."));
            }
        }

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
        foreach (var node in regions.OfType<JsonObject>())
        {
            var name = node["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
            {
                findings.Add(Finding("error", "REGION_NAME_INVALID", "regions", name, "Region names must be non-empty and unique with case-sensitive identity.", "Rename the duplicate through an explicit typed operation."));
                continue;
            }

            var values = new[] { "min_x", "min_y", "max_x", "max_y" }
                .Select(key => NumericValue(node[key]))
                .ToArray();
            if (values.Any(x => x is null || double.IsNaN(x.Value) || double.IsInfinity(x.Value)) || values[0] > values[2] || values[1] > values[3])
            {
                findings.Add(Finding("error", "REGION_BOUNDS_INVALID", "regions", name, "Region bounds must be finite and min values cannot exceed max values.", "Supply finite coordinates in world space."));
            }
        }
    }

    private static JsonObject Report(JsonArray findings)
    {
        var hasErrors = findings.OfType<JsonObject>().Any(x => string.Equals(x["severity"]?.GetValue<string>(), "error", StringComparison.Ordinal));
        return new JsonObject
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
}
