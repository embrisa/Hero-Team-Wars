using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Wc3MapEngine.Core.Gameplay;
using Wc3MapEngine.Core.Validation;
using Xunit;

namespace Wc3MapEngine.Tests.Gameplay;

public sealed class Phase5dPlayersTeamsForcesTests
{
    [Fact]
    public void CheckedInFullProfileFixtureHasTheRequiredShape()
    {
        var fixture = JsonUtilities.Read(FindFixture()) as JsonObject ?? throw new InvalidOperationException("The full-profile fixture is not a JSON object.");
        Assert.Equal(HtwProfileModel.FullProfile, fixture["profile"]!.GetValue<string>());
        Assert.Equal(12, fixture["players"]!.AsArray().Count);
        Assert.Equal(6, fixture["forces"]!.AsArray().Count);
        Assert.Equal(6, fixture["teams"]!.AsArray().Count);
        Assert.Equal(6, fixture["profile_spec"]!["team_ids"]!.AsArray().Count);
        Assert.Equal(6, fixture["team_registry"]!["living_team_order"]!.AsArray().Count);
    }

    [Fact]
    public void FullProfileBuildsTwelveSlotsSixForcesAndExplicitRegistry()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        model["profile"] = HtwProfileModel.FullProfile;
        model["profiles"] = HtwProfileModel.ProfilesDocument();
        model["profile_spec"] = HtwProfileModel.ProfileSpec(HtwProfileModel.FullProfile);
        model["teams"] = HtwProfileModel.DefaultTeams(HtwProfileModel.FullProfile);
        model["team_registry"] = HtwProfileModel.BuildTeamRegistry(model["teams"]!.AsArray());
        // The source map's native force 0 uses a signed mask with unused high
        // bits set. Those bits would become real assignments after adding
        // slots 5-12, so the full-profile fixture normalizes that mask first.
        model["forces"]![0]!["player_mask"] = 3;

        var operations = new JsonArray();
        for (var playerId = 5; playerId <= 12; playerId++)
        {
            operations.Add(new JsonObject
            {
                ["operation_id"] = Guid.NewGuid().ToString(),
                ["type"] = "create_player_slot",
                ["target"] = new JsonObject { ["id"] = playerId },
                ["value"] = new JsonObject
                {
                    ["name"] = $"Player {playerId}", ["controller"] = "User", ["race"] = "NightElf", ["flags"] = 1,
                    ["start"] = new JsonObject { ["x"] = playerId * 64, ["y"] = playerId * -64 },
                    ["ally_low_priority_mask"] = 0, ["ally_high_priority_mask"] = 0,
                    ["enemy_low_priority_mask"] = 0, ["enemy_high_priority_mask"] = 0
                },
                ["rationale"] = "Create the full-profile active slot."
            });
        }
        for (var forceIndex = 2; forceIndex <= 5; forceIndex++)
        {
            var firstPlayer = forceIndex * 2 + 1;
            operations.Add(new JsonObject
            {
                ["operation_id"] = Guid.NewGuid().ToString(),
                ["type"] = "create_force",
                ["target"] = new JsonObject { ["index"] = forceIndex },
                ["value"] = new JsonObject
                {
                    ["name"] = $"Force {forceIndex + 1}", ["flags"] = 9,
                    ["player_ids"] = new JsonArray(firstPlayer, firstPlayer + 1), ["player_mask"] = (1 << (firstPlayer - 1)) | (1 << firstPlayer)
                },
                ["rationale"] = "Create one full-profile force."
            });
        }

        var applied = OperationApplier.Apply(model, operations)["canonical_map"]!.AsObject();
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-phase5d", Guid.NewGuid().ToString("N"));
        var canonical = Path.Combine(directory, "full-profile.json");
        var output = Path.Combine(directory, "full-profile.w3m");
        try
        {
            Directory.CreateDirectory(directory);
            JsonUtilities.WriteAtomic(canonical, applied);
            var report = ValidationPipeline.ValidateCanonical(canonical, source, new JsonObject { ["project_id"] = "hero-team-wars", ["profile"] = HtwProfileModel.FullProfile });
            Assert.True(report["buildable"]!.GetValue<bool>(), report.ToJsonString());

            var build = MapBuilder.Build(source, canonical, output, HtwProfileModel.FullProfile, new JsonObject { ["project_id"] = "hero-team-wars", ["profile"] = HtwProfileModel.FullProfile });
            Assert.True(build["reopened"]!.GetValue<bool>());
            var reopened = MapInspector.Inspect(output);
            Assert.Equal(12, reopened["players"]!.AsArray().Count);
            Assert.Equal(6, reopened["forces"]!.AsArray().Count);
            Assert.Equal(6, applied["teams"]!.AsArray().Count);
            Assert.Equal(6, applied["team_registry"]!["living_team_order"]!.AsArray().Count);
            Assert.Equal("typed_write_enabled", reopened["component_status"]!["players"]!["capability"]!.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TeamForceContradictionsAndMissingAssignmentsAreReported()
    {
        var model = MapInspector.Inspect(FindSourceMap());
        model["profile"] = HtwProfileModel.MvpProfile;
        model["teams"] = new JsonArray(new JsonObject
        {
            ["id"] = "team_1", ["name"] = "Team 1", ["member_player_ids"] = new JsonArray(1, 3), ["force_index"] = 0,
            ["arena_id"] = "arena_a", ["hero_ids"] = new JsonArray(), ["life_state"] = "active", ["routing_state"] = "unassigned",
            ["provenance"] = "intended_design", ["capability"] = "typed_write_enabled"
        });
        model["forces"]![1]!["player_ids"] = new JsonArray(3);
        model["forces"]![1]!["player_mask"] = 4;
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-phase5d", Guid.NewGuid().ToString("N"));
        var canonical = Path.Combine(directory, "invalid.json");
        try
        {
            Directory.CreateDirectory(directory);
            JsonUtilities.WriteAtomic(canonical, model);
            var report = ValidationPipeline.ValidateCanonical(canonical, null, new JsonObject { ["project_id"] = "hero-team-wars", ["profile"] = HtwProfileModel.MvpProfile });
            var codes = report["findings"]!.AsArray().OfType<JsonObject>().Select(item => item["code"]!.GetValue<string>()).ToHashSet();
            Assert.Contains("PROFILE_TEAM_COUNT_INVALID", codes);
            Assert.Contains("TEAM_FORCE_MEMBERSHIP_CONTRADICTION", codes);
            Assert.Contains("PLAYER_FORCE_MISSING", codes);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PlayerForceAndTeamLifecycleRequiresExactPriorRecords()
    {
        var model = MapInspector.Inspect(FindSourceMap());
        model["profile"] = HtwProfileModel.MvpProfile;
        model["teams"] = HtwProfileModel.DefaultTeams(HtwProfileModel.MvpProfile);
        model["team_registry"] = HtwProfileModel.BuildTeamRegistry(model["teams"]!.AsArray());

        var player = new JsonObject
        {
            ["id"] = 5, ["name"] = "Player 5", ["controller"] = "User", ["race"] = "Human", ["flags"] = 1,
            ["start"] = new JsonObject { ["x"] = 512, ["y"] = 512 },
            ["ally_low_priority_mask"] = 0, ["ally_high_priority_mask"] = 0,
            ["enemy_low_priority_mask"] = 0, ["enemy_high_priority_mask"] = 0
        };
        var createdPlayer = player.DeepClone()!.AsObject();
        createdPlayer["observer"] = null;
        createdPlayer["locked"] = true;
        createdPlayer["slot_status"] = "active";
        createdPlayer["provenance"] = "intended_design";
        createdPlayer["capability"] = "typed_write_enabled";
        createdPlayer["codec_version"] = MapComponentCodec.CodecVersion;

        var force = new JsonObject
        {
            ["index"] = 2, ["name"] = "Force 3", ["flags"] = 9,
            ["player_ids"] = new JsonArray(5), ["player_mask"] = 16
        };
        var createdForce = force.DeepClone()!.AsObject();
        createdForce["alliance"] = true;
        createdForce["shared_vision"] = true;
        createdForce["shared_unit_control"] = false;
        createdForce["provenance"] = "intended_design";
        createdForce["capability"] = "typed_write_enabled";
        createdForce["codec_version"] = MapComponentCodec.CodecVersion;

        var team = new JsonObject
        {
            ["id"] = "team_3", ["name"] = "Team 3", ["member_player_ids"] = new JsonArray(5), ["force_index"] = 2,
            ["arena_id"] = "arena_3", ["hero_ids"] = new JsonArray(), ["life_state"] = "active", ["routing_state"] = "unassigned"
        };
        var createdTeam = team.DeepClone()!.AsObject();
        createdTeam["provenance"] = "intended_design";
        createdTeam["capability"] = "typed_write_enabled";
        var arenaChangedTeam = createdTeam.DeepClone()!.AsObject();
        arenaChangedTeam["arena_id"] = "arena_3_alt";
        var membersChangedTeam = arenaChangedTeam.DeepClone()!.AsObject();
        membersChangedTeam["member_player_ids"] = new JsonArray(5);
        var unlockedPlayer = createdPlayer.DeepClone()!.AsObject();
        unlockedPlayer["flags"] = 0;
        unlockedPlayer["locked"] = false;
        var sharedControlForce = createdForce.DeepClone()!.AsObject();
        sharedControlForce["flags"] = 25;
        sharedControlForce["shared_unit_control"] = true;

        var result = OperationApplier.Apply(model, new JsonArray(
            Operation("create_player_slot", new JsonObject { ["id"] = 5 }, null, player),
            Operation("create_force", new JsonObject { ["index"] = 2 }, null, force),
            Operation("create_team", new JsonObject { ["id"] = "team_3" }, null, team),
            Operation("set_player_slot", new JsonObject { ["id"] = 5 }, createdPlayer, new JsonObject { ["locked"] = false }),
            Operation("set_force", new JsonObject { ["index"] = 2 }, createdForce, new JsonObject { ["shared_unit_control"] = true }),
            Operation("set_team_arena", new JsonObject { ["id"] = "team_3" }, createdTeam, new JsonObject { ["arena_id"] = "arena_3_alt" }),
            Operation("set_team_members", new JsonObject { ["id"] = "team_3" }, arenaChangedTeam, new JsonObject { ["member_player_ids"] = new JsonArray(5) }),
            Operation("delete_team", new JsonObject { ["id"] = "team_3" }, membersChangedTeam, null),
            Operation("delete_force", new JsonObject { ["index"] = 2 }, sharedControlForce, null),
            Operation("delete_player_slot", new JsonObject { ["id"] = 5 }, unlockedPlayer, null)))["canonical_map"]!.AsObject();

        Assert.DoesNotContain(result["players"]!.AsArray().OfType<JsonObject>(), item => item["id"]!.GetValue<int>() == 5);
        Assert.DoesNotContain(result["forces"]!.AsArray().OfType<JsonObject>(), item => item["index"]!.GetValue<int>() == 2);
        Assert.DoesNotContain(result["teams"]!.AsArray().OfType<JsonObject>(), item => item["id"]!.GetValue<string>() == "team_3");

        var stale = Operation("set_team_arena", new JsonObject { ["id"] = "team_1" }, new JsonObject { ["id"] = "team_1" }, new JsonObject { ["arena_id"] = "arena_1_alt" });
        var exception = Assert.Throws<EngineException>(() => OperationApplier.Apply(model, new JsonArray(stale)));
        Assert.Equal("PRECONDITION_FAILED", exception.Code);
    }

    [Fact]
    public void SixTeamScenarioUsesSixWaveLivingOrderAndNoCarryover()
    {
        var report = ScenarioRunner.Run(new JsonObject
        {
            ["profile"] = HtwProfileModel.FullProfile,
            ["chunk_id"] = "HTW-06",
            ["scenario_ids"] = new JsonArray("six_team_six_wave_matrix", "six_team_elimination_recalculation", "six_team_no_carryover")
        });

        Assert.Equal(6, report["scenario_count"]!.GetValue<int>());
        Assert.Equal(6, report["passed_count"]!.GetValue<int>());
        var matrix = report["results"]!.AsArray().OfType<JsonObject>().First(item => item["scenario_id"]!.GetValue<string>() == "six_team_six_wave_matrix");
        Assert.Equal("destinations=2,3,4,5,6,2", matrix["actual"]!.GetValue<string>());
        var elimination = report["results"]!.AsArray().OfType<JsonObject>().First(item => item["scenario_id"]!.GetValue<string>() == "six_team_elimination_recalculation");
        Assert.Contains("living=1,2,4,5,6", elimination["actual"]!.GetValue<string>());
    }

    [Fact]
    public void GeneratedTeamIdsDoNotDependOnPlayerColor()
    {
        var manifest = GameplaySourceComposer.Compose(FindManifest(), HtwProfileModel.FullProfile);
        var source = manifest["source"]!.GetValue<string>();
        Assert.Contains("HTW_Teams_ConfigureProfile", source);
        Assert.Contains("HTW_TeamStableId[1]", source);
        Assert.Contains("team_6", source);
        Assert.DoesNotContain("GetPlayerColor", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedLivingOrderCompactsAfterElimination()
    {
        var manifest = GameplaySourceComposer.Compose(FindManifest(), HtwProfileModel.FullProfile);
        var canonical = manifest["canonical_model"]!.AsObject();
        canonical["teams"]!.AsArray()[2]! ["life_state"] = "eliminated";
        var recomposed = GameplaySourceComposer.ComposeCanonical(canonical, HtwProfileModel.FullProfile);
        var source = recomposed["source"]!.GetValue<string>();

        Assert.Contains("set HTW_LivingTeamCount = 5", source);
        Assert.Contains("set HTW_LivingTeamIds[1] = 1", source);
        Assert.Contains("set HTW_LivingTeamIds[2] = 2", source);
        Assert.Contains("set HTW_LivingTeamIds[3] = 4", source);
        Assert.DoesNotContain("set HTW_LivingTeamIds[3] = 3", source);
    }

    private static string FindSourceMap()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var candidate = Path.Combine(current.FullName, "map", "HeroTeamWars_M0_2Arena.w3m");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("The local Hero Team Wars source fixture was not found.");
    }

    private static string FindManifest()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var candidate = Path.Combine(current.FullName, "tools", "wc3-map-mcp", "scripts", "mcp", "manifest.json");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("The local Hero Team Wars gameplay manifest was not found.");
    }

    private static string FindFixture()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var candidate = Path.Combine(current.FullName, "tools", "wc3-map-mcp", "tests", "fixtures", "expected", "phase5d-full-6team-profile.json");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("The checked-in full-profile fixture was not found.");
    }

    private static JsonObject Operation(string type, JsonObject target, JsonNode? expected, JsonNode? value)
    {
        var operation = new JsonObject
        {
            ["operation_id"] = Guid.NewGuid().ToString(),
            ["type"] = type,
            ["target"] = target,
            ["rationale"] = $"Exercise {type}."
        };
        if (expected is not null) operation["expected"] = expected.DeepClone();
        if (value is not null) operation["value"] = value.DeepClone();
        return operation;
    }
}
