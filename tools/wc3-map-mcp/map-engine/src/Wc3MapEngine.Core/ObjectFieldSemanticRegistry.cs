using System.Collections.ObjectModel;

namespace Wc3MapEngine.Core;

public enum FieldSemanticProvenance
{
    StaticFixtureBacked,
    RuntimeProven
}

public sealed record ObjectFieldSemanticMetadata(
    string FieldId,
    string Category,
    string ExpectedType,
    string Description,
    FieldSemanticProvenance Provenance
);

/// <summary>
/// Semantic allow-list and registry for object-data modification fields.
/// Fields used by custom hero and custom building experiments are cataloged here
/// and labeled as StaticFixtureBacked until isolated runtime experiments prove
/// their Warcraft III engine semantics.
/// </summary>
public static class ObjectFieldSemanticRegistry
{
    private static readonly IReadOnlyDictionary<string, ObjectFieldSemanticMetadata> Fields =
        new ReadOnlyDictionary<string, ObjectFieldSemanticMetadata>(new Dictionary<string, ObjectFieldSemanticMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["unam"] = new("unam", "unit", "String", "Unit or hero name", FieldSemanticProvenance.StaticFixtureBacked),
            ["utip"] = new("utip", "unit", "String", "Tooltip description", FieldSemanticProvenance.StaticFixtureBacked),
            ["ustr"] = new("ustr", "unit", "Int", "Starting strength", FieldSemanticProvenance.StaticFixtureBacked),
            ["uagi"] = new("uagi", "unit", "Int", "Starting agility", FieldSemanticProvenance.StaticFixtureBacked),
            ["uint"] = new("uint", "unit", "Int", "Starting intelligence", FieldSemanticProvenance.StaticFixtureBacked),
            ["uhpm"] = new("uhpm", "unit", "Int", "Hit points maximum override", FieldSemanticProvenance.StaticFixtureBacked),
            ["ugol"] = new("ugol", "unit", "Int", "Gold cost override", FieldSemanticProvenance.StaticFixtureBacked),
            ["ulum"] = new("ulum", "unit", "Int", "Lumber cost override", FieldSemanticProvenance.StaticFixtureBacked),
            ["usst"] = new("usst", "unit", "Int", "Stock initial delay / start stock", FieldSemanticProvenance.StaticFixtureBacked),
            ["usrg"] = new("usrg", "unit", "Int", "Stock replenishment interval", FieldSemanticProvenance.StaticFixtureBacked),
            ["useu"] = new("useu", "unit", "String", "Building sold units list", FieldSemanticProvenance.StaticFixtureBacked),
            ["usca"] = new("usca", "unit", "Int", "Model scale", FieldSemanticProvenance.StaticFixtureBacked)
            , ["uabi"] = new("uabi", "unit", "String", "Unit ability list", FieldSemanticProvenance.StaticFixtureBacked)
            , ["uhab"] = new("uhab", "unit", "String", "Unit hero ability list", FieldSemanticProvenance.StaticFixtureBacked)
            , ["umdl"] = new("umdl", "unit", "String", "Unit model", FieldSemanticProvenance.StaticFixtureBacked)
            , ["ushu"] = new("ushu", "unit", "String", "Unit shadow", FieldSemanticProvenance.StaticFixtureBacked)
            , ["ufoo"] = new("ufoo", "unit", "Int", "Food cost", FieldSemanticProvenance.StaticFixtureBacked)
            , ["ushr"] = new("ushr", "unit", "Bool", "Unit shadow enabled", FieldSemanticProvenance.StaticFixtureBacked)
            , ["ucpt"] = new("ucpt", "unit", "Real", "Cast point", FieldSemanticProvenance.StaticFixtureBacked)
            , ["ucbs"] = new("ucbs", "unit", "Real", "Cast backswing", FieldSemanticProvenance.StaticFixtureBacked)
            , ["umvs"] = new("umvs", "unit", "Real", "Movement speed", FieldSemanticProvenance.StaticFixtureBacked)
            , ["anam"] = new("anam", "ability", "String", "Ability name", FieldSemanticProvenance.StaticFixtureBacked)
            , ["atp1"] = new("atp1", "ability", "String", "Ability tooltip", FieldSemanticProvenance.StaticFixtureBacked)
            , ["aub1"] = new("aub1", "ability", "String", "Ability extended tooltip", FieldSemanticProvenance.StaticFixtureBacked)
            , ["alev"] = new("alev", "ability", "Int", "Maximum levels", FieldSemanticProvenance.StaticFixtureBacked)
            , ["aran"] = new("aran", "ability", "Real", "Cast range", FieldSemanticProvenance.StaticFixtureBacked)
            , ["aare"] = new("aare", "ability", "Real", "Area of effect", FieldSemanticProvenance.StaticFixtureBacked)
            , ["adur"] = new("adur", "ability", "Real", "Duration", FieldSemanticProvenance.StaticFixtureBacked)
            , ["ahdu"] = new("ahdu", "ability", "Real", "Hero duration", FieldSemanticProvenance.StaticFixtureBacked)
            , ["acdn"] = new("acdn", "ability", "Real", "Cooldown", FieldSemanticProvenance.StaticFixtureBacked)
            , ["amcs"] = new("amcs", "ability", "Int", "Mana cost", FieldSemanticProvenance.StaticFixtureBacked)
            , ["atar"] = new("atar", "ability", "String", "Target type", FieldSemanticProvenance.StaticFixtureBacked)
            , ["Ncl1"] = new("Ncl1", "ability", "Real", "Channel data", FieldSemanticProvenance.StaticFixtureBacked)
            , ["Ncl2"] = new("Ncl2", "ability", "Real", "Channel data", FieldSemanticProvenance.StaticFixtureBacked)
            , ["Ncl3"] = new("Ncl3", "ability", "Real", "Channel data", FieldSemanticProvenance.StaticFixtureBacked)
            , ["Ncl6"] = new("Ncl6", "ability", "Real", "Channel data", FieldSemanticProvenance.StaticFixtureBacked)
            , ["Slo1"] = new("Slo1", "ability", "Unreal", "Movement slow factor", FieldSemanticProvenance.StaticFixtureBacked)
            , ["Slo2"] = new("Slo2", "ability", "Unreal", "Attack slow factor", FieldSemanticProvenance.StaticFixtureBacked)
            , ["Htb1"] = new("Htb1", "ability", "Real", "Storm Bolt damage", FieldSemanticProvenance.StaticFixtureBacked)
        });

    public static bool TryGetFieldMetadata(string fieldId, out ObjectFieldSemanticMetadata? metadata)
        => Fields.TryGetValue(fieldId, out metadata);

    public static bool IsKnownField(string fieldId) => Fields.ContainsKey(fieldId);

    public static IReadOnlyCollection<ObjectFieldSemanticMetadata> GetAllFields() => Fields.Values.ToArray();
}
