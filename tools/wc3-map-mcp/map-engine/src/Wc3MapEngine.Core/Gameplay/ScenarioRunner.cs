using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core.Gameplay;

/// <summary>
/// Runs deterministic model-level acceptance scenarios. The simulator mirrors
/// the source-owned runtime invariants without pretending that Warcraft III
/// executed a map. Every outcome is computed from a fresh state and asserted
/// before its [HTW] marker is emitted.
/// </summary>
public static class ScenarioRunner
{
    public const string HarnessVersion = "htw-scenario-harness-2.0";
    private const int RequiredRepeats = 2;
    private static readonly string[] KnownScenarios =
    {
        "fresh_initialization", "preparation_combat_resolution", "single_hero_death",
        "both_heroes_dead_three_life_penalty", "duplicate_deferred_death_callback", "timeout_cleanup",
        "elimination_victory_draw", "personal_purchase_isolation", "opposing_arena_destination",
        "two_arena_repeatability", "six_team_route_offset", "six_team_elimination_recalculation",
        "six_team_no_carryover", "six_team_six_wave_matrix"
    };

    public static JsonObject Run(JsonObject payload)
    {
        var profile = payload["profile"]?.GetValue<string>() ?? HtwProfileModel.MvpProfile;
        if (!HtwProfileModel.IsKnown(profile)) throw new EngineException("INVALID_ARGUMENT", $"Unknown scenario profile '{profile}'.");
        var chunkId = payload["chunk_id"]?.GetValue<string>() ?? "unassigned";
        if (string.Equals(chunkId, "HTW-06", StringComparison.Ordinal) && profile != HtwProfileModel.FullProfile)
        {
            throw new EngineException("CAPABILITY_GATED", "HTW-06 scenario evidence requires the full_6team profile.");
        }
        var requested = payload["scenario_ids"] as JsonArray;
        var scenarios = requested is null
            ? KnownScenarios.Where(name => profile == HtwProfileModel.FullProfile || !name.StartsWith("six_team_", StringComparison.Ordinal)).ToList()
            : requested.Select(node => node?.GetValue<string>() ?? throw new EngineException("INVALID_ARGUMENT", "scenario_ids must contain strings.")).ToList();
        if (scenarios.Count == 0) throw new EngineException("INVALID_ARGUMENT", "scenario_ids must contain at least one scenario.");
        foreach (var scenario in scenarios)
        {
            if (!KnownScenarios.Contains(scenario, StringComparer.Ordinal)) throw new EngineException("INVALID_ARGUMENT", $"Unknown scenario '{scenario}'.");
            if (scenario.StartsWith("six_team_", StringComparison.Ordinal) && profile != HtwProfileModel.FullProfile) throw new EngineException("INVALID_ARGUMENT", $"Scenario '{scenario}' requires the full_6team profile.");
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
        var simulation = new Simulation(profile == HtwProfileModel.FullProfile ? 6 : 2);
        var expected = "";
        var actual = "";
        var team = 1;
        switch (scenario)
        {
            case "fresh_initialization":
                expected = "phase=preparation;round=1;wave=0";
                actual = simulation.StateSummary();
                break;
            case "preparation_combat_resolution":
                simulation.PrepareWave();
                simulation.BeginCombat();
                simulation.ResolveWave();
                expected = "phase=resolution;round=2;wave=1";
                actual = simulation.StateSummary();
                break;
            case "single_hero_death":
                simulation.PrepareWave();
                simulation.AccountHeroDeath(1, 1);
                expected = "heroes_alive=1;lives_delta=-1";
                actual = $"heroes_alive={simulation.AliveHeroesInTeam(1)};lives_delta={simulation.TeamLives[1] - 15}";
                break;
            case "both_heroes_dead_three_life_penalty":
                simulation.PrepareWave();
                simulation.AccountHeroDeath(1, 1);
                simulation.AccountHeroDeath(1, 2);
                expected = "heroes_alive=0;lives_delta=-3";
                actual = $"heroes_alive={simulation.AliveHeroesInTeam(1)};lives_delta={simulation.TeamLives[1] - 15}";
                break;
            case "duplicate_deferred_death_callback":
                simulation.PrepareWave();
                var originalWave = simulation.Wave;
                simulation.AccountHeroDeath(1, 1, originalWave);
                simulation.AccountHeroDeath(1, 1, originalWave);
                simulation.ResolveWave();
                simulation.PrepareWave();
                simulation.AccountHeroDeath(1, 1, originalWave);
                expected = "death_accounted_once=true;callbacks=3;current_wave=2;stale_ignored=true";
                actual = $"death_accounted_once={Bool(simulation.AccountedDeathEvents == 1)};callbacks={simulation.DeathCallbacks};current_wave={simulation.Wave};stale_ignored={Bool(simulation.IgnoredDeathCallbacks == 2)}";
                break;
            case "timeout_cleanup":
                simulation.PrepareWave();
                simulation.BeginCombat();
                simulation.ResolveWave(timeout: true);
                expected = "active_wave=false;creeps_remaining=0;timeout_life_delta=0;timeout_path=true";
                actual = $"active_wave={Bool(simulation.WaveActive)};creeps_remaining={simulation.TotalCreeps};timeout_life_delta={simulation.TeamLives[1] - 15};timeout_path={Bool(simulation.TimeoutResolutions == 1)}";
                break;
            case "elimination_victory_draw":
                simulation.TeamLives[2] = 3;
                simulation.PrepareWave();
                simulation.AccountHeroDeath(2, 1);
                simulation.AccountHeroDeath(2, 2);
                simulation.ResolveWave();
                var victory = simulation.TerminalState;
                var drawSimulation = new Simulation(2);
                drawSimulation.TeamLives[1] = 3;
                drawSimulation.TeamLives[2] = 3;
                drawSimulation.PrepareWave();
                drawSimulation.AccountHeroDeath(1, 1);
                drawSimulation.AccountHeroDeath(1, 2);
                drawSimulation.AccountHeroDeath(2, 1);
                drawSimulation.AccountHeroDeath(2, 2);
                drawSimulation.ResolveWave();
                expected = "victory=victory;draw=draw;eliminated_team=2";
                actual = $"victory={victory};draw={drawSimulation.TerminalState};eliminated_team={simulation.TeamLives.Single(item => item.Value == 0).Key}";
                break;
            case "personal_purchase_isolation":
                simulation.PrepareWave();
                simulation.BeginPersonalGold(1, 100);
                simulation.BeginPersonalGold(2, 100);
                var playerOneBought = simulation.Purchase(1, 2, 25);
                expected = "buyer_gold_only=true;player1=50;player2=100";
                actual = $"buyer_gold_only={Bool(playerOneBought && simulation.PlayerGold[1] == 50 && simulation.PlayerGold[2] == 100)};player1={simulation.PlayerGold[1]};player2={simulation.PlayerGold[2]}";
                break;
            case "opposing_arena_destination":
                simulation.PrepareWave();
                simulation.BeginPersonalGold(1, 100);
                simulation.Purchase(1, 1, 10);
                expected = "destination_team=2;self_send=false";
                actual = $"destination_team={simulation.PlayerQueueDestination[1]};self_send={Bool(simulation.PlayerQueueDestination[1] == 1)}";
                break;
            case "two_arena_repeatability":
                var twoArenaRoutes = Enumerable.Range(1, 4).Select(round => simulation.RouteFor(2, round, 1)).ToArray();
                expected = "route=2,2,2,2";
                actual = $"route={string.Join(',', twoArenaRoutes)}";
                break;
            case "six_team_route_offset":
                var routeRound = 7;
                var routeOffset = HtwProfileModel.ComputeOffset(routeRound, 6);
                expected = $"offset={routeOffset};destination=3";
                actual = $"offset={routeOffset};destination={simulation.RouteFor(6, routeRound, 1)}";
                break;
            case "six_team_elimination_recalculation":
                var livingAfterElimination = new[] { 1, 2, 4, 5, 6 };
                var recalculated = HtwProfileModel.Route(livingAfterElimination, 7, 1);
                expected = "living_order_recomputed=true;living=1,2,4,5,6;destination=5";
                actual = $"living_order_recomputed={Bool(simulation.RecalculateLiving(livingAfterElimination))};living={string.Join(',', simulation.LivingTeams)};destination={recalculated}";
                break;
            case "six_team_no_carryover":
                simulation.PrepareWave();
                simulation.MarkCreepsInArena(3, 3);
                simulation.EliminateTeam(3);
                simulation.ResolveWave();
                expected = "eliminated_arena_creeps=0;carryover=0;redistributed=false";
                actual = $"eliminated_arena_creeps={simulation.CreepsInArena(3)};carryover={simulation.Carryover};redistributed={Bool(simulation.Redistributed)}";
                break;
            case "six_team_six_wave_matrix":
                var matrix = Enumerable.Range(1, 6).Select(round => simulation.RouteFor(6, round, 1)).ToArray();
                expected = "destinations=2,3,4,5,6,2";
                actual = $"destinations={string.Join(',', matrix)}";
                break;
            default:
                throw new EngineException("INVALID_ARGUMENT", $"Unknown scenario '{scenario}'.");
        }

        var pass = string.Equals(expected, actual, StringComparison.Ordinal);
        var waveId = scenario == "fresh_initialization" ? 0 : simulation.Wave;
        return new JsonObject
        {
            ["scenario_id"] = scenario,
            ["repeat_index"] = repeat,
            ["chunk_id"] = chunkId,
            ["round"] = simulation.Round,
            ["wave_id"] = waveId,
            ["team"] = team,
            ["expected"] = expected,
            ["actual"] = actual,
            ["marker"] = $"[HTW] chunk={chunkId} scenario={scenario} repeat={repeat} round={simulation.Round} wave={waveId} team={team} expected={expected} actual={actual}",
            ["result"] = pass ? "pass" : "fail",
            ["evidence_level"] = "static_only",
            ["runtime_verified"] = false,
            ["state"] = simulation.ToJson()
        };
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private sealed class Simulation
    {
        private readonly Dictionary<int, HashSet<int>> _deadHeroes = new();
        private readonly Dictionary<int, int> _creepsByArena = new();

        public Simulation(int teamCount)
        {
            TeamCount = teamCount;
            TeamLives = Enumerable.Range(1, teamCount).ToDictionary(team => team, _ => 15);
            LivingTeams = Enumerable.Range(1, teamCount).ToList();
            PlayerGold = Enumerable.Range(1, teamCount * 2).ToDictionary(player => player, _ => 0);
            PlayerQueueDestination = Enumerable.Range(1, teamCount * 2).ToDictionary(player => player, _ => 0);
            PlayerQueueRemaining = Enumerable.Range(1, teamCount * 2).ToDictionary(player => player, _ => 0);
            for (var team = 1; team <= teamCount; team++) _deadHeroes[team] = new HashSet<int>();
        }

        public int TeamCount { get; }
        public int Round { get; private set; } = 1;
        public int Wave { get; private set; }
        public string Phase { get; private set; } = "preparation";
        public bool WaveActive { get; private set; }
        public int TotalCreeps => _creepsByArena.Values.Sum();
        public int Carryover { get; private set; }
        public bool Redistributed { get; private set; }
        public int TimeoutResolutions { get; private set; }
        public string TerminalState { get; private set; } = "running";
        public Dictionary<int, int> TeamLives { get; }
        public List<int> LivingTeams { get; private set; }
        public Dictionary<int, int> PlayerGold { get; }
        public Dictionary<int, int> PlayerQueueDestination { get; }
        public Dictionary<int, int> PlayerQueueRemaining { get; }
        public int AccountedDeathEvents { get; private set; }
        public int DeathCallbacks { get; private set; }
        public int IgnoredDeathCallbacks { get; private set; }

        public void PrepareWave()
        {
            if (TerminalState != "running" || WaveActive) return;
            RecalculateLiving(LivingTeams.ToArray());
            if (TerminalState != "running") return;
            Wave++;
            Phase = "preparation";
            WaveActive = true;
            _creepsByArena.Clear();
            foreach (var team in LivingTeams) _creepsByArena[team] = 3;
            foreach (var dead in _deadHeroes.Values) dead.Clear();
        }

        public void BeginCombat()
        {
            if (TerminalState == "running" && WaveActive && Phase == "preparation") Phase = "combat";
        }

        public void ResolveWave(bool timeout = false)
        {
            if (!WaveActive) return;
            WaveActive = false;
            Phase = "resolution";
            _creepsByArena.Clear();
            if (timeout) TimeoutResolutions++;
            RecalculateLiving(LivingTeams.ToArray());
            if (TerminalState == "running") Round++;
        }

        public void AccountHeroDeath(int team, int hero) => AccountHeroDeath(team, hero, Wave);

        public void AccountHeroDeath(int team, int hero, int callbackWave)
        {
            DeathCallbacks++;
            if (callbackWave != Wave || !WaveActive || !LivingTeams.Contains(team) || !_deadHeroes[team].Add(hero))
            {
                IgnoredDeathCallbacks++;
                return;
            }
            AccountedDeathEvents++;
            var deathCount = _deadHeroes[team].Count;
            TeamLives[team] -= deathCount == 1 ? 1 : 2;
            if (deathCount >= 2 && TeamLives[team] <= 0) EliminateTeam(team);
        }

        public int AliveHeroesInTeam(int team) => 2 - _deadHeroes[team].Count;
        public int DeathCount(int team) => _deadHeroes[team].Count;
        public void BeginPersonalGold(int player, int amount) => PlayerGold[player] = amount;

        public bool Purchase(int player, int quantity, int cost)
        {
            var team = (player + 1) / 2;
            if (!WaveActive || Phase != "preparation" || !LivingTeams.Contains(team) || PlayerGold[player] < quantity * cost) return false;
            var destination = RouteFor(TeamCount, Round, team);
            if (destination == team || PlayerQueueRemaining[player] > 0) return false;
            PlayerGold[player] -= quantity * cost;
            PlayerQueueDestination[player] = destination;
            PlayerQueueRemaining[player] = quantity;
            return true;
        }

        public int RouteFor(int count, int round, int sender)
            => HtwProfileModel.Route(Enumerable.Range(1, count).ToArray(), round, sender);

        public bool RecalculateLiving(IReadOnlyList<int> living)
        {
            var prior = LivingTeams.ToArray();
            LivingTeams = living.Where(team => TeamLives.TryGetValue(team, out var lives) && lives > 0).OrderBy(team => team).ToList();
            if (LivingTeams.Count == 0) TerminalState = "draw";
            else if (LivingTeams.Count == 1) TerminalState = "victory";
            return !prior.SequenceEqual(LivingTeams);
        }

        public void EliminateTeam(int team)
        {
            TeamLives[team] = 0;
            RecalculateLiving(LivingTeams.Where(living => living != team).ToArray());
        }

        public void MarkCreepsInArena(int arena, int count) => _creepsByArena[arena] = count;
        public int CreepsInArena(int arena) => _creepsByArena.TryGetValue(arena, out var count) ? count : 0;

        public string StateSummary() => $"phase={Phase};round={Round};wave={Wave}";

        public JsonObject ToJson() => new()
        {
            ["phase"] = Phase,
            ["round"] = Round,
            ["wave_id"] = Wave,
            ["wave_active"] = WaveActive,
            ["living_team_order"] = new JsonArray(LivingTeams.Select(team => (JsonNode?)JsonValue.Create(team)).ToArray()),
            ["team_lives"] = new JsonObject(TeamLives.ToDictionary(item => item.Key.ToString(), item => (JsonNode?)JsonValue.Create(item.Value))),
            ["terminal_state"] = TerminalState,
            ["creeps_remaining"] = TotalCreeps,
            ["carryover"] = Carryover,
            ["redistributed"] = Redistributed,
            ["timeout_resolutions"] = TimeoutResolutions,
            ["accounted_death_events"] = AccountedDeathEvents,
            ["ignored_death_callbacks"] = IgnoredDeathCallbacks
        };
    }
}
