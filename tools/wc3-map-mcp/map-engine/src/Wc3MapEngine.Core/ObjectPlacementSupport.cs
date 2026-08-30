using System.Collections.ObjectModel;

namespace Wc3MapEngine.Core;

/// <summary>
/// Shared category, identity, and conservative standard-rawcode policy for
/// object definitions and placed objects.  The map archive does not contain
/// the game's complete standard object tables, so callers must either provide
/// a custom definition or use a rawcode in this small, explicitly maintained
/// compatibility set.  Unknown rawcodes are never guessed as standard.
/// </summary>
public static class ObjectPlacementSupport
{
    private static readonly IReadOnlyDictionary<string, string> Members = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["war3map.w3u"] = "unit",
        ["war3map.w3a"] = "ability",
        ["war3map.w3t"] = "item",
        ["war3map.w3b"] = "destructable",
        ["war3map.w3d"] = "doodad",
        ["war3map.w3h"] = "buff",
        ["war3map.w3q"] = "upgrade"
    });

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> StandardRawcodes =
        new ReadOnlyDictionary<string, IReadOnlySet<string>>(new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit"] = Set("sloc", "hfoo", "hpea", "hhou", "hbar", "hrif", "hmtm", "hkni", "hmpr", "hsor", "hgyr", "hmtt", "hmvc", "hbot", "opeo", "ogru", "otau", "oshm", "orai", "okod", "owar", "ocat", "otbr", "owvy", "nvil", "nsea", "nech", "nkob", "nspd", "nwld", "ngol", "nwgu", "nftr", "hkee", "hcas", "hwt3", "ogre", "obea", "ofor", "otto", "obar", "owtw"),
            ["ability"] = Set("AHbz", "AHwe", "AHab", "AHtc", "AHtb", "AHav", "AHhb", "AHds", "AHre", "AHad", "AHpx", "AOsh", "AOws", "AOcl", "AOeq", "AOww", "AOfs", "AOsw", "AOhw", "AOr2", "AOr3", "Aatk", "Adef", "Aamk", "Adef", "Aall", "Aneu", "Awan", "AInv", "AIvu", "AIhe", "AIam", "AIil", "AIms", "AItf", "AIrm", "AIsp", "AI2m", "AIcd", "AIso", "AItn", "AIfb", "AIfh", "AIsm", "AIsh", "AIad", "AIba", "AIbk", "AIbl", "AIbm", "AIbr", "AIbt", "AIbu", "AIcb", "AIcf", "AIct", "AIda", "AIdd", "AIdi", "AIdm", "AIdp", "AIdt", "AIem", "AIer", "AIev", "AIfa", "AIfp", "AIfr", "AIfu", "AIgv", "AIgx", "AIgz", "AIhi", "AIhm", "AIhx", "AIi1", "AIi2", "AIi3", "AIi4", "AIi5", "AIi6", "AIi7", "AIi8", "AIi9", "AIig", "AIim", "AIin", "AIir", "AIiu", "AIiv", "AIix", "AIjy", "AIjl", "AIjt", "AIjz", "AIka", "AIkb", "AIkl", "AIkm", "AIko", "AIkr", "AIks", "AIkv", "AIkw", "AIkx", "AIky", "AIkz", "AIlf", "AIlm", "AIlu", "AIlx", "AIlz", "AImb", "AImh", "AImv", "AImx", "AInb", "AInd", "AIng", "AInr", "AIny", "AIob", "AIof", "AIom", "AIos", "AIp1", "AIp2", "AIp3", "AIp4", "AIp5", "AIp6", "AIp7", "AIp8", "AIp9", "AIpb", "AIpc", "AIpm", "AIpr", "AIpx", "AIpy", "AIpz", "AIrb", "AIrc", "AIre", "AIrn", "AIrv", "AIrx", "AIs1", "AIs2", "AIs3", "AIs4", "AIs5", "AIs6", "AIs7", "AIs8", "AIs9", "AIt1", "AIt2", "AIt3", "AIt4", "AIt5", "AIt6", "AIt7", "AIt8", "AIt9", "AIta", "AItd", "AIth", "AItj", "AItl", "AItp", "AIts", "AIva", "AIvf", "AIvl", "AIvm", "AIvn", "AIvp", "AIvs", "AIvt", "AIvu", "AIvw", "AIvx", "AIvz"),
            ["item"] = Set("I000", "I001", "I002", "I003", "I004", "I005", "I006", "I007", "I008", "I009", "I00A", "I00B", "I00C", "I00D", "I00E", "I00F", "I010", "I011", "I012", "I013", "I014", "I015", "I016", "I017", "I018", "I019", "I01A", "I01B", "I01C", "I01D", "I01E", "I01F", "I020", "I021", "I022", "I023", "I024", "I025", "I026", "I027", "I028", "I029", "I02A", "I02B", "I02C", "I02D", "I02E", "I02F", "I030", "I031", "I032", "I033", "I034", "I035", "I036", "I037", "I038", "I039", "I03A", "I03B", "I03C", "I03D", "I03E", "I03F", "I040", "I041", "I042", "I043", "I044", "I045", "I046", "I047", "I048", "I049", "I04A", "I04B", "I04C", "I04D", "I04E", "I04F", "I050", "I051", "I052", "I053", "I054", "I055", "I056", "I057", "I058", "I059", "I05A", "I05B", "I05C", "I05D", "I05E", "I05F"),
            ["upgrade"] = Set("Rhme", "Rhar", "Rhst", "Rhan", "Rhri", "Rhpm", "Rhde", "Rhra", "Rhse", "Rugf", "Rugr", "Rugm", "Rugp"),
            ["doodad"] = Set("LTlt", "LTba", "LTbr", "LTc1", "LTc2", "LTcr", "LTg1", "LTg2", "LTg3", "LTg4", "LTg5", "LTg6", "LTg7", "LTg8", "LTbx", "LTbs", "LTw0", "LTw1", "LTw2", "LTw3", "LTw4", "LTw5", "LTw6", "LTw7", "LTw8", "LTw9"),
            ["destructable"] = Set("LTlt", "LTba", "LTbr", "B001", "B002"),
            ["buff"] = Set("Bapl", "Bblo", "Bstt", "Bhea", "Binf", "Bsha", "Bspo", "Bmil", "Brai", "Babr", "Bcor", "Bcri", "Bdef", "Bdet", "Bdig", "Bdom", "Bena", "Bfre", "Bfro", "Bhea", "Bspe", "Bstn", "Bsta", "Bven", "Barm", "Bfzy", "Bhrl", "Bchd", "Bvul")
        });

    public static IReadOnlyDictionary<string, string> ObjectMembers => Members;

    public static bool IsObjectMember(string path) => Members.ContainsKey(path);

    public static string CategoryForMember(string path) => Members.TryGetValue(path, out var category)
        ? category
        : throw new EngineException("INVALID_ARGUMENT", $"'{path}' is not a supported object-data member.");

    public static string MemberForCategory(string category) => Members.FirstOrDefault(item => string.Equals(item.Value, category, StringComparison.OrdinalIgnoreCase)).Key
        ?? throw new EngineException("INVALID_ARGUMENT", $"Unsupported object category '{category}'.");

    public static bool IsSupportedCategory(string? category) => category is not null && Members.Values.Contains(category, StringComparer.OrdinalIgnoreCase);

    public static bool IsValidRawcode(string? rawcode) => rawcode is { Length: 4 } && rawcode.All(character => character is >= '\x20' and <= '\x7E');

    public static bool IsKnownStandard(string category, string rawcode)
        => StandardRawcodes.TryGetValue(category, out var values) && values.Contains(rawcode);

    public static string MemberForPlacementKind(string kind) => kind.ToLowerInvariant() switch
    {
        "unit" or "building" or "item" => "war3mapUnits.doo",
        "doodad" or "destructable" or "special_doodad" => "war3map.doo",
        _ => throw new EngineException("INVALID_ARGUMENT", $"Placement kind '{kind}' is not supported.")
    };

    public static string IdentityKindForMember(string member) => member.Equals("war3map.doo", StringComparison.OrdinalIgnoreCase) ? "doodad" : "unit";

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
