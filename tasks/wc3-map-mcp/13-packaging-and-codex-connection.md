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

## Expected scripts

### `scripts/bootstrap.ps1`

Verifies prerequisite versions, restores pinned npm/NuGet packages, creates ignored local config from example only when absent, and prints next steps. It must not install machine-level Node/.NET silently.

### `scripts/build.ps1`

Builds .NET engine first, then TypeScript server, validates expected outputs, and prints exact engine/server entry paths.

### `scripts/test.ps1`

Runs .NET tests, TypeScript tests, schema validation, and STDIO integration tests. Application tests are opt-in flags because they open visible programs.

### `scripts/inspect-baseline.ps1`

Runs the read-only engine/MCP flow against the configured baseline and writes reports. It must recompute source hash before/after.

All scripts resolve their own root from script location and use native PowerShell process invocation/argument arrays.

## Development commands

Exact commands depend on Phase 0 versions, but the resulting workflow should be equivalent to:

```powershell
Set-Location 'C:\Users\hp\Documents\Warcraft III\Hero Team Wars\tools\wc3-map-mcp'
& .\scripts\bootstrap.ps1
& .\scripts\build.ps1
& .\scripts\test.ps1
```

For MCP Inspector, use the installed/pinned Inspector and actual server entry. Confirm the development command does not print to stdout outside MCP.

## Codex rollout sequence

1. Inspector with project-status only.
2. Inspector with all Phase 1 read-only tools.
3. Codex project-scoped connection with read-only allowlist.
4. Enable transaction tools after Phase 2 tests.
5. Enable build tools after Phase 3 tests.
6. Enable launch tools after Phase 4 tests.
7. Keep promotion and discard under write/destructive approval policy.

## Example project-scoped config

Use current official OpenAI documentation when implementing. Conceptually:

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
enabled_tools = ["wc3_project_status", "wc3_inspect_map", "wc3_list_archive_files", "wc3_get_component", "wc3_validate_map", "wc3_compare_maps"]
```

Do not write this config until the built entry point exists. After adding it, restart the Codex host and verify `/mcp` or the app's MCP server view reports the server connected.

## Packaging contents

The local package includes compiled server, published engine, schemas, example config, scripts, licenses/notices, compatibility report, and documentation. It excludes source maps, user imports/assets, snapshots, logs, builds, local config, and secrets.
