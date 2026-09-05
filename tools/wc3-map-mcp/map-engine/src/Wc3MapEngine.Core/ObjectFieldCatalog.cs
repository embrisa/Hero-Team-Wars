using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Wc3MapEngine.Core;

/// <summary>One embedded offline catalog for lookup, authoring and validation.</summary>
public static class ObjectFieldCatalog
{
    private static readonly byte[] CatalogBytes = Load();
    private static readonly JsonObject Catalog = JsonNode.Parse(CatalogBytes)!.AsObject();
    private static readonly JsonObject[] Fields = Catalog["fields"]!.AsArray().OfType<JsonObject>().ToArray();
    private static byte[] Load()
    {
        using var stream = typeof(ObjectFieldCatalog).Assembly.GetManifestResourceStream("Wc3MapEngine.ObjectFields.json")
            ?? throw new EngineException("DEPENDENCY_MISSING", "Embedded object-field catalog missing; rebuild the engine.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
    public static JsonObject GetCatalog() => Catalog.DeepClone().AsObject();
    public static JsonObject? Find(string category, string name) => Fields.FirstOrDefault(f => Text(f, "category") == category && (Text(f, "id") == name || Text(f, "name") == name))?.DeepClone().AsObject();
    public static JsonObject Lookup(JsonObject request)
    {
        CheckRequest(request, "field", "category", "base_rawcode");
        var field = RequiredText(request, "field");
        var matches = Filter(request).Where(f => Text(f, "id") == field || Text(f, "name") == field).ToArray();
        return new JsonObject
        {
            ["catalog_version"] = Catalog["catalog_version"]!.DeepClone(), ["catalog_sha256"] = CatalogHash(),
            ["found"] = matches.Length == 1, ["ambiguous"] = matches.Length > 1,
            ["field"] = matches.Length == 1 ? Describe(matches[0]) : null,
            ["matches"] = new JsonArray(matches.Select(f => (JsonNode)Describe(f)).ToArray()),
            ["suggestions"] = Search(new JsonObject { ["query"] = field, ["category"] = request["category"]?.DeepClone(), ["limit"] = 5 })["matches"]!.DeepClone(),
            ["identifier_kind"] = matches.Length > 0 ? "field" : field.Length < 4 ? "search_prefix" : ObjectPlacementSupport.IsValidRawcode(field) ? "unknown_field_or_object_id" : "unknown_alias"
        };
    }
    public static JsonObject Search(JsonObject request)
    {
        CheckRequest(request, "query", "category", "base_rawcode", "limit");
        var query = RequiredText(request, "query");
        var limit = request["limit"]?.GetValue<int>() ?? 10;
        if (limit is < 1 or > 50) throw new EngineException("INVALID_ARGUMENT", "limit must be 1-50.");
        var tokens = Regex.Matches(query.ToLowerInvariant(), "[a-z0-9]+").Select(m => m.Value).Distinct().ToArray();
        var matches = Filter(request).Select(f => new { Field = f, Score = Score(f, query, tokens) }).Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => Text(x.Field, "category"), StringComparer.Ordinal).ThenBy(x => Text(x.Field, "id"), StringComparer.Ordinal).ToArray();
        return new JsonObject { ["catalog_version"] = Catalog["catalog_version"]!.DeepClone(), ["catalog_sha256"] = CatalogHash(),
            ["total_matches"] = matches.Length, ["matches"] = new JsonArray(matches.Take(limit).Select(x => (JsonNode)Describe(x.Field)).ToArray()) };
    }
    private static int Score(JsonObject field, string query, string[] tokens)
    {
        if (Text(field, "id").Equals(query, StringComparison.OrdinalIgnoreCase) || Text(field, "name").Equals(query, StringComparison.OrdinalIgnoreCase)) return 1000;
        var name = $"{Text(field, "id")} {Regex.Replace(Text(field, "name"), "([a-z])([A-Z])", "$1 $2")}".ToLowerInvariant();
        var text = $"{name} {Text(field, "description")} {Text(field, "metadata_field")}".ToLowerInvariant();
        var words = Regex.Matches(text, "[a-z0-9]+").Select(m => m.Value).ToArray();
        var names = Regex.Matches(name, "[a-z0-9]+").Select(m => m.Value).ToArray();
        return tokens.Sum(token => (words.Any(w => w.StartsWith(token, StringComparison.Ordinal)) ? 10 : 0) + (names.Any(w => w.StartsWith(token, StringComparison.Ordinal)) ? 20 : 0));
    }
    private static IEnumerable<JsonObject> Filter(JsonObject request)
    {
        var category = request["category"]?.GetValue<string>(); var parent = request["base_rawcode"]?.GetValue<string>();
        if (category is not null && !ObjectPlacementSupport.IsSupportedCategory(category)) throw new EngineException("INVALID_ARGUMENT", "Unsupported object category.");
        if (parent is not null && !ObjectPlacementSupport.IsValidRawcode(parent)) throw new EngineException("INVALID_ARGUMENT", "base_rawcode must be exactly four printable ASCII characters.");
        return Fields.Where(f => (category is null || Text(f, "category") == category) && (parent is null || f["base_rawcodes"]!.AsArray().Count == 0 || f["base_rawcodes"]!.AsArray().Any(p => p!.GetValue<string>() == parent)));
    }
    private static JsonObject Describe(JsonObject field)
    {
        var result = field.DeepClone().AsObject();
        result["source_evidence"] = Catalog["sources"]![Text(field, "source")]!.DeepClone();
        if (field["values"]!.AsArray().Count > 0) result["enum_source_evidence"] = Catalog["sources"]!["channel_enums"]!.DeepClone();
        result["scope_convention"] = Text(field, "scope") == "level" ? "Native level: 0 for non-repeated fields; repeated fields use 1-based levels. Pointer equals data_pointer. No UI-to-native conversion."
            : Text(field, "scope") == "variation" ? "Native nonnegative variation and pointer; no index conversion." : "No level, pointer or variation.";
        return result;
    }
    public static string CatalogHash() => Hashing.Sha256(CatalogBytes);

    /// <summary>Normalize requested values only; never rewrite observed or expected records.</summary>
    public static void Normalize(JsonObject definition)
    {
        if (definition["modifications"] is not JsonArray mods) return;
        foreach (var mod in mods.OfType<JsonObject>())
        {
            if (mod["field"] is not JsonValue alias) continue;
            if (mod.ContainsKey("id") || mod.ContainsKey("type")) throw new EngineException("INVALID_ARGUMENT", "Use field/value or id/type/value, never both.");
            var metadata = Find(Text(definition, "category"), alias.GetValue<string>()) ?? throw new EngineException("OBJECT_FIELD_UNKNOWN", $"Unknown field '{alias}'. Look up the exact field/category; prefixes are not writable.");
            mod.Remove("field"); mod["id"] = metadata["id"]!.DeepClone(); mod["type"] = metadata["type"]!.DeepClone();
            if (Text(metadata, "scope") != "simple") mod["pointer"] ??= metadata["data_pointer"]!.DeepClone();
            var kind = Text(metadata, "value_kind");
            if (kind == "rawcode_list" && mod["value"] is JsonArray list)
            {
                if (list.Any(v => v is not JsonValue scalarValue || !scalarValue.TryGetValue<string>(out var code) || !ObjectPlacementSupport.IsValidRawcode(code)))
                    throw new EngineException("OBJECT_FIELD_VALUE_INVALID", "Rawcode-list arrays require exact four-character entries; use [] for an empty list.");
                mod["value"] = string.Join(",", list.Select(v => v!.GetValue<string>()));
            }
            if (kind == "enum" && mod["value"] is JsonValue scalar && scalar.TryGetValue<string>(out var name)) mod["value"] = EnumValue(metadata, name);
            if (kind == "flags" && mod["value"] is JsonArray flags) mod["value"] = flags.Aggregate(0, (mask, flag) => mask | EnumValue(metadata, flag!.GetValue<string>()));
        }
    }
    private static int EnumValue(JsonObject metadata, string name) => metadata["values"]!.AsArray().OfType<JsonObject>().FirstOrDefault(v => Text(v, "name") == name)?["value"]?.GetValue<int>()
        ?? throw new EngineException("OBJECT_FIELD_VALUE_INVALID", $"Unknown {Text(metadata, "name")} value '{name}'; use the catalog's exact names.");

    /// <summary>Unchanged legacy records pass through, including unknown data.</summary>
    public static void ValidateChanges(JsonObject root, JsonObject? before, JsonObject after)
    {
        var oldMods = (before?["modifications"] as JsonArray ?? new()).OfType<JsonObject>().ToArray();
        var mods = (after["modifications"] as JsonArray ?? new()).OfType<JsonObject>().ToArray();
        foreach (var old in oldMods.Where(m => Find(Text(after, "category"), Text(m, "id")) is null))
            if (mods.Count(m => JsonUtilities.Equal(old, m)) != oldMods.Count(m => JsonUtilities.Equal(old, m))) throw new EngineException("OBJECT_FIELD_UNKNOWN", $"Unknown field '{Text(old, "id")}' must be preserved unchanged, including scope and multiplicity.");
        static string ScopeKey(JsonObject mod) => $"{mod["id"]}|{mod["level"]}|{mod["variation"]}|{mod["pointer"]}";
        foreach (var group in mods.GroupBy(ScopeKey).Where(g => g.Count() > 1))
        {
            var previous = oldMods.Where(m => ScopeKey(m) == group.Key).ToArray();
            if (previous.Length != group.Count() || !group.Zip(previous).All(pair => JsonUtilities.Equal(pair.First, pair.Second)))
                throw new EngineException("OBJECT_FIELD_DUPLICATE", $"Duplicate modification scope '{group.Key}'.");
        }
        foreach (var mod in mods)
        {
            if (oldMods.Any(old => JsonUtilities.Equal(old, mod))) continue;
            ValidateModification(root, after, mod);
        }
    }
    private static void ValidateModification(JsonObject root, JsonObject definition, JsonObject mod)
    {
        var id = Text(mod, "id");
        var metadata = Find(Text(definition, "category"), id) ?? throw new EngineException("OBJECT_FIELD_UNKNOWN", $"Field '{id}' is unsupported for {Text(definition, "category")}. Use wc3_object_field_lookup; unknown fields are read-only.");
        if (Text(mod, "type") != Text(metadata, "type")) throw new EngineException("OBJECT_FIELD_TYPE_MISMATCH", $"{Text(metadata, "name")} [{id}] requires {Text(metadata, "type")}, received {Text(mod, "type")}.");
        var parents = metadata["base_rawcodes"]!.AsArray();
        if (parents.Count > 0 && !Ancestors(root, definition).Any(parent => parents.Any(p => p!.GetValue<string>() == parent))) throw new EngineException("OBJECT_FIELD_BASE_MISMATCH", $"{Text(metadata, "name")} [{id}] does not apply to base '{Text(definition, "base_rawcode")}'.");
        if (Text(metadata, "scope") == "level")
        {
            var level = mod["level"]?.GetValue<int>() ?? -1; var repeated = metadata["repeat"]!.GetValue<int>() > 0;
            if ((repeated ? level < 1 : level != 0) || mod["pointer"]?.GetValue<int>() != metadata["data_pointer"]!.GetValue<int>()) throw new EngineException("OBJECT_FIELD_SCOPE_INVALID", $"{Text(metadata, "name")} [{id}] requires {(repeated ? "native level >= 1" : "level 0")} and pointer {metadata["data_pointer"]}.");
        }
        var value = mod["value"];
        if (Text(metadata, "value_kind") == "rawcode_list")
        {
            var text = value?.GetValue<string>() ?? "";
            if (text.Length > 0 && text.Split(',').Any(code => !ObjectPlacementSupport.IsValidRawcode(code))) throw new EngineException("OBJECT_FIELD_VALUE_INVALID", $"{Text(metadata, "name")} [{id}] requires comma-separated four-character rawcodes or an empty string.");
        }
        if (Text(metadata, "value_kind") is "enum" or "flags")
        {
            if (value is not JsonValue scalar || !scalar.TryGetValue<int>(out var number)) throw new EngineException("OBJECT_FIELD_VALUE_INVALID", $"{id} requires an integer.");
            var values = metadata["values"]!.AsArray().Select(v => v!["value"]!.GetValue<int>()).ToArray();
            if (Text(metadata, "value_kind") == "enum" ? !values.Contains(number) : number < 0 || (number & ~values.Aggregate(0, (a, b) => a | b)) != 0) throw new EngineException("OBJECT_FIELD_VALUE_INVALID", $"Unsupported {Text(metadata, "name")} [{id}] value {number}; inspect catalog values.");
        }
    }
    private static IEnumerable<string> Ancestors(JsonObject root, JsonObject definition)
    {
        var parent = Text(definition, "base_rawcode"); var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(parent))
        {
            yield return parent;
            var next = (root["object_data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(d => Text(d, "category") == Text(definition, "category") && Text(d, "rawcode") == parent);
            if (next is null) yield break;
            parent = Text(next, "base_rawcode");
        }
    }
    public static JsonArray Annotate(JsonArray definitions) => new(definitions.OfType<JsonObject>().Select(d => (JsonNode)new JsonObject
    {
        ["rawcode"] = d["rawcode"]?.DeepClone(), ["category"] = d["category"]?.DeepClone(),
        ["fields"] = new JsonArray((d["modifications"] as JsonArray ?? new()).OfType<JsonObject>().Select(m =>
        {
            var info = Find(Text(d, "category"), Text(m, "id")); var view = m.DeepClone().AsObject();
            view["name"] = info?["name"]?.DeepClone(); view["description"] = info?["description"]?.DeepClone();
            view["expected_type"] = info?["type"]?.DeepClone(); view["known"] = info is not null;
            if (info is not null && m["value"] is JsonValue scalar && scalar.TryGetValue<int>(out var number))
            {
                var values = info["values"]!.AsArray().OfType<JsonObject>();
                if (Text(info, "value_kind") == "enum") view["value_name"] = values.FirstOrDefault(v => v["value"]!.GetValue<int>() == number)?["name"]?.DeepClone();
                if (Text(info, "value_kind") == "flags") view["value_names"] = new JsonArray(values.Where(v => (number & v["value"]!.GetValue<int>()) != 0).Select(v => v["name"]!.DeepClone()).ToArray());
            }
            return (JsonNode)view;
        }).ToArray())
    }).ToArray());

    public static JsonArray AnnotateChanges(JsonObject before, JsonObject after)
    {
        var old = (before["object_data"] as JsonArray ?? new()).OfType<JsonObject>().ToArray();
        var current = (after["object_data"] as JsonArray ?? new()).OfType<JsonObject>().ToArray();
        var result = new JsonArray();
        foreach (var key in old.Concat(current).Select(d => (Category: Text(d, "category"), Rawcode: Text(d, "rawcode"))).Distinct())
        {
            var left = old.FirstOrDefault(d => Text(d, "category") == key.Category && Text(d, "rawcode") == key.Rawcode);
            var right = current.FirstOrDefault(d => Text(d, "category") == key.Category && Text(d, "rawcode") == key.Rawcode);
            if (JsonUtilities.Equal(left?["modifications"], right?["modifications"])) continue;
            result.Add(new JsonObject { ["category"] = key.Category, ["rawcode"] = key.Rawcode,
                ["before"] = left is null ? null : Annotate(new JsonArray(left.DeepClone()))[0]!.DeepClone(),
                ["after"] = right is null ? null : Annotate(new JsonArray(right.DeepClone()))[0]!.DeepClone() });
        }
        return result;
    }

    // Authoring policy, separate from upstream field metadata. Only affected
    // relationships are checked, so unrelated legacy data remains passthrough.
    public static void ValidateRelations(JsonObject before, JsonObject after)
    {
        var old = (before["object_data"] as JsonArray ?? new()).OfType<JsonObject>().ToArray();
        var current = (after["object_data"] as JsonArray ?? new()).OfType<JsonObject>().ToArray();
        foreach (var definition in current)
            ValidateChanges(after, old.FirstOrDefault(o => Text(o, "category") == Text(definition, "category") && Text(o, "rawcode") == Text(definition, "rawcode")), definition);
        bool Changed(JsonObject d) => !JsonUtilities.Equal(old.FirstOrDefault(o => Text(o, "category") == Text(d, "category") && Text(o, "rawcode") == Text(d, "rawcode"))?["modifications"], d["modifications"]);
        foreach (var unit in current.Where(d => Text(d, "category") == "unit"))
        {
            var oldUnit = old.FirstOrDefault(d => Text(d, "category") == "unit" && Text(d, "rawcode") == Text(unit, "rawcode"));
            var attachmentChanged = oldUnit is null || !JsonUtilities.Equal(ModValue(oldUnit, "uhab"), ModValue(unit, "uhab")) || !JsonUtilities.Equal(ModValue(oldUnit, "uabi"), ModValue(unit, "uabi"));
            var skills = (ModValue(unit, "uhab")?.GetValue<string>() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            var normal = (ModValue(unit, "uabi")?.GetValue<string>() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawcode in normal)
            {
                var ability = current.FirstOrDefault(d => Text(d, "category") == "ability" && Text(d, "rawcode") == rawcode);
                if (ability is not null && (attachmentChanged || Changed(ability)) && Ancestors(after, ability).Contains("ANcl") && ModValue(ability, "aher")?.ToJsonString() == "true")
                    throw new EngineException("OBJECT_HERO_SKILL_INVALID", $"Learnable Channel skill '{rawcode}' must use heroAbilities [uhab], not normalAbilities [uabi], on {Text(unit, "rawcode")}.");
            }
            foreach (var skill in skills)
            {
                var ability = current.FirstOrDefault(d => Text(d, "category") == "ability" && Text(d, "rawcode") == skill);
                if (!attachmentChanged && (ability is null ? !old.Any(d => Text(d, "category") == "ability" && Text(d, "rawcode") == skill) : !Changed(ability))) continue;
                if (normal.Contains(skill)) throw new EngineException("OBJECT_HERO_SKILL_INVALID", $"{Text(unit, "rawcode")}: '{skill}' occurs in both heroAbilities [uhab] and normalAbilities [uabi].");
                if (ability is null)
                {
                    if (!ObjectPlacementSupport.IsKnownStandard("ability", skill)) throw new EngineException("OBJECT_HERO_SKILL_INVALID", $"Hero skill '{skill}' has no known ability definition.");
                    continue;
                }
                if (Ancestors(after, ability).Contains("ANcl"))
                {
                    if (ModValue(ability, "aher")?.ToJsonString() != "true" || ModValue(ability, "aite")?.ToJsonString() == "true" || string.IsNullOrWhiteSpace(ModValue(ability, "aart")?.GetValue<string>()))
                        throw new EngineException("OBJECT_HERO_SKILL_INVALID", $"Channel hero skill '{skill}' in {Text(unit, "rawcode")}.heroAbilities [uhab] requires explicit heroAbility [aher]=true, a nonempty normalIcon [aart], and must not be an item ability.");
                }
            }
        }
    }
    private static JsonNode? ModValue(JsonObject definition, string id) => (definition["modifications"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(m => Text(m, "id") == id)?["value"];
    private static void CheckRequest(JsonObject request, params string[] allowed)
    {
        if (request.Any(p => !allowed.Contains(p.Key))) throw new EngineException("INVALID_ARGUMENT", "Unexpected object-field request property.");
    }
    private static string RequiredText(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue v || !v.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text) || text.Length > 200) throw new EngineException("INVALID_ARGUMENT", $"{name} must be 1-200 nonblank characters.");
        return text;
    }
    private static string Text(JsonObject obj, string name) => obj[name]?.GetValue<string>() ?? "";
}
