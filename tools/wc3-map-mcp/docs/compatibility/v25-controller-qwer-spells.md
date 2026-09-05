# v25 Controller spells and QWER

The user reported broken Controller spells and keybinds and confirmed
Classic/default hotkeys. The inspected v24 has four `Ncl6=channel` orders,
no cast/learn hotkey or button-position overrides, and space-separated target
filters containing unsupported `unit`/`point` tokens. Its two native helper
abilities retain zero-based ranks, incorrect data pointers/types, inherited
mana costs and a hero flag on the stun. The dummy is a zero-mana peasant,
spawned at the hero, and is removed after a zero-duration sleep. These are
source/data defects; their exact in-game symptoms have not been verified here.

## Repair

| Key (learn and cast) | Spell | Target | Required hero levels |
|---|---|---|---|
| Q | Arcane Lance | Living enemy unit | 1 / 3 / 5 / 7 |
| W | Gravity Well | Point, 300 radius | 1 / 3 / 5 / 7 |
| E | Mana Relay | Living same-team hero, including self | 1 / 3 / 5 / 7 |
| R | Astral Collapse | Point, 325 radius | 6 / 8 / 10 |

Cast buttons occupy columns 0-3 on row 2; learn buttons occupy columns 0-3
on row 0. Each has matching learn/cast icons and explicit tooltips. Standard
movement, attack and inventory commands remain inherited. Rank requirements
are explicit and fit Warcraft's standard ten-level hero progression.

Channel orders are respectively `thunderbolt`, `blizzard`, `banish`, and
`flamestrike`, all present in the pinned
[HumanAbilityFunc.txt](https://raw.githubusercontent.com/inwc3/wc3libs/66b637e38df68023dc94c7ed46210a28b52e0b12/src/test/resources/wc3data/Units/HumanAbilityFunc.txt).
Point skills enable the targeting image, and all four disable Channel's
inherited disable-other-abilities flag. Target filters use comma-separated
native target names; E uses `friend,hero` with air/ground/alive filters.
Their v24 damage/heal/mana values, cooldowns and cast ranges are preserved.
Arcane Lance's tooltip describes its actual immediate scripted damage.

Both native helper definitions now have correct types, data pointers and
every rank explicitly authored. They cost zero mana, have zero cooldown,
no technology requirements, and are unit abilities. Slow remains
25/30/35/40% for 3 seconds; stun remains 1.25/1.75/2.25 seconds with zero
extra Storm Bolt damage. Each target gets its own dummy at its location.
Successful dummies receive two seconds of native timed life; rejected orders
remove them immediately. Removing sleeps also avoids serial waits across the
area-effect loop. Native buffs still own their expiration.

The offline field catalog grows from 47 to 60 entries, sourced from pinned
[AbilityMetaData.slk](https://raw.githubusercontent.com/inwc3/wc3libs/66b637e38df68023dc94c7ed46210a28b52e0b12/src/test/resources/wc3data/Units/AbilityMetaData.slk).
Hotkeys use native String tag 3, level/pointer 0. `Ncl5` uses native Int Bool
encoding, 1-based repeated levels and pointer 5. Public tool names, request
schemas, host allow-lists and evidence gates are unchanged.

## Reproduction and evidence

- Source: saved v24 build `2742b754-be4e-43b7-9061-40943cf5df93`, SHA-256
  `50639249A0E676B7EAC620492E11BC6C73BEB5775A003FA13505B9C89FC7EF74`.
- Transaction: `746ebc01-3d60-407f-a7d7-298e2c6f2b1e`, revision 1.
- Build: `d1385d7b-df5a-487f-8601-9ddda5a68ccc`, debug, `mvp_2arena`.
- Output SHA-256:
  `FF58C3AEB0B0B79FF5EBCA9DCCCDB461B9A31FF5232E6225F8B4CB76000D9335`.
- Golden source remains
  `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- Recoverable source snapshot, JASS validation, dry-run, apply, reviewed diff,
  transaction validation, build/reopen and verified build report passed.
- Automated suites: 122 engine tests and 65 MCP tests passed.
- Independent decoder consumed all 10,572 ability bytes and all six records,
  checking native tags, scopes and actual key/order/effect values at each rank.
- Only `war3map.w3a`, `war3map.j`, and `(attributes)` changed. Only the two
  helper-casting functions changed in JASS. Other object definitions, players,
  forces, regions, placements and imports match v24.

`scripts/prepare-v25-controller.mjs` produces the six typed object updates
from an exact v24 full inspection. It never writes a map. The separate typed
script operation preserves v24 except the two helper functions; the repository
module in `scripts/mcp/content/abilities.j` has the matching implementation.
Historical v21 fixtures are historical, not the current repaired definitions.
`scripts/verify-v25-controller.mjs builds/diagnostics/v25` checks the saved MCP
results, exact artifact hashes and bytes. Detailed local results and the
dedicated v24-source MCP configuration are under `builds/diagnostics/v25/`
(ignored). The normal MCP configuration is unchanged.

## Manual test handoff

Published whole validated artifact:
`C:\Users\hp\Documents\Warcraft III\Maps\Test\v25\HeroTeamWars_v25.w3m`.
This is a test handoff, not smoke-evidence promotion. Runtime is **unverified**.

The user should verify a normal-menu lobby slot, successful load, on-camera
gameplay and camera movement, then purchase H003. With Classic/default keys,
verify the learn menu and cast buttons show Q/W/E/R. Learn multiple spells
in different orders and confirm each key activates only its own spell. Check
enemy-only Q, point-target W/R, E on self and a teammate, mana/cooldowns,
slow/stun duration and expiration, and every rank including R at levels 6/8/10.
No Warcraft III UI interaction or installed-game file changes were performed.
