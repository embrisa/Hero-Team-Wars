using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Wc3MapEngine.Core.Build;
using Wc3MapEngine.Core.Scripts;
using War3Net.Build;
using War3Net.Build.Extensions;
using War3Net.IO.Mpq;

namespace Wc3MapEngine.Core;

/// <summary>
/// Compatibility facade for the Phase 3 build pipeline. It keeps the old
/// three-argument entry point while making profile and round-trip evidence
/// explicit in the result.
/// </summary>
public static class MapBuilder
{
    public const string WriterVersion = "War3Net.IO.Mpq-6.0.3";

    public static JsonObject Build(string sourcePath, string canonicalPath, string outputPath, string profile = "debug", JsonObject? validationContext = null)
    {
        if (profile is not ("debug" or "release" or "noop" or "phase0-noop"))
        {
            throw new EngineException("INVALID_ARGUMENT", $"Unknown build profile '{profile}'.");
        }

        var staged = JsonUtilities.Read(canonicalPath) as JsonObject
            ?? throw new EngineException("INVALID_JSON", "Staged canonical map root must be an object.");
        var source = MapInspector.Inspect(sourcePath);
        var validation = Validation.ValidationPipeline.ValidateCanonical(canonicalPath, sourcePath, validationContext);
        if (validation["buildable"]?.GetValue<bool>() != true)
        {
            throw new EngineException("VALIDATION_FAILED", "The staged canonical map failed build validation.");
        }

        var plan = BuildPlan.Create(source, staged);
        var replacements = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        ApplyMetadataChanges(source, staged, sourcePath, replacements);
        ApplyRegionChanges(source, staged, sourcePath, replacements);
        ApplyObjectDataChanges(source, staged, sourcePath, replacements);
        ApplyPlacementChanges(source, staged, sourcePath, replacements);
        ApplyScriptChanges(source, staged, sourcePath, replacements);

        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new EngineException("INVALID_ARGUMENT", "The build output must have a parent directory.");
        Directory.CreateDirectory(outputDirectory);
        var outputFile = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(sourcePath), Path.GetExtension(outputFile), StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineException("BUILD_FAILED", "The build output extension must match the source map extension.");
        }
        var temporaryPath = Path.Combine(outputDirectory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        if (File.Exists(outputFile))
        {
            throw new EngineException("OUTPUT_EXISTS", $"Refusing to overwrite an existing build: {outputPath}");
        }

        try
        {
            MapArchive.Rebuild(sourcePath, temporaryPath, replacements);
            var reopened = MapInspector.Inspect(temporaryPath);
            // Gameplay manifests and the composed-source record are project
            // ownership metadata; they are deliberately not serialized as
            // separate archive members. Reattach them before the semantic
            // round-trip comparison while the generated war3map.j remains
            // verified from the archive itself.
            MergeProjectOwnedGameplay(reopened, staged);
            var semanticDifferences = SemanticDiff.CompareCanonical(staged, reopened, "build-reopen");
            if (semanticDifferences.Count != 0)
            {
                throw new EngineException("BUILD_REOPEN_MISMATCH", "Reopened build semantics do not match the staged canonical model.");
            }

            var archiveComparison = ArchiveComparison.Compare(MapArchive.Read(sourcePath), MapArchive.Read(temporaryPath), plan.ReplacementMembers);
            if (archiveComparison["opaque_members_preserved"]?.GetValue<bool>() != true
                || archiveComparison["unexpected_content_changes"]?.AsArray().Count != 0)
            {
                throw new EngineException("BUILD_REOPEN_MISMATCH", "Reopened build archive membership or opaque content does not match the build plan.");
            }

            var hash = Hashing.HashFileAsync(temporaryPath).GetAwaiter().GetResult();
            File.Move(temporaryPath, outputFile);
            return new JsonObject
            {
                ["schema_version"] = "1.0",
                ["writer_version"] = WriterVersion,
                ["profile"] = profile,
                ["output_path"] = outputFile,
                ["size_bytes"] = hash.Size,
                ["sha256"] = hash.Sha256,
                ["source_sha256"] = source["source"]!["sha256"]!.DeepClone(),
                ["runtime_status"] = "untested",
                ["reopened"] = true,
                ["semantic_changes"] = plan.SemanticChanges.Count,
                ["semantic_differences_after_reopen"] = semanticDifferences,
                ["opaque_members_preserved"] = true,
                ["archive_comparison"] = archiveComparison,
                ["validation"] = validation
            };
        }
        catch
        {
            TryDelete(temporaryPath);
            TryDelete(outputFile);
            throw;
        }
    }

    private static void MergeProjectOwnedGameplay(JsonObject reopened, JsonObject staged)
    {
        foreach (var field in new[] { "trigger_mode", "gameplay_source", "gameplay_modules", "gameplay_triggers", "gameplay_variables" })
        {
            if (staged[field] is JsonNode value) reopened[field] = value.DeepClone();
            else reopened.Remove(field);
        }
    }

    private static void ApplyMetadataChanges(JsonObject source, JsonObject staged, string sourcePath, Dictionary<string, byte[]> replacements)
    {
        var sourceMetadata = Metadata(source);
        var stagedMetadata = Metadata(staged);
        var titleChanged = !JsonUtilities.Equal(sourceMetadata["title"], stagedMetadata["title"]);
        var suggestedChanged = !JsonUtilities.Equal(sourceMetadata["suggested_players"], stagedMetadata["suggested_players"]);
        var playersChanged = !JsonUtilities.Equal(source["players"], staged["players"]);
        var forcesChanged = !JsonUtilities.Equal(source["forces"], staged["forces"]);
        if (!titleChanged && !suggestedChanged && !playersChanged && !forcesChanged) return;

        var current = ReadMap(sourcePath, MapFiles.Info);
        if (current.Info is null) throw new EngineException("BUILD_UNSUPPORTED", "Map metadata could not be parsed for serialization.");
        if (titleChanged) current.Info.MapName = GetValueString(stagedMetadata["title"], "title");
        if (suggestedChanged) current.Info.RecommendedPlayers = GetValueString(stagedMetadata["suggested_players"], "suggested_players");
        if (playersChanged || forcesChanged)
        {
            MapComponentCodec.BuildInfo(
                current.Info,
                staged["players"] as JsonArray ?? throw new EngineException("BUILD_UNSUPPORTED", "Staged player slots are missing."),
                staged["forces"] as JsonArray ?? throw new EngineException("BUILD_UNSUPPORTED", "Staged forces are missing."));
        }
        replacements["war3map.w3i"] = SerializeInfo(current.Info);
    }

    private static void ApplyRegionChanges(JsonObject source, JsonObject staged, string sourcePath, Dictionary<string, byte[]> replacements)
    {
        if (JsonUtilities.Equal(source["regions"], staged["regions"])) return;

        var current = ReadMap(sourcePath, MapFiles.Regions);
        if (current.Regions is null) throw new EngineException("BUILD_UNSUPPORTED", "Map regions could not be parsed for serialization.");
        replacements["war3map.w3r"] = MapComponentCodec.SerializeRegions(
            MapComponentCodec.BuildRegions(current.Regions, staged["regions"] as JsonArray ?? throw new EngineException("BUILD_UNSUPPORTED", "Staged regions are missing.")));
    }

    private static void ApplyObjectDataChanges(JsonObject source, JsonObject staged, string sourcePath, Dictionary<string, byte[]> replacements)
    {
        if (JsonUtilities.Equal(source["object_data"], staged["object_data"])) return;
        var definitions = staged["object_data"] as JsonArray ?? throw new EngineException("BUILD_UNSUPPORTED", "Staged object definitions are missing.");
        var members = definitions.OfType<JsonObject>().Select(item => item["archive_path"]?.GetValue<string>() ?? (item["category"]?.GetValue<string>() is { } category ? MapComponentCodec.ObjectMemberForCategory(category) : null))
            .Where(path => path is not null).Select(path => path!)
            .Concat((source["object_data_members"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(item => item["archive_path"]?.GetValue<string>()).Where(path => path is not null).Select(path => path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var memberName in members)
        {
            var memberDefinitions = ObjectDefinitions(staged, memberName);
            var sourceDefinitions = ObjectDefinitions(source, memberName);
            if (JsonUtilities.Equal(sourceDefinitions, memberDefinitions)) continue;
            var member = MapArchive.Read(sourcePath).Find(memberName);
            replacements[memberName] = member is null
                ? MapComponentCodec.SerializeObjectMember(memberName, memberDefinitions)
                : MapComponentCodec.SerializeObjectMember(memberName, member.Bytes, memberDefinitions);
        }
    }

    private static void ApplyPlacementChanges(JsonObject source, JsonObject staged, string sourcePath, Dictionary<string, byte[]> replacements)
    {
        if (JsonUtilities.Equal(source["placed_objects"], staged["placed_objects"])) return;
        var placements = staged["placed_objects"] as JsonArray ?? throw new EngineException("BUILD_UNSUPPORTED", "Staged placements are missing.");
        var archive = MapArchive.Read(sourcePath);
        var unitMember = archive.Find("war3mapUnits.doo");
        var doodadMember = archive.Find("war3map.doo");
        if (!JsonUtilities.Equal(Placements(source, "war3mapUnits.doo"), Placements(staged, "war3mapUnits.doo")))
        {
            var units = unitMember is null
                ? new War3Net.Build.Widget.MapUnits(War3Net.Build.Widget.MapWidgetsFormatVersion.v8, War3Net.Build.Widget.MapWidgetsSubVersion.v9, true)
                : ReadMap(sourcePath, MapFiles.Units).Units;
            replacements["war3mapUnits.doo"] = MapComponentCodec.SerializeUnits(MapComponentCodec.BuildUnits(units!, placements));
        }
        if (!JsonUtilities.Equal(Placements(source, "war3map.doo"), Placements(staged, "war3map.doo")))
        {
            var doodads = doodadMember is null
                ? new War3Net.Build.Widget.MapDoodads(War3Net.Build.Widget.MapWidgetsFormatVersion.v8, War3Net.Build.Widget.MapWidgetsSubVersion.v9, true)
                : ReadMap(sourcePath, MapFiles.Doodads).Doodads;
            replacements["war3map.doo"] = MapComponentCodec.SerializeDoodads(MapComponentCodec.BuildDoodads(doodads!, placements));
        }
    }

    private static void ApplyScriptChanges(JsonObject source, JsonObject staged, string sourcePath, Dictionary<string, byte[]> replacements)
    {
        _ = source;
        foreach (var script in Scripts(staged))
        {
            if (script["source"] is not JsonValue sourceValue || !sourceValue.TryGetValue<string>(out var sourceText))
            {
                continue;
            }

            var archivePath = script["archive_path"]?.GetValue<string>()
                ?? throw new EngineException("INVALID_JSON", "Staged script entry is missing archive_path.");
            var member = MapArchive.Read(sourcePath).Find(archivePath)
                ?? throw new EngineException("BUILD_FAILED", $"The staged script member '{archivePath}' is not present in the source archive.");
            _ = member;
            replacements[archivePath] = Encoding.UTF8.GetBytes(sourceText);
        }
    }

    private static Map ReadMap(string path, MapFiles files)
    {
        using var archive = MpqArchive.Open(path, loadListFile: true);
        return Map.Open(archive, files);
    }

    private static byte[] SerializeInfo(War3Net.Build.Info.MapInfo info) => MapComponentCodec.SerializeInfo(info);

    private static Dictionary<string, JsonNode?> Metadata(JsonObject root)
        => root["metadata"] is JsonArray values
            ? values.OfType<JsonObject>().Where(x => x["field"]?.GetValue<string>() is not null).ToDictionary(x => x["field"]!.GetValue<string>(), x => x["value"], StringComparer.Ordinal)
            : throw new EngineException("INVALID_JSON", "Canonical map has no metadata array.");

    private static List<JsonObject> Regions(JsonObject root)
        => root["regions"] is JsonArray values ? values.OfType<JsonObject>().ToList() : new List<JsonObject>();

    private static List<JsonObject> Scripts(JsonObject root)
        => root["scripts"] is JsonArray values ? values.OfType<JsonObject>().ToList() : new List<JsonObject>();

    private static JsonArray ObjectDefinitions(JsonObject root, string member)
        => new((root["object_data"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(item => string.Equals(item["archive_path"]?.GetValue<string>() ?? (item["category"]?.GetValue<string>() is { } category ? MapComponentCodec.ObjectMemberForCategory(category) : null), member, StringComparison.OrdinalIgnoreCase))
            .Select(item => (JsonNode?)item.DeepClone()).ToArray());

    private static JsonArray Placements(JsonObject root, string member)
        => new((root["placed_objects"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(item => string.Equals(item["member"]?.GetValue<string>(), member, StringComparison.OrdinalIgnoreCase))
            .Select(item => (JsonNode?)item.DeepClone()).ToArray());

    private static string Name(JsonObject region) => region["name"]?.GetValue<string>() ?? string.Empty;

    private static double Coordinate(JsonObject region, string field)
    {
        var node = region[field] ?? throw new EngineException("INVALID_JSON", $"Region is missing {field}.");
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out var doubleValue)) return doubleValue;
            if (value.TryGetValue<float>(out var floatValue)) return floatValue;
            if (value.TryGetValue<int>(out var integerValue)) return integerValue;
        }

        throw new EngineException("INVALID_JSON", $"Region field '{field}' must be numeric.");
    }

    private static string GetValueString(JsonNode? node, string field) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) => text,
        JsonValue value when value.TryGetValue<int>(out var integer) => integer.ToString(CultureInfo.InvariantCulture),
        _ => throw new EngineException("INVALID_ARGUMENT", $"Metadata field '{field}' must be a non-empty string or integer.")
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // The caller retains the error that made the build fail.
        }
    }
}
