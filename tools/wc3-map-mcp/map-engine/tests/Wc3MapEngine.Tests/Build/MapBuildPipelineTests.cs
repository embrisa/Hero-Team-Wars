using System.Text;
using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests.Build;

public sealed class MapBuildPipelineTests
{
    [Fact]
    public async Task NoOpBuildReopensAndPreservesEveryOpaqueMember()
    {
        var source = FindSourceMap();
        var before = (await Hashing.HashFileAsync(source)).Sha256;
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-phase3", Guid.NewGuid().ToString("N"));
        var canonical = Path.Combine(directory, "canonical.json");
        var output = Path.Combine(directory, "noop.w3m");

        try
        {
            Directory.CreateDirectory(directory);
            JsonUtilities.WriteAtomic(canonical, MapInspector.Inspect(source));
            var result = MapBuilder.Build(source, canonical, output, "noop");

            Assert.True(result["reopened"]!.GetValue<bool>());
            Assert.True(result["opaque_members_preserved"]!.GetValue<bool>());
            Assert.Equal(0, result["semantic_changes"]!.GetValue<int>());
            Assert.True(result["archive_comparison"]!["membership_equal"]!.GetValue<bool>());
            Assert.Empty(result["archive_comparison"]!["content_changes"]!.AsArray());
            Assert.Equal(before, (await Hashing.HashFileAsync(source)).Sha256);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnsupportedCanonicalChangeFailsBeforePublishingOutput()
    {
        var source = FindSourceMap();
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-phase3", Guid.NewGuid().ToString("N"));
        var canonical = Path.Combine(directory, "canonical.json");
        var output = Path.Combine(directory, "unsupported.w3m");

        try
        {
            Directory.CreateDirectory(directory);
            var model = MapInspector.Inspect(source);
            var author = model["metadata"]!.AsArray().OfType<JsonObject>().Single(x => x["field"]!.GetValue<string>() == "author");
            author["value"] = "Unsupported build author";
            JsonUtilities.WriteAtomic(canonical, model);

            var exception = Assert.Throws<EngineException>(() => MapBuilder.Build(source, canonical, output, "debug"));

            Assert.Equal("VALIDATION_FAILED", exception.Code);
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RepeatedNoOpBuildsHaveEquivalentCanonicalSemantics()
    {
        var source = FindSourceMap();
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-phase3", Guid.NewGuid().ToString("N"));
        var canonical = Path.Combine(directory, "canonical.json");
        var first = Path.Combine(directory, "first.w3m");
        var second = Path.Combine(directory, "second.w3m");

        try
        {
            Directory.CreateDirectory(directory);
            JsonUtilities.WriteAtomic(canonical, MapInspector.Inspect(source));
            MapBuilder.Build(source, canonical, first, "noop");
            MapBuilder.Build(source, canonical, second, "noop");

            var differences = SemanticDiff.CompareCanonical(MapInspector.Inspect(first), MapInspector.Inspect(second));
            Assert.Empty(differences);
            Assert.Equal(
                MapArchive.Read(first).Members.Select(x => (x.Path, x.Sha256)),
                MapArchive.Read(second).Members.Select(x => (x.Path, x.Sha256)));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task McpOwnedJassBuildReplacesOnlyTheScriptMemberAndPreservesTheSource()
    {
        var source = FindSourceMap();
        var before = (await Hashing.HashFileAsync(source)).Sha256;
        var sourceArchive = MapArchive.Read(source);
        var sourceScript = sourceArchive.Find("war3map.j");
        Assert.NotNull(sourceScript);
        var originalScript = Encoding.UTF8.GetString(sourceScript!.Bytes);
        var updatedScript = originalScript + "\n// MCP script source round-trip test.\n";
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-script", Guid.NewGuid().ToString("N"));
        var canonical = Path.Combine(directory, "canonical.json");
        var output = Path.Combine(directory, "script.w3m");

        try
        {
            Directory.CreateDirectory(directory);
            var model = MapInspector.Inspect(source);
            var operation = new JsonArray(new JsonObject
            {
                ["operation_id"] = "c0a80101-0000-4000-8000-000000000010",
                ["type"] = "set_script_source",
                ["target"] = new JsonObject { ["archive_path"] = "war3map.j" },
                ["expected"] = sourceScript.Sha256,
                ["value"] = new JsonObject { ["language"] = "jass", ["source"] = updatedScript },
                ["rationale"] = "Verify MCP-owned gameplay source replacement."
            });
            var staged = OperationApplier.Apply(model, operation)["canonical_map"]!;
            JsonUtilities.WriteAtomic(canonical, staged);

            var result = MapBuilder.Build(source, canonical, output, "debug");

            Assert.True(result["reopened"]!.GetValue<bool>());
            Assert.True(result["opaque_members_preserved"]!.GetValue<bool>());
            Assert.Contains("war3map.j", result["archive_comparison"]!["content_changes"]!.AsArray().Select(x => x!["path"]!.GetValue<string>()));
            Assert.Equal(before, (await Hashing.HashFileAsync(source)).Sha256);
            var rebuiltScript = MapArchive.Read(output).Find("war3map.j");
            Assert.NotNull(rebuiltScript);
            Assert.Equal(Hashing.Sha256(Encoding.UTF8.GetBytes(updatedScript)), rebuiltScript!.Sha256);
            Assert.Equal(sourceArchive.Find("war3map.wtg")?.Sha256, MapArchive.Read(output).Find("war3map.wtg")?.Sha256);
            Assert.Equal(sourceArchive.Find("war3map.wct")?.Sha256, MapArchive.Read(output).Find("war3map.wct")?.Sha256);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
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
