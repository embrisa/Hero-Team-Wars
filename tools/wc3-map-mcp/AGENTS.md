# WC3 Map MCP Agent Instructions

Read the repository `AGENTS.md`, `docs/agent-guide.md`, and the complete
`docs/reference/tool-contracts.md` before calling or changing an MCP tool.
The catalog describes the complete implemented
surface, including the four global jassdoc tools, exact schemas, read/write
policy, transaction preconditions, artifact/hash contracts, and runtime
evidence gates. Keep that catalog synchronized with the schemas and tool
registrations when the MCP surface changes.

Read additional references for the code you touch: architecture and map-model
contracts for service/model changes; safety/recovery for persistence and paths;
testing for checks; packaging for setup; map formats for serializers. Use the
documentation index to find them. Historical work items do not override the
current user request or describe the current playable version.

Documentation is part of the definition of done. If implementation, schemas,
tool registration, configuration, outputs, errors, policy, or evidence behavior
changes, update `docs/reference/tool-contracts.md` and every affected README,
versioned contract schema, decision/compatibility note, and Codex allow-list in
the same commit. Finish with a registration/schema/documentation consistency
check; do not knowingly leave the agent contract stale.

The source map at `../../map/HeroTeamWars_M0_2Arena.w3m` is immutable. Recompute its SHA-256 before and after work. All writes use MCP-owned staging/build paths. Do not install or upgrade machine-level prerequisites without explicit authorization. Do not claim World Editor/game/runtime success without observed evidence tied to the exact build hash.

For v8 custom-hero rebuild pitfalls, War3Log vs static parse, and object-data encoding lessons, read `docs/troubleshooting/v8-custom-hero-runtime-lessons.md`. Do not duplicate jassdoc native/API facts there; look those up in the pinned jassdoc index.

## Object field discovery

Before editing an unfamiliar object field, use `wc3_object_field_search`, then
`wc3_object_field_lookup` for its exact case-sensitive ID/readable name and
category. Never guess rawcodes, types, enum values, native levels or pointers.
Prefer named `field` modifications; retain raw canonical records for expected
preconditions. Unknown fields must be preserved unchanged. Metadata and
readable annotations do not prove runtime behavior.
