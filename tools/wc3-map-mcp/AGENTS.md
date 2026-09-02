# WC3 Map MCP Agent Instructions

Before working in this directory, read these live documents completely:

1. `docs/agent-guide.md`
2. `docs/reference/tool-contracts.md`
3. `docs/reference/architecture.md`
4. `docs/reference/map-model-and-contracts.md`
5. `docs/reference/safety-recovery-and-audit.md`
6. `docs/reference/testing-and-definition-of-done.md`
7. `docs/reference/packaging-and-codex-connection.md`
8. `docs/reference/wc3-map-formats.md`

Before calling or changing an MCP tool, also read
`docs/reference/tool-contracts.md`. It catalogs the complete implemented
surface, including the four global jassdoc tools, exact schemas, read/write
policy, transaction preconditions, artifact/hash contracts, and runtime
evidence gates. Keep that catalog synchronized with the schemas and tool
registrations when the MCP surface changes.

Documentation is part of the definition of done. If implementation, schemas,
tool registration, configuration, outputs, errors, policy, or evidence behavior
changes, update `docs/reference/tool-contracts.md` and every affected README,
versioned contract schema, decision/compatibility note, and Codex allow-list in
the same commit. Finish with a registration/schema/documentation consistency
check; do not knowingly leave the agent contract stale.

The source map at `../../map/HeroTeamWars_M0_2Arena.w3m` is immutable. Recompute its SHA-256 before and after work. All writes use MCP-owned staging/build paths. Do not install or upgrade machine-level prerequisites without explicit authorization. Do not claim World Editor/game/runtime success without observed evidence tied to the exact build hash.

For v8 custom-hero rebuild pitfalls, War3Log vs static parse, and object-data encoding lessons, read `docs/troubleshooting/v8-custom-hero-runtime-lessons.md`. Do not duplicate jassdoc native/API facts there; look those up in the pinned jassdoc index.
