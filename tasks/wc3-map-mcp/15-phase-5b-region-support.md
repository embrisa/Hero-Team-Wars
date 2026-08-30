# Phase 5B Work Packet - Typed Region Support

Status: Implementation complete; exact changed-build World Editor loading is verified. Warcraft III Test Map launch was attempted twice, but the installed client returned to its main menu instead of entering the map, so in-game map runtime remains explicitly unverified.

## Goal

Make named regions fully inspectable, creatable, editable, renameable, and
deletable through MCP while preserving exact identity and all supported
references.

## Canonical model

A region contains a stable MCP ID, exact case-sensitive name, creation number,
rectangular bounds, weather, ambient sound, provenance, and references. Names
such as `Arena_A`, `Arena_B`, and existing camp names are protected identities
until an explicit rename operation is approved.

The model must distinguish:

- map regions serialized in `war3map.w3r`;
- MCP gameplay region handles generated in JASS;
- editor-trigger references and custom-text references;
- derived arena/team roles from the project manifest.

## Required operations

Extend the typed operation vocabulary with:

- `create_region`;
- `update_region` for bounds, weather, and ambient sound;
- `rename_region` with a complete reference-rewrite plan;
- `delete_region` only when no live editor/source reference remains;
- `set_region_role` for explicit roles such as arena, entrance, backline, camp,
  spawn, cleanup, and camera boundary.

Existing-region updates require the complete expected prior record. Rename and
delete operations require expected name, creation number, and reference set.
Coordinate values must be finite, ordered, and inside the inspected map
envelope unless the transaction also changes the envelope through a supported
operation.

## Engine work

Implement a versioned `war3map.w3r` parser and serializer that supports:

- stable ordering and creation numbers;
- create/delete/reorder without losing unrelated region fields;
- exact name preservation and explicit rename handling;
- WTS string references where present;
- no-op archive/member round-trip and opaque-member preservation.

The build planner must stop treating region-count or region-name changes as
automatically unsupported once this packet's fixture evidence exists. It must
still reject changes to unknown region fields.

## Cross-reference rules

MCP-owned JASS must use stable logical region IDs and generated handles rather
than guessing `gg_rct_*` names. A rename operation updates the source manifest
and generated JASS together. Editor-compatible mode must also update every
supported trigger/custom-text reference or block the operation with a report
of the unresolved reference.

## Acceptance tests

- Inspect all current regions and preserve their exact names and coordinates.
- Create, update, rename, and delete a synthetic region fixture with expected
  preconditions.
- Prove a stale expected region record is rejected without a revision change.
- Prove a rename rewrites all supported MCP-owned references and no others.
- Rebuild and re-inspect the map; only `war3map.w3r` and intentionally updated
  source members may differ.
- Open and load the exact changed build before promoting it.
- Verify `Arena_A`, `Arena_B`, camp regions, and any six-team arena regions in
  the appropriate project profile.

## Implementation evidence (2026-08-30)

- The immutable source map inspects with all ten original named regions,
  stable IDs of the form `region:<creation_number>`, and their exact stored
  bounds. Its SHA-256 remains
  `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- Typed support covers create, update, rename, delete, reorder, and explicit
  role assignment, with complete expected records, finite/envelope checks,
  protected identities, complete reference-rewrite plans, WTS resolution, and
  opaque archive-member preservation.
- Generated MCP gameplay source binds stable handles such as
  `HTW_Region_region_0` to the inspected rectangular bounds and does not use
  guessed `gg_rct_*` handles.
- Automated verification passes: .NET `47/47` tests and MCP TypeScript/Vitest
  `37/37` tests.
- The exact changed build is
  `builds/mcp/phase5b-editor-game/HeroTeamWars_Phase5B_RegionCreate.w3m`, with
  SHA-256
  `AB3184FD17A90D2FD2653A1A24C868323888D3A940F586F57FF53363AD4F045F`.
  Re-inspection finds eleven regions: the ten originals plus
  `MCP_Phase5B_EditorGame`; the archive still has seventeen members and only
  `war3map.w3r` differs from the source map.
- World Editor loaded that exact build successfully. Its Region Palette shows
  all ten original regions plus `MCP_Phase5B_EditorGame`, and the synthetic
  region was selected successfully.
- `Ctrl+F9` was attempted twice. Both launches reached the Warcraft III main
  menu rather than map gameplay, so no in-game runtime pass is claimed and no
  installed-game files were changed.

## Completion gate

Region support is complete only when canonical inspection, typed operations,
serialization, reference validation, archive comparison, and exact-build
editor evidence all pass. The available game-runtime attempt is recorded above
as unverified because the installed client did not enter the exact map. A
JASS-created runtime rect is not evidence that the corresponding map region was
written.
