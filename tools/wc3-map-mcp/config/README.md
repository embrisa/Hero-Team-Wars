# Configuration

The checked-in `wc3-map-mcp.example.json` targets this Hero Team Wars workspace and the observed local Warcraft III installation. Copy it to `wc3-map-mcp.local.json` only when using a machine-specific configuration; the local file is ignored.

Configuration defines the source maps, read roots, MCP-owned staging/artifact/build roots, and optional World Editor/game launch paths. Relative paths are resolved from the project root and checked with path-segment-aware containment. The server never accepts arbitrary shell commands or an unresolved environment variable as a destructive target.
