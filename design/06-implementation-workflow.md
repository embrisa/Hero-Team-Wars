# 6. Editor and MCP Implementation Workflow

Status: Active

This project uses a split source-of-truth model. The design documents describe
what the game should do. The editor-state ledger describes what is actually
configured in the current map. MCP-owned source and typed component manifests
describe the implementation to build. The saved `.w3m` remains the final
playable artifact.

## The four layers

| Layer | Location | Authority |
|---|---|---|
| Rules and design intent | `design/01` through `design/05` | What the game should mean and how it should behave |
| Editor state | `design/07-editor-state.yaml` | What the current map is known to contain |
| Logic source and handoff notes | `scripts/`, MCP manifests, and `design/08-implementation-chunks.md` | The implementation we are preparing or have applied |
| Playable map | `map/HeroTeamWars_M0_2Arena.w3m` plus saved test copies | What Warcraft III actually loads |

If these layers disagree, we do not silently guess. We record the disagreement, decide which layer should win, and then update the other layer.

## Recommended implementation model

Use MCP for gameplay source, runtime triggers, deterministic logic, and every
map component whose parser/serializer has passed its round-trip gate. Use the
World Editor for the parts that are safest and most useful to inspect there:

- terrain, camera boundaries, and regions;
- player slots, forces, starting locations, and alliances;
- units, shops, heroes, items, abilities, and their object-data values;
- map metadata and imported assets;
- compatibility inspection and manual runtime observation.

Keep gameplay logic in named MCP implementation chunks. The source composer
generates a complete `war3map.j`, and the MCP stages/builds it without requiring
World Editor to compile or connect pasted code.

Important limitation: importing a `.j` file into the map does not, by itself,
make Warcraft III execute it. The selected MCP-owned source pipeline must
replace the verified `war3map.j` entry point, or an editor-compatible trigger
pipeline must connect the source. The current MCP-owned JASS path is the
verified compiler pipeline; we still require exact map-load and gameplay
evidence.

## Automation options

### Safe mode — MCP build plus editor observation

This is the default workflow now:

1. MCP inspects the current source hash and editor-state prerequisites.
2. A source/component transaction stages the smallest typed change and exposes a semantic diff.
3. MCP validates and builds a uniquely named test map.
4. World Editor or Warcraft III opens the exact build and the acceptance test is observed.
5. MCP records the evidence; the ledger and change log are updated before the next chunk.

This keeps the source map untouched and makes each generated build reversible.

### MCP-owned source mode — selected gameplay path

The MCP owns the project source modules, trigger/variable manifests, and the
generated JASS entry point. Typed regions, objects, placements, players, teams,
and forces are added only after their individual serializer and fixture gates
pass. This mode reduces copy/paste but does not remove exact editor/game
compatibility testing.

### Avoid — direct binary patching as the normal workflow

The `.w3m` is a binary container, not a dependable text source. Directly rewriting it without a tested map tool can damage triggers, object data, imports, or editor compatibility. We keep recoverable map copies and use the editor or a verified compiler pipeline as the writer.

## Implementation chunk protocol

Every map change gets an ID such as `HTW-00`, `HTW-01`, or `HTW-02A`.

Each chunk must state:

- goal and scope;
- exact prerequisites from the editor-state ledger;
- files or code blocks involved;
- exact MCP operations and any required editor actions;
- acceptance test;
- what the user must report back;
- known limitations and the next chunk.

Chunks should be small enough that a failed test identifies one likely cause. A
chunk is not considered applied merely because an MCP operation or build
completed; it is applied only after the resulting exact build has the required
observable test result.

## Naming contract

Use stable names so the editor and the documentation can refer to the same things:

- trigger folders: `HTW | 00 Foundation`, `HTW | 01 Phase`, and so on;
- variables: `HTW_` followed by the purpose, such as `HTW_CurrentRound`;
- regions: preserve existing exact names; new regions use `HTW_` prefixes;
- units and abilities: record the editor name and four-character rawcode;
- map-wide tuning: keep one documented value per concept, not duplicated literals across triggers;
- debug output: prefix with `[HTW]` and include the wave or chunk ID when relevant.

Team identity must use explicit team assignments. Player color is presentation, not gameplay identity.

## Editor-state update rules

The ledger uses `unknown`, `not_verified`, and `not_applicable` instead of invented values. For every editor-owned object, record:

- editor display name;
- rawcode, when available;
- owner or owning force;
- relevant stats;
- abilities and levels;
- the map location or region;
- the last verification date;
- the chunk that created or changed it.

If a value is changed in the editor, update the ledger in the same work session. If it is not checked, leave the old value marked as stale rather than presenting it as current.

## First action after this workflow is adopted

Complete `HTW-00 — Baseline capture`. Open the current map, do not change it,
and record the trigger tree, variables, regions, player/force setup, and every
configured hero/unit/ability that matters to the next chunk. Then follow the
MCP feature packets `tasks/wc3-map-mcp/14` through `19`. The current ledger
contains only the previously verified baseline and explicitly marks the gaps
that need reconfirmation.
