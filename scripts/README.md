# Hero Team Wars Logic Source

Gameplay logic will be organized into small chunks that can be placed into the World Editor as custom script or later compiled through a verified external pipeline.

The source folder is currently a placeholder. No script is considered active in the map until the corresponding editor trigger/custom-text block is present, the map is saved, and the chunk acceptance test passes.

## Source header convention

Every logic file should begin with:

```text
// HTW-CHUNK: HTW-XX
// PURPOSE: one sentence
// REQUIRES: editor objects, regions, variables, or prior chunks
// EDITOR HOOK: the trigger/custom-text block that loads this code
// STATE KEYS: entries that must be mirrored in design/07-editor-state.yaml
// TEST: the observable acceptance test
```

Keep one subsystem per file. Do not duplicate tuning constants in multiple source files. If a function depends on an editor-created hero, unit, ability, item, or region, document its display name and rawcode in the source header and in the editor-state ledger.

An imported source file is not automatically executable in Warcraft III. The map must explicitly connect it through its supported script path.
