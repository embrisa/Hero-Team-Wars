# v21 Controller spell kit

The two-team MVP adds a custom kit exclusively to H003 / HTW Controller.
The immutable input is `map/HeroTeamWars_M0_2Arena.w3m`, SHA-256
`027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
The v20 reference is build `0b57829c-183a-4d0d-bbb0-75b25d3f96b0`, SHA-256
`E65E29FFCE8705F6DFB50759DF27C1A904F291AD49DC5CFF8EC4FB196A2DC9E5`.

| Ability | Levels | Intended behavior | Mana | Cooldown |
|---|---|---|---|---|
| A2Q1 Arcane Lance | 4 | Hostile target, 600 range, 80/120/160/200 damage | 55/65/75/85 | 5.5/5/4.5/4 s |
| A2W1 Gravity Well | 4 | Point target, 700 range, 300 radius, 60/100/140/180 damage; 25/30/35/40% movement slow for 3 s | 90/105/120/135 | 16/14/12/10 s |
| A2E1 Mana Relay | 4 | Same-team allied living hero, 600 range; 100/150/200/250 mana and 50/100/150/200 HP | 70/80/90/100 | 18/16/14/12 s |
| A2R1 Astral Collapse | 3 | Point target, 325 radius, 225/350/475 damage; 1.25/1.75/2.25 s hostile disable | 150/200/250 | 100/85/70 s |

The four primary abilities inherit Channel (`ANcl`) and use distinct orders.
Their cast UI, targeting mode, range, level count, mana cost, cooldown, icon,
and descriptions are typed object data. JASS supplies the custom damage and
support behavior. H003's normal ability list retains `AInv` and includes all
four custom abilities; its inherited Archmage hero ability list is cleared.

`A2S1` derives from native Slow (`Aslo`) and `A2T1` derives from Storm Bolt
(`AHtb`). They are implementation helpers cast by `n2D1`, never hero buttons
or altar stock. Native buff durations own the slow and stun expiration, so
there is no manual movement-speed restoration or pause/unpause state.
All assets are existing game assets.

## Preservation and evidence

The baseline composed script matched v20 exactly before editing. The final
verification compares reopened object fields, every unrelated archive member,
players, forces, regions, placements, imports, and existing gameplay functions
against that artifact. H001/H002/H004 and the n0AL roster stay unchanged.
H003 retains its identity and stats except the intended ability lists.

`scripts/verify-v21.mjs` consumes exact MCP inspection/script/build records.
Its optional `--publish` mode requires v21 to be the next unused version,
copies the complete validated artifact with exclusive creation, and verifies
the published, v20, and immutable source hashes. It never edits an archive.
This is the user-authorized whole-artifact handoff when runtime evidence is
not available; it does not mark MCP promotion or runtime evidence as passed.

Runtime status: **unverified**. No Warcraft III UI automation is permitted.

## Manual Warcraft III checklist

1. Load the exact v21 artifact from the normal Warcraft III menu.
2. Confirm all four players still see the entire map throughout selection, preparation, combat, and later waves.
3. Purchase H003 / HTW Controller from the shared altar.
4. Confirm H003 immediately appears at the exact center of the assigned team arena.
5. Confirm the ability bar contains Arcane Lance, Gravity Well, Mana Relay, and Astral Collapse.
6. Cast every spell on valid hostile and allied targets.
7. Confirm damage, healing, mana restoration, slow, and ultimate disable behavior.
8. Confirm spells do not affect the wrong team or the other three hero types.
9. Confirm hero selection completion and Round 1 preparation still work.
10. Confirm timeout auto-picks and existing MVP gameplay remain intact.
