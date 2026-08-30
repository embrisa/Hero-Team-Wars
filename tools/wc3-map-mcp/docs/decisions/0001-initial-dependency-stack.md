# ADR 0001: Initial dependency stack and worker isolation

Status: accepted for the first local release

## Context

The map service needs a stable MCP STDIO process and a separate Warcraft III archive engine. The map engine must preserve members it cannot yet interpret, while the MCP process must remain free of binary map-format code.

## Decision

- Use Node `v24.19.0` with npm `11.17.0` for the MCP process. The current host already provides these versions, so no machine-level prerequisite installation was authorized or needed.
- Use .NET SDK `10.0.400` targeting `net10.0` for the engine.
- Pin `@modelcontextprotocol/server` `2.0.0`, `zod` `4.5.4`, TypeScript `7.0.2`, Vitest `4.1.11`, and the inspector dev package `2.4.0`.
- Pin `War3Net.IO.Mpq` and `War3Net.Build.Core` to `6.0.3`.
- Run the engine as an isolated one-request process behind a newline-delimited JSON boundary. The MCP worker starts it with `--stdio`, sends one request, verifies one response, captures stderr, and enforces a timeout.
- Use uppercase SHA-256 hex for all artifact identity values.

## Alternatives considered

- A persistent engine process could reduce startup overhead, but one-shot isolation makes crashes, malformed responses, and state leakage easier to contain while format support is being proven.
- A pure TypeScript MPQ implementation would duplicate format logic and weaken the World Editor compatibility evidence.
- Installing another runtime was rejected because the required versions are already present and the charter does not authorize machine changes.

## Compatibility evidence

The engine successfully opened and re-inspected the local `HeroTeamWars_M0_2Arena.w3m`, parsed `war3map.w3i`, `war3map.w3r`, and `war3map.wts`, and classified other members for exact preservation. The source hash was checked before and after probing.

The engine is built on [War3Net](https://github.com/Drake53/War3Net). The server uses the official TypeScript MCP server APIs, including the STDIO transport described in the [MCP TypeScript SDK documentation](https://github.com/modelcontextprotocol/typescript-sdk/tree/main/docs).

## License and source records

The pinned package metadata reports MIT for `@modelcontextprotocol/server`, `@modelcontextprotocol/inspector`, `zod`, `vitest`, `tsx`, `@types/node`, and War3Net. TypeScript reports Apache-2.0. The War3Net 6.0.3 NuGet metadata records repository commit `11ff1ed081e02a91fc960ba653b0ee91e9b498b0`; the npm dependencies are reproducibly pinned by `mcp-server/package-lock.json`.

## Consequences

The worker boundary is deliberately small and versioned. It adds process startup cost, but gives the MCP layer a clear failure boundary and ensures diagnostic text cannot contaminate protocol stdout. Unsupported object, trigger, and opaque members remain read-only until a round-trip proof exists; the existing JASS entry point is now covered by ADR 0002's MCP-owned source path.

## Rollback

Change the engine executable or pinned packages only through a new ADR, rerun the full test suite, regenerate the compatibility report, and compare the original source hash before accepting the change.
