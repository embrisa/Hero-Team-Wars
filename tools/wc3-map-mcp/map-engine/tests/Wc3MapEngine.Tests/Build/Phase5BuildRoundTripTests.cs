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
    public void V8HeroObjectsFixtureProducesV2FiveObjectsAndExactInstantStockFields()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        var fixturePath = FindFixture(Path.Combine("tools", "wc3-map-mcp", "scripts", "mcp", "object-data", "v8-hero-objects.json"));
        var fixtureNode = JsonNode.Parse(File.ReadAllText(fixturePath))!.AsObject();
        var objects = fixtureNode["objects"]!.AsArray();
        var operations = new JsonArray();
        foreach (var obj in objects.OfType<JsonObject>())
        {
            var baseRawcode = obj["base_rawcode"]!.GetValue<string>();
            var customRawcode = obj["custom_rawcode"]!.GetValue<string>();
            operations.Add(Operation("create_object_definition", new JsonObject
            {
                ["id"] = $"war3map.w3u:new:{baseRawcode}:{customRawcode}",
                ["category"] = "unit",
                ["rawcode"] = customRawcode
            }, null, obj.DeepClone()));
        }
        var staged = OperationApplier.Apply(model, operations)["canonical_map"]!;
        var directory = TempDirectory();
        try
        {
            var canonical = Path.Combine(directory, "canonical.json");
            var output = Path.Combine(directory, "v8-hero-objects.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(source, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            var member = MapArchive.Read(output).Find("war3map.w3u");
            Assert.NotNull(member);
            var bytes = member!.Bytes;
            Assert.Equal(2, BitConverter.ToInt32(bytes, 0));
            var decodedDefinitions = MapComponentCodec.ToObjectDefinitions("war3map.w3u", bytes);
            Assert.Equal(bytes, MapComponentCodec.SerializeObjectMember("war3map.w3u", decodedDefinitions));
            var encoded = System.Text.Encoding.ASCII.GetString(bytes);
            Assert.DoesNotContain("hpal", encoded);
            Assert.Contains("Hpal", encoded);
            var definitions = decodedDefinitions.OfType<JsonObject>().ToArray();
            Assert.Equal(5, definitions.Length);
            var expectedHeroes = new Dictionary<string, (string Parent, string Name)>
            {
                ["H001"] = ("Hpal", "HTW Guardian"),
                ["H002"] = ("Hmkg", "HTW Striker"),
                ["H003"] = ("Hamg", "HTW Controller"),
                ["H004"] = ("Hblm", "HTW Support")
            };
            foreach (var (rawcode, expected) in expectedHeroes)
            {
                var hero = definitions.Single(item => item["rawcode"]!.GetValue<string>() == rawcode);
                Assert.Equal(expected.Parent, hero["base_rawcode"]!.GetValue<string>());
                var modifications = hero["modifications"]!.AsArray().OfType<JsonObject>().ToDictionary(item => item["id"]!.GetValue<string>());
                Assert.Equal(expected.Name, modifications["unam"]["value"]!.GetValue<string>());
                Assert.DoesNotContain("uhst", modifications.Keys);
                Assert.Equal(0, modifications["ugol"]["value"]!.GetValue<int>());
                Assert.Equal(0, modifications["ulum"]["value"]!.GetValue<int>());
                Assert.Equal(0, modifications["usst"]["value"]!.GetValue<int>());
                Assert.Equal(1, modifications["usrg"]["value"]!.GetValue<int>());
            }
            var altar = definitions.Single(item => item["rawcode"]!.GetValue<string>() == "n0AL");
            Assert.Equal("ntav", altar["base_rawcode"]!.GetValue<string>());
            var altarMods = altar["modifications"]!.AsArray().OfType<JsonObject>().ToDictionary(item => item["id"]!.GetValue<string>());
            Assert.Equal("HTW Hero Altar", altarMods["unam"]["value"]!.GetValue<string>());
            Assert.Equal("A shared altar where every player selects one hero.", altarMods["utip"]["value"]!.GetValue<string>());
            Assert.Equal("H001,H002,H003,H004", altarMods["useu"]["value"]!.GetValue<string>());
        }
        finally { DeleteTemp(directory); }
    }

    [Fact]
    public void CustomAltarWithSoldUnitsRoundTripsUseuString()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
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
        var staged = OperationApplier.Apply(model, new JsonArray(altar))["canonical_map"]!;
        var directory = TempDirectory();
        try
        {
            var canonical = Path.Combine(directory, "canonical.json");
            var output = Path.Combine(directory, "altar-useu.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(source, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            var encoded = System.Text.Encoding.ASCII.GetString(MapArchive.Read(output).Find("war3map.w3u")!.Bytes);
            Assert.Contains("H001,H002,H003,H004", encoded);
            var definition = MapInspector.Inspect(output)["object_data"]!.AsArray().OfType<JsonObject>().Single(item => item["rawcode"]!.GetValue<string>() == "n0AL");
            var sold = definition["modifications"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == "useu");
            Assert.Equal("H001,H002,H003,H004", sold["value"]!.GetValue<string>());
        }
        finally { DeleteTemp(directory); }
    }

    [Fact]
    public void ObjectFieldSemanticRegistryCoversAllV8HeroObjectModifications()
    {
        var fixturePath = FindFixture(Path.Combine("tools", "wc3-map-mcp", "scripts", "mcp", "object-data", "v8-hero-objects.json"));
        var fixtureNode = JsonNode.Parse(File.ReadAllText(fixturePath))!.AsObject();
        var objects = fixtureNode["objects"]!.AsArray();
        foreach (var obj in objects.OfType<JsonObject>())
        {
            var modifications = obj["modifications"]!.AsArray().OfType<JsonObject>();
            foreach (var mod in modifications)
            {
                var id = mod["id"]!.GetValue<string>();
                var type = mod["type"]!.GetValue<string>();
                Assert.True(ObjectFieldSemanticRegistry.TryGetFieldMetadata(id, out var meta), $"Field '{id}' should be registered in ObjectFieldSemanticRegistry.");
                Assert.NotNull(meta);
                Assert.Equal(type, meta.ExpectedType);
                Assert.Equal(FieldSemanticProvenance.StaticFixtureBacked, meta.Provenance);
            }
        }
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

    private static string FindFixture(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException($"The fixture was not found: {relativePath}");
    }
}
