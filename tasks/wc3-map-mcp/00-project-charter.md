# Project Charter

## Goal

Create a reusable local MCP service that gives AI agents safe, structured control over Warcraft III custom-map development while preserving World Editor as a visual inspection and final compatibility tool.

## First useful outcome

An agent can inspect `HeroTeamWars_M0_2Arena.w3m` and return a structured inventory of map metadata, players, forces, regions, trigger files, variables, objects, placed units, terrain metadata, and archive contents without changing any file.

## End-state outcome

An agent can propose a change, stage it in an isolated workspace, show a machine-readable diff, validate it, build a new `.w3m` or `.w3x`, launch a test copy, collect evidence, and either promote or discard the result.

## In scope

- Local STDIO MCP server for Codex and other MCP-capable agents.
- Warcraft III map archive inspection and deterministic rebuilding.
- Structured edits to supported map components.
- Lua/JASS source management and syntax/static checks.
- Object data, players, forces, regions, units, doodads, and supported trigger data.
- Safe World Editor and game launch commands.
- Snapshots, audit reports, map diffs, validation, and fixture-based tests.
- Hero Team Wars integration as the reference project.

## Out of scope for the first release

- DLL injection or binary modification of `World Editor.exe`.
- Autonomous publication to Battle.net.
- Editing the only copy of a user map.
- Claiming visual or gameplay correctness from archive inspection alone.
- Full semantic editing of every GUI trigger action in the first milestone.
- Unattended destructive cleanup outside MCP-owned artifact directories.

## Success measures

- The original map hash remains unchanged throughout development.
- Read-only inspection produces stable JSON for the same input.
- A no-op round trip produces a playable/editor-openable copy or is explicitly rejected as unsupported.
- Every write has a plan ID, snapshot, diff, validation report, and output path.
- Hero Team Wars `HTW-00` baseline information can be populated without manual transcription where the map format exposes it.

## Primary user stories

### Safe inspection

As a map author, the user can ask “what players, forces, regions, objects, and triggers are in this map?” and receive an evidence-backed inventory tied to the map hash. Unknown fields remain unknown.

### Reviewable change

As a map author, the user can ask “change preparation time from 35 to 40 seconds” and receive a staged transaction showing exactly which source/config/map component changes, without touching the accepted map.

### Reproducible build

As a map author, the user can approve a staged diff and receive a new uniquely named map plus manifest, validation report, and hash. Another agent can rebuild it from the same transaction/tool versions.

### Honest test evidence

As a map author, the user can distinguish “file built,” “editor opened it,” “game loaded it,” and “the gameplay test passed.” The MCP never collapses these into a generic success.

## Release milestones

- **R0 - Compatibility probe:** the current map is inventoried and writer viability is known.
- **R1 - Inspector:** read-only Codex tools work safely.
- **R2 - Transaction authoring:** supported changes can be staged and diffed.
- **R3 - Builder:** validated transactions produce compatible map copies.
- **R4 - Test integration:** editor/game evidence is recorded.
- **R5 - Hero Team Wars first playable:** HTW-00 through HTW-05 pass on two arenas.

## Stakeholder and authority

The user owns the map, design decisions, installation approval, promotion choice, and gameplay acceptance. Agents own implementation proposals and evidence collection but may not reinterpret locked rules or treat an automated result as user approval.
