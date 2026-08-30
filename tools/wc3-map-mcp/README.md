# WC3 Map MCP

Project scaffold for the local Warcraft III map MCP described in `../../tasks/wc3-map-mcp/README.md`.

Status: structure only. No server, map engine, package installation, build, or test has been implemented yet.

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
