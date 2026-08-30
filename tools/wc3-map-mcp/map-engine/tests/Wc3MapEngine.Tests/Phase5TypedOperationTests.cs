using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests;

public sealed class Phase5TypedOperationTests
{
    [Fact]
    public void RegionRenameRewritesOwnedReferencesAndRejectsStaleExpected()
    {
        var model = MapInspector.Inspect(FindSourceMap());
        var region = model["regions"]!.AsArray().OfType<JsonObject>().First(item => item["name"]!.GetValue<string>() == "Arena_A");
        model["gameplay_triggers"]!.AsArray().Add(new JsonObject { ["id"] = "trigger_a", ["name"] = "Trigger A", ["references"] = new JsonObject { ["region_name"] = "Arena_A" } });
        var operation = Operation("rename_region", new JsonObject { ["id"] = region["id"]!.DeepClone() }, region.DeepClone(), new JsonObject { ["name"] = "Arena_A_Renamed" });
        var result = OperationApplier.Apply(model, new JsonArray(operation));
        var renamed = result["canonical_map"]!["regions"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == region["id"]!.GetValue<string>());
        Assert.Equal("Arena_A_Renamed", renamed["name"]!.GetValue<string>());
        Assert.Equal("Arena_A_Renamed", result["canonical_map"]!["gameplay_triggers"]![0]! ["references"]!["region_name"]!.GetValue<string>());

        var stale = Operation("rename_region", new JsonObject { ["id"] = region["id"]!.DeepClone() }, new JsonObject { ["name"] = "Arena_A" }, new JsonObject { ["name"] = "Nope" });
        var exception = Assert.Throws<EngineException>(() => OperationApplier.Apply(model, new JsonArray(stale)));
        Assert.Equal("PRECONDITION_FAILED", exception.Code);
    }

    [Fact]
    public void PlacementMoveAndObjectCreateUseStableNativeIdentities()
    {
        var model = MapInspector.Inspect(FindSourceMap());
        var placement = model["placed_objects"]!.AsArray().OfType<JsonObject>().First(item => item["member"]!.GetValue<string>() == "war3mapUnits.doo");
        var moved = OperationApplier.Apply(model, new JsonArray(Operation(
            "move_object",
            new JsonObject { ["id"] = placement["id"]!.DeepClone() },
            placement.DeepClone(),
            new JsonObject { ["position"] = new JsonObject { ["x"] = placement["position"]!["x"]!.GetValue<float>() + 1, ["y"] = placement["position"]!["y"]!.GetValue<float>() + 1, ["z"] = placement["position"]!["z"]!.GetValue<float>() } })));
        Assert.Equal(placement["id"]!.GetValue<string>(), moved["canonical_map"]!["placed_objects"]![0]!["id"]!.GetValue<string>());

        var objectValue = new JsonObject
        {
            ["category"] = "unit",
            ["object_kind"] = "custom",
            ["base_rawcode"] = "hfoo",
            ["custom_rawcode"] = "Z001",
            ["rawcode"] = "Z001",
            ["unknown_ids"] = new JsonArray(),
            ["modifications"] = new JsonArray()
        };
        var created = OperationApplier.Apply(model, new JsonArray(Operation(
            "create_object_definition",
            new JsonObject { ["id"] = "war3map.w3u:new:hfoo:Z001", ["category"] = "unit", ["rawcode"] = "Z001" },
            null,
            objectValue)));
        Assert.Equal("war3map.w3u:new:hfoo:Z001", created["canonical_map"]!["object_data"]![0]!["id"]!.GetValue<string>());
    }

    private static JsonObject Operation(string type, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var operation = new JsonObject
        {
            ["operation_id"] = Guid.NewGuid().ToString(),
            ["type"] = type,
            ["target"] = target,
            ["rationale"] = "Phase 5 typed operation test"
        };
        if (expected is not null) operation["expected"] = expected;
        if (value is not null) operation["value"] = value;
        return operation;
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
