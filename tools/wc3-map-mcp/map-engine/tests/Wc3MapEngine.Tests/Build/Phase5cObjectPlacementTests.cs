using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests.Build;

public sealed class Phase5cObjectPlacementTests
{
    [Fact]
    public void AllObjectCategoriesInspectAndNoOpRoundTripWithoutSemanticDrift()
    {
        var source = FindSourceMap();
        var directory = TempDirectory();
        try
        {
            var fixture = Path.Combine(directory, "all-object-members.w3m");
            BuildAllObjectMemberFixture(source, fixture);
            var model = MapInspector.Inspect(fixture);
            var definitions = model["object_data"]!.AsArray().OfType<JsonObject>().ToArray();

            Assert.Equal(7, definitions.Length);
            Assert.Equal(7, model["object_data_members"]!.AsArray().Count);
            Assert.All(model["object_data_members"]!.AsArray().OfType<JsonObject>(), member =>
            {
                Assert.Equal("roundtrip_verified", member["capability"]!.GetValue<string>());
                Assert.NotEmpty(member["supported_operations"]!.AsArray());
            });
            Assert.Equal("mixed", model["component_status"]!["object_data"]!["capability"]!.GetValue<string>());

            var canonical = Path.Combine(directory, "all-object-members.json");
            var output = Path.Combine(directory, "all-object-members-noop.w3m");
            JsonUtilities.WriteAtomic(canonical, model);
            var result = MapBuilder.Build(fixture, canonical, output, "debug");

            Assert.True(result["reopened"]!.GetValue<bool>());
            Assert.Empty(result["archive_comparison"]!["content_changes"]!.AsArray());
            Assert.Empty(result["archive_comparison"]!["unexpected_content_changes"]!.AsArray());
            Assert.Equal(7, MapInspector.Inspect(output)["object_data"]!.AsArray().Count);
        }
        finally { DeleteTemp(directory); }
    }

    [Fact]
    public void UpdatingOneCategoryChangesOnlyThatObjectMember()
    {
        var source = FindSourceMap();
        var directory = TempDirectory();
        try
        {
            var fixture = Path.Combine(directory, "all-object-members.w3m");
            BuildAllObjectMemberFixture(source, fixture);
            var model = MapInspector.Inspect(fixture);
            var ability = model["object_data"]!.AsArray().OfType<JsonObject>().Single(item => item["category"]!.GetValue<string>() == "ability");
            var updated = OperationApplier.Apply(model, new JsonArray(Operation(
                "update_object_definition",
                new JsonObject { ["id"] = ability["id"]!.DeepClone() },
                ability.DeepClone(),
                new JsonObject { ["display_name"] = "Phase 5C Ability Changed" })))!["canonical_map"]!;

            var canonical = Path.Combine(directory, "changed-ability.json");
            var output = Path.Combine(directory, "changed-ability.w3m");
            JsonUtilities.WriteAtomic(canonical, updated);
            var result = MapBuilder.Build(fixture, canonical, output, "debug");
            var changed = result["archive_comparison"]!["content_changes"]!.AsArray().Select(item => item!["path"]!.GetValue<string>()).ToArray();

            Assert.Contains("war3map.w3a", changed);
            Assert.Contains("(attributes)", changed);
            Assert.Equal(2, changed.Length);
            Assert.Empty(result["archive_comparison"]!["unexpected_content_changes"]!.AsArray());
            var reopened = MapInspector.Inspect(output)["object_data"]!.AsArray().OfType<JsonObject>().Single(item => item["category"]!.GetValue<string>() == "ability");
            Assert.Equal("Phase 5C Ability Changed", reopened["display_name"]!.GetValue<string>());
        }
        finally { DeleteTemp(directory); }
    }

    [Fact]
    public void UnitAndBuildingPlacementsUseStableNativeCreationIdentities()
    {
        var source = FindSourceMap();
        var directory = TempDirectory();
        try
        {
            var fixture = Path.Combine(directory, "all-object-members.w3m");
            BuildAllObjectMemberFixture(source, fixture);
            var model = MapInspector.Inspect(fixture);
            var existing = model["placed_objects"]!.AsArray().OfType<JsonObject>().Where(item => item["member"]!.GetValue<string>() == "war3mapUnits.doo").ToArray();
            var next = existing.Max(item => item["creation_number"]!.GetValue<int>()) + 1;
            var createUnit = Operation("place_object", new JsonObject(), null, Placement("unit", "hfoo", next, 512));
            var afterUnit = OperationApplier.Apply(model, new JsonArray(createUnit))!["canonical_map"]!.AsObject();
            var unit = afterUnit["placed_objects"]!.AsArray().OfType<JsonObject>().Single(item => item["creation_number"]!.GetValue<int>() == next);
            Assert.Equal($"unit:{next}", unit["id"]!.GetValue<string>());

            var building = Operation("place_object", new JsonObject(), null, Placement("building", "hkee", next + 1, 768));
            var staged = OperationApplier.Apply(afterUnit, new JsonArray(building))!["canonical_map"]!.AsObject();
            var buildingValue = staged["placed_objects"]!.AsArray().OfType<JsonObject>().Single(item => item["creation_number"]!.GetValue<int>() == next + 1);
            Assert.Equal($"unit:{next + 1}", buildingValue["id"]!.GetValue<string>());
            Assert.Equal("building", buildingValue["kind"]!.GetValue<string>());

            var moved = OperationApplier.Apply(staged, new JsonArray(Operation(
                "move_object", new JsonObject { ["id"] = buildingValue["id"]!.DeepClone() }, buildingValue.DeepClone(),
                new JsonObject { ["position"] = Position(800, 800) })))!["canonical_map"]!.AsObject();
            var movedBuilding = moved["placed_objects"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == buildingValue["id"]!.GetValue<string>());
            var updated = OperationApplier.Apply(moved, new JsonArray(Operation(
                "update_placed_object", new JsonObject { ["id"] = unit["id"]!.DeepClone() }, unit.DeepClone(), new JsonObject { ["owner_id"] = 2, ["hit_points"] = 100 })))!["canonical_map"]!.AsObject();
            var removed = OperationApplier.Apply(updated, new JsonArray(Operation(
                "remove_placed_object", new JsonObject { ["id"] = movedBuilding["id"]!.DeepClone() }, movedBuilding.DeepClone(), null)))!["canonical_map"]!.AsObject();
            Assert.DoesNotContain(removed["placed_objects"]!.AsArray().OfType<JsonObject>(), item => item["id"]!.GetValue<string>() == movedBuilding["id"]!.GetValue<string>());
            staged = updated;

            var canonical = Path.Combine(directory, "placements.json");
            var output = Path.Combine(directory, "placements.w3m");
            JsonUtilities.WriteAtomic(canonical, staged);
            var result = MapBuilder.Build(fixture, canonical, output, "debug");
            Assert.True(result["reopened"]!.GetValue<bool>());
            var reopened = MapInspector.Inspect(output)["placed_objects"]!.AsArray().OfType<JsonObject>().ToArray();
            Assert.Contains(reopened, item => item["id"]!.GetValue<string>() == $"unit:{next}" && item["rawcode"]!.GetValue<string>() == "hfoo");
            Assert.Contains(reopened, item => item["id"]!.GetValue<string>() == $"unit:{next + 1}" && item["rawcode"]!.GetValue<string>() == "hkee");
            Assert.Empty(result["semantic_differences_after_reopen"]!.AsArray());

            var stale = Assert.Throws<EngineException>(() => OperationApplier.Apply(staged, new JsonArray(Operation(
                "move_object", new JsonObject { ["id"] = $"unit:{next}" }, new JsonObject { ["rawcode"] = "stale" },
                new JsonObject { ["position"] = Position(900, 900) }))));
            Assert.Equal("PRECONDITION_FAILED", stale.Code);
        }
        finally { DeleteTemp(directory); }
    }

    [Fact]
    public void MalformedAndWrongCategoryObjectBytesAreRejected()
    {
        Assert.ThrowsAny<Exception>(() => MapComponentCodec.ToObjectDefinitions("war3map.w3u", new byte[] { 1, 2, 3 }));
        var ability = MapComponentCodec.SerializeObjectMember("war3map.w3a", new JsonArray(ObjectDefinition("ability", "AHbz", "Z002", "anam", "Fixture Ability", levelBased: true)));
        Assert.ThrowsAny<Exception>(() => MapComponentCodec.ToObjectDefinitions("war3map.w3z", ability));

        var wrongCategory = Assert.Throws<EngineException>(() => OperationApplier.Apply(MapInspector.Inspect(FindSourceMap()), new JsonArray(Operation(
            "place_object", new JsonObject(), null, Placement("item", "hfoo", 5, 512)))));
        Assert.Equal("REFERENCE_MISSING", wrongCategory.Code);
    }

    [Fact]
    public void TypedObjectReferencesValidateAndUpdateDefinitionsAndPlacements()
    {
        var source = FindSourceMap();
        var directory = TempDirectory();
        try
        {
            var fixture = Path.Combine(directory, "all-object-members.w3m");
            BuildAllObjectMemberFixture(source, fixture);
            var model = MapInspector.Inspect(fixture);
            var unitDefinition = model["object_data"]!.AsArray().OfType<JsonObject>().Single(item => item["category"]!.GetValue<string>() == "unit");
            var withDefinitionReference = OperationApplier.Apply(model, new JsonArray(Operation(
                "set_object_reference", new JsonObject { ["id"] = unitDefinition["id"]!.DeepClone(), ["relation"] = "ability" }, null, "AHbz")))!["canonical_map"]!.AsObject();
            var definition = withDefinitionReference["object_data"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == unitDefinition["id"]!.GetValue<string>());
            Assert.Equal("AHbz", definition["references"]!["ability"]!.GetValue<string>());

            var placement = withDefinitionReference["placed_objects"]!.AsArray().OfType<JsonObject>().First(item => item["member"]!.GetValue<string>() == "war3mapUnits.doo");
            var withPlacementReferences = OperationApplier.Apply(withDefinitionReference, new JsonArray(
                Operation("set_object_reference", new JsonObject { ["id"] = placement["id"]!.DeepClone(), ["relation"] = "ability" }, placement["abilities"]!.DeepClone(), "AHbz"),
                Operation("set_object_reference", new JsonObject { ["id"] = placement["id"]!.DeepClone(), ["relation"] = "item" }, placement["inventory"]!.DeepClone(), new JsonObject { ["rawcode"] = "I000", ["slot"] = 0 }),
                Operation("set_object_reference", new JsonObject { ["id"] = placement["id"]!.DeepClone(), ["relation"] = "owner" }, 1, 2)))!["canonical_map"]!.AsObject();
            var changedPlacement = withPlacementReferences["placed_objects"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == placement["id"]!.GetValue<string>());
            Assert.Equal(2, changedPlacement["owner_id"]!.GetValue<int>());
            Assert.Contains(changedPlacement["abilities"]!.AsArray().OfType<JsonObject>(), item => item["rawcode"]!.GetValue<string>() == "AHbz");
            Assert.Contains(changedPlacement["inventory"]!.AsArray().OfType<JsonObject>(), item => item["rawcode"]!.GetValue<string>() == "I000");

            var missing = Assert.Throws<EngineException>(() => OperationApplier.Apply(model, new JsonArray(Operation(
                "set_object_reference", new JsonObject { ["id"] = placement["id"]!.DeepClone(), ["relation"] = "ability" }, placement["abilities"]!.DeepClone(), "ZZZZ"))));
            Assert.Equal("REFERENCE_MISSING", missing.Code);
        }
        finally { DeleteTemp(directory); }
    }

    private static void BuildAllObjectMemberFixture(string source, string fixture)
    {
        var definitions = new Dictionary<string, JsonArray>(StringComparer.OrdinalIgnoreCase)
        {
            ["war3map.w3u"] = new JsonArray(ObjectDefinition("unit", "hfoo", "Z001", "unam", "Fixture Unit")),
            ["war3map.w3a"] = new JsonArray(ObjectDefinition("ability", "AHbz", "Z002", "anam", "Fixture Ability", levelBased: true)),
            ["war3map.w3t"] = new JsonArray(ObjectDefinition("item", "I000", "Z003", "unam", "Fixture Item")),
            ["war3map.w3b"] = new JsonArray(ObjectDefinition("destructable", "LTlt", "Z004", "bnam", "Fixture Destructable")),
            ["war3map.w3d"] = new JsonArray(ObjectDefinition("doodad", "LTlt", "Z005", "dnam", "Fixture Doodad", variationBased: true)),
            ["war3map.w3h"] = new JsonArray(ObjectDefinition("buff", "Bapl", "Z006", "fnam", "Fixture Buff")),
            ["war3map.w3q"] = new JsonArray(ObjectDefinition("upgrade", "Rhme", "Z007", "gnam", "Fixture Upgrade", levelBased: true))
        };
        var replacements = definitions.ToDictionary(item => item.Key, item => MapComponentCodec.SerializeObjectMember(item.Key, item.Value), StringComparer.OrdinalIgnoreCase);
        MapArchive.Rebuild(source, fixture, replacements);
    }

    private static JsonObject ObjectDefinition(string category, string baseRawcode, string customRawcode, string modificationId, string displayName, bool levelBased = false, bool variationBased = false)
    {
        var modification = new JsonObject { ["id"] = modificationId, ["type"] = "String", ["value"] = displayName };
        if (levelBased) { modification["level"] = 0; modification["pointer"] = 0; }
        if (variationBased) { modification["variation"] = 0; modification["pointer"] = 0; }
        return new JsonObject
        {
            ["category"] = category, ["object_kind"] = "custom", ["base_rawcode"] = baseRawcode, ["custom_rawcode"] = customRawcode, ["rawcode"] = customRawcode,
            ["unknown_ids"] = new JsonArray(), ["modifications"] = new JsonArray(modification)
        };
    }

    private static JsonObject Placement(string kind, string rawcode, int creation, float x)
        => new()
        {
            ["kind"] = kind, ["rawcode"] = rawcode, ["creation_number"] = creation, ["position"] = Position(x, x), ["owner_id"] = 1
        };

    private static JsonObject Position(float x, float y)
        => new() { ["x"] = x, ["y"] = y, ["z"] = 0 };

    private static JsonObject Operation(string type, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var operation = new JsonObject { ["operation_id"] = Guid.NewGuid().ToString(), ["type"] = type, ["target"] = target, ["rationale"] = "Phase 5C typed object and placement test" };
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

    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-phase5c", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemp(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
