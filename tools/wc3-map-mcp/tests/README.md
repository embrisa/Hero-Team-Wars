# Cross-Component Tests

The executable test suites live in `mcp-server/test` and `map-engine/tests`. The MCP integration suite launches the built STDIO server, calls read-only tools and a hash-checked transaction/build workflow, and asserts the source hash is unchanged. The engine suite tests known hashing, stable local-map inventory, canonical operations, and truncated-archive rejection.

Run the full suite from any current directory with `scripts/test.ps1` after `scripts/build.ps1`.

The compatibility probe creates deliberately invalid input only in a unique system temporary directory. It does not place the user's source map in a destructive fixture path.

Do not place the user's only source map in a destructive test fixture path.
