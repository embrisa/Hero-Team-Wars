# WC3 Map MCP

Project scaffold for the local Warcraft III map MCP described in `../../tasks/wc3-map-mcp/README.md`.

Status: Phase 5 MCP-native typed compiler implementation is present for gameplay source composition, trigger/variable manifests, regions, object data, placements, format-33 players/forces, typed map flags, profile-driven teams, generated team registries, deterministic scenarios, and evidence artifacts. Player `fixed_start_position` is kept distinct from lobby slot availability; the latter is controlled by the native controller and custom-force map flags. Exact World Editor/Warcraft III compatibility and runtime acceptance remain manual gates; GUI-trigger compatibility is not enabled.

Current evidence: the source still has 17 classified archive members and its SHA-256 is `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`; typed War3Net codecs pass no-op/changed round-trip tests for map info, regions, placed units/buildings, and an all-seven-member object-data fixture; MCP-native JASS composition is deterministic, statically parsed, emitted in function dependency order with forward-reference validation, and checks MCP-owned function call arity; the deterministic harness reports 20/20 scenarios passed across two repeats; the Phase 5E source-to-build-to-record integration workflow passes; and the MCP transaction/build/evidence tools preserve source hashes, revisions, build hashes, and optional observation-session links. The observed suite is 70 .NET tests and 43 MCP tests, with `evidence_level=static_only` and `runtime_verified=false`. The checked-in example configuration remains read-only. The project-local writes configuration enables the reviewed mutation path and `mvp_2arena` profile. Exact World Editor/game acceptance remains manual. See `docs/compatibility/launch-behavior.md` for the launch contract.

## Agent entry point

Read `AGENTS.md` before working here. The task directory provides a complete no-chat-history orientation, live prerequisite snapshot, MCP SDK guidance, WC3 archive/member reference, implementation conventions, and detailed phase work packets.

Use `scripts/bootstrap.ps1`, `scripts/build.ps1`, and `scripts/test.ps1` from this directory. The checked-in example configuration is safe to copy to `config/wc3-map-mcp.local.json`; it never replaces the source map.

The trusted-project Codex entry is `.codex/config.toml` at the Hero Team Wars root. Build first so `mcp-server/dist/index.js` exists, then restart/reload the Codex client to pick up a newly added project MCP server.

## Components

- `mcp-server/`: TypeScript STDIO MCP server and orchestration layer.
- `map-engine/`: .NET/War3Net map inspection, mutation, validation, and build engine.
- `contracts/`: versioned schemas shared across the process boundary.
- `config/`: non-secret project and runtime configuration examples.
- `docs/`: implementation-specific technical documentation.
- `scripts/`: explicit setup/build/test helpers added during implementation.
- `tests/`: cross-component fixtures and integration tests.
- `artifacts/`: generated reports and build manifests; not source maps.
- `logs/`: MCP-owned diagnostic logs.
- `snapshots/`: MCP-owned recovery snapshots.
- `scripts/mcp/`: deterministic MCP-native JASS modules and gameplay manifest.

The Phase 5 gameplay tools are `wc3_compose_gameplay_source`,
`wc3_validate_gameplay_source`, `wc3_prepare_gameplay_chunk`,
`wc3_run_scenario_build`, and `wc3_record_chunk_result`. They are subject to
the same inspect -> stage -> diff -> validate -> build -> observe workflow as
the earlier map tools. Composer and scenario results never upgrade themselves
to editor-open, game-loaded, smoke-test, or playtest evidence.

The existing map remains at `../../map/HeroTeamWars_M0_2Arena.w3m`. Development outputs must go to configured staging/build locations and never overwrite it.

Last observed source SHA-256: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`. Every inspection, transaction, and build recomputes it.
