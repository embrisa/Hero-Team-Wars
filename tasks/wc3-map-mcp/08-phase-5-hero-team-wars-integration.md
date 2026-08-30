# Phase 5 Work Packet - Hero Team Wars Through the MCP

Status: blocked until inspection, transactions, builds, and test evidence all work on the current map.

This uses the completed toolchain to build the game. It does not bypass safety gates merely because the MCP exists.

## Required reading and inputs

Read the mandatory context and all Hero Team Wars design files, especially `design/02` through `design/09`. Read all Phase 0-4 handoffs and the script-ownership ADR. Do not create a second build path.

## Goal

Implement `HTW-00` through `HTW-06` as reviewed MCP transactions. Each chunk has source changes, typed operations, semantic diff, validation report, build, test session, observed result, and ledger/change-log update.

## Gameplay source layout

Exact language follows the Phase 3 ADR. Lua example:

```text
scripts/mcp/
  main.lua
  config/{tuning,teams,regions}.lua
  core/{bootstrap,state,events,debug}.lua
  systems/{phases,waves,heroes,lives,elimination,economy,sending,routing,information}.lua
  content/{base-waves,send-units}.lua
  tests/assertions.lua
```

Add modules only when their chunk starts. Centralize tuning. Use explicit teams and named regions, never color.

## Per-chunk protocol

1. Check project status and current accepted hash.
2. Inspect prerequisites and reconcile the ledger.
3. Document goal/non-goals/test.
4. Begin transaction.
5. Apply smallest operations/source changes.
6. Review diff and reject unrelated changes.
7. Validate.
8. Build unique debug map.
9. Launch/test exact build.
10. Record evidence.
11. Update ledger only from observations.
12. Append change log with transaction/build/test IDs.
13. Promote only after pass.
14. Fix current chunk before starting next.

## MCP-HTW-00 - Baseline

Generate candidate report; inspect all supported components; use World Editor for unsupported facts; capture version, trigger tree, variables, complete regions, players/forces, placed/custom objects, and test behavior; reconcile ledger with provenance; preserve unknowns.

Pass: baseline copy opens/saves/loads, original hash unchanged, ledger distinguishes archive/editor evidence.

## MCP-HTW-01 - Foundation and phases

Implement centralized 15 lives/35-second preparation/90-second soft limit, explicit two teams, match state, round/wave ID, one initialization path, timers/transitions, `[HTW]` debug events, duplicate-transition guards. No camps, sending, hero selection, talents, items, assets, or six-team routing.

Pass: observed preparation -> combat -> resolution -> next preparation without duplicate/soft lock.

## MCP-HTW-02 - Base waves

Implement data-driven deterministic base waves, arena spawn controllers, neutral-hostile ownership, wave/arena tracking, deterministic timing/positions, alive counts, 90-second cleanup, exact-once resolution.

Pass: repeated tests spawn same units in both arenas, leave no tracked creeps, and delayed deaths cannot affect later waves.

## MCP-HTW-03 - Heroes, lives, elimination

Implement placeholder heroes as needed, explicit hero/team map, exact-once death accounting, one life per death, both-heroes-dead total of three, no leak/gate/timeout loss, revival, elimination cleanup, victory/draw.

Pass: single death, team wipe, repeated callback, timeout, and elimination match locked rules.

## MCP-HTW-04 - Sending loop

Implement personal gold, preparation-only spending, two personal tier-1 camps per team, three send units, personal queues, deterministic staggered opposing-arena spawn, never fight own sends, visible incoming composition/threat.

Pass: personal spending isolation, correct destination, no self-send, no duplicate/lost purchases.

## MCP-HTW-05 - Information and repeatability

Implement phase/timer/wave/lives display, incoming composition/threat, personal queue, all team hero compositions/levels, comparable `[HTW]` events, timeout/cleanup/elimination messages.

Pass: tester can explain state and choices without hidden scouting.

## MCP-HTW-06 - Six-team expansion

Start only after repeated HTW-04/05 passes. Implement 12 slots/six teams/six arenas, living-team order, `offset = 1 + ((round - 1) mod (N - 1))`, visible locked routes, next-preparation recalculation, no mid-wave retargeting/carryover, scoped elimination cleanup, one-team victory.

Pass: six teams complete six waves without duplicate queues, wrong routes, stuck waves, desync, or carryover.

## Required tests across gameplay chunks

- Run a script/static parse check for every authored source file before building.
- Test initialization from a fresh map load, not only a restarted round.
- Repeat each chunk's happy path at least twice to detect retained state.
- Exercise timer expiry and early-completion paths.
- Exercise duplicate/deferred event callbacks against the current wave ID.
- Test one player per team first, then all four active players before six-team expansion.
- Verify map/player/region identities from inspection rather than assuming rawcodes or coordinates.
- Verify every failed gameplay test remains attached to its exact build hash and is not overwritten by a later pass.
- Recompute the original source-map hash after every chunk session.
- Search the design/ledger/change log for contradictions before marking a locked rule implemented.

Application gameplay observations remain required even if static and map validators pass.

## Change-log record

Each chunk records chunk/transaction/source/build hash/test session, changed files/components, diff, validation, observed test, ledger updates, limitations, and next readiness.

## Completion gate

Each chunk is accepted independently. The first playable is HTW-00 through HTW-05 on four players/two arenas. HTW-06 is later and must not delay proving the smaller loop.
