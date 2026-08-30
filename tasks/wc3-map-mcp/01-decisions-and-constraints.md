# Decisions and Constraints

## Architecture decisions

1. Use a local STDIO MCP server. It avoids network exposure and matches a single-user Windows workflow.
2. Use TypeScript for the MCP-facing process. It owns tool schemas, input validation, process orchestration, and agent-friendly responses.
3. Use a .NET map-engine process built around War3Net for Warcraft III archive and file-format operations.
4. Communicate between the two processes using one JSON object per line over standard input/output. Diagnostic logs go to standard error or files, never into the protocol stream.
5. Treat extracted map data and authored scripts as source; treat `.w3m`/`.w3x` files as build artifacts.
6. Make all writes transactional: inspect, plan, stage, diff, validate, build, test, promote.
7. Keep World Editor ownership explicit. Terrain polishing, visual placement, and unsupported editor data remain editor-owned until round-trip support is proven.

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
