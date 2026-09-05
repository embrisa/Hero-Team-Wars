# v24 Controller load repair

The user reported that v23 crashes while loading. The exact published v23
SHA-256 is `2A47A6E88EB2AD57F3CE66870E4997D9073D1621EA113BE7CE8813A3D1899E12`.
It matches MCP build `209ae115-6aa1-4e55-8277-4648d41baf31`.
Comparison with v22 (`68B3AC3159BFBFC6FA48D473AFBEA20529606E9572F58BBFAA18000D3002D4B9`)
shows only `war3map.w3a` and `(attributes)` changed.

## Diagnosis and limits

The v23 ability member contains eight invalid native boolean modifications:
`aher=true` and `aite=false` on A2Q1, A2W1, A2E1 and A2R1. At byte offset
563, the first `aher` has type tag 4 followed by level/pointer zero, a
one-byte value and a four-byte terminator. The next field starts 21 bytes
later; the native integer representation needs 24 bytes. This is a concrete
format defect introduced by v23 and the strongest available explanation for
the loading regression, not a runtime-confirmed root cause.

War3Net's [binary value serializer](https://github.com/Drake53/War3Net/blob/master/src/War3Net.Build.Core/Serialization/Binary/Object/ObjectDataModification.cs)
supports a one-byte Bool representation, whereas the independent
[wc3libs object format implementation](https://github.com/inwc3/wc3libs/blob/66b637e38df68023dc94c7ed46210a28b52e0b12/src/main/java/net/moonlightflower/wc3libs/bin/ObjMod.java)
defines native value tags 0 through 3. We had passed logical `Bool` directly
to War3Net. Reopening with the same library accepted its own output, so the
round-trip test did not detect the incompatible encoding.

The available `War3Log.txt` contains startup UI errors and map enumeration,
but no v23 load, script-compilation failure or crash diagnosis. The inspected
Windows Application events supplied no Warcraft crash record. Neither log
establishes the runtime cause. No game/editor was launched or controlled.

## Repair

The codec now emits logical Bool as Int tag 0 and four-byte 0/1. It reads
catalog-known native 0/1 integer fields back into the existing logical Bool
shape. Unknown integer fields and non-0/1 values remain unchanged. Historical
tag-4 records remain readable for diagnosis; untouched archive members are
not silently migrated by a no-op build.

The four primary Controller definitions also now use source-backed types,
1-based repeated levels, Channel pointers 1/2/3/6, correct unit/point target
enum values and the visible option. Single repeated values are explicitly
applied at each skill level; cooldown/mana sequences retain their values.
Icons, ability names, descriptions and base order strings are retained.
The helper abilities, H003 hero attachment, every unit, scripts, terrain and
MVP layout are unchanged. This is a loading/metadata repair candidate, not
certification of every existing targeting/order or gameplay behavior.

`scripts/prepare-v24-repair.mjs` prepares the exact four named-field operations
from the full v23 inspection artifact and refuses another source hash. It
does not write a map. Operations were applied through MCP in a dedicated
configuration allowing the exact saved v23 build as source; the normal local
configuration and golden source remain unchanged. MCP made a recoverable
source snapshot and performed dry-run, apply, diff, validate and build.

## Evidence and handoff

- Transaction: `fa733992-ea39-4989-b719-ebbc174e2c8a`, revision 1.
- Build: `2742b754-be4e-43b7-9061-40943cf5df93`, `debug`, `mvp_2arena`.
- Build/reopen validation passed; 119 engine and 47 MCP tests passed.
- Independent decoding consumed all 6,969 bytes of `war3map.w3a`, all six
  ability records, using only native tags 0/1/2/3. All eight boolean fields
  are four-byte integers; primary Channel scopes are checked.
- Only `war3map.w3a` and `(attributes)` changed, with equal archive membership
  and order. All non-primary object definitions match v23.
- Local detailed evidence: `builds/diagnostics/v24/verification.json` and
  MCP result files in that directory (ignored generated artifacts).
- Golden source SHA-256 remains
  `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- Published file:
  `C:\Users\hp\Documents\Warcraft III\Maps\Test\v24\HeroTeamWars_v24.w3m`.
- Output SHA-256:
  `50639249A0E676B7EAC620492E11BC6C73BEB5775A003FA13505B9C89FC7EF74`.

Runtime is unverified. The user should load v24 through the normal menu,
confirm an available lobby slot, successful load, on-camera gameplay and
camera movement, then inspect/learn/cast the Controller skills. Report any
crash or ability issue against this exact artifact. Publication is a manual
test handoff, not smoke-evidence promotion.

Public tools, schema shapes and host allow-lists are unchanged; serializer
and inspection semantics are documented in the tool contracts, authoring
guide and both READMEs. Catalog metadata and its generated reference do not
change: Bool remains the logical type. No installed-game files were modified.
