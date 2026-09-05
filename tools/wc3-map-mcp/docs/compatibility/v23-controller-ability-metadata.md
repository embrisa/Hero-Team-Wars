# v23 Controller ability metadata fix

The user subsequently reported a load crash. The [v24 investigation](v24-controller-load-repair.md)
found invalid one-byte/tag-4 boolean serialization in this artifact; the
previous round-trip check did not establish native-format compatibility.

Historical artifact note: the later [shared object-field catalog decision](../decisions/0004-object-field-catalog.md)
adds source-backed types, Channel target values, native levels and pointers.
This v23 note records the changes made at that time; it does not certify every
field or scope in the historical authoring fixture. New writes must satisfy
the current [authoring contract](../reference/object-field-authoring.md).

Runtime testing of v22 showed that H003 still had no visible ability names,
descriptions, or icons. The custom Channel records were missing the explicit
hero-ability flag and icon metadata, and their Channel fields used incorrect
types. v23 marks each primary ability with `aher=true`, supplies an existing
game icon through `aart`, and serializes `Ncl2`/`Ncl3` as integers and `Ncl6`
as the base order string. H003 keeps the skills in `uhab` and inventory in
`uabi`.

No imported assets are added. H001, H002, H004, the altar stock, source map,
and earlier published artifacts remain unchanged. Runtime behavior remains a
manual Warcraft III verification gate.
