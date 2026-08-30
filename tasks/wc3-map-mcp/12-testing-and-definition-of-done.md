# Testing and Definition of Done

## Test layers

### Unit tests

- schema validation;
- path-boundary enforcement;
- canonical ordering and hashing;
- transaction state transitions;
- operation application and rollback;
- error normalization.

### Map-engine fixture tests

- parse known `.w3m` and `.w3x` fixtures;
- serialize every supported component;
- preserve opaque members;
- no-op round-trip comparisons;
- malformed/truncated input rejection;
- rawcode, coordinate, and reference validation.

### MCP integration tests

- start the server over STDIO;
- list and call every tool;
- verify read-only tools do not write;
- verify mutation requires a transaction;
- simulate worker crash, timeout, and malformed response;
- verify protocol stdout is not contaminated by logs.

### Application smoke tests

- World Editor opens the built copy;
- Warcraft III loads the built copy;
- expected initialization/debug marker appears;
- chunk-specific acceptance test is observed.

## First-release definition of done

- Phase 0 compatibility report is complete.
- Read-only inspection and semantic comparison are reliable.
- Transactions cannot overwrite the source map.
- At least metadata, player/force, region, script, and one object-data edit round-trip successfully.
- Validation rejects deliberately broken fixtures.
- A no-op build opens in World Editor and loads in Warcraft III.
- Codex can connect locally and call the allowed tools.
- Installation, configuration, recovery, and limitations are documented.
- Hero Team Wars `HTW-00` report is generated and reconciled with manual unknowns.

## Not done conditions

- A command completed but its output was not checked.
- A map built but was not opened or loaded.
- An agent generated source that is not connected to the map script.
- A GUI action was attempted without observable saved state.
- A map difference cannot be explained by the transaction manifest.

## Required test matrix by phase

| Phase | Automated evidence | Application/manual evidence |
|---|---|---|
| 0 | engine builds, schemas validate, archive inventory/probe tests | no-op editor open/game load if rebuild permitted |
| 1 | STDIO integration, input/path policy, stable canonical output | MCP Inspector calls and Codex read-only visibility |
| 2 | transaction atomicity, revisions, locks, rollback/discard safety | review sample semantic diff |
| 3 | invalid fixture rejection, no-op/minimal rebuild/reinspect | editor opens and game loads exact build hashes |
| 4 | fake runner, argument safety, persisted sessions/evidence rules | real editor/game launches and recorded observations |
| 5 | script/static tests where possible, transaction/build validations | chunk-specific gameplay acceptance tests |

## Fixtures

Maintain at least:

- tiny valid synthetic `.w3m` created for testing if licensing/format permits;
- copied local Hero Team Wars baseline excluded from public distribution;
- truncated archive;
- archive with unknown opaque member;
- duplicate rawcode case;
- invalid player/force reference;
- out-of-bounds region/placement;
- disconnected script/import case;
- known canonical JSON expectations.

Each fixture README states origin, licensing/distribution rule, expected capabilities, and hash.

## Source preservation assertion

Create a shared test/setup helper that captures the original Hero Team Wars hash before applicable integration/application tests and asserts the same hash afterward. A mismatch is a test-suite failure even if all other checks pass.

## Evidence naming

Use correlation/transaction/build/test IDs in filenames. Examples:

```text
compatibility/hero-team-wars-baseline.json
transactions/<T>/reports/diff-0001.json
artifacts/builds/<B>/build-manifest.json
artifacts/tests/<S>/test-session.json
logs/<correlation>.jsonl
```

## Coverage quality

Coverage percentage alone is not the gate. Critical policy/state/build paths require branch tests: path escapes, hash drift, stale revisions, partial batch failure, engine crash, malformed response, build reopen mismatch, existing game process, and false evidence upgrade.

## Completion report review

Before accepting a phase, verify commands were actually run, outputs exist, hashes match, source remained unchanged, disabled/untested items are explicit, and the next agent can reproduce the result without chat history.
