# v26 baseline assessment

The 2026-09-06 implementation inspection found that the v26 work item's
"existing" purchase catalog, threat budgets, and neutral filler are absent
from the exact v25 playable map. The task cannot meet its full acceptance
criteria while also preserving an existing roster and balance: those
definitions have not been implemented. No gameplay changes or v26 build
were made during this assessment.

## Verified baseline

- Git HEAD at inspection: `31b7c32` (v26 work item), following the v25
  implementation commit `3519292a434e96561947a57632fa440f36e10024`.
- Playable file:
  `C:\Users\hp\Documents\Warcraft III\Maps\Test\v25\HeroTeamWars_v25.w3m`.
- SHA-256:
  `FF58C3AEB0B0B79FF5EBCA9DCCCDB461B9A31FF5232E6225F8B4CB76000D9335`.
- Saved build `d1385d7b-df5a-487f-8601-9ddda5a68ccc` has that same hash.
  Its exact `war3map.j` was retrieved through `wc3_get_script_source`;
  script SHA-256:
  `F48C027D9DF5122EE42656CA80A75F1DAF5FF916D0B1AA2CE37B5589B9E470B3`.
- Full archive inspection artifact:
  `tools/wc3-map-mcp/artifacts/reports/inspect-daf92b5c-2086-40ae-90e9-35f24ee6b202.json`.
  Its SHA-256 is
  `B1AAF624D87B40955B9574F39271ECA760D79ACBCE5FD1C89285ADA61132D6F0`;
  there were no parse warnings. This local artifact is ignored by Git.
- The golden map hash remains
  `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- `wc3_project_status` was called first. The effective normal configuration
  still selects the golden source, not v25. No configuration was changed.

The user's overall v25 playtest remains valid evidence of general playability.
It does not establish that these absent systems or their edge cases work.

## What exists and what is missing

The following findings were checked in the exact v25 script and object data.
The linked repository modules contain the corresponding implementation.

| Area | Actual v25 behavior | Missing prerequisite |
|---|---|---|
| War Camps | `HTW_Heroes_Initialize` creates owned standard `hhou` Farm markers. | Purchasable creep entries and a player-facing purchase binding. |
| Send catalog | `HTW_Content_SendUnits` is an empty hook. `HTW_Content_SendOne` accepts a caller-supplied unit type. | Authoritative roster, costs, threat values, roles, and descriptions. |
| Purchases | `HTW_Economy_Purchase` accepts caller-supplied cost/type, checks phase, route, gold, and an empty queue, then deducts once. Its only caller is an otherwise uncalled queue wrapper. | Reachable purchases, catalog-backed validation, and explained rejection reasons. |
| Personal queue | One unit type/count/destination per player; any existing queue rejects a further purchase. | Mixed-type queues and personal threat accounting. |
| Gold | Internal personal balances start at zero and gain `HTW_WaveReward + HTW_InterestGold` after resolution. | Purchase UI integration; no script binding synchronizes these balances to the native gold display. |
| Base wave | Three standard Footmen are created in each arena during preparation. | A shared planned composition/threat definition; base spawning currently occurs before combat. |
| Send deployment | One pending unit is attempted per periodic runtime tick, rotating between players. | An immutable full-wave plan independent of remaining queue counts. Sends also are not added to the base-wave cleanup groups. |
| Sparse lobby | No filler selector exists. | An implemented rule selecting filler and its composition; there is nothing to include conditionally yet. |
| Information | Broadcast numeric phase, lives, and destination text. | Countdown, hero/level overview, private queue view, and incoming preview. |

Relevant modules:
[`heroes.j`](../../scripts/mcp/systems/heroes.j),
[`send-units.j`](../../scripts/mcp/content/send-units.j),
[`economy.j`](../../scripts/mcp/systems/economy.j),
[`sending.j`](../../scripts/mcp/systems/sending.j),
[`base-waves.j`](../../scripts/mcp/content/base-waves.j),
[`waves.j`](../../scripts/mcp/systems/waves.j), and
[`information.j`](../../scripts/mcp/systems/information.j).

The unit-object member contains the four custom heroes, the shared hero
altar, and the spell dummy. It has no creep-shop definitions. The only unit
sale event registration in the script is the hero altar. The script contains
no threat or filler state and no calls into `HTW_Sending_QueueCreep`.

The design documents propose a roster and placeholder threat targets, but
do not define a current purchasable catalog with gold prices. The engine's
`ScenarioRunner` passes synthetic prices into a separate C# simulation; those
tests do not execute a War Camp purchase in Warcraft III. Neither source is
evidence of an existing implementation to preserve.

## Required scope resolution

Use a supplied, verified baseline containing the missing systems, or expand
the task to implement the sending foundation before its readability layer.
That expansion must establish the initial creep roster and prices, per-creep
threat values, and personal budget progression.
It must also connect purchases to the Camps and make preview and deployment
consume the same accepted plan. Preserve the v25 heroes/Controller, four-player
layout, personal gold ownership, and shared-life rules throughout. Neutral
filler may remain absent: the work item requires displaying it only when an
existing rule actually selects it, so its absence alone is not a blocker.

An independent read-only review confirmed these findings, including that the
saved v25 script normalized to LF exactly matches the archived script hash.

This assessment changes documentation only. No public MCP contract, gameplay
source, object data, map artifact, or runtime-evidence state changed. No new
build validation or gameplay pass is claimed.
