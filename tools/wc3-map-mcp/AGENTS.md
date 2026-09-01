# WC3 Map MCP Agent Instructions

Before working in this directory, read these files completely:

1. `../../tasks/wc3-map-mcp/AGENT-START-HERE.md`
2. `../../tasks/wc3-map-mcp/REFERENCE-MCP-AND-TOOLING.md`
3. `../../tasks/wc3-map-mcp/REFERENCE-WC3-MAP-FORMATS.md`
4. `../../tasks/wc3-map-mcp/IMPLEMENTATION-CONVENTIONS.md`
5. the assigned phase file under `../../tasks/wc3-map-mcp/`

The source map at `../../map/HeroTeamWars_M0_2Arena.w3m` is immutable. Recompute its SHA-256 before and after work. All writes use MCP-owned staging/build paths. Do not install or upgrade machine-level prerequisites without explicit authorization. Do not claim World Editor/game/runtime success without observed evidence tied to the exact build hash.

For v8 custom-hero rebuild pitfalls, War3Log vs static parse, and object-data encoding lessons, read `docs/troubleshooting/v8-custom-hero-runtime-lessons.md`. Do not duplicate jassdoc native/API facts there; look those up in the pinned jassdoc index.
