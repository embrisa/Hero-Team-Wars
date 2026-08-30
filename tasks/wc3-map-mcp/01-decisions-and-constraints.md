# Decisions and Constraints

## Architecture decisions

1. Use a local STDIO MCP server. It avoids network exposure and matches a single-user Windows workflow.
2. Use TypeScript for the MCP-facing process. It owns tool schemas, input validation, process orchestration, and agent-friendly responses.
3. Use a .NET map-engine process built around War3Net for Warcraft III archive and file-format operations.
4. Communicate between the two processes using one JSON object per line over standard input/output. Diagnostic logs go to standard error or files, never into the protocol stream.
5. Treat extracted map data and authored scripts as source; treat `.w3m`/`.w3x` files as build artifacts.
6. Make all writes transactional: inspect, plan, stage, diff, validate, build, test, promote.
7. Keep World Editor ownership explicit. Terrain polishing, visual placement, and unsupported editor data remain editor-owned until round-trip support is proven.

The planned extension of this boundary is recorded in
`tools/wc3-map-mcp/docs/decisions/0003-full-mcp-gameplay-and-component-ownership.md`.
It does not make any currently opaque component writable: each trigger, script,
region, object, placement, player, team, and force capability must first pass
its own typed serializer and round-trip gate.

## Why two processes

The MCP protocol and agent contracts evolve independently from Warcraft III binary parsing. Separating them prevents map-format code from leaking into tool handlers and allows the map engine to be tested without starting an MCP client.

## Project constraints

- Operating system: Windows.
- First input format: `.w3m`; later support `.w3x`.
- The current Hero Team Wars map is a recoverable source artifact and must not be overwritten.
- Existing region names must be preserved exactly, including underscores.
- Team identity must come from explicit team/force mapping, never player color.
- Unknown or unsupported fields must be reported as such rather than guessed.
- This workspace currently has no Git history, so MCP-owned snapshots and manifests are mandatory from day one.

## Dependency policy

- Pin Node, npm package, .NET SDK, NuGet, and map-library versions.
- Record licenses for every bundled dependency.
- Do not download or update dependencies during an MCP tool call.
- Resolve and install dependencies during explicit setup only.
- Add a compatibility fixture before upgrading any map-format dependency.

## Locked interface decisions

- Public tools use project IDs plus project-relative map/build labels. They do not accept arbitrary absolute paths.
- Public map changes are typed semantic operations. There is no generic shell, `write_file`, `replace_member`, or “run arbitrary compiler command” tool.
- Large results are written as versioned JSON/Markdown artifacts and summarized through MCP.
- Source hash and transaction revision are optimistic-concurrency preconditions on every mutation/build.
- Internal TypeScript-to-.NET communication is NDJSON with one request/response per line and independent protocol versioning.
- Runtime configuration is validated at server startup and is not mutable through MCP tools in the first release.

## Decisions deliberately deferred to Phase 0/3

- Exact Node, MCP SDK, .NET SDK, NuGet, and War3Net versions.
- Whether the initial map engine is one-shot or persistent.
- Exact MPQ writer and compression/listfile behavior.
- Editor-owned versus build-owned JASS/Lua strategy.
- Which individual `war3map.*` members can be written.
- Whether `.w3x` support ships with the first `.w3m` release.

Deferred means “gather evidence and record an ADR,” not “let each agent choose differently.”

## Rejected approaches

- **World Editor DLL injection:** patch-sensitive, unsafe, unnecessary for the first goal.
- **Pure screen automation:** too brittle for repeatable trigger/object creation and weak evidence.
- **Direct edits to the accepted `.w3m`:** no safe rollback and binary corruption risk.
- **One giant MCP tool such as `edit_map`:** ambiguous authorization, poor schemas, and unauditable changes.
- **An agent-generated absolute output path:** path-escape and overwrite risk.
- **Treating map script import as execution:** imported source is inert unless connected through the selected script/build path.
