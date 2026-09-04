# WC3 Map MCP

Live project-local Warcraft III map MCP implementation. Maintained agent-facing
documentation is indexed in [`docs/README.md`](docs/README.md).

Status: MCP-native typed compiler implementation is present for gameplay source
composition, trigger/variable manifests, regions, object data, placements,
format-33 players/forces, typed map flags, profile-driven teams, generated team
registries, deterministic scenarios, and evidence artifacts. Player
`fixed_start_position` is kept distinct from lobby slot availability; the
latter is controlled by the native controller and custom-force map flags.
Exact World Editor/Warcraft III compatibility and runtime acceptance remain
manual gates; GUI-trigger compatibility is not enabled. See
`docs/compatibility/launch-behavior.md` for the launch contract and generated
reports for current build/test evidence.

## Agent entry point

Read `AGENTS.md` before working here. The documentation directory provides the
current agent guide, MCP contract catalog, architecture/reference material,
safety rules, and verification requirements. Active implementation work items
are kept separately in `work-items/`.

Use `scripts/bootstrap.ps1`, `scripts/build.ps1`, and `scripts/test.ps1` from this directory. The checked-in example configuration is safe to copy to `config/wc3-map-mcp.local.json`; it never replaces the source map.

Bootstrap also generates the one canonical local JASS API index from the exact pinned jassdoc commit. Upstream source and generated data remain ignored because jassdoc's redistribution license is not stated. After bootstrap, lookup and validation are fully offline.

The trusted-project Codex entry is `.codex/config.toml` at the Hero Team Wars root. Build first so `mcp-server/dist/index.js` exists, then restart/reload the Codex client to pick up a newly added project MCP server.

## Components

The v21 two-team Controller kit is described in
[`docs/compatibility/v21-controller-spells.md`](docs/compatibility/v21-controller-spells.md).
It uses typed ability definitions, Channel/Slow parent compatibility, and
MCP-owned spell dispatch. The public tool schemas and runtime evidence gates
are unchanged; the exact artifact still needs the user's Warcraft III checks.
The v22 follow-up stores those skills in H003's hero-ability field; see
[`docs/compatibility/v22-h003-ability-attachment.md`](docs/compatibility/v22-h003-ability-attachment.md).

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

The gameplay-source and scenario tools are `wc3_compose_gameplay_source`,
`wc3_validate_gameplay_source`, `wc3_prepare_gameplay_chunk`,
`wc3_run_scenario_build`, and `wc3_record_chunk_result`. They are subject to
the same inspect -> stage -> diff -> validate -> build -> observe workflow as
the earlier map tools. Composer and scenario results never upgrade themselves
to editor-open, game-loaded, smoke-test, or playtest evidence.

The global read-only JASS tools are `jass_lookup`, `jass_search`,
`jass_validate_call`, and `jass_validate_source`. Use search when the operation
is known but the exact API name is not, then lookup for the exact jassdoc
signature and annotations. All script-producing transaction paths invoke the
same validator automatically before a new revision can be published.

For the complete contract of every exposed tool—including exact request fields,
response envelopes, typed operation families, policy gates, artifact/hash
links, and runtime evidence rules—read
[`docs/reference/tool-contracts.md`](docs/reference/tool-contracts.md) before
calling the MCP.

The existing map remains at `../../map/HeroTeamWars_M0_2Arena.w3m`. Development outputs must go to configured staging/build locations and never overwrite it.

Last observed source SHA-256: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`. Every inspection, transaction, and build recomputes it.
