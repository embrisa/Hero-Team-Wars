# Phase 5D Work Packet - Players, Teams, and Forces

Status: Typed player/force/team operations, profile-driven team registries, exact format-33 map-info writes, the full six-team fixture, and static/build round-trip coverage are implemented. World Editor/game runtime acceptance and six-arena geometry remain external HTW-06 gates.

## Goal

Make player slots, starts, controller/race settings, logical teams, and map
forces changeable through MCP using explicit IDs and validated relationships.

## Authority model

Player slots and forces in `war3map.w3i` are map structure. Hero Team Wars
logical teams are project gameplay data generated into JASS and, where the map
format represents them, synchronized with explicit forces. Player color is
presentation only and must never determine team identity.

The project must support these profiles:

- `mvp_2arena`: players 1-4 active, teams `[1,2]` and `[3,4]`, shared vision,
  no shared unit control;
- `full_6team`: players 1-12 active, six teams of two, six arena roles, and
  explicit living-team routing.

## Canonical model

Players contain numeric slot ID, exact name, controller, race, flags, start
location, observer/locked state, and provenance. Forces contain stable index,
exact name, player IDs, alliance/vision/control flags, and mask. Teams contain
stable logical ID, member player IDs, force index, arena ID, hero IDs, life
state, and routing state.

The model must report contradictions rather than repair them implicitly:

- missing or duplicate slots;
- a player assigned to multiple forces or no force;
- force mask differing from explicit player IDs;
- team membership differing from force membership;
- closed or computer slots required by an active profile;
- missing arena or camp assignments.

## Required operations

Extend typed operations with:

- `create_player_slot`, `set_player_slot`, and `delete_player_slot`;
- `create_force`, `set_force`, and `delete_force`;
- `create_team`, `set_team`, and `delete_team` for project gameplay data;
- `set_team_arena` and `set_team_members` with complete expected prior values.

Player/force operations must serialize `war3map.w3i` only after the exact
map-info version is proven. Team operations must update the generated JASS
team registry and validate against the map's player/force records.

## Six-team routing

The generated logic must use the living-team order and the locked rule:

`offset = 1 + ((round - 1) mod (N - 1))`

Routes are computed once during preparation, displayed to all relevant players,
and never retargeted mid-wave. Elimination cleanup is scoped to the eliminated
arena; no creep carryover or redistribution is allowed.

## Acceptance tests

- Inspect and round-trip the existing two-team profile without changing the
  source map.
- Change one player slot and one force with exact preconditions and inspect
  the resulting `war3map.w3i`.
- Reject duplicate, missing, conflicting, and stale assignments.
- Build and load the four-player/two-team profile.
- Build a six-team fixture with 12 active slots and six forces, then verify six
  waves of route calculation, elimination cleanup, and next-preparation
  recalculation.
- Confirm generated JASS team IDs never depend on player colors.

## Implementation evidence

- `map-engine/src/Wc3MapEngine.Core/Gameplay/HtwProfileModel.cs` is the shared
  source for `mvp_2arena` and `full_6team` player/team definitions, stable
  logical IDs, living-team order, and the locked routing formula.
- `map-engine/src/Wc3MapEngine.Core/MapComponentCodec.cs` and
  `MapInspector.cs` expose typed player/force writes only for the proven
  `war3map.w3i` format 33 and report unsupported formats as read-only.
- `map-engine/tests/Wc3MapEngine.Tests/Gameplay/Phase5dPlayersTeamsForcesTests.cs`
  builds the 12-player/six-force profile, validates contradiction rejection,
  exercises six-wave/elimination routing, and verifies the source map remains
  unchanged. The checked-in profile fixture is
  `tests/fixtures/expected/phase5d-full-6team-profile.json`.
- The .NET engine suite passes 57 tests and the MCP server suite passes 38
  tests. No World Editor or Warcraft III runtime test was performed in this
  environment, so runtime evidence remains `static_only`.

## Completion gate

MVP team/force support is complete after the four-player profile passes. Full
HTW-06 support additionally requires the 12-slot `war3map.w3i` writer, six
arena geometry/content, and the six-team runtime acceptance matrix.
