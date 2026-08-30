# ADR 0002: Map script ownership

Status: accepted limitation for the first release; enforced by Phase 3

## Context

The current map contains `war3map.j`, `war3map.wct`, and `war3map.wtg`. Archive inspection can identify and preserve these members, but it does not prove the World Editor trigger tree, custom-text ownership, generated entry points, or a safe source-to-map synchronization strategy.

## Decision

The first release is editor-owned for scripts. The MCP may inspect script-member metadata and preserve script-related members byte-for-byte, but it does not expose `set_script_source`, generic archive patching, or JASS/Lua injection. Script mutation is disabled until a copied-map round-trip proves the ownership and entry-point behavior.

Phase 3 enforces this decision in both layers: the validator reports opaque script limits and missing/ambiguous/disconnected entry-point evidence, while the build plan rejects staged script/trigger changes as unsupported. The engine performs strict read-only trigger-string integrity checks where possible and never compiles or injects script text.

The engine can build proven metadata and region changes while carrying the current script and trigger members through the archive. `war3map.j`, `war3map.wct`, and `war3map.wtg` are therefore preserved opaque for mutation purposes, not treated as absent.

## Alternatives considered

- Build-owned JASS/Lua injection would require a checked-in source layout, parser/static checker, deterministic entry-point connection, and evidence that World Editor saves do not overwrite or disconnect it.
- Editor-owned GUI/custom-text authoring could be supported later, but current format evidence is insufficient to synthesize GUI trigger data safely.

## Consequences

The toolchain supports safe inspection and a narrow no-op/metadata/region build, but it cannot yet implement the Hero Team Wars gameplay chunks. The limitation is explicit in capability reports and tool errors rather than guessed from opaque bytes. Phase 3's opaque-member comparison proves preservation, not semantic understanding or gameplay correctness.

## Unlock criteria

Enable script writes only after: a script-ownership test map is copied; the source language and entry point are identified; static syntax checks run; World Editor open/save/reopen results are recorded; the script hash and trigger behavior are attributable to a transaction; and the original map hash remains unchanged.
