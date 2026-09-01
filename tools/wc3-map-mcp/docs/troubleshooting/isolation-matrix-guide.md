# Map Load Crash Isolation Matrix (v9, v10, v11, v12)

Created: 2026-09-01
Purpose: Isolate the root cause of the Warcraft III runtime crash during map loading between `war3map.w3u` (object data binary definitions) and JASS execution (`hero-selection.j` altar spawn / native stock calls).

## Isolation Strategy & Build Matrix

| Version | Variant ID | Key Characteristic | Diagnostic Purpose |
|---|---|---|---|
| **v9** | `v9-objects-only-disabled-selection` | Full v8 `war3map.w3u` (5 objects: `H001`..`H004`, `n0AL`), but `HTW_HeroSelection_Begin` is a no-op | **Isolates Object Data vs JASS Runtime**: If v9 crashes on load, the bug is strictly inside `war3map.w3u` object definitions. If v9 loads cleanly, the bug is inside JASS altar/stock logic. |
| **v10** | `v10-direct-spawn-h001` | 4 custom hero objects (`H001`..`H004`) in `war3map.w3u`. Direct `CreateUnit` of `H001` at map start without any altar or tavern. | **Isolates Custom Hero Instantiation**: Tests whether Warcraft III can parse, instantiate, and render custom hero `H001` directly without altar mechanics. |
| **v11** | `v11-stock-tavern-no-w3u` | Zero custom objects (no `war3map.w3u` member in MPQ archive). Spawns standard neutral Tavern (`ntav`) at map init. | **Isolates Stock Tavern Mechanics**: Tests whether spawning `ntav` at map init via JASS works properly when no custom object data is present. |
| **v12** | `v12-minimal-custom-heroes-direct-spawn` | 4 custom heroes (`H001`..`H004`) in `war3map.w3u` with **only `unam` modified** (no stat modifications like `ustr`/`uagi`/`uint`/`uhpm`/`ugol`/`ulum`). Direct spawn of `H001`. | **Isolates Stat Modifications**: If v10 fails but v12 passes, a specific stat modification field in `war3map.w3u` is causing the unit loader corruption. |

---

## Artifact Index & SHA-256 Hashes

| Version | Map File Path | Size (Bytes) | SHA-256 |
|---|---|---|---|
| **v9** | `builds/mcp/hero-team-wars/f4d3fe36-f9e7-48e8-b647-061621cb031a/HeroTeamWars_v9-objects-only-disabled-selection_f4d3fe36-f9e7-48e8-b647-061621cb031a.w3m` | 47,151 | `47BC727800BBA455F833FF3D78C2F45324DE8C1718F1EAD2236B86DF2084CD30` |
| **v10** | `builds/mcp/hero-team-wars/91207484-a5c2-44c9-a640-116460cde65f/HeroTeamWars_v10-direct-spawn-h001_91207484-a5c2-44c9-a640-116460cde65f.w3m` | 47,161 | `82B5FD80D39006B02537523B94C0D95799B3236C8A8724884FDC89B8B21AA33C` |
| **v11** | `builds/mcp/hero-team-wars/b029679c-2200-4044-9534-e372c1da0886/HeroTeamWars_v11-stock-tavern-no-w3u_b029679c-2200-4044-9534-e372c1da0886.w3m` | 46,423 | `A52E00F2B2D27D536EB54F8A23109569478DC897259531EB457B2063DD21080B` |
| **v12** | `builds/mcp/hero-team-wars/5bf08b23-2ac5-46c6-87a4-58b8c34f46a8/HeroTeamWars_v12-minimal-custom-heroes-direct-spawn_5bf08b23-2ac5-46c6-87a4-58b8c34f46a8.w3m` | 46,511 | `241B10AC92B0FD9D7E2824669557473C930521B51F17629DF8F1FB897B562EEE` |

Golden Source Map: `map/HeroTeamWars_M0_2Arena.w3m` (SHA-256: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834` - preserved and verified unmodified).

---

## User Verification Instructions

Copy the desired `.w3m` file to `Documents/Warcraft III/Maps/Test` and run a 4-player custom game test.

### Test 1: Test `v9` first
- **If `v9` CRASHES on loading screen**: The crash is **100% inside `war3map.w3u`**. Proceed to test `v10` and `v12`.
- **If `v9` LOADS and wave begins**: The `war3map.w3u` binary is completely valid, and the crash was caused by `hero-selection.j` (altar creation or `AddUnitToStock` / `RemoveUnitFromStock` during map init). Proceed to test `v11`.
