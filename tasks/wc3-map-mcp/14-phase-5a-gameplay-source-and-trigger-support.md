# Phase 5A Work Packet - MCP Gameplay Sources and Triggers

Status: MCP-native composition, typed trigger/variable operations, and static validation are implemented; GUI compatibility and exact runtime acceptance remain gated.

## Goal

Make Hero Team Wars gameplay source, runtime triggers, script modules, and
trigger-supporting variables changeable through reviewed MCP transactions.
The result must be reproducible from project source files and must never rely
on pasting code into World Editor.

## Ownership model

The project has two explicit trigger modes, both of which are supported
targets. A map declares which mode owns its gameplay source:

1. **MCP-native gameplay mode**: project source is authoritative. JASS modules,
   trigger manifests, and variable manifests are composed into the complete
   `war3map.j` entry point. The map's `war3map.wtg` and `war3map.wct` are
   preserved opaque editor metadata. This is the mode for HTW-01 through HTW-06.
2. **Editor-compatible GUI mode**: the engine parses and writes the exact
   `war3map.wtg`, `war3map.wct`, and `war3map.wts` versions used by the map,
   then produces a matching `war3map.j`. This mode is enabled only after an
   exact-version fixture round-trip and editor/game evidence. Unsupported GUI
   event/action kinds remain explicitly unavailable rather than becoming raw
   patches.

Both modes use the same canonical trigger and variable model. A project must
declare its mode; a transaction cannot mix modes without an explicit migration
operation and a fresh baseline.

## Source layout

```text
scripts/mcp/
  manifest.json
  main.j
  config/{tuning,teams,regions,objects}.j
  core/{bootstrap,state,events,debug}.j
  systems/{phases,waves,heroes,lives,elimination,economy,sending,routing,information}.j
  content/{base-waves,send-units,heroes,items,abilities}.j
  triggers/*.trigger.json
  variables/*.variable.json
  tests/{assertions,scenarios}.j
```

The composer reads only configured project-relative source roots. It emits a
deterministically ordered complete JASS source file, a source manifest, and a
source hash. No module may define a second `main` function or silently shadow a
declared public symbol.

## Canonical trigger model

Every trigger has:

- stable MCP ID, exact display name, folder path, enabled/initially-on state;
- ordered events, conditions, and actions;
- variable references and rawcode/region/object references;
- source location and provenance;
- generated JASS handler identity;
- optional editor-compatible encoding status.

The supported MCP-native trigger vocabulary must cover map initialization,
periodic timers, elapsed time, player chat, unit death, region entry, player
state changes, and explicit custom events. Conditions and actions must include
typed variable assignment, branching, timer creation, unit/group operations,
messages, phase transitions, and calls to named gameplay functions.

GUI trigger formats are not exposed as arbitrary byte or token patches. Each
supported event/condition/action has a typed schema and a versioned encoder.

## Required operations

Extend `wc3_apply_operations` with typed operations for:

- `set_script_source` - complete `war3map.j` replacement, retaining the
  existing hash precondition;
- `upsert_script_module` and `remove_script_module` - project-source changes
  that regenerate the composed entry point;
- `create_trigger`, `update_trigger`, `move_trigger`, and `delete_trigger`;
- `create_variable`, `update_variable`, and `delete_variable`;
- `set_trigger_mode` - only in a new transaction based on a fresh inspection.

Each operation must carry an expected prior value or source/module hash,
stable identity, rationale, and optional HTW chunk reference. Applying a batch
must update the canonical model and generated-source manifest atomically.

## Validation requirements

Validation must reject:

- duplicate trigger, variable, module, or function identities;
- unresolved region, object, player, force, and script references;
- unsupported native calls or wrong typed arguments when a pinned JASS API
  catalogue is available;
- multiple entry points, missing `main`, invalid initialization order, and
  unsafe cross-module dependency cycles;
- a generated source hash that does not match the staged source;
- GUI-mode changes that cannot be encoded for the exact map/editor version.

Static parsing is not runtime evidence. The validator must state whether a
result is syntax-only, symbol-checked, editor-verified, game-loaded, or
playtested.

## Acceptance tests

- Compose the same module/trigger/variable manifest twice and obtain identical
  source and manifest hashes.
- Change one trigger or module and produce only the expected semantic diff.
- Reject a stale module, trigger, variable, or map source hash.
- Reject duplicate names and unresolved references before building.
- Build a changed JASS source while preserving unrelated archive members.
- If GUI mode is implemented, parse/serialize a fixture and open the exact
  output in World Editor before calling it editor-compatible.
- Run fresh-load tests for initialization and repeat each gameplay scenario at
  least twice.

## Completion gate

This packet is complete when MCP-native gameplay triggers and scripts are
transactional, source-owned, statically validated, buildable, and connected to
the map entry point, and when the selected map/editor trigger mode has its
required exact-version evidence. GUI trigger compatibility is a separate
sub-gate and may not be reported as complete from MCP-native JASS evidence
alone.
