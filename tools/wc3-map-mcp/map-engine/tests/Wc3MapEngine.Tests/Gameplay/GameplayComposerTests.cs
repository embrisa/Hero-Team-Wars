using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Wc3MapEngine.Core.Gameplay;
using Xunit;

namespace Wc3MapEngine.Tests.Gameplay;

public sealed class GameplayComposerTests
{
    [Fact]
    public void ComposerIsDeterministicAndStaticOnly()
    {
        var manifest = FindManifest();
        var first = GameplaySourceComposer.Compose(manifest);
        var second = GameplaySourceComposer.Compose(manifest);

        Assert.Equal(first["source_sha256"]!.GetValue<string>(), second["source_sha256"]!.GetValue<string>());
        Assert.Equal(first["manifest_sha256"]!.GetValue<string>(), second["manifest_sha256"]!.GetValue<string>());
        Assert.Equal(first["source"]!.GetValue<string>(), second["source"]!.GetValue<string>());
        Assert.Equal("static_only", first["static_validation"]!["evidence_level"]!.GetValue<string>());
        Assert.Equal(1, first["main_count"]!.GetValue<int>());
        Assert.Equal(25, first["module_order"]!.AsArray().Count);
    }

    [Fact]
    public void ComposerBindsStableRegionIdsToGeneratedRectHandles()
    {
        var composed = GameplaySourceComposer.Compose(FindManifest());
        var source = composed["source"]!.GetValue<string>();

        Assert.Contains("region HTW_Region_region_0", source);
        Assert.Contains("set HTW_Region_region_0 = CreateRegion()", source);
        Assert.Contains("RegionAddRect(HTW_Region_region_0, Rect(1152., -3072., 2496., -448.))", source);
        Assert.DoesNotContain("gg_rct_Arena_A", source);
        Assert.Equal("HTW_Region_region_0", composed["region_handles"]!["region:0"]!.GetValue<string>());
        Assert.Equal(10, composed["regions"]!.AsArray().Count);
    }

    [Fact]
    public void ScenarioHarnessUsesLockedSixTeamOffset()
    {
        var report = ScenarioRunner.Run(new JsonObject
        {
            ["profile"] = "full_6team",
            ["chunk_id"] = "HTW-06",
            ["scenario_ids"] = new JsonArray("six_team_route_offset", "six_team_elimination_recalculation", "six_team_no_carryover")
        });

        Assert.Equal(6, report["passed_count"]!.GetValue<int>());
        Assert.Equal(2, report["repeat_count"]!.GetValue<int>());
        var route = report["results"]!.AsArray().OfType<JsonObject>().First(item => item["scenario_id"]!.GetValue<string>() == "six_team_route_offset");
        Assert.Contains("offset=2", route["expected"]!.GetValue<string>());
        Assert.Equal("static_only", report["evidence_level"]!.GetValue<string>());
        Assert.False(report["runtime_verified"]!.GetValue<bool>());
    }

    [Fact]
    public void GameplayOperationRegeneratesEntryPointFromCanonicalSourceModel()
    {
        var composed = GameplaySourceComposer.Compose(FindManifest());
        var canonical = composed["canonical_model"]!.AsObject().DeepClone()!.AsObject();
        canonical["profile"] = "mvp_2arena";
        const string before = "function main takes nothing returns nothing\nendfunction\n";
        canonical["scripts"] = new JsonArray(new JsonObject
        {
            ["archive_path"] = "war3map.j",
            ["language"] = "Jass",
            ["source"] = before,
            ["source_sha256"] = Hashing.Sha256(System.Text.Encoding.UTF8.GetBytes(before)),
            ["sha256"] = Hashing.Sha256(System.Text.Encoding.UTF8.GetBytes(before)),
            ["size_bytes"] = System.Text.Encoding.UTF8.GetByteCount(before)
        });
        var phase = canonical["gameplay_variables"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == "phase");
        var operations = new JsonArray(new JsonObject
        {
            ["operation_id"] = "c0a80101-0000-4000-8000-000000000010",
            ["type"] = "update_variable",
            ["target"] = new JsonObject { ["id"] = "phase" },
            ["expected"] = phase.DeepClone(),
            ["value"] = new JsonObject { ["initial"] = 2 },
            ["rationale"] = "Exercise source-owned variable regeneration."
        });

        var applied = OperationApplier.Apply(canonical, operations)["canonical_map"]!.AsObject();

        Assert.Equal(2, applied["gameplay_variables"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == "phase")["initial"]!.GetValue<int>());
        Assert.Contains("set HTW_Phase = 2", applied["scripts"]![0]!["source"]!.GetValue<string>());
        Assert.Equal(applied["scripts"]![0]!["source_sha256"]!.GetValue<string>(), applied["gameplay_source"]!["source_sha256"]!.GetValue<string>());
    }

    [Fact]
    public void GameplayValidatorRejectsDuplicateModuleFunctions()
    {
        var root = new JsonObject
        {
            ["gameplay_modules"] = new JsonArray(
                new JsonObject { ["id"] = "one", ["source"] = "function HTW_MCP_Bootstrap takes nothing returns nothing\nendfunction\n" },
                new JsonObject { ["id"] = "two", ["source"] = "function HTW_MCP_Bootstrap takes nothing returns nothing\nendfunction\n" }),
            ["gameplay_variables"] = new JsonArray(),
            ["gameplay_triggers"] = new JsonArray()
        };

        var exception = Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateCollections(root, requireModuleSources: true));

        Assert.Equal("INVALID_ARGUMENT", exception.Code);
        Assert.Contains("Duplicate gameplay function identity", exception.Message);
    }

    [Fact]
    public void GameplayValidatorRejectsStaleModuleHashAndUnresolvedRegion()
    {
        var staleModule = new JsonObject
        {
            ["id"] = "module",
            ["source"] = "function HTW_MCP_Bootstrap takes nothing returns nothing\nendfunction\n",
            ["source_sha256"] = new string('A', 64)
        };
        var stale = Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateModule(staleModule, requireSource: true));
        Assert.Equal("SOURCE_CHANGED", stale.Code);

        var unresolved = new JsonObject
        {
            ["regions"] = new JsonArray(),
            ["gameplay_modules"] = new JsonArray(),
            ["gameplay_variables"] = new JsonArray(),
            ["gameplay_triggers"] = new JsonArray(new JsonObject
            {
                ["id"] = "trigger",
                ["name"] = "Trigger",
                ["folder_path"] = "MCP",
                ["events"] = new JsonArray(new JsonObject { ["type"] = "region_entry", ["region_name"] = "Missing" })
            })
        };

        var reference = Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateCollections(unresolved));
        Assert.Equal("INVALID_ARGUMENT", reference.Code);
        Assert.Contains("unknown region", reference.Message);
    }

    private static string FindManifest()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var candidate = Path.Combine(current.FullName, "scripts", "mcp", "manifest.json");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("The local gameplay source manifest was not found.");
    }
}
