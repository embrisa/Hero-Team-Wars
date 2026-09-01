# V8 custom-hero map: agent debugging lessons

Stopped on 2026-09-01 at user request. The load crash is **not fixed**. This note is for future agents. Do not treat it as a runtime proof or a native-API reference.

Do **not** copy JASS native signatures, stock-function behavior, or other API facts from here. Use the pinned jassdoc index (`scripts/sync-jassdoc.ps1` / `map-engine/data/jassdoc/jass-api.json`) for that.

## What was actually proven

| Claim | Status |
|---|---|
| Golden source `map/HeroTeamWars_M0_2Arena.w3m` SHA-256 `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834` | Verified before/after rebuilds; never overwrite it |
| Warcraft III rejected an earlier v8 script at **compile** time | Proven by `War3Log.txt` (`SetUnitStock` is not a real native; jassdoc lookup is null) |
| After removing that call, lobby accepted four players | User observation: map got past invalid-script into a **load crash** |
| Renaming the custom tavern from `H0AL` to `n0AL` | Tried. User reported it **still crashes**. Do not reuse this as a fix. |
| Engine validate / reopen / opaque-member preserve on uniquely named builds | Static only. Not gameplay evidence. |
| Map loads, selection works, combat works | **Unverified.** User performs all Warcraft III verification. |

## Diagnostic sources that work

- **Compile failures:** `C:\Users\hp\Documents\Warcraft III\Logs\War3Log.txt` is the source of truth. Search the whole file (or more than the last ~80 lines) for invalid JASS / unknown natives. The tail after a lobby start often only shows player slots and NGDP init.
- **Load/runtime crashes:** War3Log often does **not** name the crash. A clean tail after lobby init does not mean the map is valid. Record the exact uniquely named build hash the user copied into `Maps\Test`.
- **Static JASS parse is not Warcraft III compile.** `War3Net.CodeAnalysis.Jass` accepts unknown natives. jassdoc-backed validation is the local API gate; War3Log is still the in-game compile gate.
- **Build success is not load success.** `reopened: true` and `opaque_members_preserved: true` do not predict Warcraft III load.

## Rebuild path that works (golden source → unique artifact)

Copy the merge used by `TransactionService.begin`, not an ad-hoc inspect dump:

1. `inspect_map` the golden source. **Delete `output_path` from the inspect result before treating it as canonical.** Leaving inspect `output_path` in the model caused `BUILD_COMPONENT_UNSUPPORTED`.
2. `compose_gameplay_source` from the gameplay manifest + `mvp_2arena`.
3. `Object.assign(canonical, composition.canonical_model, { trigger_mode: "mcp_native_jass", gameplay_source: {} })`. Skipping this merge caused `TEAMS_MISSING`.
4. Bound-compose against that canonical model, stamp `war3map.j` with the bound source and hashes.
5. `apply_operations` with `create_object_definition` ops from `scripts/mcp/object-data/v8-hero-objects.json`. Include `display_name`; omitting it caused reopen/inspect mismatch.
6. `validate_canonical` with `validation_context: { project_id: "hero-team-wars", profile: "mvp_2arena" }`.
7. `build_map` to a **new** path under `builds/mcp/hero-team-wars/<build-id>/`. Never write into the golden source.

Recompute the golden SHA-256 before and after. If it changes, stop.

## Object-data encoding lessons (not jassdoc)

These are MCP/War3Net encoding facts, not native-API docs.

- **Parent rawcodes are identity-sensitive.** `ObjectPlacementSupport.IsKnownStandard` is `OrdinalIgnoreCase`, so `hpal` vs `Hpal` will **not** fail the allowlist. Warcraft III object data still needs the Paladin parent as `Hpal`. Do not treat allowlist case-insensitivity as “the game accepts either”.
- **Sold-unit list (`useu`)** round-tripped as **one** `String` value `H001,H002,H003,H004`. Repeated `useu` modifications did not.
- **Custom object create** should set `display_name` (and `unam`) or inspect/reopen comparisons can fail even when the archive wrote.

## Worktree and git

- This repo is often dirty with unrelated MCP implementation. Inspect `git status` first. Do not reset, discard, or stage those files while fixing a map artifact.
- Stage only the intended source/object/test hunks. If a test file mixes unrelated dirty changes, commit a clean isolated diff and restore the working copy afterward.
- Push the intended commit. Leave unrelated dirty files dirty.

## What wasted time

- Long diagnosis chains without a uniquely named rebuild and a user Warcraft III report.
- Inferring crash causes from rawcode folklore (`H0AL` as a hero-class tavern ID). It was a reasonable next try; it did **not** stop the crash.
- Reading only the War3Log tail after lobby init and concluding “no error”.
- Treating composer/static tests as proof Warcraft III can compile or load the map.
- Writing inspect output into canonical, or composing JASS without merging `canonical_model`/teams.

## If work on the load crash resumes

Do not relaunch Warcraft III or World Editor from the agent. Hand the user a uniquely named hash-linked map.

Isolation that was **not** done (no claim these are the cause):

- objects-only vs JASS-only builds from the same golden source;
- stock `ntav` with no custom sold list / no custom altar rawcode;
- no `CreateUnit` of the custom altar at init;
- no `AddUnitToStock` / `RemoveUnitFromStock` at init.

Keep runtime status `untested` until the user reports load vs crash against that exact hash.
