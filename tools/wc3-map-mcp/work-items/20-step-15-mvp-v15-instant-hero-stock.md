# Task: Map MVP Build v15 — Instant Custom Hero Stock Availability & Full Selection Verification

**Status**: Corrected in v16; runtime verification pending
**Target Build Artifact**: `HeroTeamWars_v16.w3m`
**Profile**: `mvp_2arena` (4 players, 2 teams of 2, 2 arenas)  
**Parent Golden Source**: `map/HeroTeamWars_M0_2Arena.w3m` (SHA-256: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`, immutable)  

---

## 1. Context & Objective
In build `v14`, the map loaded cleanly without crashing and the shared Hero Altar (`n0AL`) became fully visible and targetable thanks to persistent `FogModifierStart` vision and camera focus.

However, during runtime testing of v15, custom heroes at the altar still displayed a long stock cooldown timer (~135 seconds). The v15 patch used the wrong rawcode pair: `uhst` is not the stock-start field, and `usst` is Stock Start Delay rather than Stock Replenish Interval. The correct fields are `usst` (start delay) and `usrg` (replenish interval).

**Goal of Step 15 (corrected v16)**:
1. Eliminate the hero stock delay by explicitly overriding stock availability fields on custom heroes `H001` through `H004`.
2. Ensure custom heroes can be hired immediately (at second 0) from the shared altar.
3. Validate the complete end-to-end selection flow: purchasing a hero, slot deduction, deployment to arena spawn points, camera shift to arena, and initiation of Round 1 preparation.

---

## 2. Root Cause Analysis
- In Warcraft III, tavern/altar stock units use these unit-object modification fields:
  - `usst` (`Stock Start Delay`): Number of seconds before the unit first appears in shop stock.
  - `usrg` (`Stock Replenish Interval`): Cooldown time to replenish one stock count after purchase.
- In [v8-hero-objects.json](tools/wc3-map-mcp/scripts/mcp/object-data/v8-hero-objects.json), v15 incorrectly added `uhst=0` and `usst=1`; that did not override the inherited `usrg` replenish interval. v16 replaces those with the correct `usst=0` and `usrg=1` overrides.

---

## 3. Detailed Scope of Work for v15

### A. Object Data Modifications (`v8-hero-objects.json`)
Update all four custom hero definitions (`H001`, `H002`, `H003`, `H004`) to add:
- `{ "id": "usst", "type": "Int", "value": 0 }` — 0-second stock start delay (instant availability at game start).
- `{ "id": "usrg", "type": "Int", "value": 1 }` — 1-second replenish interval.

### B. Map Build Execution
1. Verify .NET test suite passes (`dotnet test`).
2. Run build runner with `ObjectDataFormatVersion.v2` and the corrected `v8-hero-objects.json`.
3. Output artifact to `builds/mcp/hero-team-wars/<build-uuid>/HeroTeamWars_v16-instant-hero-stock_<build-uuid>.w3m`.
4. Generate and save diagnostic build report `builds/diagnostics/v16-build-report.json`.

### C. Deployment & Test Staging
1. Create directory `C:\Users\hp\Documents\Warcraft III\Maps\Test\v16\`.
2. Copy the generated artifact to `C:\Users\hp\Documents\Warcraft III\Maps\Test\v16\HeroTeamWars_v16.w3m`.
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
- v15 runtime feedback: the user opened `C:\Users\hp\Documents\Warcraft III\Maps\Test\v15\HeroTeamWars_v15.w3m` and still saw the inherited long stock cooldown. `War3Log.txt` records the exact v15 map opening and no compile error; runtime UI behavior remains user-observed.
- Static v15 inspection confirmed the wrong fields were encoded: `uhst=0` / `usst=1` on `H001`–`H004`.
- v16 correction: use `usst=0` / `usrg=1`; build and publish a separate v16 artifact, leaving v15 intact for comparison.
- Automated verification: 94 .NET tests and 44 MCP tests passed before the v16 correction; rerun the full suite after the correction. Warcraft III runtime gates remain for the manual checklist above.
