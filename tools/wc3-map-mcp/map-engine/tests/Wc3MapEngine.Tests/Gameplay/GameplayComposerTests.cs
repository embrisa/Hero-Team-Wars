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
    public void ScenarioHarnessUsesLockedSixTeamOffset()
    {
        var report = ScenarioRunner.Run(new JsonObject
        {
            ["profile"] = "full_6team",
            ["chunk_id"] = "HTW-06",
            ["scenario_ids"] = new JsonArray("six_team_route_offset", "six_team_elimination_recalculation", "six_team_no_carryover")
        });

        Assert.Equal(3, report["passed_count"]!.GetValue<int>());
        var route = report["results"]!.AsArray().OfType<JsonObject>().Single(item => item["scenario_id"]!.GetValue<string>() == "six_team_route_offset");
        Assert.Contains("offset=2", route["expected"]!.GetValue<string>());
        Assert.Equal("static_only", report["evidence_level"]!.GetValue<string>());
        Assert.False(report["runtime_verified"]!.GetValue<bool>());
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
