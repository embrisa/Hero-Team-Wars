using System.Text;
using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Wc3MapEngine.Core.Gameplay;
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

    [Fact]
    public void McpNativeGameplayBuildReopensWithSourceOwnedModel()
    {
        var source = FindSourceMap();
        var sourceArchive = MapArchive.Read(source);
        var composed = GameplaySourceComposer.Compose(Path.Combine(FindProjectRoot(), "tools", "wc3-map-mcp", "scripts", "mcp", "manifest.json"));
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-gameplay", Guid.NewGuid().ToString("N"));
        var canonical = Path.Combine(directory, "canonical.json");
        var output = Path.Combine(directory, "gameplay.w3m");

        try
        {
            Directory.CreateDirectory(directory);
            var model = MapInspector.Inspect(source);
            var sourceModel = composed["canonical_model"]!.AsObject();
            foreach (var field in new[] { "gameplay_modules", "gameplay_triggers", "gameplay_variables" }) model[field] = sourceModel[field]!.DeepClone();
            model["trigger_mode"] = "mcp_native_jass";
            model["gameplay_source"] = new JsonObject
            {
                ["schema_version"] = "1.0",
                ["composer_version"] = composed["composer_version"]!.DeepClone(),
                ["mode"] = composed["mode"]!.DeepClone(),
                ["profile"] = composed["profile"]!.DeepClone(),
                ["source_sha256"] = composed["source_sha256"]!.DeepClone(),
                ["source_manifest_sha256"] = composed["source_manifest_sha256"]!.DeepClone(),
                ["source_manifest"] = composed["source_manifest"]!.DeepClone(),
                ["static_validation"] = composed["static_validation"]!.DeepClone()
            };
            var script = model["scripts"]!.AsArray().OfType<JsonObject>().Single(item => item["archive_path"]!.GetValue<string>() == "war3map.j");
            script["source"] = composed["source"]!.DeepClone();
            script["source_sha256"] = composed["source_sha256"]!.DeepClone();
            script["sha256"] = composed["source_sha256"]!.DeepClone();
            script["size_bytes"] = composed["source_bytes"]!.DeepClone();
            JsonUtilities.WriteAtomic(canonical, model);

            var result = MapBuilder.Build(source, canonical, output, "debug");

            Assert.True(result["reopened"]!.GetValue<bool>());
            Assert.Empty(result["semantic_differences_after_reopen"]!.AsArray());
            Assert.Equal(sourceArchive.Find("war3map.wtg")?.Sha256, MapArchive.Read(output).Find("war3map.wtg")?.Sha256);
            Assert.Equal(composed["source_sha256"]!.GetValue<string>(), MapArchive.Read(output).Find("war3map.j")?.Sha256);
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

    private static string FindProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            if (File.Exists(Path.Combine(current.FullName, "tools", "wc3-map-mcp", "scripts", "mcp", "manifest.json"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("The local Hero Team Wars project root was not found.");
    }
}
