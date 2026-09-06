# WC3 Map MCP

The [v26 implementation](docs/compatibility/v26-preparation-and-sends.md) adds
personal Camp purchases, mixed send queues, frozen wave plans and multiboards.
Typed array/multiboard variable support is aligned across MCP/engine/schema;
grouped boolean JASS validation is repaired. Tool names and evidence gates are
unchanged. `scripts/test.ps1` checks catalog generation and production JASS with
mocked natives before engine/MCP suites; this is not Warcraft runtime evidence.
Object definitions compare by stable identity across archive regrouping, while
field values and modification order remain part of semantic comparison.

The [v24 repair](docs/compatibility/v24-controller-load-repair.md) fixes native
boolean object-field encoding. Authoring keeps logical `Bool`; binary output
uses a four-byte integer. Byte-level tests supplement codec round-trip tests.

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
safety rules, and verification requirements. Historical investigations are
kept separately in `work-items/`; they do not define the current task.

The [engineering audit](docs/engineering-audit.md) records known structural
limitations and regression fixes. `scripts/test.ps1` tests .NET, republishes
that exact engine for the MCP suite, and verifies source-map preservation even
when a test fails. Runtime observations remain user-owned.

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
The v23 follow-up adds explicit hero flags, existing-game icons, and correctly
typed Channel metadata; see
[`docs/compatibility/v23-controller-ability-metadata.md`](docs/compatibility/v23-controller-ability-metadata.md).

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

## Object field knowledge and authoring

Use `wc3_object_field_search` and `wc3_object_field_lookup` before editing
unfamiliar field IDs. The shared offline catalog powers readable field names,
annotated inspection/diffs and enforced field semantics. Unknown legacy fields
remain unchanged; newly authored fields must match verified metadata. See
[object-field authoring](docs/reference/object-field-authoring.md) for exact input, scope, evidence,
preservation and regeneration rules. This is a tooling change; existing maps
and historical authoring fixtures are not automatically migrated.

Controller v25 uses the expanded 60-field offline catalog for QWER cast/learn
hotkeys, button positions, learn icons/tooltips, rank and technology requirements,
and Channel disable-other-abilities. Hotkeys serialize as native strings; tool
names, request schemas, and transaction/evidence gates are unchanged.
