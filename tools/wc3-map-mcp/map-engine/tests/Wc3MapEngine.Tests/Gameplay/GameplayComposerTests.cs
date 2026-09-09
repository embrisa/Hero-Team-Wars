using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Wc3MapEngine.Core.Gameplay;
using Xunit;

namespace Wc3MapEngine.Tests.Gameplay;

public sealed class GameplayComposerTests
{
    [Fact]
    public void ComposerIsDeterministicAndStaticOnly()
    {
        var manifest = FindManifest();
        var first = GameplaySourceComposer.Compose(manifest);
        var second = GameplaySourceComposer.Compose(manifest);

        Assert.Equal(first["source_sha256"]!.GetValue<string>(), second["source_sha256"]!.GetValue<string>());
        Assert.Equal(first["manifest_sha256"]!.GetValue<string>(), second["manifest_sha256"]!.GetValue<string>());
        Assert.Equal(first["source"]!.GetValue<string>(), second["source"]!.GetValue<string>());
        Assert.Equal("static_only", first["static_validation"]!["evidence_level"]!.GetValue<string>());
        Assert.Equal(1, first["main_count"]!.GetValue<int>());
        Assert.Equal(30, first["module_order"]!.AsArray().Count);
        Assert.Matches(@"(?m)^    framehandle array HTW_HudRoot\r?$", first["source"]!.GetValue<string>());
        var hudRoot = first["canonical_model"]!["gameplay_variables"]!.AsArray().OfType<JsonObject>().Single(variable => variable["name"]!.GetValue<string>() == "HTW_HudRoot");
        Assert.True(hudRoot["array"]!.GetValue<bool>());
        Assert.Equal(5, hudRoot["array_size"]!.GetValue<int>());
        Assert.Contains("function config takes nothing returns nothing", first["source"]!.GetValue<string>());
        Assert.Contains("call SetPlayers(4)", first["source"]!.GetValue<string>());
        Assert.DoesNotContain("SetUnitStock", first["source"]!.GetValue<string>());
        Assert.DoesNotContain("AddUnitToStock", first["source"]!.GetValue<string>());
        Assert.DoesNotContain("RemoveUnitFromStock", first["source"]!.GetValue<string>());
        Assert.Contains("call FogEnable(false)", first["source"]!.GetValue<string>());
        Assert.Contains("call FogMaskEnable(false)", first["source"]!.GetValue<string>());
        Assert.DoesNotContain("FogModifier", first["source"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("multiboard")]
    [InlineData("framehandle")]
    public void ComposerPreservesTypedHandleScalarsAndArrayMetadata(string type)
    {
        var canonical = GameplaySourceComposer.Compose(FindManifest())["canonical_model"]!.AsObject();
        canonical["gameplay_variables"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "test_hud_root", ["name"] = "TestHudRoot", ["type"] = type, ["array"] = true, ["array_size"] = 5
        });
        canonical["gameplay_variables"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "test_hud_scalar", ["name"] = "TestHudScalar", ["type"] = type, ["initial"] = null
        });

        var composed = GameplaySourceComposer.ComposeCanonical(canonical);
        var source = composed["source"]!.GetValue<string>();

        Assert.Matches($@"(?m)^    {type} array TestHudRoot\r?$", source);
        Assert.Matches($@"(?m)^    {type} TestHudScalar = null\r?$", source);
        Assert.DoesNotContain("set TestHudRoot", source);
        Assert.DoesNotContain("set TestHudScalar", source);
        var variables = composed["canonical_model"]!["gameplay_variables"]!.AsArray().OfType<JsonObject>();
        var array = variables.Single(variable => variable["id"]!.GetValue<string>() == "test_hud_root");
        Assert.Equal(type, array["type"]!.GetValue<string>());
        Assert.True(array["array"]!.GetValue<bool>());
        Assert.Equal(5, array["array_size"]!.GetValue<int>());
    }

    [Fact]
    public void ComposerInitializesEverySupportedScalarTypeButLeavesArraysBare()
    {
        var scalarTypes = new (string Type, string Initializer)[]
        {
            ("integer", "0"),
            ("real", "0."),
            ("boolean", "false"),
            ("string", "\"\""),
            ("handle", "null"),
            ("timer", "null"),
            ("trigger", "null"),
            ("unit", "null"),
            ("group", "null"),
            ("region", "null"),
            ("rect", "null"),
            ("player", "null"),
            ("force", "null"),
            ("fogmodifier", "null"),
            ("multiboard", "null"),
            ("framehandle", "null")
        };
        var canonical = GameplaySourceComposer.Compose(FindManifest())["canonical_model"]!.AsObject();
        foreach (var (type, _) in scalarTypes)
        {
            canonical["gameplay_variables"]!.AsArray().Add(new JsonObject
            {
                ["id"] = $"scalar_{type}", ["name"] = $"TestScalar_{type}", ["type"] = type
            });
            canonical["gameplay_variables"]!.AsArray().Add(new JsonObject
            {
                ["id"] = $"array_{type}", ["name"] = $"TestArray_{type}", ["type"] = type, ["array"] = true, ["array_size"] = 3
            });
        }

        var source = GameplaySourceComposer.ComposeCanonical(canonical)["source"]!.GetValue<string>();
        var globals = GlobalsBlock(source);
        foreach (var (type, initializer) in scalarTypes)
        {
            Assert.Contains($"    {type} TestScalar_{type} = {initializer}\n", globals);
            Assert.Contains($"    {type} array TestArray_{type}\n", globals);
            Assert.DoesNotContain($"TestArray_{type} =", globals);
        }
    }

    [Fact]
    public void ComposerInitializesBuiltInEventAndDevHudHandleScalars()
    {
        var source = GameplaySourceComposer.Compose(FindManifest())["source"]!.GetValue<string>();
        var globals = GlobalsBlock(source);

        foreach (var declaration in new[]
        {
            "integer HTW_Round = 0",
            "integer HTW_Wave = 0",
            "integer HTW_Phase = 0",
            "integer HTW_TeamCount = 0",
            "integer HTW_ActivePlayerCount = 0",
            "integer HTW_ArenaCount = 0",
            "integer HTW_LivingTeamCount = 0",
            "integer HTW_RouteOffset = 0",
            "integer HTW_RouteDestinationTeam = 0",
            "boolean HTW_RoutingLocked = false",
            "rect HTW_ArenaRectA = null",
            "rect HTW_ArenaRectB = null",
            "real HTW_Event_round_start = 0.",
            "real HTW_Event_wave_resolved = 0.",
            "trigger HTW_DevChatTrigger = null",
            "trigger HTW_DevUILoad = null",
            "trigger HTW_HudLoadTrigger = null"
        }) Assert.Contains($"    {declaration}\n", globals);

        Assert.DoesNotContain("call ", globals);
        Assert.Contains("    framehandle array HTW_HudRoot\n", globals);
        Assert.DoesNotContain("HTW_HudRoot =", globals);
    }

    [Fact]
    public void ComposerRetainsManifestInitialValuesInInitializationFunction()
    {
        var source = GameplaySourceComposer.Compose(FindManifest())["source"]!.GetValue<string>();
        var start = source.IndexOf("function HTW_MCP_InitializeVariables", StringComparison.Ordinal);
        var end = source.IndexOf("endfunction", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var initializer = source[start..end];

        Assert.Contains("set HTW_DevEnabled = true", initializer);
        Assert.Contains("set HTW_DevUsed = false", initializer);
        Assert.Contains("set HTW_DevClockPhase = 0", initializer);
        Assert.Contains("set HTW_DevClockRemaining = 0", initializer);
        Assert.Contains("set HTW_InformationReady = false", initializer);
        Assert.DoesNotContain("set HTW_DevChatTrigger", initializer);
        Assert.DoesNotContain("set HTW_DevUILoad", initializer);
        Assert.DoesNotContain("set HTW_HudLoadTrigger", initializer);
    }

    [Theory]
    [InlineData("multiboard")]
    [InlineData("framehandle")]
    public void GameplayHandleVariablesKeepScalarAndArrayValidation(string type)
    {
        var scalar = new JsonObject { ["id"] = "hud", ["name"] = "TestHud", ["type"] = type };
        GameplayModelValidator.ValidateVariable(scalar);
        foreach (var field in new[] { "initial", "default_value", "value" })
        {
            var candidate = scalar.DeepClone().AsObject();
            candidate[field] = null;
            GameplayModelValidator.ValidateVariable(candidate);
            foreach (var literal in new[] { "0", "true", "\"null\"", "{}", "[]" })
            {
                candidate[field] = JsonNode.Parse(literal);
                Assert.Equal("INVALID_ARGUMENT", Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateVariable(candidate)).Code);
            }
        }

        var array = scalar.DeepClone().AsObject();
        array["array"] = true;
        foreach (var size in new[] { 1, 5, 8191 })
        {
            array["array_size"] = size;
            GameplayModelValidator.ValidateVariable(array);
        }
        foreach (var size in new[] { "null", "0", "8192", "1.5", "\"5\"" })
        {
            array["array_size"] = JsonNode.Parse(size);
            Assert.Equal("INVALID_ARGUMENT", Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateVariable(array)).Code);
        }
        array.Remove("array_size");
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateVariable(array)).Code);
        array["array_size"] = 5;
        foreach (var field in new[] { "initial", "default_value", "value" })
        {
            array[field] = null;
            GameplayModelValidator.ValidateVariable(array);
            array[field] = new JsonObject { ["variable_id"] = "hud" };
            var exception = Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateVariable(array));
            Assert.Equal("INVALID_ARGUMENT", exception.Code);
            Assert.Contains("cannot declare a scalar initial value", exception.Message);
            array.Remove(field);
        }
        array["array"] = "true";
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateVariable(array)).Code);
        scalar["unexpected"] = true;
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateVariable(scalar)).Code);
        scalar.Remove("unexpected");
        scalar["type"] = "imaginaryframehandle";
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateVariable(scalar)).Code);
    }

    [Fact]
    public void ComposerDeploysPurchasedHeroesOnceAtTheirTeamArenaCenter()
    {
        var source = GameplaySourceComposer.Compose(FindManifest())["source"]!.GetValue<string>();

        Assert.Contains("set x = GetRectCenterX(HTW_ArenaRect[teamIndex])", source);
        Assert.Contains("set y = GetRectCenterY(HTW_ArenaRect[teamIndex])", source);
        Assert.Contains("call SetUnitPosition(HTW_HeroUnitByPlayer[playerId], x, y)", source);
        Assert.Contains("call PanCameraToTimed(x, y, 0.)", source);
        Assert.Single(Regex.Matches(source, @"(?m)^\s*call HTW_HeroSelection_DeployHero\(playerId\)\s*$"));
        Assert.DoesNotContain("SetUnitPosition(HTW_HeroUnitByPlayer[playerId], x + I2R(playerId * 64), y + I2R(playerId * 48))", source);
        Assert.DoesNotContain("PanCameraToTimed(x + I2R(playerId * 64), y + I2R(playerId * 48), 0.)", source);
        Assert.DoesNotContain("HTW_HeroSelectionFog", source);
    }

    [Fact]
    public void CanonicalCompositionEmitsMapBoundLobbyConfig()
    {
        var composed = GameplaySourceComposer.Compose(FindManifest(), HtwProfileModel.MvpProfile);
        var canonical = MapInspector.Inspect(FindSourceMap());
        var gameplay = composed["canonical_model"]!.AsObject();
        foreach (var field in new[] { "gameplay_modules", "gameplay_variables", "gameplay_triggers", "regions", "region_roles", "teams" })
        {
            canonical[field] = gameplay[field]!.DeepClone();
        }
        canonical["profile"] = HtwProfileModel.MvpProfile;

        var rebound = GameplaySourceComposer.ComposeCanonical(canonical, HtwProfileModel.MvpProfile);
        var source = rebound["source"]!.GetValue<string>();

        Assert.Contains("function config takes nothing returns nothing", source);
        Assert.Contains("call SetCameraBounds(-3328. + GetCameraMargin(CAMERA_MARGIN_LEFT), -3584. + GetCameraMargin(CAMERA_MARGIN_BOTTOM), 3328. - GetCameraMargin(CAMERA_MARGIN_RIGHT), 3072. - GetCameraMargin(CAMERA_MARGIN_TOP), -3328. + GetCameraMargin(CAMERA_MARGIN_LEFT), 3072. - GetCameraMargin(CAMERA_MARGIN_TOP), 3328. - GetCameraMargin(CAMERA_MARGIN_RIGHT), -3584. + GetCameraMargin(CAMERA_MARGIN_BOTTOM))", source);
        Assert.Contains("call SetDayNightModels(\"Environment\\\\DNC\\\\DNCLordaeron\\\\DNCLordaeronTerrain\\\\DNCLordaeronTerrain.mdl\", \"Environment\\\\DNC\\\\DNCLordaeron\\\\DNCLordaeronUnit\\\\DNCLordaeronUnit.mdl\")", source);
        Assert.Contains("call NewSoundEnvironment(\"Default\")", source);
        Assert.Contains("call SetAmbientDaySound(\"IceCrownDay\")", source);
        Assert.Contains("call SetAmbientNightSound(\"IceCrownNight\")", source);
        Assert.Contains("call SetMapMusic(\"Music\", true, 0)", source);
        Assert.Contains("call InitBlizzard()", source);
        Assert.True(source.IndexOf("call SetCameraBounds(", StringComparison.Ordinal) < source.IndexOf("call HTW_MCP_InitializeVariables()", StringComparison.Ordinal));
        Assert.True(source.IndexOf("call InitBlizzard()", StringComparison.Ordinal) < source.IndexOf("call HTW_MCP_InitializeVariables()", StringComparison.Ordinal));
        Assert.Single(Regex.Matches(source, @"(?im)^\s*function\s+config\s+takes\s+nothing\s+returns\s+nothing\b").Cast<Match>());
        Assert.Contains("call SetPlayers(4)", source);
        Assert.Contains("call SetTeams(4)", source);
        Assert.Equal(4, Regex.Matches(source, @"call DefineStartLocation\(").Count);
        Assert.Contains("call DefineStartLocation(0, -1856., 1728.)", source);
        Assert.Contains("call DefineStartLocation(1, -640., 1856.)", source);
        Assert.Contains("call DefineStartLocation(2, 1664., -960.)", source);
        Assert.Contains("call DefineStartLocation(3, 2240., -960.)", source);
        Assert.Equal(4, Regex.Matches(source, @"call SetPlayerController\(Player\([0-3]\), MAP_CONTROL_USER\)").Count);
        Assert.Equal(4, Regex.Matches(source, @"call SetPlayerColor\(Player\([0-3]\), ConvertPlayerColor\([0-3]\)\)").Count);
        Assert.Equal(4, Regex.Matches(source, @"call ForcePlayerStartLocation\(Player\([0-3]\), [0-3]\)").Count);
        Assert.Contains("call SetPlayerTeam(Player(0), 0)", source);
        Assert.Contains("call SetPlayerTeam(Player(2), 1)", source);
        Assert.Contains("call SetPlayerAllianceStateAllyBJ(Player(0), Player(1), true)", source);
        Assert.Contains("call SetPlayerAllianceStateVisionBJ(Player(2), Player(3), true)", source);
        Assert.Contains("call SetStartLocPrio(0, 0, 1, MAP_LOC_PRIO_HIGH)", source);
        Assert.Contains("call SetStartLocPrio(2, 0, 3, MAP_LOC_PRIO_HIGH)", source);
    }

    [Fact]
    public void CanonicalConfigEscapesTextAndMapsControllerRaceAndFixedStart()
    {
        var composed = GameplaySourceComposer.Compose(FindManifest(), HtwProfileModel.MvpProfile);
        var canonical = MapInspector.Inspect(FindSourceMap());
        var gameplay = composed["canonical_model"]!.AsObject();
        foreach (var field in new[] { "gameplay_modules", "gameplay_variables", "gameplay_triggers", "regions", "region_roles", "teams" })
        {
            canonical[field] = gameplay[field]!.DeepClone();
        }
        canonical["metadata"]!.AsArray().OfType<JsonObject>().Single(item => item["field"]!.GetValue<string>() == "title")["value"] = "Map \"quoted\" \\ title\nnext";
        canonical["metadata"]!.AsArray().OfType<JsonObject>().Single(item => item["field"]!.GetValue<string>() == "description")["value"] = "Description \"quoted\"";
        var player = canonical["players"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<int>() == 1);
        player["controller"] = "Computer";
        player["race"] = "Human";
        player["flags"] = 0;
        player["fixed_start_position"] = false;

        var source = GameplaySourceComposer.ComposeCanonical(canonical, HtwProfileModel.MvpProfile)["source"]!.GetValue<string>();

        Assert.Contains("call SetMapName(\"Map \\\"quoted\\\" \\\\ title\\nnext\")", source);
        Assert.Contains("call SetMapDescription(\"Description \\\"quoted\\\"\")", source);
        Assert.Contains("call SetPlayerColor(Player(0), ConvertPlayerColor(0))", source);
        Assert.Contains("call SetPlayerRacePreference(Player(0), RACE_PREF_HUMAN)", source);
        Assert.Contains("call SetPlayerRaceSelectable(Player(0), false)", source);
        Assert.Contains("call SetPlayerController(Player(0), MAP_CONTROL_COMPUTER)", source);
        Assert.DoesNotContain("call ForcePlayerStartLocation(Player(0), 0)", source);
    }

    [Fact]
    public void ComposerOrdersEveryMcpFunctionBeforeItsUses()
    {
        var composed = GameplaySourceComposer.Compose(FindManifest());
        var source = composed["source"]!.GetValue<string>();
        var lines = source.Split('\n');
        var declarations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < lines.Length; index++)
        {
            var declaration = Regex.Match(lines[index], @"^\s*function\s+(HTW_[A-Za-z0-9_]+)\s+takes\b", RegexOptions.IgnoreCase);
            if (declaration.Success) declarations[declaration.Groups[1].Value] = index + 1;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var code = Regex.Replace(lines[index], "//.*$", string.Empty);
            foreach (Match reference in Regex.Matches(code, @"\bfunction\s+(?<callback>[A-Za-z_][A-Za-z0-9_]*)\b|(?<![A-Za-z0-9_])(?<call>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.IgnoreCase))
            {
                var name = reference.Groups["callback"].Success ? reference.Groups["callback"].Value : reference.Groups["call"].Value;
                if (declarations.TryGetValue(name, out var declarationLine))
                {
                    Assert.True(declarationLine <= index + 1, $"{name} is used on line {index + 1} before its declaration on line {declarationLine}.");
                }
            }
        }

        var functionOrder = composed["function_order"]!.AsArray().OfType<JsonObject>().Select(item => item["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(functionOrder.Length, functionOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        for (var index = 1; index < functionOrder.Length; index++)
        {
            Assert.True(
                declarations[functionOrder[index - 1]] < declarations[functionOrder[index]],
                $"function_order is not reflected in emitted source at '{functionOrder[index - 1]}' -> '{functionOrder[index]}'.");
        }
    }

    [Fact]
    public void ComposerRejectsMcpFunctionCallArityMismatch()
    {
        var composed = GameplaySourceComposer.Compose(FindManifest());
        var canonical = composed["canonical_model"]!.AsObject().DeepClone()!.AsObject();
        var heroes = canonical["gameplay_modules"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == "systems.heroes");
        heroes["source"] = heroes["source"]!.GetValue<string>().Replace("call HTW_Lives_AccountDeath()", "call HTW_Lives_AccountDeath(1)", StringComparison.Ordinal);
        heroes["source_sha256"] = Hashing.Sha256(System.Text.Encoding.UTF8.GetBytes(heroes["source"]!.GetValue<string>()));

        var exception = Assert.Throws<EngineException>(() => GameplaySourceComposer.ComposeCanonical(canonical));

        Assert.Equal("INVALID_ARGUMENT", exception.Code);
        Assert.Contains("HTW_Lives_AccountDeath", exception.Message);
        Assert.Contains("takes 0", exception.Message);
    }

    [Fact]
    public void ComposerBindsStableRegionIdsToGeneratedRectHandles()
    {
        var composed = GameplaySourceComposer.Compose(FindManifest());
        var source = composed["source"]!.GetValue<string>();

        Assert.Contains("region HTW_Region_region_0", source);
        Assert.Contains("set HTW_Region_region_0 = CreateRegion()", source);
        Assert.Contains("RegionAddRect(HTW_Region_region_0, Rect(1152., -3072., 2496., -448.))", source);
        Assert.DoesNotContain("gg_rct_Arena_A", source);
        Assert.Equal("HTW_Region_region_0", composed["region_handles"]!["region:0"]!.GetValue<string>());
        Assert.Equal(10, composed["regions"]!.AsArray().Count);
    }

    [Fact]
    public void ComposerExpandsFullProfileToTwelvePlayersSixTeamsAndSixArenas()
    {
        var composed = GameplaySourceComposer.Compose(FindManifest(), "full_6team");
        var source = composed["source"]!.GetValue<string>();

        Assert.Equal("full_6team", composed["profile"]!.GetValue<string>());
        Assert.Equal(12, composed["teams"]!.AsArray().SelectMany(team => team!["member_player_ids"]!.AsArray()).Count());
        Assert.Equal(6, composed["region_roles"]!.AsArray().Count(item => item!["role"]!.GetValue<string>() == "arena"));
        Assert.Contains("set HTW_ActivePlayerCount = 12", source);
        Assert.Contains("set HTW_ArenaCount = 6", source);
        Assert.DoesNotContain("for i = 1 to 4", source);
        Assert.DoesNotContain("for i = 1 to 2", source);
        Assert.Equal(JassNativeCatalogue.Version, composed["native_catalogue_version"]!.GetValue<string>());
    }

    [Fact]
    public void ScenarioHarnessUsesLockedSixTeamOffset()
    {
        var report = ScenarioRunner.Run(new JsonObject
        {
            ["profile"] = "full_6team",
            ["chunk_id"] = "HTW-06",
            ["scenario_ids"] = new JsonArray("six_team_route_offset", "six_team_elimination_recalculation", "six_team_no_carryover")
        });

        Assert.Equal(6, report["passed_count"]!.GetValue<int>());
        Assert.Equal(2, report["repeat_count"]!.GetValue<int>());
        var route = report["results"]!.AsArray().OfType<JsonObject>().First(item => item["scenario_id"]!.GetValue<string>() == "six_team_route_offset");
        Assert.Contains("offset=2", route["expected"]!.GetValue<string>());
        Assert.Equal("static_only", report["evidence_level"]!.GetValue<string>());
        Assert.False(report["runtime_verified"]!.GetValue<bool>());
    }

    [Fact]
    public void ScenarioHarnessCoversMvpRuntimePathsTwice()
    {
        var report = ScenarioRunner.Run(new JsonObject
        {
            ["profile"] = HtwProfileModel.MvpProfile,
            ["chunk_id"] = "HTW-05"
        });

        Assert.Equal(20, report["scenario_count"]!.GetValue<int>());
        Assert.Equal(20, report["passed_count"]!.GetValue<int>());
        Assert.Equal(0, report["failed_count"]!.GetValue<int>());
        Assert.Equal(2, report["repeat_count"]!.GetValue<int>());
        foreach (var result in report["results"]!.AsArray().OfType<JsonObject>())
        {
            Assert.Contains("chunk=HTW-05", result["marker"]!.GetValue<string>());
            Assert.Contains("expected=", result["marker"]!.GetValue<string>());
            Assert.Contains("actual=", result["marker"]!.GetValue<string>());
            Assert.Equal("pass", result["result"]!.GetValue<string>());
        }
    }

    [Fact]
    public void GameplayOperationRegeneratesEntryPointFromCanonicalSourceModel()
    {
        var composed = GameplaySourceComposer.Compose(FindManifest());
        var canonical = composed["canonical_model"]!.AsObject().DeepClone()!.AsObject();
        canonical["profile"] = "mvp_2arena";
        const string before = "function main takes nothing returns nothing\nendfunction\n";
        canonical["scripts"] = new JsonArray(new JsonObject
        {
            ["archive_path"] = "war3map.j",
            ["language"] = "Jass",
            ["source"] = before,
            ["source_sha256"] = Hashing.Sha256(System.Text.Encoding.UTF8.GetBytes(before)),
            ["sha256"] = Hashing.Sha256(System.Text.Encoding.UTF8.GetBytes(before)),
            ["size_bytes"] = System.Text.Encoding.UTF8.GetByteCount(before)
        });
        var phase = canonical["gameplay_variables"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == "phase");
        var operations = new JsonArray(new JsonObject
        {
            ["operation_id"] = "c0a80101-0000-4000-8000-000000000010",
            ["type"] = "update_variable",
            ["target"] = new JsonObject { ["id"] = "phase" },
            ["expected"] = phase.DeepClone(),
            ["value"] = new JsonObject { ["initial"] = 2 },
            ["rationale"] = "Exercise source-owned variable regeneration."
        });

        var result = OperationApplier.Apply(canonical, operations);
        var applied = result["canonical_map"]!.AsObject();
        var scriptChanges = result["diff"]!["changes"]!.AsArray().OfType<JsonObject>()
            .Where(change => change["component"]!.GetValue<string>() == "scripts").ToArray();
        Assert.NotEmpty(scriptChanges);
        Assert.All(scriptChanges, change => Assert.Equal(operations[0]!["operation_id"]!.GetValue<string>(), change["operation_id"]!.GetValue<string>()));
        Assert.Contains(scriptChanges, change => change["before"]?.ToJsonString().Contains(canonical["scripts"]![0]!["source_sha256"]!.GetValue<string>()) == true);

        Assert.Equal(2, applied["gameplay_variables"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == "phase")["initial"]!.GetValue<int>());
        Assert.Contains("set HTW_Phase = 2", applied["scripts"]![0]!["source"]!.GetValue<string>());
        Assert.Equal(applied["scripts"]![0]!["source_sha256"]!.GetValue<string>(), applied["gameplay_source"]!["source_sha256"]!.GetValue<string>());
    }

    [Fact]
    public void OperationBatchRejectsNonObjectEntriesInsteadOfDroppingThem()
    {
        var exception = Assert.Throws<EngineException>(() => OperationApplier.Apply(new JsonObject(), new JsonArray(JsonValue.Create("invalid operation"))));
        Assert.Equal("INVALID_ARGUMENT", exception.Code);
        Assert.Throws<EngineException>(() => OperationApplier.Apply(new JsonObject(), new JsonArray()));
    }

    [Fact]
    public void GameplayValidatorRejectsDuplicateModuleFunctions()
    {
        var root = new JsonObject
        {
            ["gameplay_modules"] = new JsonArray(
                new JsonObject { ["id"] = "one", ["source"] = "function HTW_MCP_Bootstrap takes nothing returns nothing\nendfunction\n" },
                new JsonObject { ["id"] = "two", ["source"] = "function HTW_MCP_Bootstrap takes nothing returns nothing\nendfunction\n" }),
            ["gameplay_variables"] = new JsonArray(),
            ["gameplay_triggers"] = new JsonArray()
        };

        var exception = Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateCollections(root, requireModuleSources: true));

        Assert.Equal("INVALID_ARGUMENT", exception.Code);
        Assert.Contains("Duplicate gameplay function identity", exception.Message);
    }

    [Fact]
    public void GameplayValidatorRejectsStaleModuleHashAndUnresolvedRegion()
    {
        var staleModule = new JsonObject
        {
            ["id"] = "module",
            ["source"] = "function HTW_MCP_Bootstrap takes nothing returns nothing\nendfunction\n",
            ["source_sha256"] = new string('A', 64)
        };
        var stale = Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateModule(staleModule, requireSource: true));
        Assert.Equal("SOURCE_CHANGED", stale.Code);

        var unresolved = new JsonObject
        {
            ["regions"] = new JsonArray(),
            ["gameplay_modules"] = new JsonArray(),
            ["gameplay_variables"] = new JsonArray(),
            ["gameplay_triggers"] = new JsonArray(new JsonObject
            {
                ["id"] = "trigger",
                ["name"] = "Trigger",
                ["folder_path"] = "MCP",
                ["events"] = new JsonArray(new JsonObject { ["type"] = "region_entry", ["region_name"] = "Missing" })
            })
        };

        var reference = Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateCollections(unresolved));
        Assert.Equal("INVALID_ARGUMENT", reference.Code);
        Assert.Contains("unknown region", reference.Message);
    }

    [Fact]
    public void GameplayValidatorRejectsPlayerStateOutsidePinnedNativeCatalogue()
    {
        var trigger = new JsonObject
        {
            ["id"] = "state",
            ["name"] = "State",
            ["folder_path"] = "MCP",
            ["events"] = new JsonArray(new JsonObject
            {
                ["type"] = "player_state_change", ["player_id"] = 1, ["state"] = "PLAYER_STATE_NOT_REAL", ["operator"] = "equal", ["value"] = 1
            })
        };

        var exception = Assert.Throws<EngineException>(() => GameplayModelValidator.ValidateTrigger(trigger));

        Assert.Equal("INVALID_ARGUMENT", exception.Code);
        Assert.Contains(JassNativeCatalogue.Version, exception.Message);
    }

    private static string FindManifest()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var candidate = Path.Combine(current.FullName, "scripts", "mcp", "manifest.json");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("The local gameplay source manifest was not found.");
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

        throw new FileNotFoundException("The local Hero Team Wars source map was not found.");
    }

    private static string GlobalsBlock(string source)
    {
        var start = source.IndexOf("globals\n", StringComparison.Ordinal);
        var end = source.IndexOf("endglobals", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
