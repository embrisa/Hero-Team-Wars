using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Wc3MapEngine.Core;
using Wc3MapEngine.Core.Gameplay;
using Xunit;

namespace Wc3MapEngine.Tests.Gameplay;

/// <summary>
/// Static v21 contract tests for the H003 / HTW Controller spell kit.
/// These checks intentionally stop at object-data, generated-source, and
/// typed-codec evidence; Warcraft III runtime behavior remains a manual gate.
/// </summary>
public sealed class ControllerAbilityTests
{
    private static readonly string[] KitRawcodes = { "A2Q1", "A2W1", "A2E1", "A2R1" };

    private static readonly IReadOnlyDictionary<string, AbilitySpec> Kit =
        new Dictionary<string, AbilitySpec>(StringComparer.Ordinal)
        {
            ["A2Q1"] = new(
                "Arcane Lance",
                4,
                new[] { 55, 65, 75, 85 },
                new[] { 5.5f, 5f, 4.5f, 4f },
                new[] { "arcane", "hostile", "damage" }),
            ["A2W1"] = new(
                "Gravity Well",
                4,
                new[] { 90, 105, 120, 135 },
                new[] { 16f, 14f, 12f, 10f },
                new[] { "gravity", "slow", "hostile" }),
            ["A2E1"] = new(
                "Mana Relay",
                4,
                new[] { 70, 80, 90, 100 },
                new[] { 18f, 16f, 14f, 12f },
                new[] { "mana", "heal", "allied" }),
            ["A2R1"] = new(
                "Astral Collapse",
                3,
                new[] { 150, 200, 250 },
                new[] { 100f, 85f, 70f },
                new[] { "astral", "stun", "hostile" })
        };

    [Fact]
    public void V21ControllerAbilityFixtureContainsExactKitNamesLevelsCostsAndCooldowns()
    {
        var fixture = ReadFixture("v21-controller-abilities.json");
        var definitions = Definitions(fixture);
        var abilities = definitions
            .Where(item => string.Equals(item["category"]?.GetValue<string>(), "ability", StringComparison.OrdinalIgnoreCase))
            .Where(item => KitRawcodes.Contains(item["rawcode"]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal))
            .ToDictionary(item => item["rawcode"]!.GetValue<string>(), StringComparer.Ordinal);

        Assert.Equal(KitRawcodes.Length, abilities.Count);
        Assert.Equal(KitRawcodes.OrderBy(value => value), abilities.Keys.OrderBy(value => value));

        foreach (var (rawcode, expected) in Kit)
        {
            Assert.Contains(rawcode, abilities);
            var ability = abilities[rawcode];
            Assert.Equal("ability", ability["category"]!.GetValue<string>());
            Assert.Equal("custom", ability["object_kind"]!.GetValue<string>());
            Assert.Equal(rawcode, ability["custom_rawcode"]!.GetValue<string>());

            var modifications = Modifications(ability);
            Assert.Equal(expected.Name, Scalar<string>(modifications, "anam", 0));
            Assert.True(Description(modifications) is { Length: > 0 }, $"{rawcode} must have a non-empty description.");
            foreach (var keyword in expected.DescriptionKeywords)
            {
                Assert.Contains(keyword, Description(modifications), StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(expected.Levels, Scalar<int>(modifications, "alev", 0));
            Assert.Equal(expected.ManaCosts, LevelValues<int>(modifications, "amcs", expected.Levels));
            Assert.Equal(expected.Cooldowns, LevelValues<float>(modifications, "acdn", expected.Levels));
        }
    }

    [Fact]
    public void H003ReferencesOnlyTheFourControllerAbilitiesAndOtherCustomHeroesRemainUnchanged()
    {
        var fixture = ReadFixture("v8-hero-objects.json");
        var definitions = Definitions(fixture);
        var heroes = definitions
            .Where(item => string.Equals(item["category"]?.GetValue<string>(), "unit", StringComparison.OrdinalIgnoreCase))
            .Where(item => item["rawcode"] is not null)
            .ToDictionary(item => item["rawcode"]!.GetValue<string>(), StringComparer.Ordinal);

        foreach (var rawcode in new[] { "H001", "H002", "H003", "H004" })
        {
            Assert.Contains(rawcode, heroes);
        }

        Assert.Equal(KitRawcodes, HeroAbilityRawcodes(heroes["H003"]));
        Assert.Empty(HeroAbilityRawcodes(heroes["H001"]));
        Assert.Empty(HeroAbilityRawcodes(heroes["H002"]));
        Assert.Empty(HeroAbilityRawcodes(heroes["H004"]));

        var baseline = ReadFixture("v8-hero-objects.json");
        var baselineH003 = Definitions(baseline).Single(item => item["rawcode"]!.GetValue<string>() == "H003");
        var currentH003 = heroes["H003"];
        foreach (var modificationId in new[] { "unam", "utip", "ustr", "uagi", "uint", "uhpm", "ugol", "ulum", "usst", "usrg" })
        {
            Assert.Equal(
                ModificationValue(baselineH003, modificationId)?.ToJsonString(),
                ModificationValue(currentH003, modificationId)?.ToJsonString());
        }
        Assert.Equal("Hamg", currentH003["base_rawcode"]!.GetValue<string>());
        Assert.Equal("HTW Controller", currentH003["display_name"]!.GetValue<string>());
    }

    [Fact]
    public void ControllerAbilityDefinitionsRoundTripThroughTypedAbilityCodec()
    {
        var fixture = ReadFixture("v21-controller-abilities.json");
        var abilityDefinitions = new JsonArray(
            Definitions(fixture)
                .Where(item => item["category"]?.GetValue<string>() == "ability")
                .Where(item => KitRawcodes.Contains(item["rawcode"]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal))
                .Select(item => item.DeepClone())
                .ToArray());

        var encoded = MapComponentCodec.SerializeObjectMember("war3map.w3a", abilityDefinitions);
        var decoded = MapComponentCodec.ToObjectDefinitions("war3map.w3a", encoded).OfType<JsonObject>().ToArray();

        Assert.Equal(KitRawcodes.OrderBy(value => value), decoded.Select(item => item["rawcode"]!.GetValue<string>()).OrderBy(value => value));
        Assert.Equal(encoded, MapComponentCodec.SerializeObjectMember("war3map.w3a", new JsonArray(decoded.Select(item => item.DeepClone()).ToArray())));
        foreach (var rawcode in KitRawcodes)
        {
            var original = abilityDefinitions.OfType<JsonObject>().Single(item => item["rawcode"]!.GetValue<string>() == rawcode);
            var reopened = decoded.Single(item => item["rawcode"]!.GetValue<string>() == rawcode);
            Assert.Equal(original["base_rawcode"]!.GetValue<string>(), reopened["base_rawcode"]!.GetValue<string>());
            Assert.Equal(
                ModificationSignature(original),
                ModificationSignature(reopened));
        }
    }

    [Fact]
    public void SpellDispatchHandlesOnlyTheFourControllerRawcodesAndGuardsTargets()
    {
        var source = ComposedSource();
        Assert.Contains("GetSpellAbilityId()", source, StringComparison.Ordinal);
        Assert.Contains("GetSpellTargetUnit()", source, StringComparison.Ordinal);
        Assert.Contains("GetSpellTargetX()", source, StringComparison.Ordinal);
        Assert.Contains("GetSpellTargetY()", source, StringComparison.Ordinal);
        Assert.Contains("GetUnitTypeId(caster) != 'H003'", source, StringComparison.Ordinal);
        Assert.Contains("IsUnitAlly", source, StringComparison.Ordinal);
        Assert.Contains("IsUnitType", source, StringComparison.Ordinal);
        Assert.Contains("UNIT_TYPE_DEAD", source, StringComparison.Ordinal);

        foreach (var rawcode in KitRawcodes)
        {
            Assert.Contains($"'{rawcode}'", source, StringComparison.Ordinal);
        }

        var comparisons = Regex.Matches(source, @"(?:GetSpellAbilityId\(\)|abilityId)\s*==\s*'(?<rawcode>[ -~]{4})'", RegexOptions.IgnoreCase)
            .Select(match => match.Groups["rawcode"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(KitRawcodes.OrderBy(value => value), comparisons.OrderBy(value => value));
    }

    [Fact]
    public void TimedGravityAndAstralStateHaveExplicitGroupAndNativeEffectCleanup()
    {
        var source = ComposedSource();
        Assert.Contains("DestroyGroup", source, StringComparison.Ordinal);
        Assert.Contains("CreateUnit", source, StringComparison.Ordinal);
        Assert.Contains("IssueTargetOrder", source, StringComparison.Ordinal);
        Assert.Contains("RemoveUnit(dummy)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PauseUnit", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetUnitMoveSpeed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MvpGlobalVisibilityAndExactTeamArenaCenterDeploymentRemainIntact()
    {
        var source = ComposedSource();
        Assert.Contains("call FogEnable(false)", source, StringComparison.Ordinal);
        Assert.Contains("call FogMaskEnable(false)", source, StringComparison.Ordinal);
        Assert.Contains("set x = GetRectCenterX(HTW_ArenaRect[teamIndex])", source, StringComparison.Ordinal);
        Assert.Contains("set y = GetRectCenterY(HTW_ArenaRect[teamIndex])", source, StringComparison.Ordinal);
        Assert.Contains("call SetUnitPosition(HTW_HeroUnitByPlayer[playerId], x, y)", source, StringComparison.Ordinal);
        Assert.Contains("call PanCameraToTimed(x, y, 0.)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("x + I2R(playerId * 64)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("y + I2R(playerId * 48)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ControllerKitDoesNotEnterTheSharedAltarStockOrImportAssets()
    {
        var fixture = ReadFixture("v8-hero-objects.json");
        var altar = Definitions(fixture).Single(item => item["rawcode"]!.GetValue<string>() == "n0AL");
        var soldUnits = ModificationValue(altar, "useu")!.GetValue<string>().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(new[] { "H001", "H002", "H003", "H004" }, soldUnits);
        Assert.DoesNotContain(soldUnits, KitRawcodes);

        var source = ComposedSource();
        Assert.DoesNotContain("AddUnitToStock", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetUnitStock", source, StringComparison.Ordinal);
        Assert.DoesNotContain("war3mapImported", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComposedSource()
        => GameplaySourceComposer.Compose(FindFixture(Path.Combine("tools", "wc3-map-mcp", "scripts", "mcp", "manifest.json")), HtwProfileModel.MvpProfile)["source"]!.GetValue<string>();

    private static JsonObject ReadFixture(string name)
        => JsonNode.Parse(File.ReadAllText(FindFixture(Path.Combine("tools", "wc3-map-mcp", "scripts", "mcp", "object-data", name))))!.AsObject();

    private static IEnumerable<JsonObject> Definitions(JsonObject fixture)
        => fixture["objects"]!.AsArray().OfType<JsonObject>();

    private static IReadOnlyList<JsonObject> Modifications(JsonObject definition)
        => definition["modifications"]!.AsArray().OfType<JsonObject>().ToArray();

    private static string Description(IReadOnlyList<JsonObject> modifications)
    {
        var normal = modifications.FirstOrDefault(item => item["id"]?.GetValue<string>() == "aub1")
            ?? modifications.FirstOrDefault(item => item["id"]?.GetValue<string>() == "atp1");
        return normal?["value"]?.GetValue<string>() ?? string.Empty;
    }

    private static T Scalar<T>(IReadOnlyList<JsonObject> modifications, string id, int level)
        => modifications.Single(item => item["id"]?.GetValue<string>() == id && (item["level"]?.GetValue<int>() ?? 0) == level)["value"]!.GetValue<T>();

    private static T[] LevelValues<T>(IReadOnlyList<JsonObject> modifications, string id, int levels)
        => Enumerable.Range(0, levels).Select(level => Scalar<T>(modifications, id, level)).ToArray();

    private static string[] HeroAbilityRawcodes(JsonObject hero)
    {
        var values = new List<string>();
        foreach (var modification in Modifications(hero))
        {
            var id = modification["id"]?.GetValue<string>();
            if (id is not ("uhab" or "uabi" or "abil")) continue;
            var value = modification["value"]?.GetValue<string>();
            if (value is not null) values.AddRange(SplitRawcodes(value));
        }

        if (hero["abilities"] is JsonArray abilities)
        {
            values.AddRange(abilities.OfType<JsonValue>().Select(item => item.GetValue<string>()).SelectMany(SplitRawcodes));
        }
        else if (hero["abilities"] is JsonValue abilityText && abilityText.TryGetValue<string>(out var text))
        {
            values.AddRange(SplitRawcodes(text));
        }
        return values.Where(rawcode => KitRawcodes.Contains(rawcode, StringComparer.Ordinal)).ToArray();
    }

    private static IEnumerable<string> SplitRawcodes(string value)
        => value.Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static JsonNode? ModificationValue(JsonObject definition, string id)
        => Modifications(definition).FirstOrDefault(item => item["id"]?.GetValue<string>() == id)?["value"];

    private static string ModificationSignature(JsonObject definition)
        => string.Join(";", Modifications(definition)
            .OrderBy(item => item["id"]?.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(item => item["level"]?.GetValue<int>() ?? -1)
            .Select(item => string.Join("|", item["id"], item["type"], item["value"], item["level"], item["pointer"])));

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

    private sealed record AbilitySpec(
        string Name,
        int Levels,
        int[] ManaCosts,
        float[] Cooldowns,
        string[] DescriptionKeywords);
}
