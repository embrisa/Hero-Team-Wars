# Troubleshooting

## STDIO shows no response

Run the published engine self-check and the MCP server integration tests from `tools/wc3-map-mcp`. Protocol responses are written to stdout; diagnostics belong on stderr. A timeout or malformed response is reported as an engine transport error.

## A transaction is locked

The lock is intentionally fail-closed. Check `snapshots/transactions/project.lock` and confirm no MCP mutation is still running. Do not delete a lock blindly; inspect its operation, PID, correlation ID, and timestamp first.

## A build is not promotable

Builds begin with `runtime_status: untested`. Opening a file or starting a process does not upgrade evidence. Record the exact editor/game observation against the build hash before promotion.

## Source drift is reported

The source map is immutable to the toolchain. Reinspect the current source, compare its SHA-256 with the transaction manifest, and begin a new transaction if the user intentionally changed it in World Editor.
