using System.Text;
using System.Text.Json.Nodes;
using Wc3MapEngine.Core.Scripts;

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

        foreach (var scriptMember in ScriptReplacements(source, staged))
        {
            replacements.Add(scriptMember);
        }

        var sourceClone = source.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Could not clone source canonical map.");
        var stagedClone = staged.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Could not clone staged canonical map.");
        foreach (var property in new[] { "source", "metadata", "regions", "scripts", "archive_members", "capabilities", "component_status", "opaque_members", "parse_warnings" })
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

    private static IReadOnlyList<string> ScriptReplacements(JsonObject source, JsonObject staged)
    {
        var sourceScripts = Scripts(source);
        var stagedScripts = Scripts(staged);
        if (sourceScripts.Count != stagedScripts.Count)
        {
            throw new EngineException("BUILD_UNSUPPORTED", "Adding or removing script entry points is not supported; replace the existing war3map.j member.");
        }

        var replacements = new List<string>();
        for (var index = 0; index < sourceScripts.Count; index++)
        {
            var before = sourceScripts[index];
            var after = stagedScripts[index];
            if (!string.Equals(before["archive_path"]?.GetValue<string>(), after["archive_path"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
            {
                throw new EngineException("BUILD_UNSUPPORTED", "Script entry-point identity is immutable; only the existing war3map.j source may change.");
            }

            var archivePath = before["archive_path"]?.GetValue<string>() ?? string.Empty;
            if (!ScriptIdentityEqual(before, after))
            {
                throw new EngineException("BUILD_UNSUPPORTED", $"Script metadata for '{archivePath}' changed without a source replacement.");
            }

            var sourceText = after["source"]?.GetValue<string>();
            if (sourceText is null)
            {
                if (!string.Equals(before["source_sha256"]?.GetValue<string>(), after["source_sha256"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new EngineException("BUILD_UNSUPPORTED", $"Script hash for '{archivePath}' changed without carrying the staged source text.");
                }

                continue;
            }

            try
            {
                ScriptOwnership.ValidateMcpOwnedJass(archivePath, sourceText);
            }
            catch (InvalidDataException exception)
            {
                throw new EngineException("BUILD_UNSUPPORTED", exception.Message, false, exception);
            }

            var sourceHash = Hashing.Sha256(Encoding.UTF8.GetBytes(sourceText));
            if (!string.Equals(after["source_sha256"]?.GetValue<string>(), sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new EngineException("BUILD_UNSUPPORTED", $"Staged source hash for '{archivePath}' does not match its source text.");
            }

            replacements.Add(archivePath);
        }

        return replacements;
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

    private static Dictionary<string, JsonNode?> Metadata(JsonObject root)
        => root["metadata"] is JsonArray values
            ? values.OfType<JsonObject>().Where(x => x["field"]?.GetValue<string>() is not null).ToDictionary(x => x["field"]!.GetValue<string>(), x => x["value"], StringComparer.Ordinal)
            : throw new EngineException("INVALID_JSON", "Canonical map has no metadata array.");

    private static List<JsonObject> Regions(JsonObject root)
        => root["regions"] is JsonArray values ? values.OfType<JsonObject>().ToList() : new List<JsonObject>();

    private static List<JsonObject> Scripts(JsonObject root)
        => root["scripts"] is JsonArray values ? values.OfType<JsonObject>().ToList() : new List<JsonObject>();

    private static string Name(JsonObject region) => region["name"]?.GetValue<string>() ?? string.Empty;
}
