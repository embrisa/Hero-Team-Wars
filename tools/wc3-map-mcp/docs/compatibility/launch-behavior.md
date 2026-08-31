# Phase 4 launch behavior

Status: implementation complete; real World Editor and Warcraft III observations remain a separate manual acceptance gate.

## Launch contract

The launch service accepts only a build loaded through `BuildService`. Loading a build re-reads its manifest, rehashes the generated map, and rejects a missing, changed, symbolic-link, or out-of-build-root output. The caller's expected SHA-256 must match the manifest before an executable is inspected or started.

World Editor is started with this exact argument array:

```text
["-launch", "-loadfile", "<absolute-build-path>"]
```

The retail Reforged editor requires `-launch` when started outside the Battle.net
editor button; without it, `World Editor.exe` can hand focus back to Battle.net
and exit cleanly without opening a window.

Warcraft III is started with a unique MCP-owned copy under the configured `test_map_root`:

```text
["-launch", "-loadfile", "<absolute-test-copy-path>"]
```

The `-launch` flag is required for direct retail Reforged game startup outside
the Battle.net Play button; the MCP still keeps the test copy unique and
hash-linked before starting it.

The process runner uses the native executable-plus-argument-array API with `shell: false`, detached standard handles, and a visible application window. It has no terminate operation. The initial process policy is `fail_if_running`; the Windows adapter checks the target image with `tasklist.exe` and returns a conflict when an instance is already present. If process-state inspection fails, launch is refused rather than risking a duplicate.

The configured editor/game executable must be an existing regular `.exe` file. On Windows, its file version is read from the version resource and stored with the session; an unavailable version is recorded as `unknown` and does not get promoted to a claimed runtime result.

## Session record

Sessions are persisted at `tools/wc3-map-mcp/artifacts/tests/<session-id>/test-session.json`. Each record includes correlation/session IDs, project, transaction, revision, build ID/hash/path, executable/version, exact argument array, PID, UTC start time, exit-state observation, optional test-copy path/hash, milestones, recorder, notes, evidence artifacts, and current evidence level. The transaction manifest also receives the session ID. The session file is independent of the MCP process lifetime.

`exit_state: "unknown"` means the detached process was assigned a PID, but the MCP server did not keep a wait loop that could safely claim its eventual lifetime. Starting a process is recorded as `evidence_level: "process_started"`; it is not editor-open or game-load evidence.

## Evidence rules

`wc3_record_test_result` is the only path that upgrades evidence, and it accepts only an explicit `user_observation` or `agent_log_observation`:

| Target | Ordered milestones | Evidence level after a pass |
|---|---|---|
| editor | `editor_opened` | `editor_opened` |
| game | `game_loaded` → `smoke_test` → `playtest` | `game_loaded` → `smoke_passed` → `playtest_passed` |

Each milestone can be recorded once. A failure remains in the session and blocks later milestones that depend on it. A failed observation never upgrades the evidence level, and a later tool call cannot forge a pass from process start. The expected build hash is checked against both the session and the current build manifest. `wc3_get_test_session` re-verifies the generated build, game test copy, and every evidence artifact before returning `verified: true`.

## Manual acceptance procedure

The application smoke suite is intentionally not run by automated tests. To collect real evidence after building an exact no-op or minimal-change artifact:

1. Use a writes-enabled project configuration and call `wc3_launch_editor` with the build ID and uppercase output hash.
2. Observe that the World Editor window opens the exact map, then call `wc3_record_test_result` with `editor_opened`, the same hash, and notes describing the observation.
3. Call `wc3_launch_test_map` for the same build. Observe Warcraft III loading its unique test copy, then record `game_loaded` with that session ID and hash.
4. Record `smoke_test` and `playtest` only after their distinct acceptance checks are observed.
5. Call `wc3_get_test_session` and retain its verified session artifact with the build manifest.

The previous Phase 3 runtime attempt did not establish an editor/game pass: the existing World Editor session was left untouched and the installed Warcraft III process exited without exposing a game window. Phase 4 therefore claims automated launch-policy/session evidence only until this procedure is deliberately performed.
