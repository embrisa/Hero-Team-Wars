# WC3 Map MCP

Project scaffold for the local Warcraft III map MCP described in `../../tasks/wc3-map-mcp/README.md`.

Status: Phase 1 read-only inspector implemented and verified. The map engine uses War3Net 6.0.3 for MPQ access and WC3 metadata/region parsing; the default MCP configuration exposes only project status, inspection, archive listing, component lookup, read-only validation, and comparison. Later transaction/build/launch code remains disabled and is not visible through the Phase 1 server.

Current evidence: the local source has 17 classified archive members; `war3map.w3i`, `war3map.w3r`, and `war3map.wts` parse read-only; a no-op rebuild reopened successfully with all member content hashes preserved; and the original source hash remained `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`. World Editor/game runtime evidence is still a manual gate, and script/object/trigger mutation remains disabled.

## Agent entry point

Read `AGENTS.md` before working here. The task directory provides a complete no-chat-history orientation, live prerequisite snapshot, MCP SDK guidance, WC3 archive/member reference, implementation conventions, and detailed phase work packets.

Use `scripts/bootstrap.ps1`, `scripts/build.ps1`, and `scripts/test.ps1` from this directory. The checked-in example configuration is safe to copy to `config/wc3-map-mcp.local.json`; it never replaces the source map.

The trusted-project Codex entry is `.codex/config.toml` at the Hero Team Wars root. Build first so `mcp-server/dist/index.js` exists, then restart/reload the Codex client to pick up a newly added project MCP server.

## Planned components

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

The existing map remains at `../../map/HeroTeamWars_M0_2Arena.w3m`. Development outputs must go to configured staging/build locations and never overwrite it.

Last observed source SHA-256: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`. Every inspection, transaction, and build recomputes it.
