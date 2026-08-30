using System.Globalization;
using System.Text.Json.Nodes;
using Wc3MapEngine.Core.Build;
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
            var semanticDifferences = SemanticDiff.CompareCanonical(staged, reopened, "build-reopen");
            if (semanticDifferences.Count != 0)
            {
                throw new EngineException("BUILD_REOPEN_MISMATCH", "Reopened build semantics do not match the staged canonical model.");
            }

            var archiveComparison = ArchiveComparison.Compare(MapArchive.Read(sourcePath), MapArchive.Read(temporaryPath), plan.ReplacementMembers);
            if (archiveComparison["membership_equal"]?.GetValue<bool>() != true
                || archiveComparison["opaque_members_preserved"]?.GetValue<bool>() != true
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

    private static void ApplyMetadataChanges(JsonObject source, JsonObject staged, string sourcePath, Dictionary<string, byte[]> replacements)
    {
        var sourceMetadata = Metadata(source);
        var stagedMetadata = Metadata(staged);
        var titleChanged = !JsonUtilities.Equal(sourceMetadata["title"], stagedMetadata["title"]);
        var suggestedChanged = !JsonUtilities.Equal(sourceMetadata["suggested_players"], stagedMetadata["suggested_players"]);
        if (!titleChanged && !suggestedChanged) return;

        var current = ReadMap(sourcePath, MapFiles.Info);
        if (current.Info is null) throw new EngineException("BUILD_UNSUPPORTED", "Map metadata could not be parsed for serialization.");
        if (titleChanged) current.Info.MapName = GetValueString(stagedMetadata["title"], "title");
        if (suggestedChanged) current.Info.RecommendedPlayers = GetValueString(stagedMetadata["suggested_players"], "suggested_players");
        replacements["war3map.w3i"] = SerializeInfo(current.Info);
    }

    private static void ApplyRegionChanges(JsonObject source, JsonObject staged, string sourcePath, Dictionary<string, byte[]> replacements)
    {
        var sourceRegions = Regions(source);
        var stagedRegions = Regions(staged);
        if (sourceRegions.Count != stagedRegions.Count || !sourceRegions.Select(Name).SequenceEqual(stagedRegions.Select(Name), StringComparer.Ordinal))
        {
            throw new EngineException("BUILD_UNSUPPORTED", "Creating, deleting, reordering, or renaming regions is not supported by the Phase 3 serializer.");
        }

        var current = ReadMap(sourcePath, MapFiles.Regions);
        if (current.Regions is null) throw new EngineException("BUILD_UNSUPPORTED", "Map regions could not be parsed for serialization.");
        var changed = false;
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
                var before = Coordinate(sourceRegions.First(x => string.Equals(Name(x), region.Name, StringComparison.Ordinal)), field);
                var after = Coordinate(stagedRegion, field);
                if (before.Equals(after)) continue;
                assign(Convert.ToSingle(after, CultureInfo.InvariantCulture));
                changed = true;
            }
        }

        if (changed) replacements["war3map.w3r"] = SerializeRegions(current.Regions);
    }

    private static Map ReadMap(string path, MapFiles files)
    {
        using var archive = MpqArchive.Open(path, loadListFile: true);
        return Map.Open(archive, files);
    }

    private static byte[] SerializeInfo(War3Net.Build.Info.MapInfo info)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) writer.Write(info);
        return stream.ToArray();
    }

    private static byte[] SerializeRegions(War3Net.Build.Environment.MapRegions regions)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) writer.Write(regions);
        return stream.ToArray();
    }

    private static Dictionary<string, JsonNode?> Metadata(JsonObject root)
        => root["metadata"] is JsonArray values
            ? values.OfType<JsonObject>().Where(x => x["field"]?.GetValue<string>() is not null).ToDictionary(x => x["field"]!.GetValue<string>(), x => x["value"], StringComparer.Ordinal)
            : throw new EngineException("INVALID_JSON", "Canonical map has no metadata array.");

    private static List<JsonObject> Regions(JsonObject root)
        => root["regions"] is JsonArray values ? values.OfType<JsonObject>().ToList() : new List<JsonObject>();

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
