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
    public void V8HeroObjectsRoundTripParentsSoldUnitsAndCorrectInstantStockFields()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        var guardian = Operation("create_object_definition", new JsonObject { ["id"] = "war3map.w3u:new:Hpal:H001", ["category"] = "unit", ["rawcode"] = "H001" }, null, HeroDefinition("Hpal", "H001", "HTW Guardian", "A durable frontline hero for the Hero Team Wars MVP.", 25, 16, 14, 700));
        var striker = Operation("create_object_definition", new JsonObject { ["id"] = "war3map.w3u:new:Hmkg:H002", ["category"] = "unit", ["rawcode"] = "H002" }, null, HeroDefinition("Hmkg", "H002", "HTW Striker", "A quick melee damage hero for the Hero Team Wars MVP.", 18, 25, 12, 560));
        var controller = Operation("create_object_definition", new JsonObject { ["id"] = "war3map.w3u:new:Hamg:H003", ["category"] = "unit", ["rawcode"] = "H003" }, null, HeroDefinition("Hamg", "H003", "HTW Controller", "A high-intelligence control hero for the Hero Team Wars MVP.", 14, 16, 25, 480));
        var support = Operation("create_object_definition", new JsonObject { ["id"] = "war3map.w3u:new:Hblm:H004", ["category"] = "unit", ["rawcode"] = "H004" }, null, HeroDefinition("Hblm", "H004", "HTW Support", "A flexible support hero for the Hero Team Wars MVP.", 16, 18, 23, 520));
        var altar = Operation("create_object_definition", new JsonObject { ["id"] = "war3map.w3u:new:ntav:n0AL", ["category"] = "unit", ["rawcode"] = "n0AL" }, null, new JsonObject
        {
            ["object_kind"] = "custom",
            ["category"] = "unit",
            ["base_rawcode"] = "ntav",
            ["custom_rawcode"] = "n0AL",
            ["rawcode"] = "n0AL",
            ["display_name"] = "HTW Hero Altar",
            ["unknown_ids"] = new JsonArray(),
            ["modifications"] = new JsonArray(
                new JsonObject { ["id"] = "unam", ["type"] = "String", ["value"] = "HTW Hero Altar" },
                new JsonObject { ["id"] = "useu", ["type"] = "String", ["value"] = "H001,H002,H003,H004" })
        });
        var staged = OperationApplier.Apply(model, new JsonArray(guardian, striker, controller, support, altar))["canonical_map"]!;
        var directory = TempDirectory();
        try
        {
            var canonical = Path.Combine(directory, "canonical.json");
            var output = Path.Combine(directory, "v8-hero-objects.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(source, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            var encoded = System.Text.Encoding.ASCII.GetString(MapArchive.Read(output).Find("war3map.w3u")!.Bytes);
            Assert.DoesNotContain("hpal", encoded);
            Assert.Contains("Hpal", encoded);
            Assert.Contains("H001,H002,H003,H004", encoded);
            var definitions = MapInspector.Inspect(output)["object_data"]!.AsArray().OfType<JsonObject>().ToArray();
            Assert.Equal(5, definitions.Length);
            var expectedParents = new Dictionary<string, string> { ["H001"] = "Hpal", ["H002"] = "Hmkg", ["H003"] = "Hamg", ["H004"] = "Hblm" };
            foreach (var (rawcode, parent) in expectedParents)
            {
                var hero = definitions.Single(item => item["rawcode"]!.GetValue<string>() == rawcode);
                Assert.Equal(parent, hero["base_rawcode"]!.GetValue<string>());
                var modifications = hero["modifications"]!.AsArray().OfType<JsonObject>().ToDictionary(item => item["id"]!.GetValue<string>());
                Assert.DoesNotContain("uhst", modifications.Keys);
                Assert.Equal(0, modifications["usst"]["value"]!.GetValue<int>());
                Assert.Equal(1, modifications["usrg"]["value"]!.GetValue<int>());
            }
            var sold = definitions.Single(item => item["rawcode"]!.GetValue<string>() == "n0AL")["modifications"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == "useu");
            Assert.Equal("H001,H002,H003,H004", sold["value"]!.GetValue<string>());
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
        var rename = Operation("rename_region", new JsonObject { ["id"] = region["id"]!.DeepClone() }, region.DeepClone(), new JsonObject
        {
            ["name"] = "MCP_Test_Region_Renamed",
            ["reference_rewrite_plan"] = new JsonObject { ["mcp_owned"] = "not_applicable", ["editor_trigger"] = "not_applicable", ["custom_text"] = "not_applicable" }
        });
        var staged = OperationApplier.Apply(created, new JsonArray(rename))["canonical_map"]!;
        var directory = TempDirectory();
        try
        {
            var canonical = Path.Combine(directory, "canonical.json");
            var output = Path.Combine(directory, "region.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(source, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            var changed = result["archive_comparison"]!["content_changes"]!.AsArray().Select(item => item!["path"]!.GetValue<string>()).ToArray();
            Assert.Contains("war3map.w3r", changed);
            Assert.Contains("(attributes)", changed);
            Assert.Equal(2, changed.Length);
            Assert.Empty(result["archive_comparison"]!["unexpected_content_changes"]!.AsArray());
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

    [Fact]
    public void MapFlagsAndFixedStartPositionRoundTripThroughMapInfo()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        var mapFlags = model["metadata"]!.AsArray().OfType<JsonObject>().Single(item => item["field"]!.GetValue<string>() == "map_flags");
        var operation = Operation(
            "set_map_metadata",
            new JsonObject { ["field"] = "map_flags" },
            mapFlags["value"]!.DeepClone(),
            mapFlags["value"]!.GetValue<int>() & ~32);
        var staged = OperationApplier.Apply(model, new JsonArray(operation))["canonical_map"]!;

        Assert.Equal(40024, staged["metadata"]!.AsArray().OfType<JsonObject>().Single(item => item["field"]!.GetValue<string>() == "map_flags")["value"]!.GetValue<int>());
        Assert.True(staged["players"]!.AsArray()[0]!["fixed_start_position"]!.GetValue<bool>());

        var directory = TempDirectory();
        try
        {
            var canonical = Path.Combine(directory, "canonical.json");
            var output = Path.Combine(directory, "map-flags.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(source, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            var reopened = MapInspector.Inspect(output);
            Assert.Equal(40024, reopened["metadata"]!.AsArray().OfType<JsonObject>().Single(item => item["field"]!.GetValue<string>() == "map_flags")["value"]!.GetValue<int>());
            Assert.True(reopened["players"]!.AsArray()[0]!["fixed_start_position"]!.GetValue<bool>());
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

    private static JsonObject HeroDefinition(string baseRawcode, string customRawcode, string displayName, string tip, int strength, int agility, int intelligence, int hitPoints)
    {
        return new JsonObject
        {
            ["object_kind"] = "custom",
            ["category"] = "unit",
            ["base_rawcode"] = baseRawcode,
            ["custom_rawcode"] = customRawcode,
            ["rawcode"] = customRawcode,
            ["display_name"] = displayName,
            ["unknown_ids"] = new JsonArray(),
            ["modifications"] = new JsonArray(
                new JsonObject { ["id"] = "unam", ["type"] = "String", ["value"] = displayName },
                new JsonObject { ["id"] = "utip", ["type"] = "String", ["value"] = tip },
                new JsonObject { ["id"] = "ustr", ["type"] = "Int", ["value"] = strength },
                new JsonObject { ["id"] = "uagi", ["type"] = "Int", ["value"] = agility },
                new JsonObject { ["id"] = "uint", ["type"] = "Int", ["value"] = intelligence },
                new JsonObject { ["id"] = "uhpm", ["type"] = "Int", ["value"] = hitPoints },
                new JsonObject { ["id"] = "ugol", ["type"] = "Int", ["value"] = 0 },
                new JsonObject { ["id"] = "ulum", ["type"] = "Int", ["value"] = 0 },
                new JsonObject { ["id"] = "usst", ["type"] = "Int", ["value"] = 0 },
                new JsonObject { ["id"] = "usrg", ["type"] = "Int", ["value"] = 1 })
        };
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
