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
        if (!RegionsSerializedEqual(regions, stagedRegions))
        {
            foreach (var region in regions.Concat(stagedRegions))
            {
                foreach (var field in region.Select(x => x.Key))
                {
                    if (field is not ("id" or "name" or "stored_name" or "min_x" or "min_y" or "max_x" or "max_y" or "creation_number" or "weather" or "ambient_sound" or "color_argb" or "references" or "codec_version" or "provenance" or "capability"))
                    {
                        throw new EngineException("BUILD_UNSUPPORTED", $"Region field '{field}' has no proven typed serializer.");
                    }
                }
            }
            replacements.Add("war3map.w3r");
        }

        if (!JsonUtilities.Equal(source["players"], staged["players"]) || !JsonUtilities.Equal(source["forces"], staged["forces"]))
        {
            ValidatePlayerFields(staged["players"] as JsonArray);
            ValidateForceFields(staged["forces"] as JsonArray);
            replacements.Add("war3map.w3i");
        }

        if (!JsonUtilities.Equal(source["object_data"], staged["object_data"]))
        {
            foreach (var member in ObjectMembers(source).Concat(ObjectMembers(staged)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!JsonUtilities.Equal(ObjectDefinitions(source, member), ObjectDefinitions(staged, member))) replacements.Add(member);
            }
        }

        if (!JsonUtilities.Equal(source["placed_objects"], staged["placed_objects"]))
        {
            foreach (var member in new[] { "war3mapUnits.doo", "war3map.doo" })
            {
                if (!JsonUtilities.Equal(Placements(source, member), Placements(staged, member))) replacements.Add(member);
            }
        }

        foreach (var scriptMember in ScriptReplacements(source, staged))
        {
            replacements.Add(scriptMember);
        }

        var sourceMembers = (source["archive_members"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(item => item["path"]?.GetValue<string>())
            .Where(path => path is not null)
            .Select(path => path!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (replacements.Any(member => !sourceMembers.Contains(member))) replacements.Add("(listfile)");

        var sourceClone = source.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Could not clone source canonical map.");
        var stagedClone = staged.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Could not clone staged canonical map.");
        foreach (var property in new[] { "source", "metadata", "regions", "players", "forces", "object_data", "object_data_members", "placed_objects", "scripts", "archive_members", "capabilities", "component_status", "opaque_members", "parse_warnings", "profile", "profile_spec", "profiles", "teams", "team_registry", "region_roles", "gameplay_source", "gameplay_triggers", "gameplay_variables", "gameplay_modules", "trigger_mode" })
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

    private static bool RegionsSerializedEqual(IReadOnlyList<JsonObject> left, IReadOnlyList<JsonObject> right)
        => left.Count == right.Count && left.Zip(right).All(pair => RegionSupport.SerializedEqual(pair.First, pair.Second));

    private static List<JsonObject> Scripts(JsonObject root)
        => root["scripts"] is JsonArray values ? values.OfType<JsonObject>().ToList() : new List<JsonObject>();

    private static string Name(JsonObject region) => region["name"]?.GetValue<string>() ?? string.Empty;

    private static IEnumerable<string> ObjectMembers(JsonObject root)
        => (root["object_data"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(item => item["archive_path"]?.GetValue<string>() ?? (item["category"]?.GetValue<string>() is { } category ? MapComponentCodec.ObjectMemberForCategory(category) : null))
            .Where(path => path is not null)
            .Select(path => path!)
            .Concat((root["object_data_members"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(item => item["archive_path"]?.GetValue<string>()).Where(path => path is not null).Select(path => path!));

    private static JsonArray ObjectDefinitions(JsonObject root, string member)
        => new((root["object_data"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(item => string.Equals(item["archive_path"]?.GetValue<string>() ?? (item["category"]?.GetValue<string>() is { } category ? MapComponentCodec.ObjectMemberForCategory(category) : null), member, StringComparison.OrdinalIgnoreCase))
            .Select(item => (JsonNode?)item.DeepClone()).ToArray());

    private static JsonArray Placements(JsonObject root, string member)
        => new((root["placed_objects"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(item => string.Equals(item["member"]?.GetValue<string>(), member, StringComparison.OrdinalIgnoreCase))
            .Select(item => (JsonNode?)item.DeepClone()).ToArray());

    private static void ValidatePlayerFields(JsonArray? players)
    {
        foreach (var player in players?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            foreach (var field in player.Select(item => item.Key))
            {
                if (field is not ("id" or "name" or "stored_name" or "controller" or "race" or "flags" or "start" or "ally_low_priority_mask" or "ally_high_priority_mask" or "enemy_low_priority_mask" or "enemy_high_priority_mask" or "observer" or "locked" or "slot_status" or "codec_version" or "provenance" or "capability"))
                {
                    throw new EngineException("BUILD_UNSUPPORTED", $"Player field '{field}' has no proven typed serializer.");
                }
            }
        }
    }

    private static void ValidateForceFields(JsonArray? forces)
    {
        foreach (var force in forces?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            foreach (var field in force.Select(item => item.Key))
            {
                if (field is not ("index" or "name" or "stored_name" or "flags" or "player_ids" or "player_mask" or "alliance" or "shared_vision" or "shared_unit_control" or "codec_version" or "provenance" or "capability"))
                {
                    throw new EngineException("BUILD_UNSUPPORTED", $"Force field '{field}' has no proven typed serializer.");
                }
            }
        }
    }
}
