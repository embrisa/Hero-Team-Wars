using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core.Build;

public sealed record BuildPlan(IReadOnlySet<string> ReplacementMembers, JsonArray SemanticChanges)
{
    public static BuildPlan Create(JsonObject source, JsonObject staged)
    {
        var metadata = Metadata(source);
        var stagedMetadata = Metadata(staged);
        var replacements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in metadata.Keys.Union(stagedMetadata.Keys, StringComparer.Ordinal))
        {
            metadata.TryGetValue(field, out var before);
            stagedMetadata.TryGetValue(field, out var after);
            if (JsonUtilities.Equal(before, after)) continue;
            if (field is not ("title" or "suggested_players"))
            {
                throw new EngineException("BUILD_UNSUPPORTED", $"Metadata field '{field}' has no proven Phase 3 serializer.");
            }

            replacements.Add("war3map.w3i");
        }

        var regions = Regions(source);
        var stagedRegions = Regions(staged);
        if (regions.Count != stagedRegions.Count || !regions.Select(Name).SequenceEqual(stagedRegions.Select(Name), StringComparer.Ordinal))
        {
            throw new EngineException("BUILD_UNSUPPORTED", "Creating, deleting, reordering, or renaming regions is not supported by the Phase 3 serializer.");
        }

        for (var index = 0; index < regions.Count; index++)
        {
            var before = regions[index];
            var after = stagedRegions[index];
            foreach (var field in before.Select(x => x.Key).Union(after.Select(x => x.Key), StringComparer.Ordinal))
            {
                before.TryGetPropertyValue(field, out var left);
                after.TryGetPropertyValue(field, out var right);
                if (JsonUtilities.Equal(left, right)) continue;
                if (field is not ("min_x" or "min_y" or "max_x" or "max_y"))
                {
                    throw new EngineException("BUILD_UNSUPPORTED", $"Region field '{field}' has no proven Phase 3 serializer.");
                }

                replacements.Add("war3map.w3r");
            }
        }

        var sourceClone = source.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Could not clone source canonical map.");
        var stagedClone = staged.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Could not clone staged canonical map.");
        foreach (var property in new[] { "source", "metadata", "regions", "archive_members", "capabilities", "component_status", "opaque_members", "parse_warnings" })
        {
            sourceClone.Remove(property);
            stagedClone.Remove(property);
        }

        if (!JsonUtilities.Equal(sourceClone, stagedClone))
        {
            throw new EngineException("BUILD_UNSUPPORTED", "The staged transaction changes a component without a proven binary serializer.");
        }

        return new BuildPlan(replacements, SemanticDiff.CompareCanonical(source, staged, "build"));
    }

    private static Dictionary<string, JsonNode?> Metadata(JsonObject root)
        => root["metadata"] is JsonArray values
            ? values.OfType<JsonObject>().Where(x => x["field"]?.GetValue<string>() is not null).ToDictionary(x => x["field"]!.GetValue<string>(), x => x["value"], StringComparer.Ordinal)
            : throw new EngineException("INVALID_JSON", "Canonical map has no metadata array.");

    private static List<JsonObject> Regions(JsonObject root)
        => root["regions"] is JsonArray values ? values.OfType<JsonObject>().ToList() : new List<JsonObject>();

    private static string Name(JsonObject region) => region["name"]?.GetValue<string>() ?? string.Empty;
}
