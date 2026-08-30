# Packaging and Codex Connection

## Local development packaging

- Build the TypeScript server into `mcp-server/dist`.
- Publish the .NET map engine into `map-engine/publish` as a self-contained or framework-dependent Windows executable, chosen during Phase 0.
- Keep runtime configuration outside compiled code.
- Provide one explicit startup command that starts the MCP server over STDIO.
- Add a health/self-test command that does not start MCP protocol mode.

## Configuration

The local configuration must declare:

- project ID and project root;
- allowed source-map paths;
- staging, snapshot, build, log, and test-output roots;
- map-engine executable path;
- optional World Editor and Warcraft III executable paths;
- size/time limits;
- enabled tools and write policy.

Do not store secrets in the project configuration. This local server should not need an OpenAI API key because Codex is the MCP client, not a model call made by the server.

## Codex connection

Configure the server as a local STDIO MCP server after it builds successfully. Use a project-scoped Codex configuration if available and trusted, so the WC3 tools are visible only in this project. Initially enable read-only tools. Enable transaction/build tools only after Phase 1 tests pass; keep promotion and deletion approval-gated.

## Distribution decision later

The first release is private and local. A reusable plugin or public package should be considered only after dependency licenses, Blizzard asset boundaries, installation behavior, security review, and multi-project configuration are documented.

## Acceptance criteria

- Fresh setup instructions work on the target Windows machine.
- Server startup is deterministic and emits no protocol-breaking output.
- Codex lists the expected tool set.
- Disabled tools are not callable.
- A project-scoped configuration cannot access maps outside its declared roots.
