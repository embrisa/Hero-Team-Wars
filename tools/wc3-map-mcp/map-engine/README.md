# Map Engine

Buildable .NET 10 solution using War3Net 6.0.3 for read-only MPQ access, WC3 map-info/region parsing, typed canonical operations, validation, and conservative archive rebuilding.

Expected projects:

```text
src/
  Wc3MapEngine.Cli/        NDJSON process boundary
  Wc3MapEngine.Core/       inspection, operations, validation, build
  Wc3MapEngine.Contracts/  request, response, and canonical model types
tests/
  Wc3MapEngine.Tests/
```

The CLI is `publish/Wc3MapEngine.Cli.exe`. It supports `--self-test` and `--stdio`; the latter accepts one JSON request and returns one JSON response. It is usable and testable without the MCP server.

The current writer supports only proven map metadata (`title`, `suggested_players`) and region bounds. Unknown members are preserved through rebuilds; script, trigger, object, placement, terrain, and generic archive mutation remain blocked until round-trip evidence exists.
