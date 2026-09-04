# Task 21: Custom heroes and shared building investigation and experiment plan

**Status:** Investigation complete; implementation and Warcraft III runtime verification are not started by this task

**Golden source:** `map/HeroTeamWars_M0_2Arena.w3m`

**Required golden SHA-256:** `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`

**Last investigated playable artifact:** `C:\Users\hp\Documents\Warcraft III\Maps\Test\v16\HeroTeamWars_v16.w3m`

**v16 SHA-256:** `0C2347BB5B9064D39ED6EB77EC532134299F47BFD246F28B668D52F17555D9B3`
**v16 runtime status:** User opened the exact map, but reported only normal Warcraft III heroes with a long cooldown; custom-hero altar stock is not working.

## 1. Scope and evidence rules

This work item reconstructs the custom-hero/shared-altar experiments and defines the next controlled experiments. It does not authorize a map mutation, a fix, a launch, or interactive verification.

Keep these evidence classes separate:

1. **Static/source evidence:** JSON/JASS content, jassdoc lookup, source validation, schemas, and source-code inspection.
2. **MCP/War3Net evidence:** typed operation acceptance, parsing, serialization, round-trip inspection, transaction validation, and engine reopen.
3. **Archive/build evidence:** exact MPQ members, member hashes, output hashes, and copied playable artifacts.
4. **War3Log evidence:** only messages actually present in `C:\Users\hp\Documents\Warcraft III\Logs\War3Log.txt` or a retained log snapshot.
5. **User runtime observations:** explicit user reports tied to an exact artifact. These alone establish lobby/load/visibility/stock/gameplay behavior.
6. **Hypotheses/unknowns:** plausible explanations that have not passed a one-variable runtime experiment.

An engine reopen, a clean static validator, an MCP build, a process start, or a log line saying that a map was opened is not proof that Warcraft III loaded it successfully or that its altar stock works.

## 2. Executive conclusion

The history contains two distinct defects, and treating them as one problem caused confusion:

- **v8-v12 load failure:** The strongest supported cause class is the MCP/War3Net serializer emitting a newly created `war3map.w3u` as `ObjectDataFormatVersion.v3`. v9, v10, and v12 contained that member and user testing crashed; v11 had no `war3map.w3u` and user testing loaded. The leading mechanism is the extra v3 per-custom-object header word misaligning Warcraft III's expected v2 stream, but that precise parser mechanism has not been proven against an editor-authored known-good binary fixture. The serializer changed to v2 for v13. This evidence implicates the binary format, not custom heroes or a custom altar intrinsically.
- **v15-v16 stock failure after load:** v15 encoded the wrong hero object fields (`uhst=0`, `usst=1`). v16 correctly changed those positions to `usst=0`, `usrg=1`, but its custom altar `n0AL` still contains only `unam` and `utip`; it has no `useu` sold-unit list. Current JASS creates `n0AL` and calls valid `RemoveUnitFromStock`/`AddUnitToStock` natives, yet the user's exact v16 observation was still the inherited normal Tavern roster with a long cooldown. Therefore neither the object-data roster mechanism nor the runtime-population mechanism has been isolated successfully.

The most defensible next move is not a speculative v17 “fix.” It is a short, ordered, one-variable experiment chain beginning with a current-toolchain JASS-only control, then one minimal custom hero, then building creation, then stock population. The implementation agent must stop after each user-tested artifact and use the result to choose the next row.

## 3. Chronological history: v8 through v16

### v8: four custom heroes and one shared custom altar

- Object definitions: `H001` from `Hpal`, `H002` from `Hmkg`, `H003` from `Hamg`, `H004` from `Hblm`, and custom altar `H0AL` from `ntav`.
- The original artifact encoded four repeated `useu:String` modifications with values `H001`, `H002`, `H003`, and `H004`. The first SetUnitStock repair changed that representation to one comma-separated `useu:String="H001,H002,H003,H004"` value.
- JASS created the altar at `(216, -336)` for `PLAYER_NEUTRAL_PASSIVE` and initially called invented `SetUnitStock` calls.
- The original build was `a867c357-0f07-4e33-8e64-5854866357fd`, revision 2, playable hash `FDE8107E61EEAB1D19614482844D0C41CAE2635CBD8986787BD25E7B5E5B1C52`.
- The historical troubleshooting record says War3Log showed a compile rejection for `SetUnitStock`; the current log no longer retains that entry. Current jassdoc independently confirms the symbol does not exist.
- The invalid call was replaced with `RemoveUnitFromStock` plus `AddUnitToStock`. The resulting `v8-setunitstock-fix` build `10545f37-fd91-4b39-bd89-da10fe7acb86` had hash `794734701746B3D5CDB36CD484905CAB064F5A9352314E0A5139DE8B52DD382C`. User observation advanced past the compile error but still crashed while loading.
- `H0AL` was then changed to `n0AL`. Build `27afbfd8-5b70-42c4-92e4-257a75638a2d`, hash `B7653732ADABCAAA3EB8225D948C98E39F53DBDAE7A14706C88E5647EBA2BB93`, also crashed by user observation. The rawcode rename was not a fix.

### v9: full objects, selection disabled

- Five custom objects remained in `war3map.w3u`; `HTW_HeroSelection_Begin` became a no-op.
- Build `f4d3fe36-f9e7-48e8-b647-061621cb031a`, map hash `47BC727800BBA455F833FF3D78C2F45324DE8C1718F1EAD2236B86DF2084CD30`, `war3map.w3u` hash `4F21BE155269904464D428F84B0E407FDAEEA0A4EEDD5E9F7BDD5C43F21B1F41`.
- Static build/reopen passed. User runtime: crashed. This isolates the then-emitted object member from hero-selection JASS.

### v10: four custom heroes, direct H001 spawn, no altar

- Four custom heroes remained; no custom altar was defined or created. JASS directly created `H001` for each player.
- Build `91207484-a5c2-44c9-a640-116460cde65f`, map hash `82B5FD80D39006B02537523B94C0D95799B3236C8A8724884FDC89B8B21AA33C`, `war3map.w3u` hash `7C85302C86C4826C6802A1F8EED2B7C60D0966304672826ED0439DD86F07BB63`.
- Static build/reopen passed. User runtime: crashed. Removing the altar did not cure the old object-member format defect.

### v11: stock Tavern, no custom object member

- No custom objects and no `war3map.w3u`; JASS created stock `ntav` and waited five seconds before waves.
- Build `b029679c-2200-4044-9534-e372c1da0886`, map hash `A52E00F2B2D27D536EB54F8A23109569478DC897259531EB457B2063DD21080B`.
- Static build/reopen passed. User runtime: loaded cleanly. This is the strongest negative control for the v8-v12 crash.

### v12: minimal custom heroes

- Four custom heroes, each with only `unam`; JASS directly created `H001`; no altar.
- Build `5bf08b23-2ac5-46c6-87a4-58b8c34f46a8`, map hash `241B10AC92B0FD9D7E2824669557473C930521B51F17629DF8F1FB897B562EEE`, `war3map.w3u` hash `E7FBDDC11D5E451E8F5E44EEB7B9BDB61596976DB2DB829A6A101D499D894C4C`.
- Static build/reopen passed. User runtime: crashed. This disproved the theory that the extra stat fields were required to trigger the old crash.

### v13: object-data format v2 correction

- Default creation of a missing object-data member changed from `ObjectDataFormatVersion.v3` to `v2`.
- `war3map.w3u` was checked as v2 with zero base definitions, five custom definitions, and no trailing alignment bytes.
- The altar's `useu` was removed on the theory that JASS would own the stock roster; a temporary visibility modifier was added.
- Build `6ce3711c-3b26-4bf1-b5c9-b52b38ca5ca8`, map hash `DA9E1C0153C2895FE73416A9E199E3DDEC78D246B31AD854F836D74966A5CEDA`, `war3map.w3u` hash `A1F131CF8A4C3FA8C33CB4B4FD80839997FBBDDDD804944783BDFC704C256777`.
- Static build/reopen passed. The repository does not retain a precise, hash-linked v13 runtime result beyond later notes indicating that altar visibility/focus still needed work. Do not promote that implication to a stronger claim.

### v14: persistent altar vision and camera focus

- Object bytes remained identical to v13. JASS persisted per-player fog modifiers, panned each local camera, and selected the altar.
- Build `bd71f160-0475-42d2-9bc9-e345db69ae2d`, map hash `C70F11C959C755315274C002B01224957DAE9D8760627DF9A316FB8D30C05D44`, same `war3map.w3u` hash as v13.
- Work item 20 records the user's observation that v14 loaded cleanly and the altar became visible and targetable. It does not establish a correct custom roster or purchase flow.

### v15: wrong instant-stock fields

- Each custom hero added `uhst=0` and `usst=1`.
- Build `ef0827b3-56aa-4136-b1cb-bd6d6fc42b4b`, map hash `7977BB1D215A72CBB9D3F50DC61E9AB18521D70CFFC31043275F8562F070CF14`, `war3map.w3u` hash `39D5C39230E404BF5DCA7D36DDF25E584AB0AE7EE9DBA02E5886AEDF83A64F20`.
- Static build/reopen passed. User runtime: altar stock still had a long cooldown. The field choice was wrong and cannot support any positive conclusion.

### v16: corrected timing fields, missing altar roster

- Each custom hero changed the two added positions to `usst=0` and `usrg=1`; the script remained byte-identical to v15.
- Build `ea3d617a-4d4d-4d57-a4c8-66c1d798249e`, map hash `0C2347BB5B9064D39ED6EB77EC532134299F47BFD246F28B668D52F17555D9B3`, `war3map.w3u` hash `03913DD7805E7307D3948B63F081F20D26C1075B217176930115AB7DCA444274`.
- Static build/reopen passed. User runtime: only normal Warcraft III heroes appeared and retained the long cooldown. The current retained War3Log confirms the exact v16 path was opened three times on 2026-09-04, but contains no relevant JASS compile error and cannot prove load or stock behavior.
- The exact v16 archive has five v2 custom objects. `n0AL` has only `unam` and `utip`; no `useu` is present.

## 4. Evidence table

| Version/variant | Repository diagnostic artifact | Build ID | Map SHA-256 | `war3map.w3u` | Playable copy | Runtime status |
|---|---|---|---|---|---|---|
| v8 original | `builds/diagnostics/v8-inspect.json` and extracted v8 members | `a867c357-0f07-4e33-8e64-5854866357fd` | `FDE8107E61EEAB1D19614482844D0C41CAE2635CBD8986787BD25E7B5E5B1C52` | v3; 5 objects; SHA `64457AF886ACC8F3FD0CECF087D1E29AF15662E9CDA2463A1A83660A2F901BC0`; H001-H004 + H0AL; four repeated `useu`; invalid JASS called `SetUnitStock` | `Maps\Test\v8\HeroTeamWars_v8-custom-heroes-shared-altar_a867c357-0f07-4e33-8e64-5854866357fd.w3m` | Historical user/War3Log record: compile rejected |
| v8 SetUnitStock fix | `builds/diagnostics/v8-setunitstock-fix-10545f37-fd91-4b39-bd89-da10fe7acb86/summary.json` | `10545f37-fd91-4b39-bd89-da10fe7acb86` | `794734701746B3D5CDB36CD484905CAB064F5A9352314E0A5139DE8B52DD382C` | v3; 5 objects; SHA `E57FDA27F0FEE70371806610C072AE156882F2D066FD94F51C9345B65713D53D`; H0AL | `Maps\Test\v8_rebuilt\HeroTeamWars_v8-setunitstock-fix_10545f37-fd91-4b39-bd89-da10fe7acb86.w3m` | Historical documentation associates this stage with load crash; report says untested |
| v8 n0AL rename | `builds/diagnostics/v8-n0al-load-fix-27afbfd8-5b70-42c4-92e4-257a75638a2d/summary.json` | `27afbfd8-5b70-42c4-92e4-257a75638a2d` | `B7653732ADABCAAA3EB8225D948C98E39F53DBDAE7A14706C88E5647EBA2BB93` | v3; 5 objects; SHA `4F21BE155269904464D428F84B0E407FDAEEA0A4EEDD5E9F7BDD5C43F21B1F41`; n0AL | `Maps\Test\v8_fixed_rebuilt\HeroTeamWars_v8-n0al-load-fix_27afbfd8-5b70-42c4-92e4-257a75638a2d.w3m` | User: load crash; report says untested |
| v9 objects only | `builds/diagnostics/v9-objects-only-disabled-selection-f4d3fe36-f9e7-48e8-b647-061621cb031a/summary.json` | `f4d3fe36-f9e7-48e8-b647-061621cb031a` | `47BC727800BBA455F833FF3D78C2F45324DE8C1718F1EAD2236B86DF2084CD30` | 5 objects; SHA `4F21BE155269904464D428F84B0E407FDAEEA0A4EEDD5E9F7BDD5C43F21B1F41`; 967 bytes | `Maps\Test\v9_objects_only_disabled_selection\HeroTeamWars_v9-objects-only-disabled-selection_f4d3fe36-f9e7-48e8-b647-061621cb031a.w3m` | User: crashed |
| v10 heroes/no altar | `builds/diagnostics/v10-direct-spawn-h001-91207484-a5c2-44c9-a640-116460cde65f/summary.json` | `91207484-a5c2-44c9-a640-116460cde65f` | `82B5FD80D39006B02537523B94C0D95799B3236C8A8724884FDC89B8B21AA33C` | 4 objects; SHA `7C85302C86C4826C6802A1F8EED2B7C60D0966304672826ED0439DD86F07BB63`; 828 bytes | `Maps\Test\v10_direct_spawn_h001\HeroTeamWars_v10-direct-spawn-h001_91207484-a5c2-44c9-a640-116460cde65f.w3m` | User: crashed |
| v11 stock ntav/no w3u | `builds/diagnostics/v11-stock-tavern-no-w3u-b029679c-2200-4044-9534-e372c1da0886/summary.json` | `b029679c-2200-4044-9534-e372c1da0886` | `A52E00F2B2D27D536EB54F8A23109569478DC897259531EB457B2063DD21080B` | absent | `Maps\Test\v11_stock_tavern_no_w3u\HeroTeamWars_v11-stock-tavern-no-w3u_b029679c-2200-4044-9534-e372c1da0886.w3m` | User: loaded cleanly |
| v12 minimal heroes | `builds/diagnostics/v12-minimal-custom-heroes-direct-spawn-5bf08b23-2ac5-46c6-87a4-58b8c34f46a8/summary.json` | `5bf08b23-2ac5-46c6-87a4-58b8c34f46a8` | `241B10AC92B0FD9D7E2824669557473C930521B51F17629DF8F1FB897B562EEE` | 4 objects; SHA `E7FBDDC11D5E451E8F5E44EEB7B9BDB61596976DB2DB829A6A101D499D894C4C`; 176 bytes | `Maps\Test\v12_minimal_custom_heroes_direct_spawn\HeroTeamWars_v12-minimal-custom-heroes-direct-spawn_5bf08b23-2ac5-46c6-87a4-58b8c34f46a8.w3m` | User: crashed |
| v13 w3u v2 | `builds/diagnostics/v13-custom-heroes-w3u-v2-format-6ce3711c-3b26-4bf1-b5c9-b52b38ca5ca8/summary.json` | `6ce3711c-3b26-4bf1-b5c9-b52b38ca5ca8` | `DA9E1C0153C2895FE73416A9E199E3DDEC78D246B31AD854F836D74966A5CEDA` | v2; 5 objects; SHA `A1F131CF8A4C3FA8C33CB4B4FD80839997FBBDDDD804944783BDFC704C256777`; 915 bytes | `Maps\Test\v13\HeroTeamWars_v13.w3m` | Exact result not retained; no claim |
| v14 persistent vision | `builds/diagnostics/v14-persisted-altar-vision-bd71f160-0475-42d2-9bc9-e345db69ae2d/summary.json` | `bd71f160-0475-42d2-9bc9-e345db69ae2d` | `C70F11C959C755315274C002B01224957DAE9D8760627DF9A316FB8D30C05D44` | identical to v13; SHA `A1F131CF8A4C3FA8C33CB4B4FD80839997FBBDDDD804944783BDFC704C256777` | `Maps\Test\v14\HeroTeamWars_v14.w3m` | User: loaded; altar visible/targetable |
| v15 wrong fields | `builds/diagnostics/v15-instant-hero-stock-ef0827b3-56aa-4136-b1cb-bd6d6fc42b4b/summary.json` | `ef0827b3-56aa-4136-b1cb-bd6d6fc42b4b` | `7977BB1D215A72CBB9D3F50DC61E9AB18521D70CFFC31043275F8562F070CF14` | v2; 5 objects; SHA `39D5C39230E404BF5DCA7D36DDF25E584AB0AE7EE9DBA02E5886AEDF83A64F20`; 1043 bytes | `Maps\Test\v15\HeroTeamWars_v15.w3m` | User: long cooldown |
| v16 corrected fields | `builds/diagnostics/v16-instant-hero-stock-corrected-ea3d617a-4d4d-4d57-a4c8-66c1d798249e/summary.json` | `ea3d617a-4d4d-4d57-a4c8-66c1d798249e` | `0C2347BB5B9064D39ED6EB77EC532134299F47BFD246F28B668D52F17555D9B3` | v2; 5 objects; SHA `03913DD7805E7307D3948B63F081F20D26C1075B217176930115AB7DCA444274`; 1043 bytes | `Maps\Test\v16\HeroTeamWars_v16.w3m` | User: normal heroes, long cooldown |

All abbreviated member hashes above are backed by the corresponding `summary.json`; use the full value from that file in a build handoff.

## 5. Exact v15/v16 archive findings

Read-only MCP comparison generated `tools/wc3-map-mcp/artifacts/reports/compare-d05d4a36-ae34-4f6a-b503-4020673045c3.json` with report SHA-256 `FC620C4FAEF350AB819033AB673725D26B7AF01C3497C95309870289FFF960CF`.

- The only changed content members are `war3map.w3u` and the derived MPQ `(attributes)` member.
- `war3map.j` is identical in both builds: SHA-256 `3594549FB360F204379D02812B2D720AC458E239C090215E34A67ABD6B30F036`, 48,170 bytes.
- Both `war3map.w3u` files are 1,043 bytes and contain zero base definitions plus five custom definitions.
- Parents/custom IDs are exactly `Hpal->H001`, `Hmkg->H002`, `Hamg->H003`, `Hblm->H004`, and `ntav->n0AL`.
- For each hero, v15 modification positions 8/9 are `uhst:Int=0` and `usst:Int=1`; v16 changes only the IDs at those positions to `usst:Int=0` and `usrg:Int=1`. Existing names, tooltips, stats, costs, and lumber values are unchanged.
- In both versions `n0AL` contains `unam:String` and `utip:String` only. Neither contains `useu`.
- The exact v16 MCP inspection report is `tools/wc3-map-mcp/artifacts/reports/inspect-5b04df8b-e566-495a-94b5-48e370984f94.json`, report SHA-256 `E4AB6724A9C78FF45A817372D0FC9E15114650ED6608EBFA0C3C874E70D4C412`.

These findings prove the intended byte-level timing-field change. They do not prove Warcraft III's semantics for those fields.

## 6. JASS and stock mechanism findings

Pinned jassdoc results, rechecked during this investigation:

| Symbol | Canonical result | Current use |
|---|---|---|
| `SetUnitStock` | Not found; invalid symbol | Must never be called or suggested again |
| `AddUnitToStock` | `native AddUnitToStock takes unit whichUnit, integer unitId, integer currentStock, integer stockMax returns nothing` | Current four-argument calls validate with zero errors/warnings |
| `RemoveUnitFromStock` | `native RemoveUnitFromStock takes unit whichUnit, integer unitId returns nothing` | Current two-argument calls validate with zero errors/warnings |
| `AddUnitToAllStock` | Exists, takes `(integer unitId, integer currentStock, integer stockMax)` | Valid alternative API, but global scope changes the experiment and is not recommended until per-building behavior is isolated |
| `RemoveUnitFromAllStock` | Exists, takes `(integer unitId)` | Same caution as above |
| `StartStockUpdates` | Blizzard.j function taking nothing and returning nothing | Exists, but current source does not call it and no evidence proves it is required for this custom-building flow |
| `PerformStockUpdates` | Blizzard.j function taking nothing and returning nothing | Exists as the update callback; do not add it speculatively or call it directly without an isolated experiment |

Current implementation path:

- `tools/wc3-map-mcp/scripts/mcp/systems/hero-selection.j` creates `n0AL` through JASS `CreateUnit`, not through `war3mapUnits.doo` placement.
- The same file removes and then adds H001-H004 at runtime with `RemoveUnitFromStock` and `AddUnitToStock`.
- `tools/wc3-map-mcp/scripts/mcp/object-data/v8-hero-objects.json` creates `n0AL` and the four hero definitions in `war3map.w3u`.
- The current `n0AL` definition does not declare `useu`. Therefore building definition is object data, building creation is JASS, and attempted stock population is JASS. No current build uses a placed custom altar.
- The user-observed v16 result shows that syntactically valid `AddUnitToStock` calls are not sufficient evidence of the desired visible roster. Possible initialization-order/default-stock interactions remain hypotheses.

## 7. Confirmed findings

1. The golden source still matches the required SHA-256 and has no baseline drift at investigation time.
2. `ObjectDataFormatVersion.v2` is now used when creating a missing object-data member. The v13-v16 members parse and round-trip as v2.
3. The v9-v12 user results strongly isolate the old crash to the emitted `war3map.w3u`, and v12 rules out the additional stat fields as necessary for that old crash.
4. The `H0AL` to `n0AL` rename did not fix the old crash.
5. v15 and v16 differ only in four repetitions of the intended two stock-field IDs, plus `war3map.w3u`, container identity, and regenerated `(attributes)` hashes.
6. v16 contains the expected five custom definitions, correct parents/custom rawcodes, and typed `usst=0`/`usrg=1` hero modifications.
7. v16's altar has no `useu` modification.
8. `SetUnitStock` does not exist in the pinned jassdoc index. The current `AddUnitToStock` and `RemoveUnitFromStock` calls exist and match canonical signatures.
9. Existing tests prove serialization, parsing, preservation, exact modification values, and engine reopen. They do not emulate Warcraft III's object-field semantics, shop initialization, UI roster, cooldown display, or purchase events.
10. The current retained War3Log has v16 open-path evidence only. It does not retain the older `SetUnitStock` diagnostic or contain a current stock-related error.

## 8. Disproven approaches that must not be repeated

- Do not call or create a compatibility shim for `SetUnitStock`.
- Do not repeat `H0AL -> n0AL` as if the rawcode case/class alone fixes corruption.
- Do not emit a new missing `war3map.w3u` as format v3.
- Do not blame the hero stat modifications for the v8-v12 crash; minimal `unam`-only v12 also crashed under the bad format.
- Do not treat `uhst=0`/`usst=1` as start-delay/replenishment control.
- Do not claim `usst=0`/`usrg=1` fixes the roster. Those fields concern the sold unit's stock timing, while v16 did not expose the custom roster.
- Do not claim that valid `AddUnitToStock` syntax proves runtime stock population.
- Do not claim that one comma-separated `useu` string works in Warcraft III merely because it round-trips through War3Net.
- Do not combine a `useu` change, runtime stock-call change, rawcode change, and placement change in one build.
- Do not use direct MPQ/binary patching, overwrite the golden source, reuse a `Maps\Test\vN` folder, or automate Warcraft III UI verification.

## 9. Unresolved hypotheses, ranked

| Confidence | Hypothesis | Evidence for | What would disprove it |
|---|---|---|---|
| High | v16 shows inherited `ntav` roster because `n0AL` has no `useu`, and runtime stock calls do not replace/remove the inherited default roster as expected | Exact v16 object data lacks `useu`; user saw normal heroes | A no-`useu`, JASS-only population experiment that shows only the requested custom hero |
| Medium-high | `useu` is the correct object-data field for a building's sold-unit list, encoded as one comma-separated `String` | Earlier bytes round-trip; tests construct this value; conventional field ID | A v2 custom altar with only `useu=Hpal` failing while an otherwise identical known-good editor-authored comparison encodes a different representation |
| Medium | Default Tavern stock initialization runs before/after the current refill in a way that preserves or restores normal heroes | User saw inherited roster despite valid per-building calls | A controlled no-`useu` stock `ntav` experiment where remove/add produces exactly the requested roster |
| Medium | The custom altar itself is valid now, but stock behavior fails independently of definition/creation | v14 loaded and altar was visible/targetable; v2 parses | A minimal custom `ntav->n0AL` building with no stock modifications failing to load or appear while stock `ntav` succeeds |
| Medium-low | Hero timing fields are correct but are irrelevant until the building exposes H001-H004 | v16 bytes are as intended, but user never saw custom heroes | A one-custom-hero roster appearing with an unexpected cooldown despite verified `usst=0`/`usrg=1` |
| Low | Custom hero parent IDs or capitalization still cause runtime incompatibility | Parents are identity-sensitive and not field-semantics validated | A one-hero v2 experiment that loads and instantiates H001 successfully |
| Unknown | A Reforged-specific restriction affects runtime addition of custom heroes to a custom shop | User result is compatible, but no isolated experiment exists | Successful custom-altar/custom-hero population through either mechanism |

## 10. MCP implementation and contract gaps

1. **No semantic object-field registry.** Schemas accept any printable four-character modification ID with a matching scalar type. Validation checks shape/type, not that `useu`, `usst`, `usrg`, or `uhst` is legal for a unit or means what documentation claims.
2. **Round-trip is labeled more strongly than it behaves.** The capability catalog calls `war3map.w3u` `roundtrip_verified`; that means War3Net read/write/reopen, not Warcraft III-compatible semantics.
3. **Tests and live source disagree.** `Phase5BuildRoundTripTests.V8HeroObjectsRoundTripParentsSoldUnitsAndCorrectInstantStockFields` constructs and asserts `n0AL.useu=H001,H002,H003,H004`, while the live `v8-hero-objects.json` and v16 artifact omit it. The test does not consume the live JSON fixture, so it cannot prevent this drift.
4. **Documentation disagrees.** `v13-object-data-v2-fix.md` says stock is handled exclusively through runtime JASS; `v8-custom-hero-runtime-lessons.md` says `useu` round-trips as one string; work item 20 documents only hero timing fields. None currently records a proven Warcraft III stock mechanism.
5. **No dedicated building-stock operation.** The public surface can create a generic unit object definition and accept raw modifications, but has no typed `set_units_sold`, `set_stock_start_delay`, or `set_stock_replenish_interval` contract with field-specific validation.
6. **Creation and placement are easy to conflate.** A custom building definition can be serialized, a building can be placed in `war3mapUnits.doo`, and a unit/building can be created by JASS. Current gameplay uses definition plus JASS creation; tests do not cover the three concerns separately.
7. **Component endpoint inconsistency.** `wc3_inspect_map(section=object_data)` decoded exact v16 definitions, while `wc3_get_component(component=object_data)` returned `UNSUPPORTED_COMPONENT` because the aggregate status is `mixed`. The contract suggests the component tool should return supported typed values/capability.
8. **Build-report provenance gap.** `wc3_build_report` could not resolve either v15 or v16 build ID, although diagnostic summaries call them build IDs and outputs live under `builds/mcp/hero-team-wars/<id>`. These diagnostic runners did not leave the manifest identity expected by the live MCP build-report service. Treat their JSON/reopen evidence as diagnostic-runner evidence, not a currently re-verifiable MCP build record.
9. **Operation-list mismatch to review.** The capability catalog includes `set_object_data` for `war3map.w3u`, while the inspected member's `supported_operations` list omitted it. Registrations, schemas, implementation, generated capability records, and docs should be reconciled in the implementation task.
10. **No runtime-semantic test oracle.** Current .NET/MCP tests cannot prove shop UI contents, cooldowns, purchase availability, or event behavior. Those gates must remain user-owned even after better static fixture tests are added.
11. **Historical runner is not reproducible as labeled.** `builds/diagnostics/build-v15-fix.mjs` still describes `uhst=0`/`usst=1`, but reads the now-corrected shared `v8-hero-objects.json`. Rerunning it today could emit v16-style object data under stale v15 metadata. Freeze experiment inputs or embed/hash the exact fixture per variant.
12. **Creation schema/canonical requirements differ.** The executable operation schema permits object creation without `id`, `archive_path`, or `display_name`, while the versioned canonical schema requires them. The operation applier may derive a name from `unam`, but reopen comparison has previously required an explicit `display_name`. Align the wire schema, canonical schema, and builder behavior.
13. **The v2 fix is path-dependent.** New object-data members default to v2, but an existing source member preserves its detected format. A future experiment must assert the actual output format; it must not assume every input is normalized to v2.

## 11. Experiment execution protocol

Every experiment below must:

1. Re-read `AGENTS.md` and the full MCP tool contract catalog.
2. Inspect `git status --short` and preserve unrelated work.
3. Call `wc3_project_status`; require the golden hash exactly.
4. Use golden-source inspect/hash -> isolated transaction -> dry-run/apply -> diff -> validate -> build -> build report/reinspect. Never patch an MPQ directly.
5. Validate every changed JASS call through pinned jassdoc before staging.
6. Assert object-data format v2, exact object count, base/custom rawcodes, modification IDs/types/values, zero unexpected trailing bytes, script hash, archive member hashes, and source immutability.
7. Allocate the next unused numeric `Maps\Test\vN` folder at execution time. If no newer folder exists, E0 begins at v17. Never pre-create or reuse later folders.
8. Copy exactly one build to `C:\Users\hp\Documents\Warcraft III\Maps\Test\vN\HeroTeamWars_vN.w3m`; compute its SHA-256 after copying and verify it equals the build output hash.
9. Hand the user the exact absolute path, build ID, transaction/revision, build output path, build hash, copied hash, `war3map.j` hash, and `war3map.w3u` hash or explicit absence.
10. Ask only the experiment's listed runtime question. Do not launch/control Warcraft III or record success without the user's reply for that exact hash.

## 12. Prioritized one-variable experiment matrix

Run rows in order and stop for user feedback after every row. A row may direct a branch or skip a later row.

### E0 — JASS-only baseline, no custom unit object data

- **Input:** Golden source plus the current `mvp_2arena` composition, with hero-selection entry disabled and no object definitions; use v11's source shape as a reference, not its old map as a mutation base.
- **Single changed variable:** Current toolchain rebuild of the known no-`war3map.w3u` control.
- **Expected:** Four-slot map loads to visible/camera-movable normal MVP gameplay; no altar is expected.
- **Failure interpretation:** The current gameplay/build baseline regressed; stop all custom-object experiments.
- **Static checks:** No `war3map.w3u`; no H001-H004/n0AL references; JASS validates; required `config` and `main` initialization remain present.
- **Handoff:** Next unused `vN\HeroTeamWars_vN.w3m` plus exact computed map/script hashes and explicit `war3map.w3u=absent`.
- **User question:** “For this exact hash, do you see four lobby slots, does the game load, can you see gameplay, and can you move the camera?”
- **Allowed conclusion:** Success establishes only the current JASS/build baseline. Failure says nothing about custom objects.
- **Next:** On success E1; on failure stop and repair the baseline in a separate task.

### E1 — One smallest-candidate custom hero parsed but not instantiated

- **Input:** Exact passing E0 source/JASS plus one v2 object definition `Hpal->H001` with only `unam:String="HTW Guardian"`; selection remains disabled.
- **Single changed variable:** Presence of one smallest-candidate custom hero definition in `war3map.w3u`. `unam` is a candidate field, not runtime-semantic proof from the MCP.
- **Expected:** Same runtime as E0.
- **Failure interpretation:** Current v2 single-object serialization or the identity/field is incompatible; altar and stock are exonerated.
- **Static checks:** v2; 0 base/1 custom; exact parent/custom IDs; exactly one `unam` string; no altar; no H001 `CreateUnit`.
- **Handoff:** Next `vN\HeroTeamWars_vN.w3m` plus map/script/w3u hashes; copy hash must equal output hash.
- **User question:** “Does this exact map load and behave identically to E0?”
- **Allowed conclusion:** Success proves Warcraft III can parse this minimal custom hero definition; it does not prove instantiation or stock.
- **Next:** Success E2; failure stop and compare against a known-good editor-authored one-custom-unit `war3map.w3u` before further mutation.

### E2 — One minimal custom hero, direct spawn, no altar

- **Input:** Exact passing E1 artifact source model; JASS adds one direct `CreateUnit(Player(0), 'H001', <visible tested point>, 270.)` after normal initialization.
- **Single changed variable:** Instantiate the already-present H001 once.
- **Expected:** Map loads and one visible unit named HTW Guardian appears; no altar exists.
- **Failure interpretation:** Custom hero instantiation/parent identity is suspect, independent of altar/stock.
- **Static checks:** Same w3u hash as E1; only script changes; validate `CreateUnit` via jassdoc; assert one H001 call and no n0AL/stock calls.
- **Handoff:** Next exact `vN` artifact and map/script/w3u hashes, noting w3u must equal E1.
- **User question:** “Does the map load, and is one visible HTW Guardian present and selectable?”
- **Allowed conclusion:** Success proves minimal H001 instantiation only. Failure does not implicate stock.
- **Next:** Success E3; failure stop for parent/rawcode/known-good fixture comparison.

### E3 — One stock Tavern, no custom sold-unit list

- **Input:** Exact passing E0 baseline, no custom object data; create stock `ntav` by JASS at the visible altar point; do not call any stock native.
- **Single changed variable:** Create one stock Tavern.
- **Expected:** Map loads and a stock Tavern is visible/targetable with its normal inherited roster.
- **Failure interpretation:** Building creation/visibility/ownership path is broken before custom definitions or stock changes.
- **Static checks:** No w3u; exactly one `CreateUnit(..., 'ntav', ...)`; no Add/Remove stock calls; camera/vision held constant.
- **Handoff:** Next exact `vN` artifact with map/script hashes and w3u absent.
- **User question:** “Is the stock Tavern visible and selectable, and does it show its normal roster?”
- **Allowed conclusion:** Success validates stock-building creation only; it does not validate custom building or roster replacement.
- **Next:** Success E4; failure stop and isolate ownership/visibility/position.

### E4 — One minimal custom altar based on ntav

- **Input:** Exact passing E3 source/JASS. E4a adds v2 `ntav->n0AL` with only `unam:String="HTW Hero Altar"` but still creates `ntav`. After E4a passes, E4b changes only the creation rawcode from `ntav` to `n0AL`; no stock modifications/calls.
- **Single changed variable:** E4a tests inert custom-building object parsing. E4b tests instantiation of that already-present custom building.
- **Expected:** Map loads; custom-named altar is visible/targetable and otherwise inherits stock Tavern behavior.
- **Failure interpretation:** Custom building definition/instantiation is suspect, independent of custom heroes and stock override.
- **Static checks:** Both are v2 with exactly one custom object `ntav->n0AL` and only `unam`; E4a has one ntav CreateUnit, E4b one n0AL CreateUnit; neither has useu/Add/Remove stock.
- **Handoff:** Two consecutive exact `vN` artifacts with all hashes and the same one-object w3u hash; user tests E4a first.
- **User question:** E4a: “Does this behave exactly like E3?” E4b: “Does the exact map load, and is the visible selectable building named HTW Hero Altar?”
- **Allowed conclusion:** E4a success proves minimal custom-building object parsing; E4b success adds minimal custom altar JASS creation. Neither proves roster control.
- **Next:** E4a failure stops at serialization/definition. E4a success/E4b failure goes to E9. Both succeeding go to E5.

### E5 — Custom altar plus one stock hero through object data

- **Input:** Exact passing E4; add only `useu:String="Hpal"` to n0AL; keep all runtime Add/Remove calls absent.
- **Single changed variable:** Object-data sold-unit list contains one stock Paladin.
- **Expected:** Altar shows only Paladin (subject to normal purchase requirements/timing).
- **Failure interpretation:** `useu` value/type/representation or inherited stock semantics is wrong; do not add custom heroes yet.
- **Static checks:** Same one object; modifications exactly `unam` and `useu`; raw bytes contain `useu` and `Hpal`; no stock natives.
- **Handoff:** Next exact `vN` artifact with map/script/w3u hashes and decoded modification list.
- **User question:** “What exact unit icons/names are shown in the altar, and is Paladin the only sold hero?”
- **Allowed conclusion:** Success establishes object-data roster control for one stock hero. Failure does not implicate H001.
- **Next:** Success E6; failure E8 to test runtime population on the stock altar before any combined mechanism.

### E6 — Custom altar plus one custom hero through object data

- **Input:** Exact passing E5. E6a adds the already-passing E1 minimal H001 definition but leaves `useu=Hpal`. After the user confirms no behavior change, E6b changes only `useu` from `Hpal` to `H001`.
- **Single changed variable:** E6a tests an inert custom-hero definition beside the working shop. E6b changes only the sold-unit rawcode from stock Paladin to the already-proven H001.
- **Expected:** Only HTW Guardian is shown and can be purchased.
- **Failure interpretation:** Custom heroes in object-data shop lists are suspect; custom altar itself and stock `useu` were already controlled.
- **Static checks:** Both are v2 with exactly two objects; H001 only `unam`; n0AL `unam+useu`; no stock natives; E6a contains `useu=Hpal`, E6b `useu=H001`.
- **Handoff:** Two consecutive exact `vN` artifacts with hashes and decoded object lists; user tests E6a first.
- **User question:** E6a: “Is behavior identical to E5?” E6b: “Is HTW Guardian the only offered hero, is it immediately purchasable, and does buying it produce a controllable HTW Guardian?”
- **Allowed conclusion:** E6a controls for H001's mere presence. E6b success proves the smallest object-data custom-hero stock path. Failure does not justify changing four heroes at once.
- **Next:** E6a failure returns to E1/E2 identity investigation. E6b success goes to E7; E6b failure stops for an editor-authored custom-shop/custom-unit comparison.

### E7 — Custom altar plus all four custom heroes through object data

- **Input:** Exact passing E6b. First add H002, H003, and H004 as inert minimal definitions in three consecutive artifacts while leaving `useu=H001`; require unchanged user behavior after each. Then expand `useu` one rawcode per artifact: two heroes, three heroes, and finally four.
- **Single changed variable:** Each artifact either adds one inert definition or adds one already-parsed hero to the sold-unit list. No artifact changes both.
- **Expected:** Exactly four HTW heroes appear; no normal heroes.
- **Failure interpretation:** CSV/list cardinality, one added definition, or shop slot behavior is suspect; bisect 2 then 3 heroes rather than changing mechanisms.
- **Static checks:** v2; exact parent/rawcodes; every hero has only `unam`; one comma-separated useu string with the intended ordered prefix; no runtime stock calls. Diff each artifact against its predecessor.
- **Handoff:** One next-numbered exact artifact per added definition/list entry, each with full hashes and decoded roster.
- **User question:** In inert-definition steps: “Is behavior unchanged and is only Guardian offered?” In roster steps: “Are exactly the listed 2/3/4 HTW heroes shown, with no normal heroes?”
- **Allowed conclusion:** The last passing roster cardinality is proven for visibility only; purchase/deployment must still be asked explicitly before claiming selection flow.
- **Next:** Success at four heroes permits timing fields in E10; first failure identifies the added definition or roster entry without a broad bisect.

### E8 — Runtime stock population versus object-data `useu`

- **Input:** Use passing E6b as E8a. E8b removes only `useu` and has no stock calls, establishing the no-authority control. E8c then adds only the jassdoc-validated per-building Remove/Add calls for H001.
- **Single changed variable:** E8b removes object-data roster authority. E8c adds runtime roster authority. No artifact switches both at once.
- **Expected:** E8a and E8c present the same one-H001 roster; E8b shows the uncontrolled/inherited state.
- **Failure interpretation:** If E8a works and E8c does not, runtime initialization/order or native behavior is the problem. If both work, choose one authority. Never retain both without a separate justification experiment.
- **Static checks:** E8a has useu/no stock calls; E8b has neither; E8c has no useu and exact validated stock calls; all other definitions are identical.
- **Handoff:** Three consecutive versioned artifacts with exact map/script/w3u hashes and explicit mechanism labels; user tests in order.
- **User question:** “For each exact hash, list every hero shown and whether the single intended hero is immediately available.”
- **Allowed conclusion:** Only a differing user result can choose between mechanisms. Syntax validity alone cannot.
- **Next:** Prefer the successful single-authority path; if neither works, stop for a known-good editor-authored stock fixture.

### E9 — Custom building placement separate from stock behavior

- **Input:** Exact E4b custom altar definition with no `useu` and no stock calls. E9a removes only JASS `CreateUnit` and expects no altar. E9b then adds one `n0AL` through the typed `place_unit`/`place_object` workflow in `war3mapUnits.doo`, matching owner/position/facing.
- **Single changed variable:** E9a removes runtime creation. E9b adds serialized placement. No artifact changes both at once.
- **Expected:** Same visible/targetable custom altar as E4.
- **Failure interpretation:** A result differing from E4 isolates placement/initialization semantics, not stock.
- **Static checks:** Same w3u definition as E4; neither has n0AL CreateUnit; E9a has zero placed n0AL, E9b exactly one with explicit owner and creation number; no stock fields/calls.
- **Handoff:** Two consecutive exact `vN` artifacts with map/w3u/units.doo/script hashes and decoded placement count/record.
- **User question:** E9a: “Is the altar absent as expected?” E9b: “Does the preplaced HTW Hero Altar appear at the same location and remain visible/selectable?”
- **Allowed conclusion:** Comparison with E4 determines whether placement method matters for building existence/interaction only.
- **Next:** Use the passing placement method in the chosen E5/E8 stock branch.

### E10 — Restore the intended four-hero MVP fields only after the mechanism passes

- **Input:** Exact passing E7/E8 mechanism with four minimal heroes; add each intended non-stock hero modification ID/value in its own artifact, then `usst=0`, then `usrg=1`, one artifact/user check per field. Do not start from v16.
- **Single changed variable:** One modification field per build; stock timing fields are separate builds.
- **Expected:** Four correct names remain visible; after timing fields, the user reports immediate availability and the intended replenishment behavior.
- **Failure interpretation:** The first failing field group is causal; revert to the last passing artifact and split that group further.
- **Static checks:** Exact diff from previous passing build; no mechanism/placement/rawcode changes; full v2 object dump and hashes.
- **Handoff:** One next-numbered artifact per field group, each hash-linked to its predecessor.
- **User question:** “Did the roster remain exactly four custom heroes, are they immediately purchasable, and what cooldown appears before and after one purchase?”
- **Allowed conclusion:** Only success of the exact timing-field artifact permits a claim about instant stock. Full hero-selection/deployment still requires its own checklist.
- **Next:** Run the end-to-end manual checklist below, then implement regression/contract improvements without changing the proven map behavior.

## 13. Manual verification checklist — user owned

For every handed artifact, the user records the exact path and SHA-256 before answering:

- [ ] Normal custom-game menu shows the expected real player slots.
- [ ] Loading completes without disconnect, compile rejection, or crash.
- [ ] Expected scene/gameplay is visible on camera.
- [ ] Camera can move normally.
- [ ] Expected building exists at the stated location.
- [ ] Building name matches the experiment.
- [ ] Building is visible, selectable, and targetable.
- [ ] List every displayed sold unit by name; explicitly note unexpected normal heroes.
- [ ] Record displayed cooldown/start delay before purchase.
- [ ] If purchase is in scope, record whether the click succeeds and which unit is created.
- [ ] Record ownership/control, spawn location, selection, camera movement, and any rejection message.
- [ ] If replenishment is in scope, record the observed post-purchase cooldown.
- [ ] Record whether selection completion removes the altar, cleans fog, deploys heroes, and starts Round 1.
- [ ] Immediately preserve relevant War3Log lines with timestamp and exact map path; absence of an error is not a pass.

## 14. Stop conditions and safety rules

Stop immediately if:

- The golden source hash differs from the required hash before or after any operation.
- The working tree contains overlapping edits that cannot be preserved and staged independently.
- A build would overwrite the golden source, an existing build, or an existing `Maps\Test\vN` folder.
- A new `war3map.w3u` is not v2, has unexpected object counts/IDs/types/trailing bytes, or fails reopen/reinspection.
- JASS lookup/validation is unavailable or any changed call is unknown/invalid.
- More than one runtime variable changed without an intermediate control artifact.
- Build/output/copy hashes differ, or the user's result cannot be tied to the exact hash.
- The user reports a compile rejection or load crash. Preserve the log/artifact, return to the last passing row, and do not continue down the matrix.
- `useu` or stock timing semantics are being asserted from model memory, a field-name website, or round-trip tests rather than a known-good comparison plus user observation.
- Any agent is about to use Windows UI automation or infer runtime success from launch/process/static evidence.

Never use arbitrary MPQ patching, mutate installed-game files, discard old artifacts, reset unrelated work, or record a user-observed MCP evidence milestone without an exact matching session/build/hash.

## 15. Recommended next implementation task

Create a scoped implementation task named **“Run E0-E2 current-toolchain custom-hero isolation”**. Its agent should:

1. Add a reusable diagnostic variant runner that consumes explicit source/JASS/object-definition inputs and persists an MCP-compatible manifest when using an MCP build ID.
2. Make the live fixture the source of truth for tests; tests must load `v8-hero-objects.json` or a new minimal experiment fixture instead of reconstructing a different altar in test code.
3. Add semantic allow-list metadata/tests for the specific object fields used by the experiment, while labeling them static/fixture-backed rather than runtime-proven.
4. Build and hand off E0 only, wait for the user's exact-hash result, then E1, wait again, then E2. Do not prebuild the entire matrix or implement the final altar fix.
5. Commit and push only the runner/tests/fixtures and hash-linked evidence required for the completed rows. Runtime conclusions remain in the user-owned observation record.

After E0-E2 establish custom-unit parsing and instantiation, the following task should execute E3-E6 to isolate custom building creation and one-hero stock. Four-hero roster, timing fields, and end-to-end selection come only after those controls pass.
