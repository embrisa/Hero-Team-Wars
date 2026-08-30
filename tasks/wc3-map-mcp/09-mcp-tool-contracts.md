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

## Error model

Stable codes should include `INVALID_ARGUMENT`, `PATH_OUTSIDE_ROOT`, `SOURCE_CHANGED`, `UNSUPPORTED_COMPONENT`, `PARSE_FAILED`, `VALIDATION_FAILED`, `BUILD_FAILED`, `LAUNCH_FAILED`, `TRANSACTION_STATE`, and `INTERNAL_ERROR`.

Errors must include a user-actionable message, affected component/path, retryability, and artifact/log reference when available. Stack traces stay in diagnostic logs.
