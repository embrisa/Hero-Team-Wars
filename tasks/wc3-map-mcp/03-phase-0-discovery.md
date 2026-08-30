# Phase 0 Work Packet - Prerequisites and Compatibility Baseline

Status: not started.

This is the first implementation task. It does not add public MCP tools and it does not modify the source map. Its purpose is to make later implementation decisions from observed compatibility evidence rather than assumptions.

## Required reading

Read `AGENT-START-HERE.md`, both `REFERENCE-*` files, `IMPLEMENTATION-CONVENTIONS.md`, `01-decisions-and-constraints.md`, `02-architecture.md`, `10-map-model-and-contracts.md`, and `11-safety-recovery-and-audit.md` before acting.

Also inspect `design/06-implementation-workflow.md`, `design/07-editor-state.yaml`, `design/08-implementation-chunks.md`, and the current `tools/wc3-map-mcp/` contents.

## Starting facts to verify

- Source map: `map/HeroTeamWars_M0_2Arena.w3m`.
- Recorded SHA-256: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- World Editor and game: `C:\Warcraft III\_retail_\x86_64`, version `2.0.4.23745` when last checked.
- Node 24.19.0 and npm 11.17.0 were most recently observed.
- .NET SDK 10.0.400 and runtime/host 10.0.11 were most recently observed.

These are evidence from 2026-08-30. Record refreshed values in the compatibility report.

## Goal

Deliver a buildable minimal TypeScript workspace, a buildable minimal .NET/War3Net solution, and a read-only engine command that inventories the current map archive and classifies every member. Prove whether a no-op rebuild is safe enough to continue.

## User authorization boundary

Installing a newer Node release or .NET SDK changes the machine. If the assigning user did not explicitly authorize prerequisites, stop after producing an exact prerequisite proposal. State proposed versions, official source, why each is required, the existing version affected, whether side-by-side installation is possible, and how installation will be verified.

Do not substitute portable runtimes in an arbitrary folder merely to avoid asking.

## Dependency decisions

1. Select a supported Node 20+ LTS compatible with the current `@modelcontextprotocol/server` release.
2. Select a supported .NET SDK target that can consume the selected War3Net packages.
3. Pin exact stable versions for MCP server, Zod, TypeScript, test runner, War3Net packages, and schema tooling.
4. Record package licenses and repository tag/commit.
5. Decide whether the engine is initially one-request/process or persistent NDJSON. Prefer isolation until correctness is proven.

Create `tools/wc3-map-mcp/docs/decisions/0001-initial-dependency-stack.md` with the decision and alternatives.

## Files to create

```text
tools/wc3-map-mcp/
  .gitignore
  config/wc3-map-mcp.example.json
  docs/decisions/0001-initial-dependency-stack.md
  docs/compatibility/hero-team-wars-baseline.md
  docs/compatibility/hero-team-wars-baseline.json
  scripts/bootstrap.ps1
  scripts/build.ps1
  scripts/test.ps1
  scripts/inspect-baseline.ps1
  mcp-server/package.json
  mcp-server/package-lock.json
  mcp-server/tsconfig.json
  mcp-server/src/index.ts
  mcp-server/test/environment.test.ts
  map-engine/Wc3MapEngine.sln
  map-engine/Directory.Build.props
  map-engine/src/Wc3MapEngine.Contracts/*
  map-engine/src/Wc3MapEngine.Core/*
  map-engine/src/Wc3MapEngine.Cli/*
  map-engine/tests/Wc3MapEngine.Tests/*
  contracts/schemas/engine-request.schema.json
  contracts/schemas/engine-response.schema.json
  contracts/schemas/archive-inventory.schema.json
```

Use names consistent with `IMPLEMENTATION-CONVENTIONS.md`. Remove `.gitkeep` placeholders when real files occupy the directory.

## Engine operations required

Implement these internal CLI/worker operations; do not expose them through MCP yet.

### `environment_status`

Return engine version, OS/runtime architecture, .NET runtime, dependency versions, and whether configured files exist. Do not search the whole filesystem.

### `hash_file`

Return file size, last-write UTC, and SHA-256 for an already policy-validated path. This is an internal primitive, never a future arbitrary-path MCP tool.

### `list_archive_members`

Open the map read-only and return every member name, sizes if available, content hash, and whether War3Net recognizes it.

### `probe_map`

Parse each supported `war3map.*` member independently. One parse failure must not erase the inventory. Per-member result:

```json
{
  "path": "war3map.w3i",
  "status": "parsed_read_only",
  "parser": "War3Net package/type",
  "parser_version": "pinned version",
  "warnings": [],
  "error": null
}
```

Use `preserved_opaque` for unknown members that can be copied exactly and `unsupported_blocking` if safe preservation is not possible.

## Discovery procedure

1. Recompute and record the source hash.
2. Inspect exact game/editor executable versions and map/test directories.
3. Verify Node/npm and `dotnet --info`.
4. Resolve dependency versions and record the ADR.
5. After authorization, install prerequisites from official supported sources and verify in a fresh process.
6. Scaffold TypeScript and .NET projects.
7. Make scripts work from the MCP root without depending on caller current directory.
8. Copy the source into a unique MCP fixture workspace.
9. Run inventory and parser probes on the copy.
10. Generate JSON and Markdown compatibility reports.
11. Attempt a no-op rebuild only if every member is parsed or preservable.
12. Compare membership, opaque hashes, and parsed semantics.
13. Open the separately named rebuild in World Editor.
14. If it opens, test-load that exact copy in Warcraft III.
15. Record the actual evidence level.
16. Recompute and verify the original source hash.

## No-op rebuild rules

- Use a name such as `HeroTeamWars_M0_2Arena_MCP_P0_noop_<timestamp>.w3m`.
- Never output to the source path.
- Preserve every opaque member byte-for-byte.
- Report compression/order changes separately from semantic changes.
- If listfile/attributes handling is uncertain, block the rebuild.
- Compatibility requires both editor and game observations.

## Tests to implement and run

- request/response schema round trip;
- SHA-256 known vector;
- truncated/non-MPQ rejection without crash;
- stable archive inventory order;
- isolated member parse failure;
- read-only source access;
- opaque-member extraction hash;
- no-op canonical comparison when allowed;
- scripts work from another current directory;
- stdout contains only one-line JSON in worker mode;
- original hash before/after is identical.

## Required deliverables

- dependency ADR;
- compatibility JSON and Markdown;
- complete member capability matrix;
- build/test command evidence;
- source and no-op hashes;
- observed editor/game result or explicit reason not run;
- Phase 1 go/no-go recommendation.

## Completion gate and handoff

Complete only when the environment is reproducibly buildable, every current-map member is classified, source hash is unchanged, and the report states whether inspection and no-op rebuild are safe. If rebuilding is blocked, Phase 1 may expose read-only tools only.

The handoff must name exact versions, commands, engine entry path, report paths, supported reads, disabled writes, opaque members, source hash, and evidence level.
