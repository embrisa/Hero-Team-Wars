# Phase 3 - Build and Validation

## Objective

Turn staged map state into deterministic test artifacts and reject structurally unsafe builds.

## Implement

- `wc3_validate_transaction`
- `wc3_build_map`
- `wc3_build_report`
- script syntax/static checking for the selected language;
- map archive reconstruction with opaque-member preservation;
- reproducible build manifest containing dependency versions and input hashes;
- validation severity levels: error, warning, information.

## Validators

- required map members and supported versions;
- duplicate or malformed rawcodes;
- dangling references to regions, players, units, abilities, and variables;
- player/force contradictions;
- invalid coordinates or placements outside bounds;
- map-size, import-size, and archive-name rules;
- missing script entry point or disconnected imported script;
- reserved output collision;
- Hero Team Wars invariants such as explicit teams and preserved region names.

## Build outputs

- timestamped test map;
- SHA-256 hash;
- transaction manifest;
- semantic diff;
- validation report;
- dependency and environment versions;
- explicit `runtime_status: untested` until a launch is observed.

## Acceptance criteria

- Identical inputs and tool versions produce semantically identical outputs.
- An invalid transaction cannot produce a promotable build.
- Unknown archive members survive a no-op round trip.
- World Editor opens the no-op build without reporting corruption.
- Warcraft III test-loads the no-op build.
