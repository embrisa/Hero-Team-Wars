# Phase 3 Work Packet - Map Build and Validation

Status: implementation complete; manual editor/game compatibility evidence remains pending.

This phase serializes staged state and creates new map artifacts. The challenge is not merely creating an MPQ; it is preserving unsupported data and producing something the installed World Editor and Warcraft III accept.

Implementation note (2026-08-30): the Phase 3 engine/server path is implemented and automated checks pass. The compatibility gate is intentionally still visible because the installed World Editor already has a user-owned Untitled/Trigger Editor session, and the installed Warcraft III executable exited without exposing a game window during a controlled launch attempt. No runtime pass is claimed.

## Required reading and inputs

Read the mandatory context, compatibility report, transaction handoff, `REFERENCE-WC3-MAP-FORMATS.md`, `10-map-model-and-contracts.md`, `11-safety-recovery-and-audit.md`, and `12-testing-and-definition-of-done.md`.

Start with a no-op transaction. Do not use gameplay changes to debug the writer.

## Goal

Create deterministic, uniquely named map outputs from validated transactions, produce reports/hashes, preserve opaque members, and prevent invalid builds from promotion.

## Files to create or complete

```text
mcp-server/src/services/build-service.ts
mcp-server/src/tools/builds.ts
map-engine/src/Wc3MapEngine.Core/Build/*
map-engine/src/Wc3MapEngine.Core/Validation/*
map-engine/src/Wc3MapEngine.Core/Scripts/*
contracts/schemas/validation-report.schema.json
contracts/schemas/build-manifest.schema.json
mcp-server/test/integration/build-*.test.ts
map-engine/tests/Wc3MapEngine.Tests/{Build,Validation}/*
docs/decisions/0002-map-script-ownership.md
```

## Decide script ownership

Inspect the current script language and trigger members. ADR `0002-map-script-ownership.md` chooses MCP-owned JASS source for the existing `war3map.j` entry point while preserving GUI/custom-text members opaque. Explain World Editor save effects, entry-point connection, round-trip behavior, static parser checks, and user limitations. Lua and GUI trigger mutation remain disabled until separately proven.

## Tools

### `wc3_validate_transaction`

Extend it with serialization/build validators. Return transaction/revision/source hash, validator version, errors/warnings/info, component/target, suggested action, buildable flag, and report path/hash.

Implemented: validation reports are persisted under the transaction staging root, include stable finding codes/severity/components/suggested actions, and the tool returns `report_path` and `report_sha256` for the exact validated revision.

### `wc3_build_map`

Input project, transaction, expected revision/hash, debug build profile, optional safe label.

Algorithm:

1. acquire project/transaction lock;
2. verify validated exact revision;
3. verify source hash has not drifted;
4. create unique temporary build directory beneath build root;
5. serialize typed-write members;
6. copy opaque members byte-for-byte;
7. construct archive with one documented writer;
8. flush/close and hash output;
9. reopen and re-inspect output;
10. compare output to intended staged model;
11. atomically rename to final unique output;
12. write manifest/report;
13. transition transaction to `built`;
14. return path/hash/warnings with `runtime_status: untested`.

Never accept an arbitrary absolute output path.

Implemented: the server supplies a generated output path beneath the configured build root; the engine refuses source overwrite, existing outputs, extension drift, unsupported staged components, and any reopen/preservation mismatch. Failed builds emit a failure artifact and do not leave a final output directory.

### `wc3_build_report`

Return build manifest by ID and verify the artifact still matches its recorded hash.

### `wc3_promote_build`

Add only after correctness is proven. Copy to an explicitly configured destination, never default to source. Require expected build hash, refuse drift, and audit source/destination/copy hash.

Implemented and approval-gated: promotion requires a hash-checked built artifact plus recorded smoke/playtest evidence, uses only the configured `test_map_root`, refuses overwrite, and writes an audit record.

## Validators

- required members and format versions;
- preservation plan for every source member;
- duplicate/malformed rawcodes;
- detectable dangling references;
- player/force contradictions;
- coordinates outside bounds;
- import traversal/duplicates/size limits;
- missing/ambiguous script entry or disconnected source;
- WTS integrity;
- extension/map-info agreement;
- output collision/path policy;
- protected HTW region names and explicit teams.

State when checks are impossible due to opaque data and classify the risk according to the compatibility matrix.

## Determinism

Use stable ordering and serialization. Build timestamp belongs in manifest, not gameplay data. Whole MPQ bytes may differ due to compression/order; canonical semantics and opaque hashes must match. Every artifact gets SHA-256 identity.

## Test sequence

1. invalid fixture fails and produces no promotable output;
2. minimal synthetic fixture builds/reopens;
3. Hero Team Wars no-op transaction builds/reopens;
4. no-op membership matches except documented metadata;
5. opaque hashes match;
6. parsed semantics match;
7. one supported change yields exactly intended diff;
8. original source hash unchanged;
9. manually open/load no-op and changed builds for compatibility evidence until Phase 4 automates launch.

Automated evidence completed: the current source baseline validates with zero errors; engine tests cover no-op reopen, deterministic canonical/member comparisons, opaque preservation, unsupported-change rejection, malformed fixtures, and script ownership; MCP integration covers exact-revision no-op build/report behavior and cleanup of temporary build directories. The source hash remained `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.

## Failure behavior

- Validation error: no final artifact.
- Serializer error: failed report/log; no promotion.
- Reopen mismatch: failed build, no final rename.
- Source drift: invalidate attempt.
- Editor/game failure: artifact remains `built` but not `tested`.

## Completion gate and handoff

The implementation gate is complete: no-op/minimal outputs re-inspect, opaque data is preserved, reports are complete, invalid builds do not promote, and the original is unchanged. The overall Phase 3 compatibility gate remains open until one exact no-op build is observed opening in World Editor and loading in Warcraft III; promotion stays approval-gated.

Handoff build contracts, schemas, writer versions, script ADR, validators, build IDs/hashes/paths, compatibility evidence, and limitations.
