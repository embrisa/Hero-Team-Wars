using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Wc3MapEngine.Core.Validation;
using Xunit;

namespace Wc3MapEngine.Tests;

public sealed class ObjectFieldCatalogTests
{
    [Fact]
    public void LookupDistinguishesExactFieldsPrefixesObjectIdsAndCategories()
    {
        Assert.False(ObjectFieldCatalog.Lookup(new() { ["field"] = "Ncl" })["found"]!.GetValue<bool>());
        Assert.False(ObjectFieldCatalog.Lookup(new() { ["field"] = "ANcl" })["found"]!.GetValue<bool>());
        Assert.False(ObjectFieldCatalog.Lookup(new() { ["field"] = "ncl2" })["found"]!.GetValue<bool>());
        Assert.True(ObjectFieldCatalog.Lookup(new() { ["field"] = "unam" })["ambiguous"]!.GetValue<bool>());
        var field = ObjectFieldCatalog.Lookup(new() { ["field"] = "heroAbilities", ["category"] = "unit" })["field"]!;
        Assert.Equal("uhab", field["id"]!.GetValue<string>());
        Assert.NotEmpty(field["source_evidence"]!["sha256"]!.GetValue<string>());
        Assert.Equal("uhab", ObjectFieldCatalog.Search(new() { ["query"] = "learnable hero skills", ["category"] = "unit", ["limit"] = 1 })["matches"]![0]!["id"]!.GetValue<string>());
        Assert.Empty(ObjectFieldCatalog.Search(new() { ["query"] = "Ncl", ["base_rawcode"] = "AHtb" })["matches"]!.AsArray());
    }

    [Fact]
    public void NamedChannelAuthoringResolvesVerifiedEnumsTypesAndNativePointers()
    {
        var definition = Ability(new JsonObject() { ["field"] = "channelTargetType", ["value"] = "point", ["level"] = 1 },
            new() { ["field"] = "channelOptions", ["value"] = new JsonArray("visible", "physical"), ["level"] = 1 });
        var result = OperationApplier.Apply(Root(), new JsonArray(Create(definition)));
        var mods = result["canonical_map"]!["object_data"]![0]!["modifications"]!.AsArray();
        Assert.Equal("Ncl2", mods[0]!["id"]!.GetValue<string>());
        Assert.Equal("Int", mods[0]!["type"]!.GetValue<string>());
        Assert.Equal(2, mods[0]!["value"]!.GetValue<int>());
        Assert.Equal(2, mods[0]!["pointer"]!.GetValue<int>());
        Assert.Equal(5, mods[1]!["value"]!.GetValue<int>());
        Assert.Equal(3, mods[1]!["pointer"]!.GetValue<int>());
        Assert.NotEmpty(result["diff"]!["object_fields"]!.AsArray());
        Assert.Null(mods[0]!["field"]);
        Assert.NotNull(definition["modifications"]![0]!["field"]); // caller-owned input unchanged
    }

    [Theory]
    [InlineData("{\"id\":\"Ncl2\",\"type\":\"String\",\"value\":\"2\",\"level\":1,\"pointer\":2}", "OBJECT_FIELD_TYPE_MISMATCH")]
    [InlineData("{\"id\":\"Ncl2\",\"type\":\"Int\",\"value\":4,\"level\":1,\"pointer\":2}", "OBJECT_FIELD_VALUE_INVALID")]
    [InlineData("{\"id\":\"Ncl3\",\"type\":\"Int\",\"value\":32,\"level\":1,\"pointer\":3}", "OBJECT_FIELD_VALUE_INVALID")]
    [InlineData("{\"id\":\"Ncl2\",\"type\":\"Int\",\"value\":1,\"level\":0,\"pointer\":0}", "OBJECT_FIELD_SCOPE_INVALID")]
    [InlineData("{\"id\":\"uhab\",\"type\":\"String\",\"value\":\"A000\",\"level\":0,\"pointer\":0}", "OBJECT_FIELD_UNKNOWN")]
    [InlineData("{\"field\":\"Ncl\",\"value\":1,\"level\":1}", "OBJECT_FIELD_UNKNOWN")]
    public void InvalidFieldWritesAreAtomic(string json, string code)
    {
        var root = Root(); var original = root.ToJsonString();
        var valid = Ability(new JsonObject() { ["field"] = "abilityName", ["value"] = "Valid", ["level"] = 0 });
        var invalid = Ability(JsonNode.Parse(json)!.AsObject()); invalid["custom_rawcode"] = "Z002"; invalid["rawcode"] = "Z002";
        Assert.Equal(code, Assert.Throws<EngineException>(() => OperationApplier.Apply(root, new JsonArray(Create(valid), Create(invalid)))).Code);
        Assert.Equal(original, root.ToJsonString());
    }

    [Fact]
    public void BaseMismatchDuplicateAliasesAndUnknownMutationAreRejected()
    {
        var wrongBase = Ability(new JsonObject() { ["field"] = "channelTargetType", ["value"] = "unit", ["level"] = 1 }); wrongBase["base_rawcode"] = "AHtb";
        Assert.Equal("OBJECT_FIELD_BASE_MISMATCH", Assert.Throws<EngineException>(() => OperationApplier.Apply(Root(), new JsonArray(Create(wrongBase)))).Code);
        var duplicate = Ability(new JsonObject() { ["field"] = "normalIcon", ["value"] = "icon.blp", ["level"] = 0 }, new() { ["id"] = "aart", ["type"] = "String", ["value"] = "icon.blp", ["level"] = 0, ["pointer"] = 0 });
        Assert.Equal("OBJECT_FIELD_DUPLICATE", Assert.Throws<EngineException>(() => OperationApplier.Apply(Root(), new JsonArray(Create(duplicate)))).Code);
        var legacy = Ability(new JsonObject() { ["id"] = "zzzz", ["type"] = "String", ["value"] = "opaque", ["level"] = 7, ["pointer"] = 9 });
        var root = Root(legacy); var update = legacy.DeepClone().AsObject();
        update["modifications"]!.AsArray().Add(new JsonObject { ["id"] = "aart", ["type"] = "String", ["value"] = "icon.blp", ["level"] = 0, ["pointer"] = 0 });
        ObjectFieldCatalog.ValidateChanges(root, legacy, update);
        update["modifications"]![0]!["value"] = "changed";
        Assert.Equal("OBJECT_FIELD_UNKNOWN", Assert.Throws<EngineException>(() => ObjectFieldCatalog.ValidateChanges(root, legacy, update)).Code);
        update["modifications"]!.AsArray().RemoveAt(0);
        Assert.Equal("OBJECT_FIELD_UNKNOWN", Assert.Throws<EngineException>(() => ObjectFieldCatalog.ValidateChanges(root, legacy, update)).Code);
    }

    [Fact]
    public void HeroAttachmentPolicyChecksTheCompletedBatch()
    {
        var unit = new JsonObject { ["category"] = "unit", ["object_kind"] = "custom", ["base_rawcode"] = "Hamg", ["custom_rawcode"] = "Z002", ["rawcode"] = "Z002",
            ["modifications"] = new JsonArray(new JsonObject { ["field"] = "heroAbilities", ["value"] = new JsonArray("Z001") }) };
        var ability = Ability(new JsonObject() { ["field"] = "heroAbility", ["value"] = true, ["level"] = 0 }, new() { ["field"] = "normalIcon", ["value"] = "icon.blp", ["level"] = 0 });
        var result = OperationApplier.Apply(Root(), new JsonArray(Create(unit), Create(ability)));
        Assert.Equal(2, result["canonical_map"]!["object_data"]!.AsArray().Count);
        unit["modifications"]![0]!["field"] = "normalAbilities";
        Assert.Equal("OBJECT_HERO_SKILL_INVALID", Assert.Throws<EngineException>(() => OperationApplier.Apply(Root(), new JsonArray(Create(unit), Create(ability)))).Code);
        unit["modifications"]![0]!["field"] = "heroAbilities";
        ability["modifications"]![0]!["value"] = false;
        Assert.Equal("OBJECT_HERO_SKILL_INVALID", Assert.Throws<EngineException>(() => OperationApplier.Apply(Root(), new JsonArray(Create(unit), Create(ability)))).Code);
    }

    [Fact]
    public void EveryCatalogFieldHasAStaticTypedCodecRoundTrip()
    {
        foreach (var field in ObjectFieldCatalog.GetCatalog()["fields"]!.AsArray().OfType<JsonObject>())
        {
            var category = field["category"]!.GetValue<string>(); var type = field["type"]!.GetValue<string>();
            JsonNode value = type switch { "Int" => JsonValue.Create(0)!, "Real" or "Unreal" => JsonValue.Create(1.25)!, "Bool" => JsonValue.Create(true)!, _ => JsonValue.Create("")! };
            var modification = new JsonObject { ["id"] = field["id"]!.DeepClone(), ["type"] = type, ["value"] = value };
            var scope = field["scope"]!.GetValue<string>();
            if (scope == "level") { modification["level"] = field["repeat"]!.GetValue<int>() > 0 ? 1 : 0; modification["pointer"] = field["data_pointer"]!.DeepClone(); }
            if (scope == "variation") { modification["variation"] = 0; modification["pointer"] = 0; }
            var definition = Ability(modification); definition["category"] = category;
            definition["base_rawcode"] = field["base_rawcodes"]!.AsArray().FirstOrDefault()?.DeepClone() ?? JsonValue.Create(category switch { "unit" => "hfoo", "ability" => "ANcl", "buff" => "Bapl", "item" => "I000", "upgrade" => "Rhme", _ => "LTlt" });
            ObjectFieldCatalog.ValidateChanges(Root(), null, definition);
            var member = ObjectPlacementSupport.MemberForCategory(category);
            var encoded = MapComponentCodec.SerializeObjectMember(member, new JsonArray(JsonNode.Parse(definition.ToJsonString())));
            var decoded = MapComponentCodec.ToObjectDefinitions(member, encoded);
            Assert.Equal(modification.ToJsonString(), decoded[0]!["modifications"]![0]!.ToJsonString());
            Assert.Equal(encoded, MapComponentCodec.SerializeObjectMember(member, decoded));
        }
    }

    [Fact]
    public void BuildValidationRejectsInvalidCanonicalWritesAndPreservesLegacyUnknownFields()
    {
        var source = FindSource(); var sourceHash = Hashing.Sha256(File.ReadAllBytes(source));
        var directory = Path.Combine(Path.GetDirectoryName(source)!, "..", "tools", "wc3-map-mcp", "snapshots", "field-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var invalid = MapInspector.Inspect(source);
            invalid["object_data"]!.AsArray().Add(Ability(new JsonObject { ["id"] = "Ncl2", ["type"] = "String", ["value"] = "2", ["level"] = 1, ["pointer"] = 2 }));
            var canonical = Path.Combine(directory, "canonical.json");
            JsonUtilities.WriteAtomic(canonical, invalid);
            var report = ValidationPipeline.ValidateCanonical(canonical, source);
            Assert.False(report["buildable"]!.GetValue<bool>());
            Assert.Contains(report["findings"]!.AsArray().OfType<JsonObject>(), f => f["code"]!.GetValue<string>() == "OBJECT_FIELD_TYPE_MISMATCH");

            var legacy = Ability(new JsonObject { ["id"] = "zzzz", ["type"] = "String", ["value"] = "preserve me", ["level"] = 7, ["pointer"] = 9 });
            var legacySource = Path.Combine(directory, "legacy.w3m");
            MapArchive.Rebuild(source, legacySource, new Dictionary<string, byte[]> { ["war3map.w3a"] = MapComponentCodec.SerializeObjectMember("war3map.w3a", new JsonArray(legacy)) });
            var model = MapInspector.Inspect(legacySource);
            var observed = model["object_data"]![0]!.AsObject();
            var mods = observed["modifications"]!.DeepClone().AsArray();
            mods.Add(new JsonObject { ["field"] = "normalIcon", ["value"] = "icon.blp", ["level"] = 0 });
            var operation = new JsonObject { ["operation_id"] = Guid.NewGuid().ToString(), ["type"] = "update_object_definition", ["target"] = new JsonObject { ["id"] = observed["id"]!.DeepClone() },
                ["expected"] = observed.DeepClone(), ["value"] = new JsonObject { ["modifications"] = mods }, ["rationale"] = "Preserve unknown field during named update" };
            var changed = OperationApplier.Apply(model, new JsonArray(operation))["canonical_map"]!;
            JsonUtilities.WriteAtomic(canonical, changed);
            var output = Path.Combine(directory, "updated.w3m");
            Assert.True(MapBuilder.Build(legacySource, canonical, output, "debug")["reopened"]!.GetValue<bool>());
            var reopened = MapInspector.Inspect(output)["object_data"]!.AsArray();
            Assert.True(JsonUtilities.Equal(observed["modifications"]![0], reopened[0]!["modifications"]![0]));
            Assert.False(ObjectFieldCatalog.Annotate(reopened)[0]!["fields"]![0]!["known"]!.GetValue<bool>());
            Assert.Equal(sourceHash, Hashing.Sha256(File.ReadAllBytes(source)));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string FindSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "map", "HeroTeamWars_M0_2Arena.w3m");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Source map not found.");
    }

    private static JsonObject Root(params JsonObject[] definitions) => new() { ["schema_version"] = "1.0", ["object_data"] = new JsonArray(definitions.Select(d => (JsonNode)d.DeepClone()).ToArray()) };
    private static JsonObject Ability(params JsonObject[] mods) => new() { ["category"] = "ability", ["object_kind"] = "custom", ["base_rawcode"] = "ANcl", ["custom_rawcode"] = "Z001", ["rawcode"] = "Z001", ["modifications"] = new JsonArray(mods.Select(m => (JsonNode)m.DeepClone()).ToArray()) };
    private static JsonObject Create(JsonObject value) => new() { ["operation_id"] = Guid.NewGuid().ToString(), ["type"] = "create_object_definition", ["target"] = new JsonObject(), ["value"] = value.DeepClone(), ["rationale"] = "Verify field authoring" };
}
