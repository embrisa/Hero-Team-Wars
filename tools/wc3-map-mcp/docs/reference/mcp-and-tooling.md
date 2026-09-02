# Reference - MCP and Tooling

This file explains the tooling choices to an agent that has never implemented an MCP server or a WC3 build tool.

## What MCP does here

Model Context Protocol lets a host such as Codex start a local process and call named tools with validated structured arguments. Our MCP server is an adapter; it does not contain an AI model and does not need an OpenAI API key. Codex supplies the model and calls the tools.

The transport is local STDIO:

- the host starts the server command;
- MCP requests arrive on process stdin;
- MCP responses leave on process stdout;
- process lifetime is owned by the host;
- anything printed casually to stdout corrupts the JSON-RPC protocol.

Therefore use `console.error` or a file logger for diagnostics. Do not use `console.log` anywhere in the server process, including dependency banners and debugging left in handlers.

## Current TypeScript SDK shape

Use the current v2 package split, not older examples importing everything from `@modelcontextprotocol/sdk`.

Phase 0 should resolve and pin mutually compatible stable versions of:

- Node.js 20 or later;
- `@modelcontextprotocol/server`;
- `zod` 4.2 or later, imported as `zod/v4`;
- `typescript`;
- `tsx` for local development;
- a test runner such as `vitest`;
- `@modelcontextprotocol/inspector` as a development tool.

The current machine was most recently observed with Node 24.19.0 and npm 11.17.0, which satisfy the Node 20+ baseline. Verify package compatibility and pin exact versions before installing project dependencies.

The package must use ESM by setting `"type": "module"` in `package.json`. TypeScript output should target a Node-20-compatible ESM configuration.

## Minimal server shape

The implementation should use a server factory because current STDIO serving can negotiate protocol eras per connection:

```ts
import { McpServer } from "@modelcontextprotocol/server";
import { serveStdio } from "@modelcontextprotocol/server/stdio";
import * as z from "zod/v4";

function createServer(): McpServer {
  const server = new McpServer(
    { name: "wc3-map-mcp", version: "0.1.0" },
    {
      instructions:
        "Inspect before changing. All changes require a transaction. Never overwrite a source map. Validate before build; build before launch; record observed evidence separately."
    }
  );

  server.registerTool(
    "wc3_project_status",
    {
      description: "Inspect configured WC3 projects and dependency readiness without modifying files.",
      inputSchema: z.object({ project_id: z.string().min(1) }),
      annotations: {
        readOnlyHint: true,
        destructiveHint: false,
        idempotentHint: true
      }
    },
    async ({ project_id }) => {
      // Call a service, not the worker directly from the tool body.
      return {
        content: [{ type: "text", text: `Project ${project_id} inspected.` }],
        structuredContent: { ok: true, project_id }
      };
    }
  );

  return server;
}

void serveStdio(createServer);
console.error("wc3-map-mcp listening on stdio");
```

Tool handlers must be thin. Expected call chain:

```text
registered tool -> input schema -> project resolver -> policy check
                -> application service -> worker client -> map engine
                -> response validator -> MCP response mapper
```

## MCP server instructions

Codex reads the server-level `instructions` value. Keep the first 512 characters self-contained and state the most important workflow there. Do not rely on instructions as authorization; all path and transaction checks still run in code.

Recommended full instruction content:

```text
Inspect before changing. The original map is immutable. Mutations require a transaction tied to the inspected source hash. Call transaction_diff and validate_transaction before build_map. A build is untested until a separate editor/game result is recorded. Never infer unknown map values or team identity from player color. Destructive tools may remove only an MCP-owned transaction directory.
```

## Tool schemas and output

Use `server.registerTool(name, config, handler)`. Each input is a Zod object with:

- descriptions on non-obvious fields;
- `.strict()` when forward compatibility does not require extra keys;
- bounds for strings, arrays, counts, and timeouts;
- enums instead of loosely documented strings;
- project-relative paths rather than arbitrary absolute paths.

Return both:

- concise `content` text the agent can read;
- `structuredContent` matching the documented output contract.

For an expected tool failure, return `isError: true` and a structured error. Throw only for unexpected internal failures; the SDK will turn throws into errors, but explicit errors are easier for agents to correct.

Tool annotations inform clients but do not enforce security. Use the appropriate hints:

- inspection/diff/validation: read-only and idempotent;
- begin/apply/build/promote: not read-only;
- discard: destructive but restricted to MCP-owned transaction state;
- launch: externally observable, not destructive by default.

## Testing the MCP layer

Use the MCP Inspector to start the local command and call tools before connecting Codex. The Inspector command should point to the actual built/development entry point. A successful test must demonstrate:

1. initialization succeeds;
2. tools list contains the expected names;
3. invalid input is rejected by schema validation;
4. a read-only tool returns structured content;
5. stderr diagnostics do not appear on protocol stdout;
6. the process exits cleanly when the client disconnects.

Also write automated STDIO integration tests. Do not rely only on clicking through Inspector.

## Codex configuration model

Codex supports a local STDIO server started by `command` plus `args`, optional `cwd`, environment values, and per-tool policy. The server can be project-scoped in `.codex/config.toml` for a trusted project.

The final config will look conceptually like:

```toml
[mcp_servers.wc3_map]
command = "C:\\Program Files\\nodejs\\node.exe"
args = ["C:\\Users\\hp\\Documents\\Warcraft III\\Hero Team Wars\\tools\\wc3-map-mcp\\mcp-server\\dist\\index.js"]
cwd = "C:\\Users\\hp\\Documents\\Warcraft III\\Hero Team Wars\\tools\\wc3-map-mcp\\mcp-server"
startup_timeout_sec = 20
tool_timeout_sec = 120
enabled = true
required = false
default_tools_approval_mode = "writes"
```

Do not create this file until the server has passed Inspector tests. Use the exact schema supported by the installed Codex version and re-check official OpenAI documentation during the packaging phase.

## Internal TypeScript-to-.NET protocol

The map engine is not an MCP server. It is a CLI that accepts one NDJSON request per line and returns one response per line. Standard error is reserved for diagnostics.

Request example:

```json
{"protocol_version":"1.0","request_id":"uuid","operation":"inspect_map","payload":{"map_path":"C:\\...\\HeroTeamWars_M0_2Arena.w3m"}}
```

Success example:

```json
{"protocol_version":"1.0","request_id":"uuid","ok":true,"result":{"schema_version":"1.0","map":{}}}
```

Failure example:

```json
{"protocol_version":"1.0","request_id":"uuid","ok":false,"error":{"code":"PARSE_FAILED","message":"war3map.w3i could not be parsed","retryable":false,"details":{}}}
```

Requirements:

- one response for every accepted request;
- `request_id` echoed exactly;
- no pretty-printed multi-line JSON on stdout;
- timeout and cancellation handled by the TypeScript worker client;
- engine process may be one-shot initially for isolation; persistent mode can be added only after correctness tests;
- all worker responses are schema-validated before reaching an MCP handler.

## Warcraft tooling candidates already discovered

### War3Net

Primary candidate for the .NET map engine. Relevant packages include:

- `War3Net.IO.Mpq`: MPQ archive read/write;
- `War3Net.Build.Core`: parsers and serializers for `war3map.*` files;
- `War3Net.Build`: complete map building and asset packaging;
- `War3Net.CodeAnalysis.Jass`: JASS parsing/rendering when needed.

The repository advertises .NET 6+ support, but the exact NuGet versions and target framework must be proven in Phase 0. Do not assume every roadmap feature exists; its standalone CLI and runtime/emulation items may still be unfinished.

### warcraft-vscode/wc3 CLI

Useful as a reference or fallback. It already demonstrates Lua compilation, object editing, MPQ packing, and launching Reforged with `-loadfile`. Do not silently combine its writer with War3Net; select one archive writer for a transaction and document the reason.

### World Editor

World Editor remains the compatibility authority for editor-owned data and visual work. The MCP should launch it with a copied test build; it should not automate GUI edits as the normal write path.

## Sources an implementation agent should re-check

- OpenAI/Codex MCP configuration: `https://learn.chatgpt.com/docs/extend/mcp`
- MCP TypeScript SDK server guide: `https://github.com/modelcontextprotocol/typescript-sdk/blob/main/docs/server.md`
- MCP TypeScript first server: `https://github.com/modelcontextprotocol/typescript-sdk/blob/main/docs/get-started/first-server.md`
- War3Net: `https://github.com/Drake53/War3Net`
- warcraft-vscode: `https://github.com/warcraft-iii/warcraft-vscode`

These are current-tooling references and may change. Record the commit/tag/version actually used in the Phase 0 compatibility report.
