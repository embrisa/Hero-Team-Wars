# Map Engine

Buildable .NET 10 solution using War3Net 6.0.3 for MPQ access, WC3 map-info/region parsing, typed canonical operations, JASS static validation, and conservative archive rebuilding.

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

The current writer supports proven map metadata (`title`, `suggested_players`), complete typed `war3map.w3r` region records (including create/update/rename/delete/reorder, WTS token preservation, stable native identities, and reference-aware MCP operations), all seven category-aware object-data members, typed `war3mapUnits.doo`/`war3map.doo` placement changes, and hash-checked replacement of the existing JASS `war3map.j` entry point when MCP-owned script policy is enabled. Unknown members are preserved through rebuilds; object support is reported per member and exact GUI trigger/editor/game compatibility remains a manual gate.
