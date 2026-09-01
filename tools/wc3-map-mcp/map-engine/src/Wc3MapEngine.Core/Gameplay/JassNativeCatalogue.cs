using Wc3MapEngine.Core.Jass;

namespace Wc3MapEngine.Core.Gameplay;

/// <summary>
/// Compatibility facade for typed gameplay-model checks. API membership is
/// resolved from the single pinned jassdoc dataset, never a maintained list.
/// </summary>
public static class JassNativeCatalogue
{
    public const string Version = "jassdoc-deddec452ec16ea355ca0aa47046b88d416dbc65";
    public const string LegacyManifestVersion = "war3-jass-native-catalogue-1.0";

    public static bool IsPlayerState(string value)
    {
        var symbol = JassApiRepository.Default.Lookup(value);
        return symbol is not null
            && symbol.Kind == "global"
            && string.Equals(symbol.ReturnType, "playerstate", StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> PlayerStates => JassApiRepository.Default.Symbols
        .Where(symbol => symbol.Kind == "global" && string.Equals(symbol.ReturnType, "playerstate", StringComparison.Ordinal))
        .Select(symbol => symbol.Name)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
}
