# 6. Editor-Led Implementation Workflow

Status: Active

This project uses a split source-of-truth model. The design documents describe what the game should do. The editor-state ledger describes what is actually configured in the current map. Source files describe the logic we intend to place into the map. The saved `.w3m` remains the final playable artifact.

## The four layers

| Layer | Location | Authority |
|---|---|---|
| Rules and design intent | `design/01` through `design/05` | What the game should mean and how it should behave |
| Editor state | `design/07-editor-state.yaml` | What the current map is known to contain |
| Logic source and handoff notes | `scripts/` and `design/08-implementation-chunks.md` | The implementation we are preparing or have applied |
| Playable map | `map/HeroTeamWars_M0_2Arena.w3m` plus saved test copies | What Warcraft III actually loads |

If these layers disagree, we do not silently guess. We record the disagreement, decide which layer should win, and then update the other layer.

## Recommended implementation model

Use the World Editor for the parts that are safest and most useful to inspect there:

- terrain, camera boundaries, and regions;
- player slots, forces, starting locations, and alliances;
- units, shops, heroes, items, abilities, and their object-data values;
- map metadata and imported assets;
- a small amount of bootstrap or test-trigger glue.

Keep gameplay logic in named implementation chunks. I can write those chunks as exact JASS/custom-script text and give you the editor actions needed to place them. The editor then compiles the code when the map is saved.

Important limitation: importing a `.j` file into the map does not, by itself, make Warcraft III execute it. The source must be connected to the map's generated script through a custom-text trigger, an editor-supported script system, or a separately verified compiler pipeline. We will not assume that an external compiler or map patcher is installed until it is checked.

## Automation options

### Safe mode — editor plus generated code

This is the default workflow now:

1. I write a small, self-contained code chunk and its prerequisites.
2. You create or update the named trigger/custom-text block in World Editor.
3. You save the map, launch it, and run the acceptance test.
4. You report the observed result and any editor values that were different from the plan.
5. I update the state ledger and change log before preparing the next chunk.

This avoids trying to patch the binary `.w3m` directly and keeps each editor action reversible.

### Compiler mode — source files compiled into the map

Later, we can test a JASS/vJASS/Wurst-style source pipeline if you want one. In that setup, `scripts/` becomes the primary logic source and a compiler or map tool injects the result into the map. Before adopting it, we must verify the exact Warcraft III/Reforged version, installed toolchain, map format support, and whether object-data changes are still expected to happen in the editor.

Compiler mode can reduce copy/paste, but it does not remove the need to document editor-owned rawcodes, regions, object data, and map settings. It also adds a new build failure mode, so it is not the first dependency for proving the two-arena loop.

### Avoid — direct binary patching as the normal workflow

The `.w3m` is a binary container, not a dependable text source. Directly rewriting it without a tested map tool can damage triggers, object data, imports, or editor compatibility. We keep recoverable map copies and use the editor or a verified compiler pipeline as the writer.

## Implementation chunk protocol

Every map change gets an ID such as `HTW-00`, `HTW-01`, or `HTW-02A`.

Each chunk must state:

- goal and scope;
- exact prerequisites from the editor-state ledger;
- files or code blocks involved;
- exact editor actions;
- acceptance test;
- what the user must report back;
- known limitations and the next chunk.

Chunks should be small enough that a failed test identifies one likely cause. A chunk is not considered applied merely because the editor accepted a paste; it is applied only after a save and an observable test result.

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

Complete `HTW-00 — Baseline capture`. Open the current map, do not change it, and record the trigger tree, variables, regions, player/force setup, and every configured hero/unit/ability that matters to the next chunk. The current ledger contains only the previously verified baseline and explicitly marks the gaps that need reconfirmation.
