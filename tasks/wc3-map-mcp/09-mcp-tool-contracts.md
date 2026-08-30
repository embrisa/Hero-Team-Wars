# MCP Tool Contracts

## Contract rules

- Tool names use the `wc3_` prefix.
- Inputs and outputs use JSON Schema with closed objects where practical.
- Every response contains `ok`, `correlation_id`, and either `data` or `error`.
- Map references use configured project IDs plus project-relative paths, not arbitrary absolute paths.
- Mutating tools require a transaction ID and expected source hash.
- Large inventories return summaries plus cursors or artifact paths.
- Tool descriptions state whether the operation reads, stages, builds, launches, promotes, or deletes MCP-owned data.

## Minimum tool set

| Tool | Class | Purpose |
|---|---|---|
| `wc3_project_status` | read | Report configuration, dependency, and compatibility state |
| `wc3_inspect_map` | read | Return canonical map inventory |
| `wc3_get_component` | read | Return one supported component in detail |
| `wc3_compare_maps` | read | Produce semantic and archive-level differences |
| `wc3_begin_transaction` | write-stage | Create isolated staged state |
| `wc3_apply_operations` | write-stage | Apply typed changes atomically |
| `wc3_transaction_diff` | read | Show staged before/after values |
| `wc3_validate_transaction` | read | Validate staged state without building |
| `wc3_build_map` | write-build | Produce a new test artifact |
| `wc3_launch_editor` | external | Open a selected build in World Editor |
| `wc3_launch_test_map` | external | Launch Warcraft III with a selected build |
| `wc3_record_test_result` | write-metadata | Attach observed evidence |
| `wc3_promote_build` | write-copy | Copy a validated build to an explicit output |
| `wc3_discard_transaction` | delete-owned | Delete only one MCP-owned staging transaction |

## Phase 5 feature-extension tools

These tools extend the minimum surface without bypassing the existing
transaction, hash, diff, validation, build, and evidence gates:

| Tool | Class | Purpose |
|---|---|---|
| `wc3_compose_gameplay_source` | read-artifact | Compose approved JASS modules, trigger manifests, and variable manifests into deterministic source |
| `wc3_validate_gameplay_source` | read | Check syntax, symbols, dependencies, references, and the selected HTW profile |
| `wc3_prepare_gameplay_chunk` | write-stage | Stage generated gameplay source with all input hashes and chunk metadata |
| `wc3_run_scenario_build` | write-build | Produce a uniquely named debug build for one deterministic runtime scenario |
| `wc3_record_chunk_result` | write-metadata | Link a chunk, scenario, transaction, build, and exact test-session observation |

`wc3_apply_operations` must also accept the typed feature operations defined in
packets `14` through `17`. Composite tools return the underlying IDs and may
not hide or skip a semantic diff, validation report, or exact-build hash.

### Feature-tool contract minimums

| Tool | Required inputs | Essential output |
|---|---|---|
| `wc3_compose_gameplay_source` | project, manifest path, expected module/manifest hashes, profile | generated source artifact, module hashes, symbol index, source hash |
| `wc3_validate_gameplay_source` | project, source/manifest artifact, profile | syntax/symbol/reference findings and buildable result |
| `wc3_prepare_gameplay_chunk` | project, map, expected source hash, chunk ID, manifest | transaction ID, revision, generated-source artifact, input hashes |
| `wc3_run_scenario_build` | project, transaction, exact revision, scenario ID, expected source hash | build ID, output hash, scenario manifest, untested runtime status |
| `wc3_record_chunk_result` | project, chunk/scenario IDs, transaction/build/session IDs, expected hashes, observed result | immutable chunk-evidence artifact and linked status |

The feature tools must reject absolute paths, unapproved source roots, stale
module/manifest hashes, unknown profiles, and attempts to mutate a map without
an existing transaction.

## Error model

Stable codes should include `INVALID_ARGUMENT`, `PATH_OUTSIDE_ROOT`, `SOURCE_CHANGED`, `UNSUPPORTED_COMPONENT`, `PARSE_FAILED`, `VALIDATION_FAILED`, `BUILD_FAILED`, `LAUNCH_FAILED`, `TRANSACTION_STATE`, and `INTERNAL_ERROR`.

Errors must include a user-actionable message, affected component/path, retryability, and artifact/log reference when available. Stack traces stay in diagnostic logs.

## Common schemas

Conceptual request identity fields:

```json
{
  "project_id": "hero-team-wars",
  "map": "map/HeroTeamWars_M0_2Arena.w3m"
}
```

Conceptual success `structuredContent`:

```json
{
  "ok": true,
  "correlation_id": "0cc7f7f0-7bd7-4e99-b2b1-d2f5ff0bd63f",
  "data": {},
  "warnings": [],
  "artifacts": [
    {
      "kind": "canonical_map",
      "path": "artifacts/hero-team-wars/inspect-0cc7.json",
      "sha256": "...",
      "size_bytes": 12345
    }
  ]
}
```

Conceptual failure:

```json
{
  "ok": false,
  "correlation_id": "...",
  "error": {
    "code": "SOURCE_CHANGED",
    "message": "The source map no longer matches the hash used to create this transaction.",
    "retryable": false,
    "component": "source_map",
    "details": {
      "expected_sha256": "...",
      "actual_sha256": "..."
    },
    "log_path": "logs/....jsonl"
  }
}
```

## Tool-by-tool minimum inputs and outputs

| Tool | Required inputs | Essential output |
|---|---|---|
| `wc3_project_status` | project ID | source hash, dependencies, capability summary, enabled tools |
| `wc3_inspect_map` | project, allowed map | canonical summary, map hash, full artifact |
| `wc3_get_component` | project, map, component enum | paginated typed component values and provenance |
| `wc3_compare_maps` | project, two allowed artifact refs | archive and semantic differences |
| `wc3_begin_transaction` | project, source, expected hash | transaction ID, revision 0, snapshot manifest |
| `wc3_apply_operations` | project, transaction, expected revision, operations | new revision, applied IDs, diff |
| `wc3_transaction_diff` | project, transaction, revision range | grouped semantic changes |
| `wc3_validate_transaction` | project, transaction, exact revision | buildable flag and findings report |
| `wc3_build_map` | project, transaction, exact revision/hash, profile | build ID/path/hash, status untested |
| `wc3_launch_editor` | project, build, expected build hash | test session and process-start evidence |
| `wc3_launch_test_map` | project, build, expected build hash | test session/copy hash/process evidence |
| `wc3_record_test_result` | session, expected hash, milestone/result/notes | updated evidence level |
| `wc3_promote_build` | project, build, hash, configured destination ID | destination/copy hash/audit record |
| `wc3_discard_transaction` | project, transaction, expected source hash, confirmation | tombstone and deletion result |

## Operation example

```json
{
  "operation_id": "3f57cd60-f319-416d-9a65-d16e9b1ebca1",
  "type": "update_region",
  "target": { "name": "Arena_A" },
  "expected": {
    "name": "Arena_A",
    "min_x": -1024.0,
    "min_y": -1024.0,
    "max_x": 1024.0,
    "max_y": 1024.0
  },
  "value": {
    "name": "Arena_A",
    "min_x": -1152.0,
    "min_y": -1024.0,
    "max_x": 1152.0,
    "max_y": 1024.0
  },
  "rationale": "Widen the existing arena without renaming it.",
  "design_reference": "HTW-00"
}
```

All coordinates in this example are illustrative. Agents must inspect actual bounds and supply them as preconditions.

## Pagination

Use opaque cursors tied to map hash, component, filter, and stable ordering. A cursor from another map hash returns `CURSOR_STALE`. Do not expose array offsets alone if the underlying artifact may change.

## Tool descriptions for model usability

Descriptions should say when the tool is appropriate and what must be called before/after it. Example: “Build a new test artifact from an exact validated transaction revision. Call `wc3_transaction_diff` and `wc3_validate_transaction` first. This never overwrites the source map and returns runtime status untested.”

Avoid vague descriptions such as “Edit a Warcraft map.”

## Required typed operation families

The public operation schema must grow by closed, typed families rather than a
generic archive patch:

- gameplay: script modules, triggers, variables, and source ownership/mode;
- regions: create, update, rename, delete, and explicit role assignment;
- objects: definitions, typed fields, references, placements, movement, and
  removal;
- map structure: player slots, starts, teams, forces, alliances, vision, and
  control flags.

Each family has its own capability and serializer version. An operation is
rejected when the selected project profile does not enable that family.

## Cross-feature validation order

For a transaction containing multiple families, validate in this order:

`map identity -> players/forces -> regions -> object definitions -> placements -> triggers/scripts -> generated logic`

The diff reports generated reference rewrites separately from user-requested
changes.
