# Task: Map MVP Build v15 — Instant Custom Hero Stock Availability & Full Selection Verification

**Status**: Implemented; runtime verification pending
**Target Build Artifact**: `HeroTeamWars_v15.w3m`  
**Profile**: `mvp_2arena` (4 players, 2 teams of 2, 2 arenas)  
**Parent Golden Source**: `map/HeroTeamWars_M0_2Arena.w3m` (SHA-256: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`, immutable)  

---

## 1. Context & Objective
In build `v14`, the map loaded cleanly without crashing and the shared Hero Altar (`n0AL`) became fully visible and targetable thanks to persistent `FogModifierStart` vision and camera focus.

However, during runtime testing, custom heroes at the altar displayed a long stock cooldown timer (~135 seconds) inherited from standard melee hero tavern defaults (`uhst` / `usst`), preventing instant hero selection.

**Goal of Step 15 (v15)**:
1. Eliminate the hero stock delay by explicitly overriding stock availability fields on custom heroes `H001` through `H004`.
2. Ensure custom heroes can be hired immediately (at second 0) from the shared altar.
3. Validate the complete end-to-end selection flow: purchasing a hero, slot deduction, deployment to arena spawn points, camera shift to arena, and initiation of Round 1 preparation.

---

## 2. Root Cause Analysis
- In Warcraft III, tavern/altar stock units rely on two object modification fields:
  - `uhst` (`Stock Initial Delay` / `Start Delay`): Number of seconds into the game before the unit first appears in shop stock. Defaults to **135 seconds** for standard melee hero base types (`Hpal`, `Hmkg`, `Hamg`, `Hblm`).
  - `usst` (`Stock Replenish Interval`): Cooldown time (in seconds) to replenish 1 stock count once bought. Defaults to **135 seconds**.
- In [v8-hero-objects.json](tools/wc3-map-mcp/scripts/mcp/object-data/v8-hero-objects.json), `H001`–`H004` defined base attributes (`ustr`, `uagi`, `uint`, `uhpm`, `ugol`, `ulum`), but omitted explicit `uhst` and `usst` overrides. Consequently, Warcraft III enforced the 135-second initial delay.

---

## 3. Detailed Scope of Work for v15

### A. Object Data Modifications (`v8-hero-objects.json`)
Update all four custom hero definitions (`H001`, `H002`, `H003`, `H004`) to add:
- `{ "id": "uhst", "type": "Int", "value": 0 }` — 0-second initial stock cooldown (instant availability at game start).
- `{ "id": "usst", "type": "Int", "value": 1 }` — 1-second replenish interval.

### B. Map Build Execution
1. Verify .NET test suite passes (`dotnet test`).
2. Run build runner with `ObjectDataFormatVersion.v2` and the updated `v8-hero-objects.json`.
3. Output artifact to `builds/mcp/hero-team-wars/<build-uuid>/HeroTeamWars_v15-instant-hero-stock_<build-uuid>.w3m`.
4. Generate and save diagnostic build report `builds/diagnostics/v15-build-report.json`.

### C. Deployment & Test Staging
1. Create directory `C:\Users\hp\Documents\Warcraft III\Maps\Test\v15\`.
2. Copy the generated artifact to `C:\Users\hp\Documents\Warcraft III\Maps\Test\v15\HeroTeamWars_v15.w3m`.
3. Compute and log SHA-256 hash.

### D. Source Control & Handoff
1. Stage modified files (`v8-hero-objects.json`, task file, build runner/reports).
2. Create Git commit and push to `origin/main`.
3. Provide manual verification checklist for in-game testing.

---

## 4. Manual In-Game Verification Checklist (for User)
- [ ] **Map Load**: Map loads past the loading screen into the game without crashing.
- [ ] **Altar Visibility**: Altar is visible and automatically selected under active line of sight.
- [ ] **Stock Availability**: All 4 heroes (`HTW Guardian`, `HTW Striker`, `HTW Controller`, `HTW Support`) have **no cooldown** and can be clicked immediately.
- [ ] **Hero Purchase & Deployment**:
  - Buying a hero removes the altar selection.
  - The hero is spawned in the correct player arena team slot.
  - The player's camera shifts to center on the deployed hero.
- [ ] **Game Loop Progression**: After selection, the hero selection building is removed, fog modifiers are cleaned up, and Round 1 wave preparation timer starts.

---

## 5. Agent Execution Record

- Golden source rechecked unchanged: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- MCP transaction `453d8e4e-de39-44bf-b806-ec92f6fae4aa`, revision `1`, staged five typed object definitions and passed validation (`buildable=true`).
- MCP build `dbb6102b-21ba-4b14-beb0-5519cb691fb6` reopened successfully with no semantic differences; output hash `954EBEBCB4FB3564C3BA71736AC2B30E90BE1357D2B66FC95D971D97FE98575C`.
- Diagnostic runner `builds/diagnostics/build-v15-fix.mjs` generated the published artifact and report `builds/diagnostics/v15-build-report.json`. Published artifact hash: `7977BB1D215A72CBB9D3F50DC61E9AB18521D70CFFC31043275F8562F070CF14`.
- Published map: `C:\Users\hp\Documents\Warcraft III\Maps\Test\v15\HeroTeamWars_v15.w3m` (49,026 bytes). Static inspection confirmed five `war3map.w3u` definitions and `uhst=0` / `usst=1` on `H001`–`H004`.
- Automated verification: 94 .NET tests and 44 MCP tests passed. Warcraft III runtime gates remain for the manual checklist above.
