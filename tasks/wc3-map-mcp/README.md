# WC3 Map MCP - Agent Task Index

Status: Phase 3 build and validation implementation complete; required runtimes are installed (Node.js 24.19.0/npm 11.17.0 and .NET SDK 10.0.400), and the manual World Editor/Warcraft III compatibility gate remains pending.

This directory is a self-contained implementation manual for agents that know nothing about the preceding conversation. The objective is a local Model Context Protocol server that allows Codex or another MCP-capable agent to inspect, safely modify, build, validate, and test Warcraft III custom maps.

The first target is `C:\Users\hp\Documents\Warcraft III\Hero Team Wars\map\HeroTeamWars_M0_2Arena.w3m`. That file is immutable source input. All experiments must use snapshots, staging directories, and separately named builds.

## Mandatory context for every assigned agent

Do not give an agent only a phase title. Give it the phase file and require it to read these four files first:

1. `AGENT-START-HERE.md` - user goal, current machine state, project state, terminology, and non-negotiable safety rules.
2. `REFERENCE-MCP-AND-TOOLING.md` - exact MCP SDK model, STDIO rules, TypeScript packages, Codex connection, .NET worker protocol, and external tooling.
3. `REFERENCE-WC3-MAP-FORMATS.md` - how WC3 maps are packaged, important archive members, War3Net responsibilities, and compatibility risks.
4. `IMPLEMENTATION-CONVENTIONS.md` - exact folder ownership, naming, configuration, error, testing, logging, and handoff conventions.

An agent must then read the phase file it is assigned plus any documents listed in that file's `Required reading` section. It must inspect the current filesystem before acting because later agents may have completed earlier work.

## Task order

| Order | File | Outcome |
|---:|---|---|
| 0 | `00-project-charter.md` | Scope, success definition, and exclusions |
| 1 | `01-decisions-and-constraints.md` | Locked architecture decisions and constraints |
| 2 | `02-architecture.md` | Component, process, data, and transaction architecture |
| 3 | `03-phase-0-discovery.md` | Install/verify prerequisites and prove current-map compatibility |
| 4 | `04-phase-1-readonly-inspector.md` | Working STDIO MCP with safe inspection tools |
| 5 | `05-phase-2-transactional-writer.md` | Typed staged changes with diff and rollback |
| 6 | `06-phase-3-build-and-validation.md` | Deterministic validated map builds |
| 7 | `07-phase-4-test-runner.md` | Editor/game launch and evidence recording |
| 8 | `08-phase-5-hero-team-wars-integration.md` | Implement HTW chunks through the verified pipeline |
| reference | `09-mcp-tool-contracts.md` | Exact public tools and schemas |
| reference | `10-map-model-and-contracts.md` | Canonical model and worker protocol |
| reference | `11-safety-recovery-and-audit.md` | Path, snapshot, locking, deletion, and audit algorithms |
| reference | `12-testing-and-definition-of-done.md` | Required tests and evidence gates |
| reference | `13-packaging-and-codex-connection.md` | Build, local install, Inspector, and Codex configuration |

## Phase-gate rule

No phase may be marked complete because source code exists or a command exited successfully. Each phase file defines observable evidence. Examples:

- Parsing a map does not prove it can be rebuilt.
- Rebuilding an archive does not prove World Editor can open it.
- Opening it in World Editor does not prove Warcraft III can load it.
- Loading it does not prove a gameplay chunk behaves correctly.

The completion report must say which evidence level was actually reached and list anything not tested.

Current implementation evidence: Phase 3 engine and MCP build/validation paths are implemented, with 24 .NET tests and 24 MCP tests passing. The current source remains hash-stable at `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`. Runtime launch is not marked passed: the existing World Editor session was left untouched, and the installed Warcraft III executable exited without exposing a game window during the controlled artifact-load attempt.

## Existing Hero Team Wars authority layers

- Intended game rules: `design/01-vision-and-pillars.md` through `design/05-open-decisions.md`.
- Current editor-led workflow: `design/06-implementation-workflow.md`.
- Last observed editor state: `design/07-editor-state.yaml`.
- Current implementation queue: `design/08-implementation-chunks.md`.
- Actual edits and test observations: `design/09-change-log.md`.
- Playable source map: `map/HeroTeamWars_M0_2Arena.w3m`.

If these disagree, report the conflict and its provenance. Do not silently make the design document, ledger, generated model, or map win.
