# v28 solo developer controls

Solo testing now has a collapsible **DEV** button beside the icon HUD. It is
available automatically when exactly one of the four MVP slots is a playing
human. Open it with the button or `-dev`; hover buttons for their effects and
matching commands. Closed, the panel occupies only the small DEV toggle.

Both buttons and chat use the same command handler. Numeric commands accept
one positive whole number; malformed values, extra arguments and out-of-range
values are rejected before gameplay changes. Commands affect the caller's
tracked hero, personal gold and shared team lives. Computers and empty/left
slots do not count as humans. Every action rechecks eligibility. Setting
`dev_enabled` to false in `variables/dev.variable.json` disables these controls.

| Command | Effect |
|---|---|
| `-dev` / `-dev menu` | Toggle the button panel. |
| `-dev help` | Print all commands and limits. |
| `-dev next` | Finish selection, start combat, or resolve combat normally. Combat resolution grants the normal reward exactly once. |
| `-dev prep` | Restart preparation for the current wave, clearing current creeps, queues and plans without rewards. |
| `-dev combat` | Start combat from preparation; finish hero selection first if needed. |
| `-dev hold` | Toggle the preparation/combat countdown hold. Units, purchases, combat and spawning continue. |
| `-dev resume` | Release a held timer with its saved remaining time. |
| `-dev gold 5000` | Add personal gold, mirrored to Warcraft gold. Amount 1–100000; balance cap 1000000. Omit the amount for +1000. |
| `-dev xp 2500` | Add 1–100000 XP to the chosen hero. Omit the amount for +1000. |
| `-dev level 5` | Set hero level 1–10. Lowering levels can remove learned ability ranks. |
| `-dev levelup` | Add one hero level, up to 10. |
| `-dev heal` | Full HP/mana and cooldown reset; checked revival if dead and the team is still living. |
| `-dev lives 50` / `-dev addlives` | Set own team's lives to 1–999, or add five up to 999. |
| `-dev wave 10` / `-dev nextwave` | Fresh preparation for wave 1–100, or the next wave, without a reward. |
| `-dev reset` | Restart practice at wave 1 with both teams living and 15 lives, clear creeps/queues/terminal state and restore selected heroes. Keep heroes, XP and gold. |

After elimination, use Reset practice to restore play. Selection jumps auto-pick
any active unselected hero using the existing timeout path. Phase jumps release
held clocks. Restart prep and wave jumps preserve hero health/death state; use
Heal or Reset practice when recovery is needed. Reset reports failed revivals
instead of marking a dead hero alive. An internal `HTW_DevUsed` flag records
successful gameplay-changing controls; opening the menu or help does not set it.

## Implementation

`systems/dev-tools.j` owns authority, strict parsing, chat dispatch and cleanup.
`systems/dev-menu.j` creates stock text buttons with hover tooltips. Allocation
runs on every client at the first elapsed runtime tick, never at map init.
All roots are created in a stable order. Only root/panel visibility is local;
gameplay does not branch on `GetLocalPlayer()` or read UI text for command data.
The synchronized `FRAMEEVENT_CONTROL_CLICK` event calls the handler once with
`GetTriggerPlayer()` and the matching player-owned frame. There is no extra
sync-data hop. The handler ignores foreign and stale button handles.

The expanded panel occupies 0.250 by 0.214 in the regular 0.8 by 0.6 UI area,
left of the existing HUD. It has two columns of six buttons and a timer status
label. Frame hover text explains exact effects. `EVENT_GAME_LOADED` invalidates
the frame-ready flag and destroys the previous click trigger. The next elapsed
tick rebuilds frames and registrations without touching stale framehandles.

Held clocks store remaining time and use `PauseTimer`. Resume uses `TimerStart`
with that duration and the original one-shot callback; canonical jassdoc warns
that `ResumeTimer` can double-fire. Phase callbacks and the regular tick honor
holds, while preparation still refreshes its plan. Terminal state, phase change
or loss of solo eligibility releases the hold state; loss of eligibility
restarts the appropriate timer so normal play is not stranded.

Normal combat advancement calls the existing resolution path and relies on its
wave-resolved event to prepare the next wave. Practice restarts deliberately
clear units and plans without firing reward/resolution events. Existing object
definitions, hero kits, routing and ordinary gameplay remain unchanged.

Native declarations and behavioral constraints were checked against the pinned
local jassdoc catalog. The stock button template follows the author's
[text-button example](https://www.hiveworkshop.com/threads/ui-create-a-textbutton.315897/).
No MCP tool, request/response schema, serializer, policy, evidence gate or host
allow-list changes are required; these controls use the existing typed gameplay
composition and `framehandle` support.

## Build and verification

The build driver is `scripts/build-v28-dev-menu.mjs`. From the repository root,
run `node tools/wc3-map-mcp/scripts/build-v28-dev-menu.mjs stage <evidence-dir>`
with a fresh directory below `tools/wc3-map-mcp/artifacts/`. Review dry-run
evidence, then run the same command with `build`. Use `rebuild` only to retry an
already applied revision. Attempt-specific evidence filenames preserve retries.

The exact v27 baseline is copied and hash-checked before staging. Revision-zero
composition is checked explicitly. Only dev functions/globals and seven named
integration functions may change; existing initialization and tick behavior
must survive removal of the new dev calls/initial values. The driver validates,
applies, diffs, builds, verifies the build report, and reopens the archive using
MCP tools. Only `war3map.j` and `(attributes)` may change; object data and all
other inspected map components must match v27. The immutable golden map and
v27 source hashes are rechecked. Publication is a separate numbered test-copy
handoff, with no fabricated smoke evidence or promotion.

Build evidence on 2026-09-08:

- Published folder: `C:\Users\hp\Documents\Warcraft III\Maps\Test\v28\`.
- Map filename: `HeroTeamWars_v28.w3m`. The published hash matches the output
  hash below; v27 and the golden source remain unchanged.
- Transaction `228c1c36-7036-4208-9e0a-a87066a32ed6`, revision 1.
- Build `4ee28f78-6adc-467a-9f22-a054a792c6cb`, `debug`, `mvp_2arena`.
- Output SHA-256:
  `E845EBCF3113B77AFC266900D095FFA6E169B21BE90D0FAE4B0348428154C02C`.
- Source v27 SHA-256:
  `DDAE5227FAB5373247BE9325EE6CC029DEBADA4197E06387B2E039E2C18A3DAB`.
- Golden source SHA-256:
  `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- Evidence directory: `tools/wc3-map-mcp/artifacts/v28-dev-20260908-a/`.
- Canonical generated-source validation: 0 errors, 166 API annotation or type
  inference warnings. Transaction validation: 0 errors, 0 warnings, 14 info.
- Dry-run, apply, diff, validation, build, verified build report and archive
  reinspection passed. Reopened semantic differences and unexpected content
  changes are empty. Only `war3map.j` and `(attributes)` changed. Revision 0
  already contains composed JASS, so the empty revision 0-to-1 operation diff
  must be read alongside the explicit v27-to-composed function/global checks
  and final source-to-build archive comparison.
- Independent read-only review found no actionable correctness defects.
- Full `scripts/test.ps1` passed: 48 source tests (26 retained, 22 new), 128
  engine tests and 70 MCP tests. This includes catalog generation and
  registration/schema/documentation/host allow-list consistency. Full log:
  `tools/wc3-map-mcp/artifacts/v28-full-tests.txt`.
- After the final test-fixture cleanup, all 48 source tests passed again;
  log: `tools/wc3-map-mcp/artifacts/v28-final-source-tests.txt`.

Automated source execution uses mocked Warcraft natives. It cannot establish
rendering, hover/click behavior, save/load persistence, or actual game runtime.
The user owns Warcraft III verification: open the panel in a solo slot, try
phase advance and hold/resume, grant gold/XP, heal after death, restart during
combat and after elimination, and save/load then recheck buttons. A match with
two humans should hide and reject controls. These are pending manual checks.
