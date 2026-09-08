# v27 compact icon HUD

The user reported that v26's text HUD and boards were too large and requested
icons with hover tooltips. This records that readability observation only; it
does not establish a complete v26 gameplay or multiplayer pass.

## Presentation

The 27-row multiboard is replaced by a regular Frame panel at the upper right.
Its root occupies 0.250 by 0.185 in Warcraft's 0.8 by 0.6 UI coordinates. It
contains a phase/time/wave header, two teams' lives and hero portraits, personal
gold/budget/routes, three outgoing send icons, and three incoming unit icons.
Small numbers remain visible; descriptions and breakdowns appear on hover.

Hover a team shield for shared lives, team status and routes; a hero portrait
for player, hero, level and inactive/unselected/down/eliminated status; gold or
budget for personal resources and purchase availability; or the Camp icon for
outgoing and incoming routes. SEND counts belong to the viewing player.
Incoming tooltips split base, enemy sends and filler, show per-type and whole
wave threat, and distinguish preparation updates from the locked full wave.
The IN row's `th` number is whole-wave threat. Hero captions use `P` for player,
`L` for level and `X` for down/eliminated; hover explains the exact state.

These are informational hover targets. War Camp Q/W/E remains the purchase
control. Icons retrieve the existing unit/ability art at runtime, while gold
uses the stock tooltip gold image. No imported assets or new object definitions
are required. Catalog values, purchases, queues, budgets, wave plans, combat,
hero kits and routing rules are unchanged.

## Frame lifecycle and evidence limits

All four roots and their children are allocated in the same order on every
client, beginning at the first elapsed runtime tick. Root handles are acquired
before any local branch. Every tooltip is a regular Frame owned by its hover
target, bound exactly once, initially hidden and reused during refreshes.
The single local branch changes only the corresponding root's visibility.
An `EVENT_GAME_LOADED` listener invalidates the ready flag. The next elapsed
display callback recreates all HUD frames and restores local visibility without
touching stale framehandles or registering duplicate load listeners.
Disabled hover buttons preserve tooltips while allowing clicks through and
avoid keyboard focus or gameplay-changing UI events. Text and icon children
are also disabled for mouse input. Tooltip backgrounds follow wrapped text
below the panel inside the regular 4:3 frame area.

The MCP now accepts `framehandle` typed variables, including arrays. Tool names,
transaction gates and evidence requirements are unchanged. Canonical JASS
lookups cover every new frame API, including creation timing, synchronized
allocation, safe templates and tooltip binding constraints.

The template choice follows the author's [Frame tooltip example](https://www.hiveworkshop.com/threads/ui-frames-and-tooltips.316665/)
and [stock template inventory](https://www.hiveworkshop.com/threads/ui-list-default-mainframes.317884/).
The stock gold image is recorded in [War3Skins.txt](https://github.com/theQuazz/wc3-devkit/blob/master/tools/widgetizer/Input/War3Skins.txt).
Native signatures and constraints remain sourced from the local pinned
jassdoc index, rather than inferred from examples.

## Verification and handoff

Published on 2026-09-08:

- Folder: `C:\Users\hp\Documents\Warcraft III\Maps\Test\v27\`.
- Filename: `HeroTeamWars_v27.w3m`.
- Output/published SHA-256:
  `DDAE5227FAB5373247BE9325EE6CC029DEBADA4197E06387B2E039E2C18A3DAB`.
- `scripts/test.ps1`: 26 source tests, 128 engine tests and 70 MCP tests passed.
  This includes catalog drift and schema/documentation/registration consistency.
  Full log: `tools/wc3-map-mcp/artifacts/v27-full-tests.txt`.
- Nine HUD source tests cover geometry, allocation order, per-player visibility,
  one-time bindings, public/personal data, frozen/cleared previews, and load
  recovery. Frame mocks observe 316 allocated frames and 60 tooltip bindings
  per client, reused between refreshes. This is automated source evidence only.
- Golden source and v26 hashes remain unchanged. Golden SHA-256:
  `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.

Reproduce from the repository root with
`node tools/wc3-map-mcp/scripts/build-v27-icon-hud.mjs stage <evidence-directory>`.
Use a fresh project-relative directory under `tools/wc3-map-mcp/artifacts/`.
Review the saved dry-run and changed-function list, then run the same command
with `build` instead of `stage`. A failed applied build can be retried with
`rebuild` without reapplying or replacing failed evidence. Each invocation uses
a unique response-file prefix so repeated retries retain separate evidence. The driver copies
and hash-checks exact v26, explicitly checks revision-zero composition, then
validates/applies/diffs/builds/reopens through MCP tools. Every existing
non-information function, non-HUD global and object must match v26; only `war3map.j` and
`(attributes)` archive changes are permitted. Publication is a separate copy
to the next unused numbered test folder, not smoke-evidence promotion.

The reviewed build uses transaction `5008f6e3-170a-4747-9790-58feea22d340`,
revision 1, and build `ad6ce3fd-98fd-4c00-a170-bf4249a07377` (`debug`,
`mvp_2arena`). Exact source baseline is v26, SHA-256
`16C577CB12F73C30B86C5FD8F870106F0EF5CDE337795A50CE8D26B63B8C617C`.

JASS validation, dry-run, apply, transaction diff/validation, map build,
verified build report and archive reinspection passed. Reopened semantic
differences and unexpected archive changes are empty. Only `war3map.j` and
`(attributes)` changed. All v26 objects, players, forces, regions, placements,
imports, non-information functions and non-HUD globals are preserved.
Evidence: `tools/wc3-map-mcp/artifacts/v27-hud-20260908-b/`.

Independent review identified missing save/load recreation and retry filename
collisions. Both were fixed before this build; the earlier dry-run remains
separate evidence and is not the handoff.

Source tests with mocked frame natives cannot establish rendered appearance,
hover behavior, click-through behavior, save/load persistence or multiplayer
synchronization. The user owns Warcraft III verification.

Manual checks: load v27; confirm the panel stays clear of native UI; hover every
icon; compare two teammates' queues and the incoming breakdown; start combat
and verify locked counts persist through later spawns; check the next wave,
inactive/unselected/down/eliminated states, and clicking the arena through
informational icons. Save/load a match and recheck the panel/tooltips. Check
both 4:3 and widescreen if available.
