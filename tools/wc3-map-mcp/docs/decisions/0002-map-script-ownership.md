# ADR 0002: Map script ownership

Status: accepted; MCP-owned JASS source for the first gameplay release

## Context

The current map contains a JASS entry point at `war3map.j` plus World Editor
GUI/custom-text members at `war3map.wtg` and `war3map.wct`. Those members are
related but are not interchangeable: World Editor may regenerate
`war3map.j` from the GUI/custom-text data, while importing or replacing a
script without a proven entry point can produce a map that loads without
running the intended gameplay code.

Hero Team Wars gameplay must be editable through the MCP. The map's existing
JASS entry point is therefore the authoritative gameplay source for MCP-built
artifacts. GUI/custom-text members remain preserved opaque data and are not
regenerated or edited by the MCP pipeline.

## Decision

The MCP owns the JASS source for `war3map.j`. The `set_script_source` typed
operation may replace only that existing member and requires:

1. an exact expected current script SHA-256;
2. `language: jass` and complete UTF-8 source text;
3. a `function main takes nothing returns nothing` entry point;
4. successful parsing by `War3Net.CodeAnalysis.Jass`; and
5. an explicitly enabled project `script_policy: "mcp_owned_jass"` in a
   writes-enabled local configuration.

Transactions store the staged source and its hash, semantic diffs expose the
new source hash, and builds replace only `war3map.j` while preserving all
other archive members. The original source map is never overwritten.

World Editor remains available for terrain, visual placement, and compatibility
checks, but saving an MCP-owned build in World Editor may regenerate the GUI
script and overwrite MCP-owned gameplay source. MCP-owned build artifacts must
therefore be treated as the gameplay-authoritative outputs.

## Consequences

The toolchain can now change gameplay logic by replacing the map's JASS source
inside a reviewed transaction and rebuilding a uniquely named map artifact.
The static parser proves JASS syntax and entry-point structure; it does not
prove Warcraft III native availability, multiplayer synchronization, or
gameplay behavior. World Editor open/load and Warcraft III smoke/playtest
observations remain required before promotion.

GUI triggers, custom-text triggers, and generic MPQ patching remain outside this
ADR. The typed object, placement, region, player/force, and MCP-native gameplay
paths are now covered by ADR 0003, but each remains subject to its own
round-trip and exact editor/game evidence gate.

## Verification completed

- The current `war3map.j` parses with the pinned War3Net JASS parser.
- Invalid JASS is rejected before build.
- A script transaction requires the current script hash.
- A build replacing `war3map.j` reopens successfully, preserves `war3map.wtg`
  and `war3map.wct`, and leaves the original source hash unchanged.

## Remaining runtime gate

The exact changed artifact still requires deliberate World Editor and
Warcraft III observation through the Phase 4 test-session protocol. Static
validation and archive round-trip success must not be recorded as gameplay
success.
