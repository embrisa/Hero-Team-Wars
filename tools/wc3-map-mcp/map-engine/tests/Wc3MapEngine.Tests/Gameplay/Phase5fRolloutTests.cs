using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Wc3MapEngine.Core.Gameplay;
using War3Net.Build;
using War3Net.Build.Extensions;
using War3Net.Build.Widget;
using Xunit;

namespace Wc3MapEngine.Tests.Gameplay;

public sealed class Phase5fRolloutTests
{
    [Fact]
    public void Htw06RequiresFullSixTeamProfileEvenForNonSixTeamScenario()
    {
        var exception = Assert.Throws<EngineException>(() => ScenarioRunner.Run(new JsonObject
        {
            ["profile"] = HtwProfileModel.MvpProfile,
            ["chunk_id"] = "HTW-06",
            ["scenario_ids"] = new JsonArray("fresh_initialization")
        }));

        Assert.Equal("CAPABILITY_GATED", exception.Code);
    }

    [Fact]
    public void FullProfileRunsSixTeamScenarioTwiceAsStaticEvidence()
    {
        var report = ScenarioRunner.Run(new JsonObject
        {
            ["profile"] = HtwProfileModel.FullProfile,
            ["chunk_id"] = "HTW-06",
            ["scenario_ids"] = new JsonArray("six_team_route_offset")
        });

        Assert.Equal(2, report["repeat_count"]!.GetValue<int>());
        Assert.Equal(2, report["scenario_count"]!.GetValue<int>());
        Assert.Equal(2, report["passed_count"]!.GetValue<int>());
        Assert.False(report["runtime_verified"]!.GetValue<bool>());
    }

    [Fact]
    public void SpecialDoodadsSurviveTypedCodecRoundTrip()
    {
        var placement = new JsonObject
        {
            ["id"] = "special-doodad:APic:-128:256",
            ["member"] = "war3map.doo",
            ["kind"] = "special_doodad",
            ["rawcode"] = "APic",
            ["variation"] = 3,
            ["position"] = new JsonObject { ["x"] = -128, ["y"] = 256, ["z"] = 0 }
        };
        var source = new MapDoodads(MapWidgetsFormatVersion.v8, MapWidgetsSubVersion.v9, true);
        var built = MapComponentCodec.BuildDoodads(source, new JsonArray(placement));
        using var stream = new MemoryStream(MapComponentCodec.SerializeDoodads(built));
        using var reader = new BinaryReader(stream);
        var reopened = reader.ReadMapDoodads();

        var canonical = MapComponentCodec.ToDoodads(reopened).OfType<JsonObject>().Single();
        Assert.Single(reopened.SpecialDoodads);
        Assert.Equal("special-doodad:APic:-128:256", canonical["id"]!.GetValue<string>());
        Assert.Equal(3, canonical["variation"]!.GetValue<int>());
    }

    [Fact]
    public void RegionDeletionSeesWaygateCreationNumberReferences()
    {
        var model = MapInspector.Inspect(FindSourceMap());
        var region = model["regions"]!.AsArray().OfType<JsonObject>().Last().DeepClone()!.AsObject();
        region["id"] = "region:9999";
        region["name"] = "Waygate_Target";
        region["creation_number"] = 9999;
        model["regions"]!.AsArray().Add(region);
        var placementIndex = model["placed_objects"]!.AsArray().Count;
        model["placed_objects"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "unit:999999",
            ["member"] = "war3mapUnits.doo",
            ["kind"] = "unit",
            ["waygate_destination_region_id"] = region["creation_number"]!.DeepClone()
        });
        region["references"] = RegionSupport.EmptyReferences();
        region["references"]!["mcp_owned"]!.AsArray().Add(new JsonObject
        {
            ["section"] = "placed_objects",
            ["path"] = $"$[{placementIndex}].waygate_destination_region_id",
            ["kind"] = "creation_number"
        });

        var exception = Assert.Throws<EngineException>(() => OperationApplier.Apply(model, new JsonArray(new JsonObject
        {
            ["operation_id"] = Guid.NewGuid().ToString(),
            ["type"] = "delete_region",
            ["target"] = new JsonObject { ["id"] = region["id"]!.DeepClone() },
            ["expected"] = region.DeepClone(),
            ["rationale"] = "Verify waygate region reference protection."
        })));

        Assert.Equal("REFERENCE_IN_USE", exception.Code);
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

        throw new FileNotFoundException("The source map was not found.");
    }
}
