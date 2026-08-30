# Phase 1 Work Packet - Read-Only MCP Inspector

Status: complete (implemented and verified 2026-08-30).

Phase 0 cleared this packet with `GO_READONLY_AND_NOOP_BUILD_WITH_MANUAL_RUNTIME_GATE`. The default checked-in configuration and the project-scoped Codex configuration expose exactly the six read-only tools in this packet.

This creates the first real MCP server. It must remain useful even if rebuilding is unsafe. No mutating, build, launch, or deletion tools are exposed.

## Required reading and inputs

Read the four mandatory context files, `03-phase-0-discovery.md`, `09-mcp-tool-contracts.md`, `10-map-model-and-contracts.md`, `12-testing-and-definition-of-done.md`, the Phase 0 ADR, and compatibility report.

Use the exact versions and commands delivered by Phase 0.

## Goal

Codex and MCP Inspector can connect over local STDIO and inspect the configured Hero Team Wars project without arbitrary filesystem access. Results include concise text, versioned structured data, provenance, and explicit unknown/unsupported values.

## Files to create or complete

```text
mcp-server/src/index.ts
mcp-server/src/server.ts
mcp-server/src/config/{schema,load-config,resolve-project}.ts
mcp-server/src/errors/{app-error,map-error}.ts
mcp-server/src/schemas/{common,tools,worker}.ts
mcp-server/src/transport/{ndjson,worker-client}.ts
mcp-server/src/services/{project-service,inspection-service}.ts
mcp-server/src/tools/{register-tools,project-status,inspect-map,list-archive-files,get-component,compare-maps,validate-map}.ts
mcp-server/test/unit/*
mcp-server/test/integration/*
contracts/schemas/canonical-map.schema.json
```

Complete engine handlers/canonical types required by these tools. Do not implement writes early.

## Startup behavior

Use `createServer()` and `serveStdio(createServer)`. Set server instructions to the inspect-before-change workflow. At startup load/validate config, resolve safe roots, verify engine exists, avoid eager map parsing, remain within startup timeout, and write diagnostics to stderr only.

## Tools

### `wc3_project_status`

Input: `project_id`. Return root/source labels, source existence/size/time/hash, baseline drift, engine/server/runtime versions, configured game/editor presence/version, capability summary, enabled tools, and later tools still disabled. Server instructions should recommend this first.

### `wc3_inspect_map`

Input: project ID, allowed project-relative map path, optional section enum, `include_provenance`, and bounded `max_items_per_section`. Return canonical summary and full JSON artifact path/hash when large. Include the exact map hash inspected.

### `wc3_list_archive_files`

Input: project/map plus optional prefix/cursor. Return path, sizes, hash, capability classification, and parser data in stable order. Reject traversal.

### `wc3_get_component`

Input: project/map, component enum, and optional cursor/filter. Do not accept arbitrary member paths. Supported component names come from Phase 0, such as metadata, players, forces, regions, triggers, variables, unit object data, placed units, or imports. Opaque requests return `UNSUPPORTED_COMPONENT` plus reason.

### `wc3_validate_map`

Read-only validation. Return error/warning/info findings with code, component, target identity, provenance, message, and suggested remediation. It does not mutate or build.

### `wc3_compare_maps`

Compare two artifacts beneath configured read roots. Return member-level and semantic differences. Do not present MPQ compression/order differences as gameplay changes.

## Canonical behavior

- Stable ordering for all collections.
- UTC ISO 8601 timestamps.
- Rawcodes as four characters plus numeric form when helpful.
- WTS strings include stored token and resolved text.
- Deterministic float serialization without lossy rounding.
- Binary/unsupported members return metadata/hash, not bytes.
- Every section states capability and provenance.

## HTW-00 candidate report

Generate:

- `artifacts/hero-team-wars/htw-00-candidate.json`;
- `artifacts/hero-team-wars/htw-00-candidate.md`.

Match the `HTW-00 STATE REPORT` headings from `design/08-implementation-chunks.md`. Separate archive observations, editor verification needs, ledger conflicts, and unsupported/absent values. Do not update `design/07-editor-state.yaml` automatically.

## Security/path tests

Reject `..`, absolute paths in relative fields, same-prefix sibling roots, unknown project IDs, junction/symlink escape when applicable, directories where maps are required, and maps outside configured read roots.

## MCP tests

- Inspector initialization and exact tool list;
- invalid input schema rejection;
- text plus structured output;
- expected domain errors use `isError: true`;
- no stdout contamination;
- worker timeout/crash/malformed JSON mapping;
- repeated inspection yields identical canonical JSON;
- source hash remains unchanged.

Manually call project status, map inspection, one supported component, one unsupported component, and validation in Inspector. Then connect Codex and confirm only read-only tools are visible.

## Completion gate and handoff

Complete when all read-only tools work over STDIO, path escapes are blocked, canonical output is reproducible, HTW-00 candidate reports exist, and source hash is unchanged. No writer tool may be visible.

Handoff exact tool/schema/model/protocol versions, config path, test evidence, unsupported components, report paths, and source hashes.

## Completion evidence

- MCP server: `wc3-map-mcp` `0.1.0`, `@modelcontextprotocol/server` `2.0.0`, Zod `4.5.4`, worker protocol `1.0`, canonical schema `1.0`.
- Runtime/toolchain: Node `24.19.0`, npm `11.17.0`, .NET SDK `10.0.400`, .NET runtime `10.0.11`, War3Net `6.0.3`.
- Runtime config: `tools/wc3-map-mcp/config/wc3-map-mcp.example.json` (`read_only`, exact seven-tool inspection allow list).
- Codex config: `.codex/config.toml` (`wc3_map`, STDIO, project-local writes profile with the seven inspection tools plus the reviewed transaction/build/launch/evidence tools).
- Build/test command: from `tools/wc3-map-mcp`, run `./scripts/build.ps1` and `./scripts/test.ps1`.
- Automated evidence: 29/29 .NET tests and 37/37 TypeScript/MCP tests passed. Coverage includes exact tool visibility, schema errors, text plus structured content, expected `isError`, clean stdout, worker timeout/crash/malformed/multiple-response mapping, stable repeated inspection, source preservation, traversal/absolute/sibling-root/directory/outside-root/symlink policy, validation hash, component status, semantic/container diff separation, and MCP-owned JASS source staging/build round-trip.
- MCP Inspector CLI manually initialized the built STDIO server, listed exactly the seven read-only tools, and called project status, metadata inspection, supported `regions`, unsupported `variables`, and validation. The unsupported component returned `UNSUPPORTED_COMPONENT` with `isError: true`.
- The checked-in example configuration and an explicit STDIO smoke test confirm the seven inspection tools and no mutation/build/launch tools in read-only mode. The active project-local configuration exposes the reviewed write path, including `wc3_get_script_source` and `set_script_source`; a desktop task that was already open before the config was changed still requires restart/reload.
- HTW-00 reports: `tools/wc3-map-mcp/artifacts/hero-team-wars/htw-00-candidate.json` and `.md`. They retain separate archive observations, editor verification needs, ledger conflicts, and unsupported/absent values, and match the `HTW-00 STATE REPORT` fields. `design/07-editor-state.yaml` was not changed.
- Parsed read-only components: metadata, players, forces, regions, archive inventory/capabilities, and direct `war3map.j` source read-back. Cameras, GUI/custom-text triggers, variables, object data, placed-object detail, terrain detail, and imports remain explicit opaque/unsupported values rather than inferred data.
- Source SHA-256 before and after: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- World Editor and Warcraft III were not launched in Phase 1; runtime evidence remains `built_reopened_by_engine_only` from Phase 0 and is not claimed as editor/game verification.
