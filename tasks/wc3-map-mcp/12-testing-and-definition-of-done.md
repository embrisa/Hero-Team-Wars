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
