# Scripts

All helpers resolve their own MCP root, so the caller's current directory does not matter.

- `bootstrap.ps1`: verifies existing Node/npm/.NET runtimes, restores locked dependencies, and optionally creates the ignored local config. It does not install machine prerequisites.
- `build.ps1`: builds/publishes the .NET engine and compiles the TypeScript server.
- `test.ps1`: builds the TypeScript server, then runs both automated test suites.
- `inspect-baseline.ps1`: probes the real source map, attempts a separately named no-op rebuild, verifies source preservation, and writes the compatibility and HTW-00 candidate reports.
- `compose-gameplay.ps1`: composes a selected MCP-native gameplay profile through the published engine.
- `validate-gameplay.ps1`: validates a gameplay manifest and profile through the published engine.
- `run-scenario.ps1`: builds an explicitly supplied canonical revision and runs its deterministic static scenario harness.
- `verify-feature-roundtrip.ps1`: builds an explicitly supplied canonical fixture and prints the reopen/archive evidence.

Every helper fails on errors and prints the artifact or report path it produced.
