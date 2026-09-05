# Object field authoring

Use `wc3_object_field_search` for unfamiliar concepts and
`wc3_object_field_lookup` for exact field meaning before a write. Both are
global, read-only, offline tools. The engine embeds
`map-engine/data/object-fields.json`; inspection annotations, normalization,
validation, and the [generated reference](object-fields.generated.md) use that
same catalog. There is no second TypeScript field dictionary.

## Identity and evidence

`ANcl` is an ability object ID. `Ncl2` is a case-sensitive field ID applicable
to Channel; `Ncl` is a search prefix, never a writable ID. `unam` is shared
by unit/item categories, so lookup without a category returns an ambiguous
result. Suggestions never resolve writes. Unknown fields remain unknown.

The catalog is a bounded set of 47 reviewed fields, not the complete game's
metadata. Every entry records its pinned source, source file hash, upstream
metadata type, readable name, canonical type, base applicability, scope,
data pointer, editor display key and evidence. The metadata fixture does not
declare a game patch; Channel enumeration evidence separately names 1.27.1.
Do not assume compatibility with every game version. Runtime verification
remains user-owned.

To maintain metadata, run `python scripts/sync-object-fields.py` from the MCP
root. This explicit maintenance command downloads only pinned source files;
review changes before rebuilding. Normal builds and lookup need no network.
The script's field selection/readable vocabulary is curated; types and scope
facts are extracted. New fields require source evidence and codec tests.

## Readable modifications

`Bool` is a logical authoring type, not a native binary tag. The codec writes
it as native Int tag `0` with a four-byte `0` or `1`. Inspection maps native
0/1 integers back to `Bool` only for catalog-known boolean fields; unknown
fields and other integer values stay integers. Historical tag-4 Bool records
remain readable for diagnosis, but are repaired when their member is rewritten.
A byte-for-byte no-op does not repair an untouched legacy member. See the
[v24 diagnosis](../compatibility/v24-controller-load-repair.md).

The existing create/update/set-object-data operations accept either a raw
`{ id, type, value, ...scope }` record or a named
`{ field, value, ...scope }` record. Do not mix `field` with `id` or `type`.
Use exact readable names or exact IDs in `field`; the engine resolves types.

For example, a new Channel ability can contain:

```json
{
  "category": "ability",
  "object_kind": "custom",
  "base_rawcode": "ANcl",
  "custom_rawcode": "Z001",
  "rawcode": "Z001",
  "modifications": [
    { "field": "abilityName", "value": "Example skill", "level": 0 },
    { "field": "heroAbility", "value": true, "level": 0 },
    { "field": "normalIcon", "value": "ReplaceableTextures\\CommandButtons\\BTNArcaneTower.blp", "level": 0 },
    { "field": "channelTargetType", "value": "point", "level": 1 },
    { "field": "channelOptions", "value": ["visible"], "level": 1 }
  ]
}
```

`point` resolves to integer 2; `unit` resolves to 1. Flag arrays are combined
as bit masks, so `visible` is 1. Named records infer the exact data pointer
(2 for `Ncl2`, 3 for `Ncl3`). Supply native levels explicitly: non-repeated
fields use 0, repeated fields start at 1. No implicit level conversion occurs.
Raw records must carry their exact type and scope. Doodads require native
variation; simple categories have no scope fields.

A unit's named `{ "field": "heroAbilities", "value": ["Z001"] }` becomes
`{ "id": "uhab", "type": "String", "value": "Z001" }`. Rawcode lists also
accept their native comma-separated string. Enum/flag names are case-sensitive;
numeric values remain available and are validated against the catalog.

**Update modification arrays replace the complete prior array.** Preserve
unrelated modifications, especially unknown fields, when constructing updates.
The operation still requires the complete raw expected prior record and the
transaction's current revision. Readable annotations are a separate response
view; never paste them into `expected` or the canonical record.

## Gates and preservation

Before a revision is published, the engine checks changed fields for category,
canonical type, restricted ability parent (including staged ancestry), native
scope, duplicate scopes, and enum/flag/rawcode-list values. Validation before
building repeats these checks against the source, including direct canonical
input that bypassed authoring. No error rewrites a value or advances a revision.

Unchanged legacy modifications pass through, including unknown IDs and old
metadata mismatches. Adding, changing, removing, or duplicating an unknown
modification is blocked; explicitly deleting its entire object still uses the
existing object-deletion operation and its reference gates. Read-only inspection
and unchanged binary round trips do not require every field to be supported.

Hero attachment is a separate authoring policy, not a claim extracted from a
single metadata row. On affected relationships, a skill cannot be in both
`uhab` and `uabi`; skill references must resolve; custom Channel skills assigned
through `uhab` must explicitly have `aher=true`, nonempty `aart`, and must not
have `aite=true`. Learnable custom Channel skills (`aher=true`) in `uabi`
are rejected with a direction to use `uhab`. Unrelated edits to a unit's name
or stats do not revalidate unchanged attachments. All operations in a batch finish before relationship checks,
so a hero and its skills can be staged together. This is a conservative
authoring gate and does not prove the icon exists or the skill works in game.

`OBJECT_FIELD_UNKNOWN`, `OBJECT_FIELD_TYPE_MISMATCH`,
`OBJECT_FIELD_BASE_MISMATCH`, `OBJECT_FIELD_SCOPE_INVALID`,
`OBJECT_FIELD_VALUE_INVALID`, `OBJECT_FIELD_DUPLICATE`, and
`OBJECT_HERO_SKILL_INVALID` explain failed semantic gates. Structural errors
continue to use the existing schema/`INVALID_ARGUMENT` envelope.

## Annotated inspection and diffs

`wc3_inspect_map` (all sections or object_data) and `wc3_get_component`
(object_data) return a separate bounded `object_fields` array alongside their
unchanged canonical data/values. Each entry has category, rawcode and fields
with raw id/type/value/scope plus name, description, expected_type and known.
Recognized enums also carry value_name; flags carry value_names for known bits.
Unknown names/types are null and known is false. Canonical inspection artifacts,
stored hashes, expected records and archive serializers contain raw data only.

Apply/dry-run and transaction diffs contain `diff.object_fields`: changed objects
with category/rawcode and annotated before/after records (null for create/delete).
Revision-range diffs concatenate these records in revision order, as they do
ordinary changes. Older saved diffs without annotations remain readable.

## Verification and generated contracts

Run `scripts/build.ps1`, then `scripts/test.ps1`. In `mcp-server`,
`npm run generate:object-fields` regenerates the reference and the two tool
request JSON schemas. `npm run check:object-fields` fails on drift and runs as
part of `npm test`. Tests cover exact/ambiguous lookup, search ranking, named
resolution, semantic rejection, atomicity, legacy preservation, hero attachment,
all 47 fields through the binary codecs, and the actual MCP STDIO surface.
