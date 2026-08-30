# Phase 5 - Hero Team Wars Integration

## Objective

Use the verified MCP pipeline to implement the existing Hero Team Wars chunk queue without losing the editor-owned baseline.

## Migration sequence

### MCP-HTW-00 - Baseline capture

- Inspect the existing map.
- Generate the candidate editor-state report.
- Manually verify fields not exposed by the archive.
- Update the ledger only after user confirmation or observed editor evidence.

### MCP-HTW-01 - Foundation and phase state

- Add centralized tuning data.
- Add explicit two-team mapping.
- Add phase, round, wave ID, preparation timer, and combat-limit logic.
- Provide deterministic debug output prefixed `[HTW]`.

### MCP-HTW-02 - Deterministic base wave

- Define a small base-wave data table.
- Spawn neutral-hostile units into the two active arenas.
- Track spawned units by wave ID and clean them at resolution.

### MCP-HTW-03 - Death, lives, respawn, elimination

- Apply hero-death life loss exactly once.
- Preserve the invariant that leaks and timeouts do not remove lives.
- Respawn eligible heroes after resolution.
- Remove an eliminated arena from future rounds.

### MCP-HTW-04 - Two-arena sending loop

- Personal gold and player-owned purchases.
- Two personal War Camps per team.
- Three initial creep choices and an attack queue.
- Opposing-arena delivery; buyers never fight their own sends.

### MCP-HTW-05 - Information and repeatability

- Phase, timer, lives, incoming composition, threat worth, and queue visibility.
- Stable debug events for repeatable tests.

### MCP-HTW-06 - Six-team expansion

- Begin only after the two-arena loop passes its tests.
- Implement locked visible round-robin routes over living teams.
- Recalculate next preparation after elimination; never mid-wave.

## Required integration rule

Each MCP-HTW chunk must reference the corresponding `HTW-XX` entry, list exact map operations, produce a semantic diff, build a new artifact, and append observed results to `design/09-change-log.md`. Planning or code generation alone does not mark a chunk applied.
