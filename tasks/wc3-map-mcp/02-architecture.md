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
