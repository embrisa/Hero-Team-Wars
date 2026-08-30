# WC3 Map MCP Implementation Plan

Status: planning and project scaffold complete; required runtimes are installed (Node.js 24.19.0/npm 11.17.0 and .NET SDK 10.0.400), and no implementation has started.

This folder is the execution plan for a local Model Context Protocol server that lets AI agents inspect, modify, build, validate, and test Warcraft III custom maps without directly patching the World Editor executable.

The first integration target is `map/HeroTeamWars_M0_2Arena.w3m`. The original map is immutable input until a deliberate adoption step. Generated maps, snapshots, reports, and test copies must be written elsewhere.

## Reading order

1. `00-project-charter.md`
2. `01-decisions-and-constraints.md`
3. `02-architecture.md`
4. `03-phase-0-discovery.md`
5. `04-phase-1-readonly-inspector.md`
6. `05-phase-2-transactional-writer.md`
7. `06-phase-3-build-and-validation.md`
8. `07-phase-4-test-runner.md`
9. `08-phase-5-hero-team-wars-integration.md`
10. `09-mcp-tool-contracts.md`
11. `10-map-model-and-contracts.md`
12. `11-safety-recovery-and-audit.md`
13. `12-testing-and-definition-of-done.md`
14. `13-packaging-and-codex-connection.md`

## Phase gates

No phase may begin until the preceding phase meets its acceptance criteria. Reading a map is not proof that it can be rebuilt safely. Producing a map archive is not proof that World Editor or Warcraft III can open it. A test launch is not proof that gameplay behavior is correct.

## Existing project documents that remain authoritative

- Game rules: `design/01-vision-and-pillars.md` through `design/05-open-decisions.md`
- Current implementation workflow: `design/06-implementation-workflow.md`
- Observed editor state: `design/07-editor-state.yaml`
- Existing implementation chunks: `design/08-implementation-chunks.md`
- Observed change/test log: `design/09-change-log.md`

The MCP must report disagreements between these layers. It must not silently choose one.
