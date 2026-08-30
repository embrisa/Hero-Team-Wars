using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests.Build;

public sealed class Phase5BuildRoundTripTests
{
    [Fact]
    public void PlacementChangeReopensWithOnlyPlacementMemberPlanned()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        var placement = model["placed_objects"]!.AsArray().OfType<JsonObject>().First(item => item["member"]!.GetValue<string>() == "war3mapUnits.doo");
        var position = placement["position"]!.AsObject();
        var operation = Operation("move_object", new JsonObject { ["id"] = placement["id"]!.DeepClone() }, placement.DeepClone(), new JsonObject
        {
            ["position"] = new JsonObject
            {
                ["x"] = position["x"]!.GetValue<float>() + 1,
                ["y"] = position["y"]!.GetValue<float>() + 1,
                ["z"] = position["z"]!.GetValue<float>()
            }
        });
        var staged = OperationApplier.Apply(model, new JsonArray(operation))["canonical_map"]!;
        var directory = TempDirectory();
        try
        {
            var canonical = Path.Combine(directory, "canonical.json");
            var output = Path.Combine(directory, "placement.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(source, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            Assert.Contains("war3mapUnits.doo", result["archive_comparison"]!["content_changes"]!.AsArray().Select(item => item!["path"]!.GetValue<string>()));
            var reopenedPlacement = MapInspector.Inspect(output)["placed_objects"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == placement["id"]!.GetValue<string>());
            Assert.Equal(staged["placed_objects"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == placement["id"]!.GetValue<string>())["position"]!.ToJsonString(), reopenedPlacement["position"]!.ToJsonString());
        }
        finally { DeleteTemp(directory); }
    }

    [Fact]
    public void NewUnitObjectDefinitionIsSerializedIntoItsCategoryMember()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        var operation = Operation("create_object_definition", new JsonObject { ["id"] = "war3map.w3u:new:hfoo:Z001", ["category"] = "unit", ["rawcode"] = "Z001" }, null, new JsonObject
        {
            ["object_kind"] = "custom", ["category"] = "unit", ["base_rawcode"] = "hfoo", ["custom_rawcode"] = "Z001", ["rawcode"] = "Z001", ["unknown_ids"] = new JsonArray(), ["modifications"] = new JsonArray()
        });
        var staged = OperationApplier.Apply(model, new JsonArray(operation))["canonical_map"]!;
        var bytes = MapComponentCodec.SerializeObjectMember("war3map.w3u", staged["object_data"]!.AsArray());
        Assert.NotEmpty(MapComponentCodec.ToObjectDefinitions("war3map.w3u", bytes));
        var directory = TempDirectory();
        try
        {
            var canonical = Path.Combine(directory, "canonical.json");
            var output = Path.Combine(directory, "object.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(source, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            var member = MapArchive.Read(output).Find("war3map.w3u");
            Assert.NotNull(member);
            Assert.Contains(MapInspector.Inspect(output)["object_data"]!.AsArray().OfType<JsonObject>(), item => item["rawcode"]!.GetValue<string>() == "Z001");
        }
        finally { DeleteTemp(directory); }
    }

    [Fact]
    public void TypedObjectModificationRoundTripsItsValue()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        var operation = Operation("create_object_definition", new JsonObject { ["id"] = "war3map.w3u:new:hfoo:Z002", ["category"] = "unit", ["rawcode"] = "Z002" }, null, new JsonObject
        {
            ["object_kind"] = "custom", ["category"] = "unit", ["base_rawcode"] = "hfoo", ["custom_rawcode"] = "Z002", ["rawcode"] = "Z002", ["unknown_ids"] = new JsonArray(),
            ["modifications"] = new JsonArray(new JsonObject { ["id"] = "usca", ["type"] = "Int", ["value"] = 42 })
        });
        var staged = OperationApplier.Apply(model, new JsonArray(operation))["canonical_map"]!;
        var directory = TempDirectory();
        try
        {
            var canonical = Path.Combine(directory, "canonical.json");
            var output = Path.Combine(directory, "object-modification.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(source, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            var definition = MapInspector.Inspect(output)["object_data"]!.AsArray().OfType<JsonObject>().Single(item => item["rawcode"]!.GetValue<string>() == "Z002");
            var modification = definition["modifications"]!.AsArray().OfType<JsonObject>().Single();
            Assert.Equal("usca", modification["id"]!.GetValue<string>());
            Assert.Equal(42, modification["value"]!.GetValue<int>());
        }
        finally { DeleteTemp(directory); }
    }

    [Fact]
    public void RegionCreateAndRenameRoundTripThroughRegionMember()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        var reference = model["regions"]!.AsArray().OfType<JsonObject>().First();
        var create = Operation("create_region", new JsonObject(), null, new JsonObject
        {
            ["name"] = "MCP_Test_Region",
            ["min_x"] = reference["min_x"]!.DeepClone(),
            ["min_y"] = reference["min_y"]!.DeepClone(),
            ["max_x"] = reference["max_x"]!.DeepClone(),
            ["max_y"] = reference["max_y"]!.DeepClone()
        });
        var created = OperationApplier.Apply(model, new JsonArray(create))["canonical_map"]!;
        var region = created["regions"]!.AsArray().OfType<JsonObject>().Single(item => item["name"]!.GetValue<string>() == "MCP_Test_Region");
        var rename = Operation("rename_region", new JsonObject { ["id"] = region["id"]!.DeepClone() }, region.DeepClone(), new JsonObject { ["name"] = "MCP_Test_Region_Renamed" });
        var staged = OperationApplier.Apply(created, new JsonArray(rename))["canonical_map"]!;
        var directory = TempDirectory();
        try
        {
            var canonical = Path.Combine(directory, "canonical.json");
            var output = Path.Combine(directory, "region.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(source, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            Assert.Contains(MapInspector.Inspect(output)["regions"]!.AsArray().OfType<JsonObject>(), item => item["name"]!.GetValue<string>() == "MCP_Test_Region_Renamed");
        }
        finally { DeleteTemp(directory); }
    }

    [Fact]
    public void PlayerAndForceEditsRoundTripThroughMapInfo()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        var player = model["players"]!.AsArray().OfType<JsonObject>().First(item => item["id"]!.GetValue<int>() == 1);
        var force = model["forces"]!.AsArray().OfType<JsonObject>().First(item => item["index"]!.GetValue<int>() == 0);
        var operations = new JsonArray(
            Operation("set_player_slot", new JsonObject { ["id"] = 1 }, player.DeepClone(), new JsonObject { ["name"] = "Player One MCP" }),
            Operation("set_force", new JsonObject { ["index"] = 0 }, force.DeepClone(), new JsonObject { ["name"] = "Team One MCP" }));
        var staged = OperationApplier.Apply(model, operations)["canonical_map"]!;
        var directory = TempDirectory();
        try
        {
            var canonical = Path.Combine(directory, "canonical.json");
            var output = Path.Combine(directory, "info.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(source, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            var reopened = MapInspector.Inspect(output);
            Assert.Equal("Player One MCP", reopened["players"]!.AsArray()[0]!["name"]!.GetValue<string>());
            Assert.Equal("Team One MCP", reopened["forces"]!.AsArray()[0]!["name"]!.GetValue<string>());
        }
        finally { DeleteTemp(directory); }
    }

    private static JsonObject Operation(string type, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var operation = new JsonObject { ["operation_id"] = Guid.NewGuid().ToString(), ["type"] = type, ["target"] = target, ["rationale"] = "Phase 5 binary round-trip test" };
        if (expected is not null) operation["expected"] = expected;
        if (value is not null) operation["value"] = value;
        return operation;
    }

    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-phase5", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemp(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
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
