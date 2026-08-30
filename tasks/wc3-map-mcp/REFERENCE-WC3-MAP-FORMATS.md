# Reference - Warcraft III Map Formats

This is the minimum domain context needed before touching a `.w3m` or `.w3x`.

## Container model

A Warcraft III map is not one flat document. `.w3m` and `.w3x` files are MPQ-based archives containing multiple `war3map.*` members plus imports and sometimes list/attribute metadata. Different members describe different editor systems. Safe mutation means changing the intended member while preserving all others, including members the tool does not understand.

Never treat a map archive as a ZIP. Never edit bytes in place. Use a verified MPQ/map library and rebuild a separate output.

## Common archive members

The exact set varies by map/editor version. During inspection, list what actually exists and classify each member.

| Member | Typical responsibility | Initial MCP treatment |
|---|---|---|
| `war3map.w3i` | map metadata, players, forces, camera bounds, options | parse and canonicalize first |
| `war3map.w3e` | terrain/environment grid and tileset | summarize first; write only after round-trip proof |
| `war3map.w3r` | regions | parse/read early; typed edits after proof |
| `war3map.w3c` | cameras | inspect early; edit later |
| `war3map.wtg` | GUI trigger definitions | inspect if library supports exact version; otherwise opaque |
| `war3map.wct` | custom trigger text/comments | inspect; never assume imported text executes |
| `war3map.j` | generated JASS map script | inspect/validate; replacing requires an explicit build strategy |
| `war3map.lua` | Lua map script on Lua maps | inspect/validate; entry-point behavior must be proven |
| `war3map.w3u` | custom unit data | parse and typed edits after proof |
| `war3map.w3a` | custom ability data | parse and typed edits after proof |
| `war3map.w3t` | custom item data | parse and typed edits after proof |
| `war3map.w3b` | custom destructable data | parse and typed edits after proof |
| `war3map.w3d` | custom doodad data | parse and typed edits after proof |
| `war3map.w3h` | custom buff/effect data | parse and typed edits after proof |
| `war3map.w3q` | custom upgrade data | parse and typed edits after proof |
| `war3mapUnits.doo` | placed units/items/start locations | parse/read early; typed placement edits after proof |
| `war3map.doo` | placed doodads/destructibles | parse/read; write after proof |
| `war3map.wpm` | pathing map | preserve first; write later |
| `war3map.shd` | shadow map | preserve first |
| `war3mapMap.blp` | minimap image | hash/list; optional rendering later |
| `war3mapPreview.tga` | preview image | hash/list; optional rendering later |
| `war3map.wts` | trigger strings/localized text | parse so references can resolve |
| `(listfile)` | archive member list when present | preserve/regenerate only with tested rules |
| `(attributes)` | MPQ attributes when present | preserve/regenerate only with tested rules |

This table is guidance, not proof. The engine must base its capability report on actual parser success for the current map.

## Capability classifications

Every archive member receives one of these states:

- `parsed_read_only`: the engine can parse and report it, but write support is not proven.
- `roundtrip_verified`: parse/serialize output is editor/game compatible for the current version.
- `typed_write_enabled`: defined semantic operations and tests exist.
- `preserved_opaque`: bytes are copied unchanged into builds.
- `unsupported_blocking`: the member cannot be safely preserved or the operation would require changing it.

A tool capability is enabled only when every member it needs has sufficient status. For example, region editing requires `war3map.w3r` typed write support and may require cross-reference validation against triggers/scripts.

## What a no-op round trip means

1. Open the source read-only.
2. Extract or load all members.
3. Parse supported members.
4. Serialize them without semantic changes.
5. Preserve opaque members byte-for-byte.
6. Build a new uniquely named archive.
7. Compare archive membership and canonical semantics.
8. Open the new map in World Editor.
9. Test-load it in Warcraft III.

Byte-for-byte equality of the whole MPQ is not required because compression/order/metadata may change. Semantic equality and preservation of opaque bytes are required, followed by editor/game compatibility evidence.

## Scripts and triggers are a major risk

GUI triggers, custom trigger text, generated JASS, and Lua are related but not interchangeable.

- Importing `something.j` or `something.lua` into the MPQ does not make it execute.
- A World Editor save may regenerate `war3map.j` from GUI triggers and custom-text triggers.
- Replacing generated script may make later World Editor saves overwrite agent code.
- JASS and Lua maps require different compile/injection paths.
- `war3map.wtg` and `war3map.wct` versions must be compatible with the installed editor.

Phase 0 must determine the current map's script language and contents. Phase 3 must choose and document exactly one script ownership strategy:

1. **Editor-owned script**: MCP edits GUI/custom-text structures supported by the library, then World Editor generates the final script.
2. **Build-owned script**: source files are compiled/injected during the MCP build, and the map is treated as generated output that may not round-trip through arbitrary editor saves.

Do not mix the two without a documented merge point.

## Object data model

Custom objects are usually modifications derived from a base object rawcode or new object IDs. A rawcode is four characters and is category-sensitive. Preserve:

- object category;
- old/base ID;
- new/custom ID when applicable;
- field ID;
- value type;
- level/data pointer where the format uses them;
- original ordering where necessary for compatibility.

Do not expose a generic field write until the engine validates the expected field type. Initial operations should use semantic helpers such as `set_unit_name`, `set_unit_max_hp`, or a typed field descriptor with strict validation.

## Coordinates and placed objects

WC3 uses world coordinates. Region rectangles, start locations, units, cameras, and terrain bounds can disagree if units are placed outside playable bounds. Preserve floating-point values without unnecessary rounding. A placement operation should include:

- object rawcode;
- owner/player ID;
- x/y and facing;
- creation number/native identity if present;
- hero level or inventory data when applicable;
- source provenance and transaction operation ID.

The canonical model may assign a stable synthetic ID, but the builder must preserve native identities needed by triggers.

## Strings

Names and descriptions may be literal strings or `TRIGSTR_xxx` references resolved through `war3map.wts`. Inspection should return both the stored reference and resolved display value. A change must state whether it edits the reference, creates a new WTS entry, or writes a literal.

## First-map-specific rules

For Hero Team Wars:

- preserve `Arena_A`, `Camp_A_Player1`, and every other discovered region name exactly;
- use Players 1-2 and 3-4 as explicit teams in the first playable;
- preserve Players 5-12 as closed unless the six-team expansion is explicitly being implemented;
- do not create gameplay triggers until baseline inspection and no-op compatibility pass;
- never copy design placeholder values into the observed ledger merely because the map lacks them;
- the original `.w3m` must retain its recorded hash throughout MCP development.

## War3Net responsibility

The initial .NET spike should use:

- `War3Net.IO.Mpq` to enumerate and preserve archive contents;
- `War3Net.Build.Core` to parse/serialize supported map members;
- `War3Net.Build` only after inspection proves the required builder path;
- `War3Net.CodeAnalysis.Jass` only if JASS parsing becomes part of the selected script strategy.

Do not assume War3Net's future CLI/runtime roadmap is implemented. Write our own narrow engine CLI around the library APIs we verify.

If War3Net cannot preserve or write the current map, compare the `warcraft-vscode` WC3 CLI as a fallback builder. A fallback decision must include compatibility evidence, license review, and a clear statement about which process owns each map member.
