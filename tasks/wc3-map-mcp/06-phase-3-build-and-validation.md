# Phase 3 Work Packet - Map Build and Validation

Status: blocked until Phase 2 has validated transactions and Phase 0 permits writing.

This phase serializes staged state and creates new map artifacts. The challenge is not merely creating an MPQ; it is preserving unsupported data and producing something the installed World Editor and Warcraft III accept.

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

Inspect the current script language and trigger members. ADR `0002-map-script-ownership.md` must choose editor-owned GUI/custom-text script or build-owned JASS/Lua injection. Explain World Editor save effects, entry-point connection, round-trip behavior, and user limitations. If uncertain, disable script mutation while permitting proven non-script builds.

## Tools

### `wc3_validate_transaction`

Extend it with serialization/build validators. Return transaction/revision/source hash, validator version, errors/warnings/info, component/target, suggested action, buildable flag, and report path/hash.

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

### `wc3_build_report`

Return build manifest by ID and verify the artifact still matches its recorded hash.

### `wc3_promote_build`

Add only after correctness is proven. Copy to an explicitly configured destination, never default to source. Require expected build hash, refuse drift, and audit source/destination/copy hash.

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

## Failure behavior

- Validation error: no final artifact.
- Serializer error: failed report/log; no promotion.
- Reopen mismatch: failed build, no final rename.
- Source drift: invalidate attempt.
- Editor/game failure: artifact remains `built` but not `tested`.

## Completion gate and handoff

Complete when no-op and minimal outputs re-inspect, opaque data is preserved, reports are complete, original is unchanged, and the no-op is observed opening in World Editor and loading in Warcraft III. Promotion stays approval-gated.

Handoff build contracts, schemas, writer versions, script ADR, validators, build IDs/hashes/paths, compatibility evidence, and limitations.
