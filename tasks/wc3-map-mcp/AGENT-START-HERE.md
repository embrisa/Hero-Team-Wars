# Agent Start Here

This is the context packet for any agent working on the WC3 Map MCP. Read it completely before modifying files, installing software, or starting Warcraft III tools.

## What the user is trying to build

The user wants AI agents to create and maintain Warcraft III custom maps through explicit tools rather than through fragile mouse control of World Editor. The system should eventually let an agent:

1. inspect a `.w3m` or `.w3x` and understand its structure;
2. propose structured changes such as players, forces, regions, units, object data, scripts, and imports;
3. stage those changes without touching the source map;
4. show a semantic diff the user can review;
5. validate and build a separately named test map;
6. open that exact build in World Editor or Warcraft III;
7. record what was actually observed;
8. promote or discard the build while retaining recovery evidence.

This is not an attempt to modify `World Editor.exe`, inject a DLL, reverse-engineer Blizzard code in memory, or publish maps autonomously. It is an external companion toolchain.

## Absolute locations

| Purpose | Path |
|---|---|
| Hero Team Wars root | `C:\Users\hp\Documents\Warcraft III\Hero Team Wars` |
| MCP implementation root | `C:\Users\hp\Documents\Warcraft III\Hero Team Wars\tools\wc3-map-mcp` |
| MCP task documents | `C:\Users\hp\Documents\Warcraft III\Hero Team Wars\tasks\wc3-map-mcp` |
| Current source map | `C:\Users\hp\Documents\Warcraft III\Hero Team Wars\map\HeroTeamWars_M0_2Arena.w3m` |
| Existing project builds | `C:\Users\hp\Documents\Warcraft III\Hero Team Wars\builds` |
| Warcraft test maps | `C:\Users\hp\Documents\Warcraft III\Maps\Test` or another path verified during Phase 0 |
| World Editor | `C:\Warcraft III\_retail_\x86_64\World Editor.exe` |
| Warcraft III | `C:\Warcraft III\_retail_\x86_64\Warcraft III.exe` |

Do not substitute a similarly named directory without confirming it. Do not create the MCP implementation under the Warcraft installation directory.

## Machine state observed on 2026-08-30

These are observations, not timeless assumptions. Verify them before using them:

- World Editor file/product version: `2.0.4.23745`.
- Warcraft III file/product version: `2.0.4.23745`.
- Node executable: `C:\Program Files\nodejs\node.exe`.
- Node version: `24.19.0`.
- npm version: `11.17.0`.
- The current MCP TypeScript SDK documentation requires Node 20 or later; the observed Node installation satisfies that baseline, subject to package compatibility checks.
- `dotnet` host exists at `C:\Program Files\dotnet\dotnet.exe`.
- .NET SDK `10.0.400` and host/runtime `10.0.11` are installed alongside older .NET 5 and .NET 6 runtimes.
- No additional .NET workloads were reported; the planned console/library solution should not require optional workloads.
- No MCP npm packages or War3Net NuGet packages have been installed for this project.
- The Hero Team Wars workspace is not currently a Git repository. Do not assume Git rollback exists.

Installing/upgrading Node or a .NET SDK is a machine-level change. An implementation agent should present the exact supported version and request authorization before installation unless the assigning user explicitly authorized prerequisites.

## Current map state

The source map is `HeroTeamWars_M0_2Arena.w3m`. Its last verified SHA-256 is:

`027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`

Before every phase, recompute the hash. If it differs, stop treating the value above as current and create a fresh baseline report. Never overwrite the source map to make the hash match.

Last verified editor facts:

- Map title: `Hero Team Wars - Two Arena MVP`.
- Suggested players: 4.
- Players 1-4: human with fixed starts.
- Players 5-12: closed.
- Team 1: Players 1-2.
- Team 2: Players 3-4.
- Teammates are allied with shared vision and without shared unit control.
- Known region names include `Arena_A` and `Camp_A_Player1`; the complete region inventory is not verified.
- The Trigger Editor was last verified with an empty `Initialization` folder.
- The map was saved and test-launched on 2026-08-25.
- A later attempt to add Phase 1 triggers was interrupted. No later trigger edits or runtime behavior are verified.

The design ledger at `design/07-editor-state.yaml` deliberately contains `unknown` and `not_verified` values. Preserve those markers until there is evidence.

## Existing game scope

The first playable is four users, two teams of two, and two mirrored arenas. It is not the full six-team game yet. The initial gameplay chunks are already defined in `design/08-implementation-chunks.md`:

- `HTW-00`: no-edit baseline capture;
- `HTW-01`: initialization and phase state;
- `HTW-02`: deterministic base wave;
- `HTW-03`: hero death, lives, respawn, and elimination;
- `HTW-04`: two-arena sending loop;
- `HTW-05`: information and repeatability;
- `HTW-06`: six-team routing expansion only after the two-arena loop is stable.

The feature-extension packets define the broader MCP ownership target:

- `14-phase-5a-gameplay-source-and-trigger-support.md`: MCP-native gameplay
  sources, runtime triggers, variables, and optional GUI-trigger compatibility;
- `15-phase-5b-region-support.md`: complete typed `war3map.w3r` support;
- `16-phase-5c-object-and-placement-support.md`: object definitions and placed
  units/buildings/items/doodads;
- `17-phase-5d-players-teams-and-forces.md`: `war3map.w3i`, player slots,
  logical teams, forces, and six-team profiles;
- `18-phase-5e-runtime-logic-and-evidence.md`: deterministic HTW source modules,
  scenario builds, and chunk evidence;
- `19-phase-5f-full-mcp-feature-rollout.md`: dependency order and release gates.

The MCP implementation must first automate the inspection/build workflow. Do not jump directly to implementing gameplay in a binary map.

## The chosen technical architecture

There are two local processes:

1. **TypeScript MCP server**: exposes agent-facing tools, validates tool inputs, enforces project/path permissions, coordinates transactions, and translates worker results into MCP content and structured output.
2. **.NET map engine**: uses War3Net or a verified alternative to parse/write WC3 formats, create canonical map inventories, apply typed operations, validate references, preserve unknown archive members, and build map archives.

The MCP server launches the map engine as a child process. They exchange one JSON request and one JSON response per line. This private NDJSON protocol is not MCP; it is an internal boundary that keeps Warcraft binary code out of the MCP process.

Why not use only TypeScript? The most complete candidate library discovered for WC3 map building and file formats is War3Net, a .NET library. Why not expose the map engine directly as MCP? Keeping MCP policy/orchestration separate makes the engine testable from a normal CLI and prevents protocol/tool concerns from contaminating binary-format code.

This choice can change only if Phase 0 proves War3Net cannot safely support the installed editor/map. Record a decision document before changing it.

## Terms used throughout the tasks

- **source map**: the user's existing `.w3m` or `.w3x`; opened read-only.
- **archive member**: an internal file such as `war3map.w3i` or `war3map.w3u` inside the MPQ-based map archive.
- **canonical model**: stable JSON representation used for inspection, diff, and typed operations.
- **opaque member**: an archive member the engine cannot parse but can preserve byte-for-byte.
- **transaction**: an isolated staged change set tied to an exact source hash.
- **build**: a newly produced `.w3m`/`.w3x` plus manifest and hash.
- **promotion**: explicit copy of a selected validated build to an approved destination; never implicit source replacement.
- **evidence level**: built, editor opened, game loaded, smoke passed, or playtest passed.
- **provenance**: where a value came from: archive, editor observation, runtime observation, derivation, design intent, or unknown.

## Non-negotiable safety rules

1. Never write to the source map path.
2. Resolve and verify every path stays beneath its configured root before writing, moving, copying, or deleting.
3. Never accept `C:\`, a user profile root, `$HOME`, `~`, an unresolved environment variable, or a wildcard as a destructive target.
4. Delete only a single MCP-owned transaction directory after verifying its manifest and resolved parent.
5. Preserve unknown archive members unless an explicit typed operation removes a named member.
6. A mutating operation requires transaction ID plus expected source hash.
7. Put protocol messages on stdout only. Put diagnostic text on stderr or in structured log files.
8. Do not kill World Editor or Warcraft III processes without explicit permission.
9. Do not claim a test passed without an observable result tied to the exact build hash.
10. Do not infer team identity from player color.
11. Preserve existing region names exactly.
12. Do not update the editor-state ledger from intended design values.
13. Do not enable a trigger, object, region, player, team, or force capability
    until its exact member serializer and round-trip evidence pass.
14. Keep MCP-native JASS source ownership separate from editor-compatible GUI
    trigger ownership; do not silently mix the two modes.

## What an assigned agent must do first

1. Read this file and the three reference files named by `README.md`.
2. Read the assigned phase file fully.
3. Inspect `tools/wc3-map-mcp` and determine what earlier agents actually completed.
4. Inspect the source map hash and the relevant design/ledger files.
5. State which phase gate is currently satisfied.
6. Make the smallest implementation that satisfies the assigned task.
7. Run the specified tests.
8. Produce the required artifacts and completion report.

## Required completion report format

Every agent returns:

```text
TASK:
STATUS: complete | partial | blocked
FILES CREATED OR CHANGED:
DEPENDENCIES INSTALLED OR CHANGED:
COMMANDS/TESTS ACTUALLY RUN:
OBSERVED RESULTS:
SOURCE MAP HASH BEFORE:
SOURCE MAP HASH AFTER:
ARTIFACTS AND LOGS:
UNTESTED OR UNSUPPORTED:
DECISIONS MADE:
NEXT PHASE READINESS:
```

If blocked, include the exact error and the smallest user decision or external change needed. Do not mark a phase complete because the token/time budget ended.
