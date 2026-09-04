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
        });

    public static bool TryGetFieldMetadata(string fieldId, out ObjectFieldSemanticMetadata? metadata)
        => Fields.TryGetValue(fieldId, out metadata);

    public static bool IsKnownField(string fieldId) => Fields.ContainsKey(fieldId);

    public static IReadOnlyCollection<ObjectFieldSemanticMetadata> GetAllFields() => Fields.Values.ToArray();
}
