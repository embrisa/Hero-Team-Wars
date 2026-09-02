# Implementation Conventions

These conventions make work from separate agents fit together. Deviations require an architecture decision in `tools/wc3-map-mcp/docs/decisions/`.

## Expected implementation tree

```text
tools/wc3-map-mcp/
  AGENTS.md
  README.md
  package-lock.json                     only if root orchestration uses npm
  mcp-server/
    package.json
    package-lock.json
    tsconfig.json
    src/
      index.ts                          process entry; starts STDIO only
      server.ts                         createServer factory
      config/
        schema.ts
        load-config.ts
        resolve-project.ts
      tools/
        register-tools.ts
        project-status.ts
        inspect-map.ts
        transactions.ts
        builds.ts
        launches.ts
      services/
        project-service.ts
        transaction-service.ts
        build-service.ts
        launch-service.ts
      schemas/
        common.ts
        tools.ts
        worker.ts
      transport/
        worker-client.ts
        ndjson.ts
      errors/
        app-error.ts
        map-error.ts
    test/
      unit/
      integration/
  map-engine/
    Wc3MapEngine.sln
    Directory.Build.props
    src/
      Wc3MapEngine.Cli/
      Wc3MapEngine.Contracts/
      Wc3MapEngine.Core/
    tests/
      Wc3MapEngine.Tests/
  contracts/
    schemas/
      engine-request.schema.json
      engine-response.schema.json
      canonical-map.schema.json
      transaction-manifest.schema.json
      validation-report.schema.json
  config/
    wc3-map-mcp.example.json
    wc3-map-mcp.local.json              ignored; machine paths
  docs/
    decisions/
    compatibility/
    troubleshooting/
  scripts/
    bootstrap.ps1
    build.ps1
    test.ps1
    inspect-baseline.ps1
  tests/
    fixtures/
      maps/
      expected/
    integration/
  artifacts/                             generated, ignored except README
  logs/                                  generated, ignored except README
  snapshots/                             generated, ignored except README
```

Create a file only when its phase needs it. Remove placeholder `.gitkeep` files when real files occupy the directory.

## Source ownership

- `mcp-server` owns MCP protocol, validation, permissions, orchestration, and user-facing results.
- `map-engine` owns WC3 formats, canonical model, typed operations, validation, and archive creation.
- `contracts/schemas` owns cross-process wire schemas.
- Neither component imports implementation code from the other.
- TypeScript and C# may generate types from shared schemas, but generated files must say how to regenerate them.

## Configuration shape

Use a checked-in example and ignored local file. Minimum conceptual structure:

```json
{
  "schema_version": "1.0",
  "engine": {
    "executable": "map-engine/publish/Wc3MapEngine.Cli.exe",
    "request_timeout_ms": 120000
  },
  "projects": {
    "hero-team-wars": {
      "root": "C:/Users/hp/Documents/Warcraft III/Hero Team Wars",
      "source_maps": ["map/HeroTeamWars_M0_2Arena.w3m"],
      "staging_root": "tools/wc3-map-mcp/snapshots/transactions",
      "artifact_root": "tools/wc3-map-mcp/artifacts",
      "build_root": "builds/mcp",
      "log_root": "tools/wc3-map-mcp/logs",
      "world_editor": "C:/Warcraft III/_retail_/x86_64/World Editor.exe",
      "warcraft": "C:/Warcraft III/_retail_/x86_64/Warcraft III.exe",
      "test_map_root": "C:/Users/hp/Documents/Warcraft III/Maps/Test"
    }
  }
}
```

All paths are canonicalized at load time. Store the resolved path and verify it is beneath the correct root using path-segment-aware comparison, not string prefix alone. On Windows, comparisons should be case-insensitive after resolution.

## Naming

- MCP tools: `wc3_` prefix and snake case.
- TypeScript files: kebab case; types/classes: PascalCase; variables/functions: camelCase.
- C# projects/namespaces: `Wc3MapEngine.*`.
- JSON properties: snake case across the worker boundary and artifacts.
- IDs: UUID strings.
- Dates in artifacts: UTC ISO 8601.
- Content hashes: uppercase SHA-256 hex or one documented canonical casing everywhere.
- Error codes: uppercase snake case.
- Hero Team Wars debug output: `[HTW]` prefix plus wave/chunk ID.

## Response envelope

Do not invent different envelopes per tool. Application/worker structured results should follow:

```json
{
  "ok": true,
  "correlation_id": "uuid",
  "data": {},
  "warnings": [],
  "artifacts": []
}
```

Failure:

```json
{
  "ok": false,
  "correlation_id": "uuid",
  "error": {
    "code": "SOURCE_CHANGED",
    "message": "The source map hash no longer matches this transaction.",
    "retryable": false,
    "details": {}
  }
}
```

MCP `content` should summarize this without dumping enormous JSON. Put large results in an artifact and return its project-relative path, size, hash, and a compact summary.

## Error boundary

- Invalid tool input: SDK/Zod validation error.
- Expected domain failure: stable application error returned with `isError: true`.
- Engine returned `ok:false`: map to a stable application error and retain engine details.
- Timeout/crash/malformed NDJSON: `ENGINE_UNAVAILABLE` or `ENGINE_PROTOCOL_ERROR` plus log path.
- Unexpected exception: correlation ID, generic user-safe message, detailed stderr/file log.

Never return a raw stack trace as the only error message.

## Logging

Each operation receives one correlation ID passed from MCP to services, engine request, manifest, validation/build report, and launch/test record.

Structured log fields:

- timestamp;
- severity;
- correlation ID;
- project ID;
- transaction/build/test ID when applicable;
- component;
- event name;
- safe fields.

Do not log full environment blocks, secrets, or the contents of imported assets. Redact paths only if they contain credentials; local project paths are useful evidence.

## Testing conventions

- Tests must never use the live source path as an output.
- Copy fixtures into a unique temporary directory per test.
- Assert source hash before and after mutating test suites.
- Golden/canonical JSON uses stable ordering and LF endings.
- Integration tests should run without World Editor except the explicitly marked application smoke suite.
- Tests requiring installed Warcraft are opt-in and state why they were skipped.

## Documentation and decisions

For a dependency or architecture change, create `docs/decisions/NNNN-short-title.md` with context, decision, alternatives, compatibility evidence, consequences, and rollback.

When completing a phase, update:

- the phase file's status/evidence section;
- implementation README with actual commands;
- compatibility report or tool contract if behavior changed;
- no unrelated Hero Team Wars design rules.

## Agent handoff quality bar

Another agent must be able to:

1. identify exactly what exists;
2. reproduce the build/test command;
3. locate the outputs;
4. understand any unsupported component;
5. know which phase gate is satisfied;
6. verify the original map stayed unchanged.

If any of these require reading chat history, the handoff is incomplete.
