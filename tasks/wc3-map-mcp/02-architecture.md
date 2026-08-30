# Architecture

## Component view

```text
Codex or another MCP client
        |
        | MCP over local STDIO
        v
TypeScript MCP Server
  - tool registration and schemas
  - workspace boundary enforcement
  - approval/destructive annotations
  - transaction coordinator
  - response and error normalization
        |
        | NDJSON request/response
        v
.NET WC3 Map Engine
  - MPQ/map archive reader and writer
  - Warcraft III file parsers/serializers
  - canonical map model
  - semantic diff and validators
        |
        +---- source map (read-only)
        +---- staging workspace
        +---- snapshots and reports
        +---- built test maps
        |
        v
World Editor / Warcraft III test launch
```

## Responsibility boundaries

### MCP server

- Reject paths outside configured roots.
- Validate every tool argument before starting a worker.
- Assign transaction and correlation IDs.
- Expose small semantic tools rather than arbitrary shell execution.
- Normalize engine errors into stable MCP responses.
- Enforce read-only versus mutating tool behavior.
- Never parse Warcraft binary formats itself.

### Map engine

- Read and write supported map components.
- Produce a canonical inventory and stable hashes.
- Apply typed change operations to staging data.
- Preserve unknown archive members by default.
- Generate semantic and file-level diffs.
- Validate referential integrity, format limits, scripts, and build output.
- Never decide whether an operation is authorized.

### Launch adapter

- Discover configured World Editor and Warcraft III paths.
- Copy only validated builds into the game's test-map directory.
- Launch with explicit arguments and capture process/timestamp evidence.
- Never terminate unrelated game/editor processes without explicit permission.

## Transaction lifecycle

```text
inspect -> begin transaction -> stage snapshot -> apply operations
        -> diff -> validate -> build test artifact -> optional launch
        -> promote or discard
```

A transaction is immutable after promotion. Revisions create new transactions.

## Source-of-truth policy

- Design Markdown states intended gameplay.
- `design/07-editor-state.yaml` states last observed editor configuration.
- MCP transaction manifests state proposed and applied machine changes.
- A built map states what Warcraft can load.
- A recorded launch/playtest states observed runtime behavior.

Disagreement produces a warning with all conflicting values and their origins.

## Failure isolation

- MCP server crash: no staged map is promoted.
- Map-engine crash: transaction remains failed with logs and snapshot intact.
- Validation failure: build cannot be promoted.
- Editor/game launch failure: build remains available but is marked untested.
- Runtime test failure: source input remains unchanged; discard or revise the transaction.

## End-to-end example

Suppose a later agent wants to change the map title in a component proven writable:

1. `wc3_project_status` returns source hash `H1` and supported metadata capability.
2. `wc3_inspect_map` reports current title with `observed_archive` provenance.
3. `wc3_begin_transaction(expected_source_hash=H1)` creates transaction `T1`, revision 0.
4. `wc3_apply_operations(T1, revision=0, set_map_metadata expected old title -> new title)` creates revision 1.
5. `wc3_transaction_diff(T1)` reports only the title change.
6. `wc3_validate_transaction(T1, revision=1)` produces report `V1` with no errors.
7. `wc3_build_map(T1, revision=1)` produces build `B1`, hash `H2`, status untested.
8. `wc3_launch_editor(B1, expected_hash=H2)` creates session `S1`, initially process-started.
9. User confirms the exact build opened; `wc3_record_test_result(S1, editor_opened=pass)` records evidence.
10. Warcraft loads the same `H2`; session records `game_loaded=pass`.
11. `wc3_promote_build(B1, expected_hash=H2, configured destination)` copies it and verifies the destination hash.

At no point is the source map overwritten. Every identifier and hash links the audit trail.

## Internal request flow

```text
MCP tool input
  -> Zod schema validation
  -> configured project resolution
  -> path and operation policy
  -> application service
  -> worker request schema validation
  -> .NET engine operation
  -> worker response schema validation
  -> application error/result mapping
  -> concise MCP text + structuredContent + artifact references
```

Tool handlers should not perform path arithmetic, spawn processes, parse map members, or write manifests directly. Those duties belong to dedicated services.

## Persistence model

- Configuration: checked-in example plus ignored machine-local file.
- Transactions: filesystem directories with atomic manifest/revision files.
- Builds: immutable artifacts addressed by build ID and SHA-256.
- Test sessions: append/update records tied to a build hash.
- Logs: structured per-correlation files.
- No database is required initially. If filesystem persistence becomes inadequate, adopt a database through an ADR without changing public tool contracts.
