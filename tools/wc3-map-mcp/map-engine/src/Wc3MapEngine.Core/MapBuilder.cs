using System.Globalization;
using System.Text.Json.Nodes;
using War3Net.Build;
using War3Net.Build.Extensions;
using War3Net.IO.Mpq;

namespace Wc3MapEngine.Core;

public static class MapBuilder
{
    public static JsonObject Build(string sourcePath, string canonicalPath, string outputPath)
    {
        var staged = JsonUtilities.Read(canonicalPath) as JsonObject ?? throw new EngineException("INVALID_JSON", "Staged canonical map root must be an object.");
        var source = MapInspector.Inspect(sourcePath);
        var replacements = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        var metadataChanged = ApplyMetadataChanges(source, staged, sourcePath, replacements);
        var regionsChanged = ApplyRegionChanges(source, staged, sourcePath, replacements);
        EnsureOnlySupportedChanges(source, staged, metadataChanged, regionsChanged);

        MapArchive.Rebuild(sourcePath, outputPath, replacements);
        try
        {
            var reopened = MapInspector.Inspect(outputPath);
            EnsurePublishedValues(staged, reopened);
            var hash = Hashing.HashFileAsync(outputPath).GetAwaiter().GetResult();
            return new JsonObject
            {
                ["schema_version"] = "1.0",
                ["output_path"] = outputPath,
                ["size_bytes"] = hash.Size,
                ["sha256"] = hash.Sha256,
                ["source_sha256"] = source["source"]!["sha256"]!.DeepClone(),
                ["runtime_status"] = "untested",
                ["reopened"] = true,
                ["semantic_changes"] = SemanticDiff.Compare(source, staged, "build").Count,
                ["opaque_members_preserved"] = true
            };
        }
        catch
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            throw;
        }
    }

    private static bool ApplyMetadataChanges(JsonObject source, JsonObject staged, string sourcePath, Dictionary<string, byte[]> replacements)
    {
        var sourceMetadata = Metadata(source);
        var stagedMetadata = Metadata(staged);
        var changed = false;
        var titleChanged = !JsonUtilities.Equal(sourceMetadata["title"], stagedMetadata["title"]);
        var suggestedChanged = !JsonUtilities.Equal(sourceMetadata["suggested_players"], stagedMetadata["suggested_players"]);
        if (!titleChanged && !suggestedChanged)
        {
            return false;
        }

        var current = ReadMap(sourcePath, MapFiles.Info);
        if (current.Info is null)
        {
            throw new EngineException("BUILD_UNSUPPORTED", "Map metadata could not be parsed for serialization.");
        }

        if (titleChanged)
        {
            current.Info.MapName = GetValueString(stagedMetadata["title"], "title");
            changed = true;
        }

        if (suggestedChanged)
        {
            var value = stagedMetadata["suggested_players"];
            if (value is null)
            {
                throw new EngineException("BUILD_UNSUPPORTED", "Clearing suggested player metadata is not supported.");
            }

            current.Info.RecommendedPlayers = GetValueString(value, "suggested_players");
            changed = true;
        }

        replacements["war3map.w3i"] = SerializeInfo(current.Info);
        return changed;
    }

    private static bool ApplyRegionChanges(JsonObject source, JsonObject staged, string sourcePath, Dictionary<string, byte[]> replacements)
    {
        var sourceRegions = Regions(source);
        var stagedRegions = Regions(staged);
        if (sourceRegions.Count != stagedRegions.Count || sourceRegions.Select(Name).SequenceEqual(stagedRegions.Select(Name), StringComparer.Ordinal) is false)
        {
            return sourceRegions.Count != stagedRegions.Count || !sourceRegions.Select(Name).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(stagedRegions.Select(Name).OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal)
                ? throw new EngineException("BUILD_UNSUPPORTED", "Creating, deleting, or renaming regions is staged-only in this release.")
                : false;
        }

        var changed = false;
        var current = ReadMap(sourcePath, MapFiles.Regions);
        if (current.Regions is null)
        {
            throw new EngineException("BUILD_UNSUPPORTED", "Map regions could not be parsed for serialization.");
        }

        foreach (var region in current.Regions.Regions)
        {
            var stagedRegion = stagedRegions.First(x => string.Equals(Name(x), region.Name, StringComparison.Ordinal));
            foreach (var (field, assign) in new (string Field, Action<float> Assign)[]
            {
                ("min_x", value => region.Left = value),
                ("min_y", value => region.Bottom = value),
                ("max_x", value => region.Right = value),
                ("max_y", value => region.Top = value)
            })
            {
                var originalValue = Coordinate(sourceRegions.First(x => string.Equals(Name(x), region.Name, StringComparison.Ordinal)), field);
                var stagedValue = Coordinate(stagedRegion, field);
                if (!originalValue.Equals(stagedValue))
                {
                    assign(Convert.ToSingle(stagedValue, CultureInfo.InvariantCulture));
                    changed = true;
                }
            }
        }

        if (changed)
        {
            replacements["war3map.w3r"] = SerializeRegions(current.Regions);
        }

        return changed;
    }

    private static void EnsureOnlySupportedChanges(JsonObject source, JsonObject staged, bool metadataChanged, bool regionsChanged)
    {
        var sourceClone = source.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Could not clone source canonical map.");
        var stagedClone = staged.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Could not clone staged canonical map.");
        _ = metadataChanged;
        _ = regionsChanged;
        sourceClone.Remove("source");
        stagedClone.Remove("source");
        sourceClone.Remove("archive_members");
        stagedClone.Remove("archive_members");
        sourceClone.Remove("capabilities");
        stagedClone.Remove("capabilities");
        sourceClone.Remove("component_status");
        stagedClone.Remove("component_status");
        sourceClone.Remove("opaque_members");
        stagedClone.Remove("opaque_members");
        sourceClone.Remove("cameras");
        stagedClone.Remove("cameras");
        sourceClone.Remove("parse_warnings");
        stagedClone.Remove("parse_warnings");
        sourceClone.Remove("metadata");
        stagedClone.Remove("metadata");
        sourceClone.Remove("regions");
        stagedClone.Remove("regions");
        if (!JsonUtilities.Equal(sourceClone, stagedClone))
        {
            throw new EngineException("BUILD_UNSUPPORTED", "The staged transaction changes a component without a proven binary serializer.");
        }
    }

    private static void EnsurePublishedValues(JsonObject staged, JsonObject reopened)
    {
        var stagedMetadata = Metadata(staged);
        var reopenedMetadata = Metadata(reopened);
        foreach (var field in new[] { "title", "suggested_players" })
        {
            if (!JsonUtilities.Equal(stagedMetadata[field], reopenedMetadata[field]))
            {
                throw new EngineException("BUILD_REOPEN_MISMATCH", $"Reopened build metadata field '{field}' does not match the staged model.");
            }
        }

        var stagedRegions = Regions(staged);
        var reopenedRegions = Regions(reopened);
        if (!JsonUtilities.Equal(ToJsonArray(stagedRegions), ToJsonArray(reopenedRegions)))
        {
            throw new EngineException("BUILD_REOPEN_MISMATCH", "Reopened build regions do not match the staged model.");
        }
    }

    private static Map ReadMap(string path, MapFiles files)
    {
        using var archive = MpqArchive.Open(path, loadListFile: true);
        return Map.Open(archive, files);
    }

    private static byte[] SerializeInfo(War3Net.Build.Info.MapInfo info)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(info);
        }

        return stream.ToArray();
    }

    private static byte[] SerializeRegions(War3Net.Build.Environment.MapRegions regions)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(regions);
        }

        return stream.ToArray();
    }

    private static JsonObject Metadata(JsonObject root) => root["metadata"] as JsonArray is { } metadata
        ? metadata.OfType<JsonObject>().ToDictionary(x => x["field"]!.GetValue<string>(), x => x["value"], StringComparer.Ordinal).ToJsonObject()
        : throw new EngineException("INVALID_JSON", "Canonical map has no metadata array.");

    private static List<JsonObject> Regions(JsonObject root) => root["regions"] is JsonArray array
        ? array.OfType<JsonObject>().ToList()
        : throw new EngineException("INVALID_JSON", "Canonical map has no regions array.");

    private static string Name(JsonObject region) => region["name"]?.GetValue<string>() ?? string.Empty;

    private static double Coordinate(JsonObject region, string field)
    {
        var node = region[field] ?? throw new EngineException("INVALID_JSON", $"Region is missing {field}.");
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue;
            }

            if (value.TryGetValue<float>(out var floatValue))
            {
                return floatValue;
            }

            if (value.TryGetValue<int>(out var integerValue))
            {
                return integerValue;
            }
        }

        throw new EngineException("INVALID_JSON", $"Region field '{field}' must be numeric.");
    }

    private static JsonArray ToJsonArray(IEnumerable<JsonObject> values) => new(values.Select(x => (JsonNode)x.DeepClone()).ToArray());

    private static string GetValueString(JsonNode? node, string field) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.TryGetValue<int>(out var integer) => integer.ToString(CultureInfo.InvariantCulture),
        _ => throw new EngineException("INVALID_ARGUMENT", $"Metadata field '{field}' must be a string or integer.")
    };
}

internal static class JsonObjectDictionaryExtensions
{
    public static JsonObject ToJsonObject(this IReadOnlyDictionary<string, JsonNode?> values)
    {
        var result = new JsonObject();
        foreach (var pair in values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            result[pair.Key] = pair.Value?.DeepClone();
        }

        return result;
    }
}
