# Decision: shared offline object-field catalog

Status: implemented.

The v25 map repair expands the catalog to 60 fields using the same pinned
metadata, adding cast/learn UI, rank requirements, technology requirements,
and Channel disable-other-abilities. `char` hotkeys and `techList` requirements
use native String values. New fields are included in the all-catalog codec
test, with separate byte-level hotkey tests. No public tool or schema changes.

The unused hard-coded ObjectFieldSemanticRegistry is replaced by one embedded
catalog generated from selected pinned metadata facts. It serves lookup,
search, readable authoring, annotations, and pre-write/build validation.
See [authoring contract](../reference/object-field-authoring.md).

Two global MCP tools and named modification input are additive. Existing raw
canonical records, binary serializers, transaction/hash/evidence protocols and
schema major versions remain unchanged. The existing 1.0 schemas are extended
with the optional authoring alternative and derived response annotations.
Validation is intentionally stricter for new/changed writes; a previously
accepted payload with incorrect field metadata can now fail. Older unchanged
records remain pass-through. Catalog version and source hashes identify the
metadata used; no automatic game-version switching or runtime download exists.

The catalog corrects the old registry's model-scale and movement-speed types,
Channel follow-through type, and shadow-on-water description. It also records
Channel's exact target enumeration, bit flags and native pointers/levels.
Historical v21/v23 authoring fixtures contain legacy scope/type/target choices
that this catalog does not certify. They and published maps are not silently
migrated by a tooling task. Re-author affected records through lookup and named
fields on the next map change, review the resulting diff and publish a new
playable build for user verification. A successful codec round trip alone does
not validate the meaning of those historical values.

The regression suite now supplies explicit skill dependencies in its unit-stock
fixture and tests model scale with a real value. No production gameplay source,
map, or installed-game file is modified by this change.
