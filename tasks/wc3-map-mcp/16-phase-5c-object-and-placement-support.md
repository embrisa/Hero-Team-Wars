# Phase 5C Work Packet - Typed Object Data and Placed Objects

Status: planned; current object and placement members are preserved opaque.

## Goal

Make object definitions and placed map objects changeable through typed MCP
operations without exposing unsafe generic archive patches.

## Supported archive members

Implement capability records and independent round-trip serializers for the
members present in a map:

| Member | Domain |
|---|---|
| `war3map.w3u` | custom units and heroes |
| `war3map.w3a` | custom abilities |
| `war3map.w3t` | custom items |
| `war3map.w3b` | custom destructables |
| `war3map.w3d` | custom doodads |
| `war3map.w3h` | custom buffs/effects |
| `war3map.w3q` | custom upgrades |
| `war3mapUnits.doo` | placed units, heroes, buildings, and items |
| `war3map.doo` | placed doodads and destructables |

Do not enable a member merely because a library type exists. Each member needs
a fixture, no-op round-trip, changed-field round-trip, malformed-input tests,
and exact editor/game evidence.

## Canonical object model

Object definitions contain category, base rawcode, custom rawcode, display
name, typed fields, dependencies, referenced abilities/items/upgrades, and
unknown-field preservation data. Placed objects contain stable MCP ID, native
creation number, rawcode, owner/player ID, position, facing, scale, variation,
inventory, abilities, and map-region role where available.

Rawcodes are exactly four printable ASCII characters and are unique within
their category. Standard objects and custom definitions must be distinguishable
so an update cannot overwrite a game-native definition accidentally.

## Required operations

Extend the typed operation vocabulary with:

- `create_object_definition`, `update_object_definition`,
  `delete_object_definition`;
- `place_object`, `move_object`, `update_placed_object`,
  `remove_placed_object`;
- `set_object_reference` for typed ability, item, upgrade, owner, and region
  relationships.

Every operation requires a category, rawcode/stable ID, expected prior value,
and a typed value. Unknown fields cannot be changed through this interface.

## Validation requirements

Reject duplicate rawcodes, invalid base references, missing ability/item/
upgrade references, invalid owner/player references, out-of-envelope
coordinates, duplicate placement IDs, and placements that violate the map's
pathing or object limits when those limits are available.

Validate object-data changes before placements. Validate placements before
generated JASS source so the source cannot refer to an object that the build
will not contain.

## Hero Team Wars use

The first playable may use standard rawcodes and JASS-created runtime units.
Typed object support becomes necessary for custom heroes, War Camps, shops,
items, abilities, and decorative six-arena content. The project manifest must
record which content is standard, custom, or runtime-created.

## Acceptance tests

- Inspect and canonicalize every object/placement member present in a fixture.
- No-op rebuild preserves every unchanged member byte/content hash.
- One typed field change changes only its intended object member.
- Place, move, update, and remove one unit and one building with stable IDs.
- Reject a wrong-category rawcode and a stale placed-object precondition.
- Re-inspect exact builds and compare object references and coordinates.
- Open/load the exact build and verify the changed object visually and at
  runtime before promotion.

## Completion gate

Object support is enabled per category/member, never as a blanket
`object_data=true` flag. Unsupported fields remain visible as unknown and
cannot be mutated until their serializer and round-trip evidence exist.
