import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { buildMapVariant, findNextTestVersion, publishPlayableMap } from "./build-matrix-runner.mjs";

const root = "C:\\Users\\hp\\Documents\\Warcraft III\\Hero Team Wars";

// JASS module overrides for E0: Hero selection entry disabled, no custom hero references
export const stubContentHeroesJass = `function HTW_Content_Heroes takes nothing returns nothing
endfunction

function HTW_Content_HeroTypeForSlot takes integer slot returns integer
    return 'Hpal'
endfunction

function HTW_Content_IsHeroType takes integer heroType returns boolean
    return heroType == 'Hpal'
endfunction

function HTW_Content_HeroName takes integer heroType returns string
    return "Paladin"
endfunction

function HTW_Content_CreateHero takes integer playerId, integer heroType, real x, real y returns unit
    return CreateUnit(Player(playerId - 1), heroType, x, y, 270.)
endfunction
`;

export const stubHeroSelectionJass = `function HTW_HeroSelection_RefillStock takes nothing returns nothing
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

// Minimal candidate custom hero H001 for future E1/E2 (frozen fixture)
export const minimalH001Hero = {
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
};

export async function runE0() {
  console.log("===============================================================");
  console.log("=== Running Experiment E0: JASS-Only Baseline (No Custom W3U) ===");
  console.log("===============================================================");

  const targetVersion = findNextTestVersion();
  const versionLabel = `v${targetVersion}`;
  console.log(`Allocated next test version: ${versionLabel}`);

  const summary = await buildMapVariant({
    variantId: "e0-jass-baseline-no-custom-objects",
    versionLabel,
    description: "E0 baseline: current toolchain rebuild with hero-selection entry disabled, no custom objects, no war3map.w3u in archive.",
    heroObjects: [],
    moduleOverrides: {
      "content.heroes": stubContentHeroesJass,
      "systems.hero-selection": stubHeroSelectionJass
    }
  });

  // Static checks per Task 21 protocol
  console.log("\n--- Performing Static Checks ---");
  if (summary.objects.has_w3u) {
    throw new Error("Static check failed: war3map.w3u must be ABSENT in E0 artifact");
  }
  console.log("  [PASS] war3map.w3u is absent");

  const war3mapJPath = join(summary.diagnostics, "output-war3map.j");
  const scriptContent = readFileSync(war3mapJPath, "utf8");

  for (const forbidden of ["H001", "H002", "H003", "H004", "n0AL"]) {
    if (scriptContent.includes(forbidden)) {
      throw new Error(`Static check failed: script contains forbidden rawcode '${forbidden}'`);
    }
  }
  console.log("  [PASS] Zero references to H001-H004 and n0AL");

  if (!scriptContent.includes("function main") || !scriptContent.includes("function config")) {
    throw new Error("Static check failed: script missing required main or config function");
  }
  console.log("  [PASS] Required main and config functions present");

  if (!summary.reopened) {
    throw new Error("Static check failed: map engine did not confirm successful reopen");
  }
  console.log("  [PASS] Map engine confirmed clean reopen");

  if (!summary.source_unchanged) {
    throw new Error("Static check failed: golden source map was modified");
  }
  console.log("  [PASS] Golden source map is strictly unchanged");

  // Publish to Test folder
  console.log("\n--- Publishing Playable Map ---");
  const published = publishPlayableMap(summary, targetVersion);
  console.log(`  Published to: ${published.published_path}`);
  console.log(`  Published SHA-256: ${published.published_sha256}`);

  const reportPath = join(root, "builds", "diagnostics", "e0-build-report.json");
  const report = {
    experiment: "E0",
    build_id: summary.build_id,
    version_label: versionLabel,
    summary,
    published,
    static_checks: {
      has_w3u: false,
      forbidden_rawcodes_absent: true,
      main_and_config_present: true,
      reopened: true,
      source_unchanged: true
    }
  };
  writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(`Report saved to: ${reportPath}`);

  return report;
}

// Allow CLI invocation
const stepArg = process.argv.find(arg => arg.startsWith("--step="));
const step = stepArg ? stepArg.split("=")[1].toLowerCase() : "e0";

if (process.argv[1]?.endsWith("run-e0-e2-isolation.mjs")) {
  if (step === "e0") {
    runE0().then(report => {
      console.log("\n=== E0 Complete ===");
      console.log("Playable Path:", report.published.published_path);
      console.log("Playable SHA-256:", report.published.published_sha256);
    }).catch(err => {
      console.error("E0 failed:", err);
      process.exit(1);
    });
  } else {
    console.error(`Step '${step}' is not authorized until previous steps are verified by the user.`);
    process.exit(1);
  }
}
