# 8. Implementation Chunks

Gameplay source and proven typed map-component changes are applied through the
WC3 Map MCP. World Editor is used for the HTW-00 baseline, visual inspection,
unsupported editor data, and exact runtime observations. The detailed MCP
feature packets are in `tasks/wc3-map-mcp/14` through `19`.

This is the active queue for editor work. A chunk moves from `planned` to `applied` only after the map has been saved and the stated acceptance test has been observed.

## HTW-00 — Baseline capture

Status: planned

Goal: establish the actual editor state before adding gameplay logic.

Editor changes: none.

### Editor procedure

1. Open `map/HeroTeamWars_M0_2Arena.w3m` in the World Editor.
2. Before changing anything, use **Save As** and create a recoverable copy named `builds/HeroTeamWars_M0_2Arena_HTW00_baseline.w3m`. Continue the inspection on that copy.
3. Record the exact editor version from the editor's About/version view.
4. Open the map/scenario properties and record the title, suggested-player count, map dimensions if shown, and any documented loading-screen or gameplay settings.
5. Open the Trigger Editor. Record every folder and trigger name, including empty folders and any custom-text triggers. Do not create or rename anything yet.
6. Open the variable list/editor and record every variable's exact name, type, array status/size, and initial value. If the editor does not expose a field, mark it `unknown`.
7. Open the Region Palette and record every region's exact name, purpose if obvious, and approximate arena/camp location. Preserve spelling and underscores exactly.
8. Inspect the Object Editor's custom units, heroes, buildings/shops, items, abilities, and spells. For each relevant object, record its display name, rawcode if visible, important stats, abilities and levels, and where it is placed in the map. Do not tune values yet.
9. Test-launch the baseline copy once. Record only what actually happens: loading success, visible units/terrain, errors, and whether any triggers run.
10. Close the editor or leave the baseline copy open without further changes. Send the results using the required report sections below.

Screenshots are acceptable for editor panes when a value is difficult to transcribe. A rawcode or stat that is not visible should remain `unknown`; do not infer it from the display name.

Capture:

- Warcraft III/Reforged editor version;
- map title, suggested players, and player-slot status;
- force names, membership, alliances, vision, and unit-control settings;
- complete trigger tree, including empty folders and custom-text triggers;
- all existing variables and their types/initial values;
- every region name and purpose;
- existing heroes, units, shops, abilities, spells, and items that the map already contains;
- relevant object-data values and four-character rawcodes;
- a save/test-launch result.

Acceptance test: the map opens without an editor error, the existing map can be saved to a recoverable test copy, and the current state is reported without guessing at uninspected values.

Return report using this compact format (screenshots may be attached where useful):

```text
HTW-00 STATE REPORT
editor_version:
map_copy:
map_properties:
trigger_tree:
variables:
regions:
player_slots_and_forces:
heroes_units_abilities_items:
test_result:
unknown_or_unclear:
```

The report is then transcribed into `design/07-editor-state.yaml` and this chunk is marked applied only after the baseline opens, saves, and test-launches successfully.

## HTW-01 — Foundation and phase state

Status: blocked until HTW-00 is applied

Goal: create the smallest reliable foundation for initialization, preparation, combat, resolution, and match-ending state.

Planned scope:

- named `HTW | 00 Foundation` trigger folder;
- explicit team mapping for the two-arena MVP;
- centralized tuning values;
- one current wave ID and phase variable;
- preparation timer and combat soft limit;
- debug messages that expose phase transitions;
- no creep shop, talents, custom hero spells, or six-team expansion yet.

Acceptance test: a test launch completes a preparation-to-combat-to-resolution loop without a stuck timer, duplicate phase transition, or untracked wave.

## HTW-02 — Deterministic base wave

Status: planned

Goal: spawn a small deterministic neutral-hostile base wave into each active arena and clean it up at resolution.

Deferred: attack purchases, routing, War Camps, hero selection, talents, imported assets, and full six-team mode.

## HTW-03 — Hero death, lives, respawn, and elimination

Status: planned

Goal: apply the locked two-hero life rule exactly once per wave, respawn living-team heroes after resolution, and clean up an eliminated arena.

Invariant: only hero deaths remove lives. Leaks, gates, timeouts, and creep reach do not remove lives.

## HTW-04 — Two-arena sending loop

Status: planned

Goal: add personal gold, two personal War Camps per team, three simple creep purchases, an attack queue, and visible arrival in the opposing arena.

Acceptance test: a player can buy an attack composition with personal gold, the assigned opposing arena receives it, and the buyer never fights its own purchased creeps.

## HTW-05 — Information and repeatability pass

Status: planned

Goal: expose phase/timer/lives, incoming composition and total threat worth, personal queue, and enough debug information to compare repeated test runs.

## HTW-06 — Six-team routing expansion

Status: planned

Goal: expand only after the two-arena loop is stable, then implement the visible locked round-robin route over living teams.

Rule: `offset = 1 + ((round - 1) mod (N - 1))`; no mid-wave retargeting or creep carryover after elimination.

## Chunk completion record

For each applied chunk, add an entry to `design/09-change-log.md` containing the editor actions actually completed, map save/test evidence, observed differences, and the next ledger update.
