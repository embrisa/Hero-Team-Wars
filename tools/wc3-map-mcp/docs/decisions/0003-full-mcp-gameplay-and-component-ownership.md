# ADR 0003: Full MCP gameplay and map-component ownership

Status: MCP-native/static implementation accepted; GUI compatibility and exact editor/game acceptance remain gated by the current capability matrix and exact editor/game evidence.

## Context

The current MCP safely replaces the existing `war3map.j` entry point but keeps
GUI trigger data, object data, placements, and most map structure opaque. That
is sufficient for a JASS-owned two-arena prototype, but it prevents MCP-driven
changes to the full Hero Team Wars surface: triggers, scripts, regions,
objects, logic, teams, and forces.

## Decision

Extend the MCP as a typed map compiler with two explicit ownership modes:

1. **MCP-native gameplay mode** owns project source modules, trigger/variable
   manifests, generated JASS, regions, object registries, placements, teams,
   and runtime logic. The generated `war3map.j` is authoritative for gameplay.
2. **Editor-compatible GUI mode** additionally owns the exact supported
   `war3map.wtg`, `war3map.wct`, and `war3map.wts` representations and emits a
   compatible `war3map.j`. It is enabled per map/editor version only after
   fixture round-trip and application evidence.

All binary changes use typed operations and independent serializers. Unknown
members and fields remain preserved but immutable. Capability is reported per
member, category, field, and project profile.

## Consequences

- HTW-01 through HTW-05 can progress through MCP-native JASS before all binary
  serializers exist.
- HTW-06 requires at least a full `war3map.w3i` writer plus six-team regions,
  placements, and required content support.
- GUI-trigger editing is a separate compatibility feature, not an implicit
  consequence of JASS source replacement.
- Every feature must pass the same inspect -> stage -> diff -> validate -> build
  -> exact-load -> observed-test -> promote workflow.

## Required safeguards

- Never expose generic archive patching or untyped object fields.
- Require map/source/member hashes and expected prior values on mutation.
- Do not infer teams from player color.
- Do not rewrite protected region names without explicit reference migration.
- Never treat parser, build, or process-start evidence as gameplay success.

## Implementation references

- `../reference/tool-contracts.md`
- `../reference/architecture.md`
- `../reference/map-model-and-contracts.md`
- `../reference/safety-recovery-and-audit.md`
- `../reference/testing-and-definition-of-done.md`
