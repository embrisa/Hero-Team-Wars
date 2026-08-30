using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests;

public sealed class MapInspectionTests
{
    [Fact]
    public async Task HeroTeamWarsInventoryIsStableAndSourceIsUnchanged()
    {
        var source = FindSourceMap();
        var before = await Hashing.HashFileAsync(source);
        var first = MapArchive.Read(source);
        var second = MapArchive.Read(source);

        Assert.Equal(17, first.Members.Count);
        Assert.Equal(first.Members.Select(x => (x.Path, x.Sha256)), second.Members.Select(x => (x.Path, x.Sha256)));
        var inspection = MapInspector.Inspect(source);
        Assert.Equal("Hero Team Wars - Two Arena MVP", inspection["metadata"]![0]!["value"]!.GetValue<string>());
        var after = await Hashing.HashFileAsync(source);
        Assert.Equal(before.Sha256, after.Sha256);
        Assert.Equal(before.Size, after.Size);
    }

    [Fact]
    public async Task ReadOnlyValidationReturnsTheExactInspectedMapHash()
    {
        var source = FindSourceMap();
        var expected = await Hashing.HashFileAsync(source);

        var report = MapValidator.ValidateMap(source);

        Assert.Equal(expected.Sha256, report["map_sha256"]!.GetValue<string>());
        Assert.Equal(expected.Sha256, report["source_sha256"]!.GetValue<string>());
        Assert.Equal(expected.Sha256, report["map_hash"]!.GetValue<string>());
        Assert.Equal(expected.Size, report["size_bytes"]!.GetValue<long>());
    }

    [Fact]
    public void CanonicalInspectionReportsSectionStatusForEverySection()
    {
        var inspection = MapInspector.Inspect(FindSourceMap());
        var statuses = inspection["component_status"] as System.Text.Json.Nodes.JsonObject;

        Assert.NotNull(statuses);
        foreach (var section in new[]
        {
            "source", "metadata", "players", "forces", "regions", "cameras",
            "archive_members", "capabilities", "triggers", "scripts", "variables",
            "object_data", "placed_objects", "terrain_summary", "imports",
            "opaque_members", "parse_warnings"
        })
        {
            var status = statuses![section] as System.Text.Json.Nodes.JsonObject;
            Assert.NotNull(status);
            Assert.False(string.IsNullOrWhiteSpace(status!["capability"]?.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(status["provenance"]?.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(status["reason"]?.GetValue<string>()));
        }

        Assert.IsType<System.Text.Json.Nodes.JsonArray>(inspection["cameras"]);
        Assert.IsType<System.Text.Json.Nodes.JsonArray>(inspection["parse_warnings"]);
        Assert.Empty((System.Text.Json.Nodes.JsonArray)inspection["parse_warnings"]!);
    }

    [Fact]
    public void TruncatedArchiveIsRejectedWithoutWritingBesideSource()
    {
        var source = FindSourceMap();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-tests", Guid.NewGuid().ToString("N"));
        var temporaryPath = Path.Combine(temporaryDirectory, "truncated.w3m");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllBytes(temporaryPath, File.ReadAllBytes(source).Take(32).ToArray());
            var exception = Assert.Throws<EngineException>(() => MapArchive.Read(temporaryPath));
            Assert.Contains(exception.Code, new[] { "PARSE_FAILED", "FILE_NOT_FOUND" });
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (IOException)
            {
                // War3Net can retain a handle when rejecting a truncated archive on Windows.
                // The unique temp directory is outside the workspace and will be reclaimed by the OS.
            }
        }
    }

    [Fact]
    public void NonMpqFileIsRejectedWithoutCrash()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-tests", Guid.NewGuid().ToString("N"));
        var temporaryPath = Path.Combine(temporaryDirectory, "not-an-mpq.w3m");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllText(temporaryPath, "not an MPQ archive");
            var exception = Assert.Throws<EngineException>(() => MapArchive.Read(temporaryPath));
            Assert.Equal("PARSE_FAILED", exception.Code);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (IOException)
            {
                // War3Net can retain a handle while rejecting a non-MPQ file on Windows.
                // The unique temp directory is outside the workspace and is safe to reclaim later.
            }
        }
    }

    [Fact]
    public void OneMemberParseFailureDoesNotEraseOtherMemberResults()
    {
        var source = FindSourceMap();
        var sourceArchive = MapArchive.Read(source);
        var region = sourceArchive.Find("war3map.w3r")!;
        var invalidInfo = new byte[] { 0x01, 0x02, 0x03 };
        var fixture = new MapArchiveSnapshot("synthetic-map.w3m", new[]
        {
            new ArchiveMemberData("war3map.w3i", 0, invalidInfo.Length, Hashing.Sha256(invalidInfo), true, invalidInfo, default),
            new ArchiveMemberData(region.Path, region.CompressedSize, region.Size, region.Sha256, true, region.Bytes, default)
        });

        var results = MapInspector.Probe(fixture);
        var infoResult = results.OfType<System.Text.Json.Nodes.JsonObject>().Single(x => x["path"]!.GetValue<string>() == "war3map.w3i");
        var regionResult = results.OfType<System.Text.Json.Nodes.JsonObject>().Single(x => x["path"]!.GetValue<string>() == "war3map.w3r");

        Assert.Equal("unsupported_blocking", infoResult["status"]!.GetValue<string>());
        Assert.NotNull(infoResult["error"]);
        Assert.Equal("typed_write_enabled", regionResult["status"]!.GetValue<string>());
    }

    [Fact]
    public void OpaqueMemberBytesMatchTheirReportedHash()
    {
        var archive = MapArchive.Read(FindSourceMap());
        var terrain = archive.Find("war3map.w3e");

        Assert.NotNull(terrain);
        Assert.Equal(terrain!.Sha256, Hashing.Sha256(terrain.Bytes));
    }

    [Fact]
    public void NoOpRebuildPreservesMemberContentHashes()
    {
        var source = FindSourceMap();
        var outputDirectory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(outputDirectory, "noop.w3m");
        try
        {
            Directory.CreateDirectory(outputDirectory);
            MapArchive.Rebuild(source, output, new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase));
            var sourceMembers = MapArchive.Read(source).Members.Select(x => (x.Path, x.Sha256)).ToArray();
            var outputMembers = MapArchive.Read(output).Members.Select(x => (x.Path, x.Sha256)).ToArray();

            Assert.Equal(sourceMembers, outputMembers);
        }
        finally
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static string FindSourceMap()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var candidate = Path.Combine(current.FullName, "map", "HeroTeamWars_M0_2Arena.w3m");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("The local Hero Team Wars source fixture was not found.");
    }
}
