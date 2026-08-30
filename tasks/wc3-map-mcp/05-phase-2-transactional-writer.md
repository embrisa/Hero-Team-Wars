# Phase 2 Work Packet - Transactional Map Writer

Status: complete as of 2026-08-30. Phase 1 is complete; Phase 2 typed staging, semantic transactions, validation, rollback, and discard/audit behavior are implemented and verified. Binary map build/editor/game runtime remain Phase 3+ gates.

This adds safe staging and typed semantic changes. It does not add final map building or editor/game launch. Each transaction is an isolated copy tied to an exact source hash.

## Required reading and inputs

Read the mandatory context, Phase 0 compatibility report, Phase 1 handoff, `09-mcp-tool-contracts.md`, `10-map-model-and-contracts.md`, and `11-safety-recovery-and-audit.md`.

Enable operations only for components marked `typed_write_enabled` or promoted by tests in this phase.

## Goal

An agent can begin a transaction, apply an atomic batch of typed changes, inspect a semantic diff, validate staged state, revise or discard it, and never modify the source map.

## Files to create or complete

```text
mcp-server/src/services/transaction-service.ts
mcp-server/src/tools/transactions.ts
mcp-server/src/schemas/operations.ts
mcp-server/src/storage/{transaction-store,project-lock}.ts
map-engine/src/Wc3MapEngine.Core/Operations/*
map-engine/src/Wc3MapEngine.Core/Diff/*
map-engine/src/Wc3MapEngine.Core/Transactions/*
contracts/schemas/change-operation.schema.json
contracts/schemas/transaction-manifest.schema.json
contracts/schemas/semantic-diff.schema.json
mcp-server/test/{unit,integration}/transaction-*.test.ts
map-engine/tests/Wc3MapEngine.Tests/Operations/*
```

## Transaction layout

```text
snapshots/transactions/<uuid>/
  manifest.json
  source/source-map.w3m
  working/canonical-map.json
  working/members/                     only if extraction strategy needs it
  revisions/0000-initial.json
  revisions/0001-after-operations.json
  reports/diff-0001.json
  reports/validation-0001.json
  logs/
```

The manifest includes project/source identity, source hash/size/time, engine/server/schema versions, state, revision, timestamps, and operation IDs.

## State machine

```text
created -> staged -> modified -> validated
staged/modified/validated -> discarded
any active state -> failed
validated -> built                    Phase 3
built -> tested/promoted              later phases
```

Reject invalid transitions with `TRANSACTION_STATE`.

## Tools and algorithms

### `wc3_begin_transaction`

Input project, allowed source, expected hash, optional label. Resolve policy, lock project, rehash source, reject drift, create one UUID directory, copy and verify source, canonicalize the copy, atomically write manifest, release lock, and return ID/revision/hash/report paths.

### `wc3_apply_operations`

Input project, transaction, expected revision, typed operation array, optional dry run. Validate/lock, reject stale revision, clone canonical state in memory, apply entire batch to clone, persist nothing if any operation fails, create diff, and atomically write revision/manifest. Dry run changes nothing.

Each operation includes operation UUID, type enum, target, expected prior value/revision, requested value, rationale, and optional `HTW-XX` reference.

### `wc3_transaction_diff`

Return semantic changes grouped by component/target with before, after, operation ID, and provenance. Store large diffs as artifacts.

### `wc3_validate_transaction`

Validate staged canonical state and preconditions. Errors keep state `modified`; error-free exact revision may become `validated`.

### `wc3_discard_transaction`

Require project, transaction, expected source hash, and explicit confirmation. Resolve directory, prove its parent is the configured transaction root, verify manifest identity, lock, retain an audit tombstone, and delete only that directory. Never use a broad glob.

## Initial typed operations

Target operations, enabled only with compatibility proof:

- selected `set_map_metadata` fields;
- `set_player_slot`;
- `set_force` with explicit player IDs/flags;
- `create_region`, `update_region`, `delete_region`;
- `place_unit`, `move_unit`, `remove_placed_unit`;
- one narrow object-data operation;
- `set_script_source` for the existing `war3map.j` entry point, after an exact expected hash, MCP-owned JASS policy, static parse, and copied-map round-trip check.

Do not expose generic archive patching, arbitrary writes, raw shell, or untyped object fields.

## Preconditions

Updates/deletes require expected old values. Examples: old region bounds/name, placed unit coordinates/rawcode/owner, previous map title. Stale plans fail instead of overwriting newer editor work.

## Tests

- hash mismatch rejected before staging;
- staged copy hash equals source;
- atomic batch rollback;
- dry run does not change files/revision;
- stale revision rejected;
- valid revision increments once;
- stable attributable diff;
- concurrent mutations serialize;
- crash-safe temp write;
- traversal/junction escape rejected;
- discard cannot target root/sibling/source/build/other transaction;
- original hash unchanged.

## Completion gate and handoff

Complete when a fixture and copied Hero Team Wars map can be staged, changed in canonical form, diffed, validated, rolled back/discarded, and audited without changing the original. Archive build is not part of this gate.

Handoff states, schemas, enabled/disabled operations, storage/locking behavior, sample manifest/diff, test evidence, and one validated transaction ready for Phase 3.

## Completion evidence

- The TypeScript transaction service now stages and inspects an isolated source copy, rechecks the original and staged hashes, records server/engine/schema/protocol/dependency versions, and exposes begin/apply/diff/validate/discard tools only when project write policy permits them.
- The transaction store uses UUID-owned directories under the configured staging root, same-directory atomic temporary files, canonical/revision hashes, manifest state transitions, exact-parent and symlink/junction checks, bounded lock waiting, and audit tombstones for confirmed discard.
- The .NET engine applies seven typed operations with clone-before-apply semantics and explicit expected-value preconditions: selected map metadata, player slots, forces, region create/update/delete, and hash-checked MCP-owned JASS source replacement for `war3map.j`. Opaque unit placement and object-data operations remain schema-recognized but return `UNSUPPORTED_OPERATION` until their binary ownership and round-trip behavior are proven.
- Semantic diffs are grouped by component and target, preserve before/after values, operation IDs, and provenance, and exclude parser/container observations from gameplay semantic claims. Canonical validation checks schema, metadata, player slots, forces, region identity, and finite ordered bounds.
- A copied Hero Team Wars source was staged, dry-run, changed, diffed, validated, built through the existing Phase 3 no-op profile, and rehashed in the integration suite. Failed batches and stale revisions were rejected without changing the prior revision; a confirmed discard removed only the exact transaction directory and retained its audit tombstone. The source map was unchanged.

### Commands actually run

From the project root:

```powershell
.\tools\wc3-map-mcp\scripts\build.ps1
.\tools\wc3-map-mcp\scripts\test.ps1
```

Observed results: build succeeded with 0 warnings and 0 errors; 16 .NET tests passed; 23 TypeScript tests passed across 6 test files. The source SHA-256 before and after the integration workflow was `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.

The checked-in example configuration intentionally remains `read_only` with the six Phase 1 tools. To exercise Phase 2, use a separately approved project configuration with `write_policy: "writes"`; an empty allow-list enables the implemented transaction/build surface, while explicit allow-lists remain supported.
