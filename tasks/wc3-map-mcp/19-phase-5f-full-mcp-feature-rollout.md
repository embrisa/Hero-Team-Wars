# Phase 5F Work Packet - Full MCP Feature Rollout

Status: MCP-native/static feature rollout is implemented for the MVP map and the map-independent full six-team gameplay profile. The checked-in source map remains an MVP two-arena map; full-profile archive/runtime promotion, exact editor/game evidence, and GUI compatibility remain explicitly gated until their map/editor fixtures exist.

## Scope

The target is not a generic archive editor. It is a typed, source-controlled
map compiler for the Hero Team Wars project that can inspect, change, validate,
build, test, and promote the following as one reviewable transaction:

- gameplay scripts and MCP-native runtime triggers;
- editor-compatible GUI triggers for the selected exact map/editor format;
- variables and trigger references;
- named regions and region roles;
- object definitions and placed units/buildings/items/doodads;
- player slots, logical teams, forces, starts, alliances, vision, and control;
- deterministic HTW logic, tuning, waves, lives, economy, sending, routing,
  information, and elimination.

## Implementation order

1. Complete HTW-00 and capture a fresh map/editor baseline.
2. Implement the gameplay source composer, manifests, native catalogue, and
   scenario harness.
3. Implement MCP-native triggers/scripts and pass HTW-01 through HTW-05 using
   the existing two-arena map.
4. Implement full regions and the four-player teams/forces profile.
5. Implement object definitions and placed-object support as categories become
   necessary.
6. Implement the `war3map.w3i` player/force writer and the six-team profile.
7. Add six-arena placements, object content, terrain/pathing, and GUI trigger
   compatibility only when their individual round-trip gates pass.
8. Run the full integration matrix and promote only a build with recorded
   exact-build runtime evidence.

## Cross-feature transaction rule

A transaction may include source, trigger, region, object, placement, player,
force, and team operations together, but the engine must validate dependencies
in this order:

`map identity -> players/forces -> regions -> object definitions -> placements -> triggers/scripts -> generated logic`

The semantic diff must group changes by component and show all generated
reference rewrites. The archive diff must separately show changed member
content hashes and preserved opaque members.

## Release profiles

Every project configuration declares a capability profile:

- `mvp_2arena`: four active players, two teams, two arenas, HTW-01..05;
- `full_6team`: twelve active players, six teams, six arenas, HTW-06;
- `gui_compatible`: required for maps whose gameplay source is editor-owned;
  enabled only after exact GUI trigger member evidence.

The server must report enabled capabilities per member, operation, and profile.
It must not expose a tool simply because another profile supports it.

## Definition of done

- All five feature packets have implementation, fixture, and integration tests.
- Every enabled member has no-op and changed round-trip evidence.
- Every mutation has source hash, transaction ID, revision, semantic diff,
  validation report, build ID/hash, and test-session references.
- The current source map remains unchanged.
- HTW-00 through HTW-05 pass on the MVP profile.
- HTW-06 passes only on the full six-team profile.
- GUI-compatible trigger support is reported independently from MCP-native JASS
  support and must pass for the selected map/editor profile when applicable.
- World Editor/game observations are recorded separately from static/engine
  evidence.

## Current automated evidence

Captured 2026-08-31 in the working tree:

- 67 .NET engine tests pass, including cross-feature dependency ordering,
  native-catalogue rejection, waygate reference protection, special-doodad
  preservation, MVP invariants, and the static six-team scenario harness.
- 43 MCP server tests pass, including MCP-wire capability reporting, the
  reverse-request-order cross-feature transaction, validation/build/reopen
  checks, per-operation diff/validation/build evidence fields, and HTW-06
  profile gating. Test-session fields remain empty until an exact observed
  session is recorded.
- The published engine build succeeds and reports archive reopen plus opaque
  member preservation for the tested MVP build.
- The source map hash remains
  `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- Static/engine evidence is not World Editor evidence, Warcraft III load
  evidence, gameplay playtest evidence, or GUI-trigger compatibility evidence.

## Explicit non-goals

- arbitrary MPQ member replacement;
- raw binary patches;
- inferring teams from player color;
- silently rewriting unknown fields;
- claiming gameplay success from a parser, build, or process-start result;
- saving MCP-owned builds in World Editor and treating the regenerated script
  as the authoritative source.
