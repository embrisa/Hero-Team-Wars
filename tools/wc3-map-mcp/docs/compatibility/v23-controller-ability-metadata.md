# v23 Controller ability metadata fix

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
