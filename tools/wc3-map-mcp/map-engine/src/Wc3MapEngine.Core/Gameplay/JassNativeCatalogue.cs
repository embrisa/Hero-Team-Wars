namespace Wc3MapEngine.Core.Gameplay;

/// <summary>
/// Pinned subset of the Warcraft III JASS native catalogue used by the typed
/// gameplay model.  Identifiers emitted into a native call are never accepted
/// merely because they look like JASS identifiers; they must be present here
/// or be owned by the MCP source model.
/// </summary>
public static class JassNativeCatalogue
{
    public const string Version = "war3-jass-native-catalogue-1.0";

    private static readonly IReadOnlySet<string> PlayerStateConstants = new HashSet<string>(StringComparer.Ordinal)
    {
        "PLAYER_STATE_GAME_RESULT",
        "PLAYER_STATE_RESOURCE_GOLD",
        "PLAYER_STATE_RESOURCE_LUMBER",
        "PLAYER_STATE_RESOURCE_FOOD_CAP",
        "PLAYER_STATE_RESOURCE_FOOD_USED",
        "PLAYER_STATE_FOOD_CAP_CEILING",
        "PLAYER_STATE_GIVES_BOUNTY",
        "PLAYER_STATE_ALLIED_VICTORY",
        "PLAYER_STATE_PLACED",
        "PLAYER_STATE_OBSERVER_ON_DEATH",
        "PLAYER_STATE_OBSERVER",
        "PLAYER_STATE_UNFOLLOWABLE",
        "PLAYER_STATE_GOLD_UPKEEP_RATE",
        "PLAYER_STATE_LUMBER_UPKEEP_RATE",
        "PLAYER_STATE_GOLD_GATHERED",
        "PLAYER_STATE_LUMBER_GATHERED",
        "PLAYER_STATE_RESCUABLE",
        "PLAYER_STATE_SHARED_CONTROL",
        "PLAYER_STATE_SHARED_ADVANCED_CONTROL",
        "PLAYER_STATE_UNIT_CAP",
        "PLAYER_STATE_HERO_LIMIT",
        "PLAYER_STATE_RESOURCE_HERO_TOKENS",
        "PLAYER_STATE_RESOURCE_DEATH",
        "PLAYER_STATE_RESOURCE_HERO_LEVEL",
        "PLAYER_STATE_RESOURCE_HERO_SKILL"
    };

    public static bool IsPlayerState(string value) => PlayerStateConstants.Contains(value);

    public static IReadOnlyList<string> PlayerStates => PlayerStateConstants.OrderBy(value => value, StringComparer.Ordinal).ToArray();
}
