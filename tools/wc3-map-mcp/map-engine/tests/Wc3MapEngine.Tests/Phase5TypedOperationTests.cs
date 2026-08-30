using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests;

public sealed class Phase5TypedOperationTests
{
    [Fact]
    public void InspectionExposesCompleteTypedRegionRecords()
    {
        var model = MapInspector.Inspect(FindSourceMap());
        var regions = model["regions"]!.AsArray().OfType<JsonObject>().ToArray();

        Assert.Equal(10, regions.Length);
        Assert.Equal("typed_write_enabled", model["component_status"]!["regions"]!["capability"]!.GetValue<string>());
        Assert.All(regions, region =>
        {
            var creation = region["creation_number"]!.GetValue<int>();
            Assert.Equal($"region:{creation}", region["id"]!.GetValue<string>());
            Assert.Equal("war3net-6.0.3-mapregions-v5", region["codec_version"]!.GetValue<string>());
            Assert.Equal(4, region["references"]!.AsObject().Count);
            Assert.Equal("typed_write_enabled", region["capability"]!.GetValue<string>());
        });
    }

    [Fact]
    public void RegionUpdateValidatesEnvelopeAndPreservesIdentity()
    {
        var model = MapInspector.Inspect(FindSourceMap());
        var region = model["regions"]!.AsArray().OfType<JsonObject>().Single(item => item["name"]!.GetValue<string>() == "Arena_A");
        var updated = OperationApplier.Apply(model, new JsonArray(Operation(
            "update_region",
            new JsonObject { ["id"] = region["id"]!.DeepClone() },
            region.DeepClone(),
            new JsonObject { ["min_x"] = 1280, ["max_x"] = 2368, ["weather"] = "None", ["ambient_sound"] = "HTW_Ambient", ["color_argb"] = 305419896 })))!["canonical_map"]!.AsObject();
        var result = updated["regions"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == region["id"]!.GetValue<string>());
        Assert.Equal("Arena_A", result["name"]!.GetValue<string>());
        Assert.Equal("HTW_Ambient", result["ambient_sound"]!.GetValue<string>());
        Assert.Equal(305419896, result["color_argb"]!.GetValue<int>());

        var outOfBounds = Assert.Throws<EngineException>(() => OperationApplier.Apply(model, new JsonArray(Operation(
            "update_region", new JsonObject { ["id"] = region["id"]!.DeepClone() }, region.DeepClone(), new JsonObject { ["min_x"] = -9999 }))));
        Assert.Equal("COORDINATE_OUT_OF_BOUNDS", outOfBounds.Code);
    }

    [Fact]
    public void RegionReorderUsesTheCompleteExpectedNativeOrder()
    {
        var model = MapInspector.Inspect(FindSourceMap());
        var ids = model["regions"]!.AsArray().OfType<JsonObject>().Select(item => item["id"]!.GetValue<string>()).ToArray();
        var reversed = ids.Reverse().ToArray();
        var reordered = OperationApplier.Apply(model, new JsonArray(Operation(
            "reorder_regions", new JsonObject(), new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)! ).ToArray()), new JsonObject
            { ["region_ids"] = new JsonArray(reversed.Select(id => (JsonNode)JsonValue.Create(id)! ).ToArray()) })))!["canonical_map"]!.AsObject();

        Assert.Equal(reversed, reordered["regions"]!.AsArray().OfType<JsonObject>().Select(item => item["id"]!.GetValue<string>()));
        Assert.Equal("Arena_A", reordered["regions"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == "region:0")["name"]!.GetValue<string>());
    }

    [Fact]
    public void ProtectedRegionsRequireExplicitRenameAndUnreferencedRegionsCanBeDeleted()
    {
        var model = MapInspector.Inspect(FindSourceMap());
        var protectedRegion = model["regions"]!.AsArray().OfType<JsonObject>().Single(item => item["name"]!.GetValue<string>() == "Arena_A");
        var protectedDelete = Assert.Throws<EngineException>(() => OperationApplier.Apply(model, new JsonArray(Operation("delete_region", new JsonObject { ["id"] = protectedRegion["id"]!.DeepClone() }, protectedRegion.DeepClone(), null))));
        Assert.Equal("REGION_PROTECTED", protectedDelete.Code);

        var created = OperationApplier.Apply(model, new JsonArray(Operation("create_region", new JsonObject(), null, new JsonObject
        {
            ["name"] = "MCP_Delete_Me", ["min_x"] = 0, ["min_y"] = 0, ["max_x"] = 128, ["max_y"] = 128
        })))!["canonical_map"]!.AsObject();
        var region = created["regions"]!.AsArray().OfType<JsonObject>().Single(item => item["name"]!.GetValue<string>() == "MCP_Delete_Me");
        var deleted = OperationApplier.Apply(created, new JsonArray(Operation("delete_region", new JsonObject { ["creation_number"] = region["creation_number"]!.DeepClone() }, region.DeepClone(), null)))!["canonical_map"]!.AsObject();
        Assert.DoesNotContain(deleted["regions"]!.AsArray().OfType<JsonObject>(), item => item["name"]!.GetValue<string>() == "MCP_Delete_Me");
    }

    [Fact]
    public void RegionRenameRewritesOwnedReferencesAndRejectsStaleExpected()
    {
        var model = MapInspector.Inspect(FindSourceMap());
        var region = model["regions"]!.AsArray().OfType<JsonObject>().First(item => item["name"]!.GetValue<string>() == "Arena_A");
        model["gameplay_triggers"]!.AsArray().Add(new JsonObject { ["id"] = "trigger_a", ["name"] = "Trigger A", ["folder_path"] = "Phase5", ["events"] = new JsonArray(new JsonObject { ["type"] = "map_initialization" }), ["references"] = new JsonObject { ["region_name"] = "Arena_A" } });
        var expected = region.DeepClone() as JsonObject ?? throw new InvalidOperationException();
        expected["references"] = new JsonObject
        {
            ["mcp_owned"] = new JsonArray(new JsonObject { ["section"] = "gameplay_triggers", ["path"] = "$[0].references.region_name", ["kind"] = "name" }),
            ["editor_trigger"] = new JsonArray(),
            ["custom_text"] = new JsonArray(),
            ["derived_roles"] = new JsonArray()
        };
        var operation = Operation("rename_region", new JsonObject { ["id"] = region["id"]!.DeepClone() }, expected, new JsonObject
        {
            ["name"] = "Arena_A_Renamed",
            ["reference_rewrite_plan"] = new JsonObject { ["mcp_owned"] = "rewrite", ["editor_trigger"] = "not_applicable", ["custom_text"] = "not_applicable" }
        });
        var result = OperationApplier.Apply(model, new JsonArray(operation));
        var renamed = result["canonical_map"]!["regions"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == region["id"]!.GetValue<string>());
        Assert.Equal("Arena_A_Renamed", renamed["name"]!.GetValue<string>());
        Assert.Equal("Arena_A_Renamed", result["canonical_map"]!["gameplay_triggers"]![0]! ["references"]!["region_name"]!.GetValue<string>());
        Assert.Contains(result["diff"]!["reference_rewrites"]!.AsArray().OfType<JsonObject>(), rewrite => rewrite["path"]!.GetValue<string>().Contains("region_name", StringComparison.Ordinal));

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
