# Phase 4 - Editor and Game Test Runner

## Objective

Collect observable compatibility and runtime evidence for a built test map.

## Implement

- `wc3_launch_editor`
- `wc3_launch_test_map`
- `wc3_record_test_result`
- process discovery and configured executable paths;
- safe copy into a dedicated Warcraft III test directory;
- launch timestamps, command arguments, exit state, and log discovery;
- optional screenshot attachment/manual checklist handoff;
- test-session manifest linked to the build transaction.

## Evidence levels

1. `built`: archive was created.
2. `editor_opened`: World Editor opened the selected build.
3. `game_loaded`: Warcraft III loaded the selected build.
4. `smoke_passed`: defined initialization behavior was observed.
5. `playtest_passed`: the chunk-specific gameplay acceptance test passed.

Never upgrade evidence levels automatically without the corresponding observation.

## Automation boundary

Launching processes and collecting logs can be automated. Judging terrain appearance or gameplay feel requires a user observation or a separately designed visual/runtime harness. Screen automation may assist but is not the source of truth by itself.

## Acceptance criteria

- The runner launches only the explicitly selected build.
- Existing editor/game processes are not killed without explicit permission.
- Every reported test result includes map hash and transaction ID.
- Failed launch, failed load, and failed gameplay test remain distinct outcomes.
