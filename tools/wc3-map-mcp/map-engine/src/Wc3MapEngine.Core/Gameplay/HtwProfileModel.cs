using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core.Gameplay;

/// <summary>
/// The map-independent Hero Team Wars profile contract.  Team identity is
/// deliberately represented by stable logical IDs; player colour is never
/// consulted by this model.
/// </summary>
public static class HtwProfileModel
{
    public const string MvpProfile = "mvp_2arena";
    public const string FullProfile = "full_6team";

    public static bool IsKnown(string? profile)
        => profile is MvpProfile or FullProfile;

    public static JsonObject ProfilesDocument()
        => new()
        {
            [MvpProfile] = ProfileSpec(MvpProfile),
            [FullProfile] = ProfileSpec(FullProfile)
        };

    public static JsonObject ProfileSpec(string profile)
    {
        return profile switch
        {
            MvpProfile => new JsonObject
            {
                ["active_player_ids"] = IntArray(1, 2, 3, 4),
                ["team_ids"] = StringArray("team_1", "team_2"),
                ["arena_ids"] = StringArray("arena_a", "arena_b"),
                ["shared_team_lives"] = 15,
                ["shared_vision"] = true,
                ["shared_unit_control"] = false,
                ["team_definitions"] = DefaultTeams(MvpProfile)
            },
            FullProfile => new JsonObject
            {
                ["active_player_ids"] = IntArray(Enumerable.Range(1, 12).ToArray()),
                ["team_ids"] = StringArray(Enumerable.Range(1, 6).Select(id => $"team_{id}").ToArray()),
                ["arena_ids"] = StringArray(Enumerable.Range(1, 6).Select(id => $"arena_{id}").ToArray()),
                ["shared_team_lives"] = 15,
                ["shared_vision"] = true,
                ["shared_unit_control"] = false,
                ["team_definitions"] = DefaultTeams(FullProfile)
            },
            _ => throw new EngineException("INVALID_ARGUMENT", $"Unknown Hero Team Wars profile '{profile}'.")
        };
    }

    public static JsonArray DefaultTeams(string profile)
    {
        var count = profile switch
        {
            MvpProfile => 2,
            FullProfile => 6,
            _ => throw new EngineException("INVALID_ARGUMENT", $"Unknown Hero Team Wars profile '{profile}'.")
        };
        var teams = new JsonArray();
        for (var index = 1; index <= count; index++)
        {
            var firstPlayer = profile == MvpProfile ? (index == 1 ? 1 : 3) : (index * 2) - 1;
            var arena = profile == MvpProfile ? (index == 1 ? "arena_a" : "arena_b") : $"arena_{index}";
            teams.Add(new JsonObject
            {
                ["id"] = $"team_{index}",
                ["name"] = $"Team {index}",
                ["member_player_ids"] = IntArray(firstPlayer, firstPlayer + 1),
                ["force_index"] = index - 1,
                ["arena_id"] = arena,
                ["hero_ids"] = new JsonArray(),
                ["life_state"] = "active",
                ["routing_state"] = "unassigned",
                ["provenance"] = "intended_design",
                ["capability"] = "typed_write_enabled"
            });
        }

        return teams;
    }

    public static JsonArray NormalizeTeams(string profile, JsonNode? teams)
    {
        if (teams is JsonArray explicitTeams)
        {
            var normalized = new JsonArray();
            foreach (var item in explicitTeams.OfType<JsonObject>())
            {
                var team = item.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Team record could not be cloned.");
                team["provenance"] ??= "intended_design";
                team["capability"] ??= "typed_write_enabled";
                normalized.Add(team);
            }
            return normalized;
        }

        return DefaultTeams(profile);
    }

    public static JsonObject BuildTeamRegistry(JsonArray teams)
    {
        var registryTeams = new JsonArray();
        foreach (var item in teams.OfType<JsonObject>())
        {
            var team = item.DeepClone() as JsonObject ?? throw new EngineException("INVALID_JSON", "Team record could not be cloned.");
            team["provenance"] ??= "intended_design";
            team["capability"] ??= "typed_write_enabled";
            registryTeams.Add(team);
        }

        var registry = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["identity"] = "logical_team_id",
            ["teams"] = registryTeams,
            ["living_team_order"] = new JsonArray(registryTeams.OfType<JsonObject>()
                .Where(team => !string.Equals(team["life_state"]?.GetValue<string>(), "eliminated", StringComparison.OrdinalIgnoreCase))
                .OrderBy(team => team["id"]?.GetValue<string>(), StringComparer.Ordinal)
                .Select(team => (JsonNode?)JsonValue.Create(team["id"]?.GetValue<string>() ?? string.Empty))
                .ToArray()),
            ["provenance"] = "intended_design",
            ["capability"] = "staged_typed_write"
        };
        return registry;
    }

    public static int ComputeOffset(int round, int livingTeamCount)
    {
        if (round < 1) throw new EngineException("INVALID_ARGUMENT", "Routing rounds start at 1.");
        if (livingTeamCount < 2) throw new EngineException("INVALID_ARGUMENT", "At least two living teams are required for routing.");
        return 1 + ((round - 1) % (livingTeamCount - 1));
    }

    public static int Route(IReadOnlyList<int> livingTeamIds, int round, int senderTeamId)
    {
        var senderIndex = -1;
        for (var index = 0; index < livingTeamIds.Count; index++)
        {
            if (livingTeamIds[index] == senderTeamId)
            {
                senderIndex = index;
                break;
            }
        }
        if (senderIndex < 0) throw new EngineException("INVALID_ARGUMENT", $"Sender team '{senderTeamId}' is not living.");
        var offset = ComputeOffset(round, livingTeamIds.Count);
        return livingTeamIds[(senderIndex + offset) % livingTeamIds.Count];
    }

    private static JsonArray IntArray(params int[] values)
        => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static JsonArray StringArray(params string[] values)
        => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
}
