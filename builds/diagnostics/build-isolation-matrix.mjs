import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { buildMapVariant } from "./build-matrix-runner.mjs";

const root = "C:\\Users\\hp\\Documents\\Warcraft III\\Hero Team Wars";
const objectsPath = join(root, "tools", "wc3-map-mcp", "scripts", "mcp", "object-data", "v8-hero-objects.json");
const v8Objects = JSON.parse(readFileSync(objectsPath, "utf8")).objects;

// 4 Custom heroes without altar
const fourHeroes = v8Objects.filter(o => o.rawcode.startsWith("H00"));

// Minimal Paladin hero H001 only with no stats modifications (only unam)
const minimalH001 = [
  {
    category: "unit",
    object_kind: "custom",
    base_rawcode: "Hpal",
    custom_rawcode: "H001",
    rawcode: "H001",
    display_name: "HTW Guardian",
    dependencies: [],
    references: {},
    unknown_ids: [],
    modifications: [
      { id: "unam", type: "String", value: "HTW Guardian" }
    ],
    codec_version: "wc3-object-data-v1",
    provenance: "source_of_truth",
    capability: "mcp_native_object_data"
  }
];

// Minimal 4 heroes with only unam
const minimalFourHeroes = [
  {
    category: "unit",
    object_kind: "custom",
    base_rawcode: "Hpal",
    custom_rawcode: "H001",
    rawcode: "H001",
    display_name: "HTW Guardian",
    dependencies: [],
    references: {},
    unknown_ids: [],
    modifications: [{ id: "unam", type: "String", value: "HTW Guardian" }],
    codec_version: "wc3-object-data-v1",
    provenance: "source_of_truth",
    capability: "mcp_native_object_data"
  },
  {
    category: "unit",
    object_kind: "custom",
    base_rawcode: "Hmkg",
    custom_rawcode: "H002",
    rawcode: "H002",
    display_name: "HTW Striker",
    dependencies: [],
    references: {},
    unknown_ids: [],
    modifications: [{ id: "unam", type: "String", value: "HTW Striker" }],
    codec_version: "wc3-object-data-v1",
    provenance: "source_of_truth",
    capability: "mcp_native_object_data"
  },
  {
    category: "unit",
    object_kind: "custom",
    base_rawcode: "Hamg",
    custom_rawcode: "H003",
    rawcode: "H003",
    display_name: "HTW Controller",
    dependencies: [],
    references: {},
    unknown_ids: [],
    modifications: [{ id: "unam", type: "String", value: "HTW Controller" }],
    codec_version: "wc3-object-data-v1",
    provenance: "source_of_truth",
    capability: "mcp_native_object_data"
  },
  {
    category: "unit",
    object_kind: "custom",
    base_rawcode: "Hblm",
    custom_rawcode: "H004",
    rawcode: "H004",
    display_name: "HTW Support",
    dependencies: [],
    references: {},
    unknown_ids: [],
    modifications: [{ id: "unam", type: "String", value: "HTW Support" }],
    codec_version: "wc3-object-data-v1",
    provenance: "source_of_truth",
    capability: "mcp_native_object_data"
  }
];

// Stub hero selection jass where HTW_HeroSelection_Begin is a no-op that just starts wave prep
const stubHeroSelectionJass = `function HTW_HeroSelection_RefillStock takes nothing returns nothing
endfunction

function HTW_HeroSelection_PlayerHasTeammateHero takes integer playerId, integer heroType returns boolean
    return false
endfunction

function HTW_HeroSelection_DeployHero takes integer playerId returns nothing
endfunction

function HTW_HeroSelection_Complete takes nothing returns nothing
endfunction

function HTW_HeroSelection_SelectForPlayer takes integer playerId, integer heroType returns boolean
    return true
endfunction

function HTW_HeroSelection_AllPlayersReady takes nothing returns boolean
    return true
endfunction

function HTW_HeroSelection_OnSell takes nothing returns nothing
endfunction

function HTW_HeroSelection_AutoPick takes integer playerId returns nothing
endfunction

function HTW_HeroSelection_OnTimeout takes nothing returns nothing
endfunction

function HTW_HeroSelection_Begin takes nothing returns nothing
    call HTW_Debug_LogText("hero selection disabled for isolation testing")
    set HTW_Phase = 1
    call HTW_Waves_Prepare()
endfunction
`;

// Selection JASS that creates 'ntav' (stock tavern) instead of 'n0AL' and no custom hero stock manipulation
const stockTavernSelectionJass = `function HTW_HeroSelection_RefillStock takes nothing returns nothing
endfunction

function HTW_HeroSelection_PlayerHasTeammateHero takes integer playerId, integer heroType returns boolean
    return false
endfunction

function HTW_HeroSelection_DeployHero takes integer playerId returns nothing
endfunction

function HTW_HeroSelection_Complete takes nothing returns nothing
    set HTW_HeroSelectionComplete = true
    if HTW_HeroSelectionTimer != null then
        call PauseTimer(HTW_HeroSelectionTimer)
        call DestroyTimer(HTW_HeroSelectionTimer)
        set HTW_HeroSelectionTimer = null
    endif
    if HTW_HeroSelectionBuilding != null then
        call RemoveUnit(HTW_HeroSelectionBuilding)
        set HTW_HeroSelectionBuilding = null
    endif
    set HTW_Phase = 1
    call HTW_Debug_LogText("hero selection complete; first preparation phase started")
    call HTW_Waves_Prepare()
endfunction

function HTW_HeroSelection_SelectForPlayer takes integer playerId, integer heroType returns boolean
    return true
endfunction

function HTW_HeroSelection_AllPlayersReady takes nothing returns boolean
    return true
endfunction

function HTW_HeroSelection_OnSell takes nothing returns nothing
endfunction

function HTW_HeroSelection_AutoPick takes integer playerId returns nothing
endfunction

function HTW_HeroSelection_OnTimeout takes nothing returns nothing
    call HTW_HeroSelection_Complete()
endfunction

function HTW_HeroSelection_Begin takes nothing returns nothing
    set HTW_HeroSelectionComplete = false
    set HTW_HeroSelectionBuilding = CreateUnit(Player(PLAYER_NEUTRAL_PASSIVE), 'ntav', 216., -336., 270.)
    set HTW_HeroSelectionTimer = CreateTimer()
    call TimerStart(HTW_HeroSelectionTimer, 5., false, function HTW_HeroSelection_OnTimeout)
    call DisplayTextToPlayer(GetLocalPlayer(), 0., 0., "Testing standard Tavern (ntav) creation.")
endfunction
`;

// JASS that spawns custom hero H001 directly on map init (no tavern)
const directSpawnH001Jass = `function HTW_HeroSelection_RefillStock takes nothing returns nothing
endfunction

function HTW_HeroSelection_PlayerHasTeammateHero takes integer playerId, integer heroType returns boolean
    return false
endfunction

function HTW_HeroSelection_DeployHero takes integer playerId returns nothing
endfunction

function HTW_HeroSelection_Complete takes nothing returns nothing
endfunction

function HTW_HeroSelection_SelectForPlayer takes integer playerId, integer heroType returns boolean
    return true
endfunction

function HTW_HeroSelection_AllPlayersReady takes nothing returns boolean
    return true
endfunction

function HTW_HeroSelection_OnSell takes nothing returns nothing
endfunction

function HTW_HeroSelection_AutoPick takes integer playerId returns nothing
endfunction

function HTW_HeroSelection_OnTimeout takes nothing returns nothing
endfunction

function HTW_HeroSelection_Begin takes nothing returns nothing
    local integer playerId
    local integer teamIndex
    local real x
    local real y
    set playerId = 1
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        set teamIndex = HTW_Teams_FindByPlayer(playerId)
        set x = GetRectCenterX(HTW_ArenaRect[teamIndex])
        set y = GetRectCenterY(HTW_ArenaRect[teamIndex])
        set HTW_HeroUnitByPlayer[playerId] = CreateUnit(Player(playerId - 1), 'H001', x + I2R(playerId * 64), y + I2R(playerId * 48), 270.)
        set HTW_HeroAliveByPlayer[playerId] = true
        set HTW_HeroDeathAccountedByPlayer[playerId] = false
        set HTW_AliveHeroCount = HTW_AliveHeroCount + 1
        set playerId = playerId + 1
    endloop
    call HTW_Debug_LogText("direct H001 heroes spawned; wave preparation started")
    set HTW_Phase = 1
    call HTW_Waves_Prepare()
endfunction
`;

async function main() {
  console.log("=== Building Isolation Matrix: v9, v10, v11, v12 ===");

  // Build v9: Full v8 Object Data (w3u with H001..H004 and n0AL) but Hero Selection is NO-OP.
  // Test Purpose: Is the crash caused by war3map.w3u binary format/data during map load, or by JASS execution during map init?
  console.log("\nBuilding v9 (Isolation A: Full v8 w3u objects, disabled hero selection init)...");
  const v9 = await buildMapVariant({
    variantId: "v9-objects-only-disabled-selection",
    versionLabel: "v9",
    description: "Contains all 5 v8 object definitions in war3map.w3u (H001-H004, n0AL), but HTW_HeroSelection_Begin is a no-op. Proves whether war3map.w3u itself crashes the engine loader.",
    heroObjects: v8Objects,
    heroSelectionSourceOverride: stubHeroSelectionJass
  });
  console.log("v9 built:", v9.output_path, "SHA-256:", v9.output_sha256);

  // Build v10: Direct spawn of custom hero H001 for all players (w3u has H001-H004), no altar/tavern.
  // Test Purpose: Can Warcraft III successfully instantiate and render custom hero 'H001'?
  console.log("\nBuilding v10 (Isolation B: Direct spawn of custom hero H001, no altar)...");
  const v10 = await buildMapVariant({
    variantId: "v10-direct-spawn-h001",
    versionLabel: "v10",
    description: "Contains all 4 custom hero definitions (H001-H004) in war3map.w3u. At map init, directly spawns 'H001' for each player without an altar.",
    heroObjects: fourHeroes,
    heroSelectionSourceOverride: directSpawnH001Jass
  });
  console.log("v10 built:", v10.output_path, "SHA-256:", v10.output_sha256);

  // Build v11: Standard Tavern ('ntav') creation at map init, NO custom w3u object data.
  // Test Purpose: Does creating a neutral tavern ('ntav') at map init crash or succeed?
  console.log("\nBuilding v11 (Isolation C: Standard ntav creation, zero custom w3u objects)...");
  const v11 = await buildMapVariant({
    variantId: "v11-stock-tavern-no-w3u",
    versionLabel: "v11",
    description: "Zero custom objects (no war3map.w3u in archive). At map init, creates standard tavern ('ntav') at (216, -336) with a 5-second timer before starting waves.",
    heroObjects: [],
    heroSelectionSourceOverride: stockTavernSelectionJass
  });
  console.log("v11 built:", v11.output_path, "SHA-256:", v11.output_sha256);

  // Build v12: Minimal hero definitions (H001-H004 with only unam name changes, no stat modifications, no altar).
  // Test Purpose: Are specific hero modifications (uhpm/ustr/uagi/uint) in w3u causing corruption?
  console.log("\nBuilding v12 (Isolation D: Minimal custom heroes with only unam, direct spawn)...");
  const v12 = await buildMapVariant({
    variantId: "v12-minimal-custom-heroes-direct-spawn",
    versionLabel: "v12",
    description: "Contains 4 custom heroes (H001-H004) with ONLY unam modified (no ustr/uagi/uint/uhpm/ugol/ulum). Directly spawns H001 at start.",
    heroObjects: minimalFourHeroes,
    heroSelectionSourceOverride: directSpawnH001Jass
  });
  console.log("v12 built:", v12.output_path, "SHA-256:", v12.output_sha256);

  const manifestReport = {
    generated_utc: new Date().toISOString(),
    expected_source_sha256: "027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834",
    variants: [v9, v10, v11, v12]
  };

  const matrixReportPath = join(root, "builds", "diagnostics", "isolation-matrix-report.json");
  writeFileSync(matrixReportPath, JSON.stringify(manifestReport, null, 2));
  console.log("\n=== All builds generated successfully! ===");
  console.log("Matrix report written to:", matrixReportPath);
}

main().catch((err) => {
  console.error("Build matrix failed:", err);
  process.exit(1);
});
