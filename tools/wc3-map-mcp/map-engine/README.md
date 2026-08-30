# Map Engine

Planned .NET solution using War3Net or another verified Warcraft III format library selected during Phase 0.

Expected projects:

```text
src/
  Wc3MapEngine.Cli/        NDJSON process boundary
  Wc3MapEngine.Core/       inspection, operations, validation, build
  Wc3MapEngine.Contracts/  request, response, and canonical model types
tests/
  Wc3MapEngine.Tests/
```

This component must be usable and testable without the MCP server.
