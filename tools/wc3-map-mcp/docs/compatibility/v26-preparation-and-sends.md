# v26 preparation, purchases, and incoming waves

The exact v25 baseline lacked reachable creep purchases and threat definitions.
The user authorized changing MVP units and adding the missing foundation to
exercise MCP capabilities, superseding the original preservation-only scope.

## Player behavior and tuning

Select your War Camp and use Q/W/E to queue Footmen, Riflemen, or Knights.
Command tooltips show personal gold, threat, role, and a short description.
Standard creep combat mechanics remain inherited.

| Send | Role | Gold | Threat |
|---|---|---:|---:|
| Footman | Frontline | 10 | 1 |
| Rifleman | Ranged | 20 | 2 |
| Knight | Heavy | 40 | 4 |

`content/send-catalog.json` generates JASS accessors and typed Camp/command
definitions. `scripts/generate-v26-send-content.mjs --check` rejects drift.
Commands have zero native mana/cooldown, instant Channel targeting, and distinct
orders. Only synchronized purchase code charges gold. Invalid or forged
purchases, inactive/unselected players, missing/lost routes, eliminated teams,
closed purchases, insufficient gold, and insufficient threat reject before
any balance or queue change. Accepted purchases refresh the board without chat
spam; rejections explain their reason.

Each active player starts with 60 gold and receives the existing 50+10 after
resolution. Native creep bounty is disabled; the scripted balance is mirrored
to native gold after grants/purchases. Personal threat budget starts at 6,
grows by 2 each wave, and caps at 20. These values and base/filler counts live
in `config/tuning.j`. No camp tiers or additional hero abilities were added.

The board separates public phase/time, shared lives, heroes/levels and routes
from personal gold, mixed queues, remaining budget, and incoming counts. Base,
sends and filler are identified separately, with total catalog-weighted threat.
Updates occur every second and on purchases/transitions. Every client's board
and item handles are created/updated in the same order; only visibility is
local. Item handles are released. No local branch changes gameplay state.

## Shared plan and lifecycle

Preparation contains no spawned wave creeps. Each living receiving arena plans
three base Footmen plus both routed enemy players' accepted purchases. If the
sender has no active humans and no accepted purchases, two Footmen and one
Rifleman provide neutral filler. Accepted buys survive disconnect and suppress
filler for that wave. Empty/computer slots appear inactive, receive no Camp,
cannot spend and do not block hero selection. Empty teams remain available as
practice destinations.

Combat entry refreshes and freezes the complete plan. One unit per living
arena arrives each second in catalog order; a separate remaining-count array
drives deployment. The final preview includes later arrivals and never shrinks
with spawns or deaths. Failed creation retains the pending count for retry.
The existing 90-second combat limit remains.

All emitted units join their arena cleanup group. Elimination removes only
that arena's units and plan, without retargeting sends. A terminal result also
cleans/resolves the active wave exactly once. Resolution and next preparation
clear old queues, budgets and plans. Fifteen shared lives and hero-death-only
loss remain, with lives clamped at zero. The four heroes and v25 Controller
definitions/effects are preserved.

## MCP changes and automated evidence

Typed variables now support `multiboard`; operation schemas also expose the
engine's existing arrays/array_size and `fogmodifier`. Runtime and versioned
schemas agree. All 30 tool names and allow-lists remain unchanged. The JASS
scanner accepts grouped lowercase `and`/`or` expressions while validating
nested calls and canonical `And`/`Or` natives normally.
The first actual build also exposed positional object-definition comparison:
adding abilities to a map with units regrouped the reopened records and produced
a false `BUILD_REOPEN_MISMATCH`. Definitions now compare by unique stable ID;
values and modification order remain checked. A regression test covers both.

Tests execute actual repository JASS through a bounded, fail-closed subset
interpreter with mocked Warcraft natives. They cover atomic purchases and
ownership, exact boundaries, teammate aggregation, filler, frozen preview,
delayed deployment/retry, cleanup, five timer-driven waves, and board output.
This is source-executed automated evidence, not Warcraft behavior, visual
readability, or multiplayer synchronization evidence.

Run `scripts/test.ps1` for catalog drift, JASS behavior, engine and MCP suites.
Use `scripts/build-v26-preparation.mjs stage <evidence-directory>`, review the
saved dry-run and changed-function list, then `build <evidence-directory>`.
After an applied revision's failed build, `rebuild <evidence-directory>` retries
validation/build without reapplying operations or replacing the failed evidence.
The fresh directory must be under `tools/wc3-map-mcp/artifacts/`. The workflow
copies/hash-checks v25, uses a separate MCP configuration/transaction, validates
JASS, dry-runs/applies typed operations, diffs, validates, builds and reinspects.
Existing revision-zero composition is explicitly checked against v25; Controller,
main/config and generated map/team setup must be unchanged. Final inspection
compares every old object and map component and permits only `war3map.j`,
`war3map.w3u`, `war3map.w3a` and `(attributes)` archive changes.

## Final build and handoff

- Source baseline: v25, SHA-256
  `FF58C3AEB0B0B79FF5EBCA9DCCCDB461B9A31FF5232E6225F8B4CB76000D9335`.
- Transaction `aaa79870-d119-429f-bd6b-116560a527bd`, revision 1.
- Build `5e3fc540-35d8-49e6-93c1-fea98b42c5dc`, debug, `mvp_2arena`.
- Published folder:
  `C:\Users\hp\Documents\Warcraft III\Maps\Test\v26\`.
- Filename: `HeroTeamWars_v26.w3m`.
- Output/published SHA-256:
  `16C577CB12F73C30B86C5FD8F870106F0EF5CDE337795A50CE8D26B63B8C617C`.
- JASS validation, dry-run, apply, transaction diff/validation, map build,
  verified build report, and archive reinspection all passed. Reopened
  semantic differences and unexpected archive changes are empty. All v25
  objects, players, forces, regions, placements and imports are unchanged;
  four catalog-generated objects are added.
- `scripts/test.ps1`: 17 source-executed checks, 124 engine tests, 66 MCP tests
  passed, including generated object-field/schema/registration checks.
- Independent review findings were addressed: the spawn helper now returns
  success after nulling its local unit reference; array schemas require a
  size and prohibit scalar initial values, matching engine constraints.
  Follow-up checks on those final changes: 17 JASS and 5 schema tests passed,
  then the final exact map was staged/validated/built/reopened again.
- Golden source remains
  `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- Full final local evidence: `tools/wc3-map-mcp/artifacts/v26-final-20260906/`.
  Earlier failed build evidence is retained separately; it is not the handoff.

This is a playable-test handoff, not smoke-evidence promotion. Warcraft runtime,
readability and multiplayer synchronization are **unverified**. No Warcraft
interaction, installed-game changes, or promotion/evidence claims were made.

User checks: load v26 and choose a hero (including Controller); select your
Camp and try Q/W/E purchases; compare both teammates' personal charges/queues
and the enemy's preview; try insufficient-gold, over-budget and combat purchases;
watch the entire staggered wave against the locked counts; play five waves and
check resets, timers, deaths/revival and elimination labels. Sparse-lobby play
should show the explicitly labeled filler when the sender is empty.
