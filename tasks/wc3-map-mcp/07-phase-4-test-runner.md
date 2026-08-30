# Phase 4 Work Packet - World Editor and Warcraft III Test Runner

Status: implementation complete; real editor/game observations remain pending.

Implementation note (2026-08-30): exact-build launch, test-copy hashing, durable session records, ordered evidence recording, artifact re-verification, process conflict handling, and fake-runner coverage are implemented. The manual application gate is intentionally still open; no World Editor or Warcraft III pass is claimed from process start.

This automates launch and evidence recording. It does not automatically judge gameplay quality and must not kill unrelated editor/game processes.

## Required reading and inputs

Read the mandatory context, Phase 3 handoff, `11-safety-recovery-and-audit.md`, `12-testing-and-definition-of-done.md`, and launch behavior recorded in Phase 0.

## Goal

An agent can launch one exact build in World Editor or Warcraft III and obtain a test session linked to the build hash. Process start, editor open, game load, smoke success, and playtest success remain distinct.

## Files to create or complete

```text
mcp-server/src/services/launch-service.ts
mcp-server/src/tools/launches.ts
mcp-server/src/platform/windows/{process-runner,warcraft-paths}.ts
contracts/schemas/test-session.schema.json
mcp-server/test/{unit,integration}/launch-*.test.ts
docs/compatibility/launch-behavior.md
```

## Test-session record

Record session UUID, project/transaction/revision/build/hash, target, executable/version, exact argument array, test-copy path/hash, PID/start UTC, milestones and recorder, exit state, artifacts/notes, and final evidence level.

## Tools

### `wc3_launch_editor`

Verify editor path/version and build manifest/hash; create session; launch `World Editor.exe` using the Phase 0-proven argument array such as `-loadfile <map>`; avoid shell command strings; record PID/start; return only `process_started`. A later observation upgrades to `editor_opened`.

### `wc3_launch_test_map`

Verify build and test root, copy to unique test name when required, verify copy hash, launch game with tested argument array, and return `process_started`. Initial existing-process policy is `fail_if_running`; do not terminate anything.

### `wc3_record_test_result`

Input session, expected build hash, milestone, result, notes, and optional artifacts. Milestones: `editor_opened`, `game_loaded`, `smoke_test`, `playtest`. Recorder is `user_observation`, `agent_log_observation`, or a future verified harness. Do not silently skip evidence order.

### `wc3_get_test_session`

Return session and verify referenced artifacts still exist/match.

## Process safety

- Native process API, executable plus argument array.
- No broad termination.
- Existing process returns a clear conflict.
- Only editor/game windows are visible.
- Bound waits and return while apps remain open.
- Running process is not proof of map load.

## Chunk checklists

Support manual checklists. Example for `HTW-01`: preparation begins once; timer counts down; combat begins once; resolution occurs once; next preparation begins; `[HTW]` includes correct wave ID; no soft lock/duplicate transition.

## Tests

- fake runner verifies executable/arguments;
- labels cannot inject arguments;
- hash mismatch blocks launch;
- outside-root test destination rejected;
- existing-process policy does not terminate;
- session survives server exit;
- milestones cannot reference another build;
- evidence levels remain distinct;
- failures never become passed;
- source hash unchanged.

## Manual acceptance

Launch Phase 3 no-op in editor, record observed open, launch in game, record observed load, then repeat for minimal changed build. Keep failures separate.

## Completion gate and handoff

The implementation gate is complete: exact builds launch through a native executable/argument-array boundary, sessions persist across server lifetime, evidence upgrades require ordered observations, and no tool can kill unrelated processes or forge evidence from process start. The manual application gate remains open until an exact no-op and minimal changed build are observed in World Editor and Warcraft III.

Handoff contracts, verified arguments, conflict behavior, session schema, session IDs/evidence, log locations, and checklist format.
