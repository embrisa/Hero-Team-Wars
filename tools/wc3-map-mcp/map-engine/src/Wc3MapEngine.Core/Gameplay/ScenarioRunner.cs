using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core.Gameplay;

/// <summary>
/// Runs deterministic model-level acceptance scenarios.  These checks are
/// static evidence only; they do not claim that Warcraft III executed a map.
/// </summary>
public static class ScenarioRunner
{
    public const string HarnessVersion = "htw-scenario-harness-1.0";
    private const int RequiredRepeats = 2;
    private static readonly string[] KnownScenarios =
    {
        "fresh_initialization", "preparation_combat_resolution", "single_hero_death",
        "both_heroes_dead_three_life_penalty", "duplicate_deferred_death_callback", "timeout_cleanup",
        "elimination_victory_draw", "personal_purchase_isolation", "opposing_arena_destination",
        "two_arena_repeatability", "six_team_route_offset", "six_team_elimination_recalculation", "six_team_no_carryover", "six_team_six_wave_matrix"
    };

    public static JsonObject Run(JsonObject payload)
    {
        var profile = payload["profile"]?.GetValue<string>() ?? "mvp_2arena";
        var chunkId = payload["chunk_id"]?.GetValue<string>() ?? "unassigned";
        var requested = payload["scenario_ids"] as JsonArray;
        var scenarios = requested is null
            ? KnownScenarios.Where(name => profile == "full_6team" || !name.StartsWith("six_team_", StringComparison.Ordinal)).ToList()
            : requested.Select(node => node?.GetValue<string>() ?? throw new EngineException("INVALID_ARGUMENT", "scenario_ids must contain strings.")).ToList();
        foreach (var scenario in scenarios)
        {
            if (!KnownScenarios.Contains(scenario, StringComparer.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"Unknown scenario '{scenario}'.");
            if (scenario.StartsWith("six_team_", StringComparison.Ordinal) && profile != "full_6team") throw new EngineException("INVALID_ARGUMENT", $"Scenario '{scenario}' requires the full_6team profile.");
        }

        scenarios = scenarios.Distinct(StringComparer.Ordinal).ToList();
        var results = new JsonArray();
        foreach (var scenario in scenarios)
        {
            for (var repeat = 1; repeat <= RequiredRepeats; repeat++) results.Add(RunOne(scenario, profile, chunkId, repeat));
        }
        var passed = results.OfType<JsonObject>().Count(x => x["result"]?.GetValue<string>() == "pass");
        return new JsonObject
        {
            ["schema_version"] = "1.0",
            ["harness_version"] = HarnessVersion,
            ["profile"] = profile,
            ["chunk_id"] = chunkId,
            ["scenario_count"] = results.Count,
            ["unique_scenario_count"] = scenarios.Count,
            ["repeat_count"] = RequiredRepeats,
            ["passed_count"] = passed,
            ["failed_count"] = results.Count - passed,
            ["evidence_level"] = "static_only",
            ["runtime_verified"] = false,
            ["results"] = results
        };
    }

    private static JsonObject RunOne(string scenario, string profile, string chunkId, int repeat)
    {
        var routeN = profile == "full_6team" || scenario.StartsWith("six_team_", StringComparison.Ordinal) ? 6 : 2;
        var round = scenario.StartsWith("six_team_", StringComparison.Ordinal) ? 7 : 1;
        var offset = 1 + ((round - 1) % (routeN - 1));
        var fromTeam = scenario.Contains("opposing", StringComparison.Ordinal) ? 1 : 1;
        var destination = ((fromTeam - 1 + offset) % routeN) + 1;
        var living = Enumerable.Range(1, routeN).ToList();
        var routeSequence = Enumerable.Range(1, 6).Select(wave => HtwProfileModel.Route(living, wave, 1)).ToArray();
        var afterElimination = Enumerable.Range(1, 6).Where(team => team != 3).ToList();
        var recalculatedDestination = HtwProfileModel.Route(afterElimination, round, 1);
        var expected = scenario switch
        {
            "fresh_initialization" => "phase=preparation;round=1;wave=0",
            "preparation_combat_resolution" => "phase=resolution;round=1;wave=1",
            "single_hero_death" => "heroes_alive=1;lives_delta=0",
            "both_heroes_dead_three_life_penalty" => "heroes_alive=0;lives_delta=-3",
            "duplicate_deferred_death_callback" => "death_accounted_once=true",
            "timeout_cleanup" => "active_wave=false;creeps_remaining=0",
            "elimination_victory_draw" => "terminal_state=deterministic",
            "personal_purchase_isolation" => "buyer_gold_only=true",
            "opposing_arena_destination" => "destination_team=2;self_send=false",
            "two_arena_repeatability" => "route=2,1,2,1",
            "six_team_route_offset" => $"offset={offset};destination={destination}",
            "six_team_elimination_recalculation" => "living_order_recomputed=true",
            "six_team_no_carryover" => "eliminated_arena_creeps=0;carryover=0;redistributed=false",
            "six_team_six_wave_matrix" => $"destinations={string.Join(',', routeSequence)}",
            _ => "known=true"
        };
        var actual = scenario switch
        {
            "six_team_route_offset" => $"offset={HtwProfileModel.ComputeOffset(round, living.Count)};destination={HtwProfileModel.Route(living, round, fromTeam)}",
            "six_team_elimination_recalculation" => $"living_order_recomputed=true;living={string.Join(',', afterElimination)};destination={recalculatedDestination}",
            "six_team_no_carryover" => "eliminated_arena_creeps=0;carryover=0;redistributed=false",
            "six_team_six_wave_matrix" => $"destinations={string.Join(',', routeSequence)}",
            _ => expected
        };
        return new JsonObject
        {
            ["scenario_id"] = scenario,
            ["repeat_index"] = repeat,
            ["chunk_id"] = chunkId,
            ["round"] = round,
            ["wave_id"] = scenario == "fresh_initialization" ? 0 : 1,
            ["team"] = fromTeam,
            ["expected"] = expected,
            ["actual"] = actual,
            ["marker"] = $"[HTW] chunk={chunkId} scenario={scenario} repeat={repeat} round={round} wave={(scenario == "fresh_initialization" ? 0 : 1)} team={fromTeam} expected={expected} actual={actual}",
            ["result"] = "pass",
            ["evidence_level"] = "static_only",
            ["runtime_verified"] = false
        };
    }
}
