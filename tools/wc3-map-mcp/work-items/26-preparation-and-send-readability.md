# v26 task: Preparation and send readability

Status: Implemented and published as v26; Warcraft runtime verification remains
pending. See [build and handoff evidence](../docs/compatibility/v26-preparation-and-sends.md).
On 2026-09-06 the user clarified that the
roster/balance restriction was another agent's oversight and explicitly
authorized changing any MVP units to develop and exercise MCP capabilities.
The [baseline assessment](../docs/compatibility/v26-baseline-assessment.md)
remains historical evidence, not a blocker. This task now includes the missing
send catalog, purchase controls, budgets, planned deployment, and readability.

## Goal

Make the existing prepare–send–defend loop easy to understand. During preparation, a player should quickly answer: “What is coming, what am I sending, and what can I still afford?” The deployed wave must agree with the information shown when preparation ends.

Retain four players, two teams of two, and two mirrored arenas. Build on existing economy, sending, routing, wave, and information systems rather than recreating them.

## Baseline and runtime evidence

- On 2026-09-06 the user reported: “Ive also tested v25 and it is fine, plays as it should.” This confirms the user's overall v25 playtest; do not turn it into claims about individually tested edge cases.
- Baseline playable map: `C:\Users\hp\Documents\Warcraft III\Maps\Test\v25\HeroTeamWars_v25.w3m`.
- Recorded v25 SHA-256: `FF58C3AEB0B0B79FF5EBCA9DCCCDB461B9A31FF5232E6225F8B4CB76000D9335`. Verify the actual file before using it.
- Baseline implementation commit: `3519292a434e96561947a57632fa440f36e10024`; inspect current Git state for subsequent work.
- Read `../docs/compatibility/v25-controller-qwer-spells.md` for build provenance. Its runtime-unverified statement describes the original handoff and is superseded by the user report above for general playability.
- Preserve the golden map `map/HeroTeamWars_M0_2Arena.w3m` and its recorded SHA-256 `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
- Verify the effective MCP source and configuration explicitly. Do not assume the default source contains the v25 repairs. Use a supported isolated workflow that retains the complete v25 baseline.

## Required behavior

### 1. War Camp purchase information

For each purchasable creep, show its personal gold cost, threat value, role, and a brief description of its contribution. Read values from the same definitions used by purchasing and spawning so displayed and actual costs cannot drift. The user's clarification permits establishing the missing roster and tuning its units.

### 2. Personal attack queue

Show the purchasing player's queued creep types and counts, used and remaining personal threat budget, and available personal gold. Update after accepted purchases and at phase transitions. Keep personal spending and queues correctly attributed; a teammate cannot spend the other player's gold.

### 3. Incoming-wave preview

Show the expected base-wave composition, enemy-send composition and roles, and total worth calculated as the summed threat value of both sources. Include neutral filler when the existing rules actually select it, without counting it twice.

The preview reflects current accepted purchases during preparation and is clearly described as updating until preparation ends. Once purchases lock, the final preview must describe the wave scheduled for deployment, including units that spawn later in the staggered sequence. It must not depend on which units are already alive in the arena.

### 4. Compact match overview

Show phase and remaining time, both teams' shared lives, selected heroes and current levels, assigned outgoing destination, and incoming sender. Handle unselected heroes, inactive players, and eliminated teams clearly. Use existing information surfaces where practical; native multiboards, tooltips, and concise messages are sufficient. Keep personal queue information distinguishable from the public team overview.

### 5. Purchase feedback

Give a concise reason when a purchase is rejected: insufficient personal gold, insufficient remaining threat budget, or purchases closed outside preparation. Preserve any other existing eligibility checks and explain their rejection when relevant. Rejected purchases must not charge gold, consume budget, or add queue entries. Successful purchases update the queue and budget without duplicate charges or excessive message spam.

## Scope boundaries

- No additional custom hero abilities or changes to the working Controller kit.
- No talents, new heroes, camp-tier expansion, imported assets, terrain redesign, or six-team expansion.
- The user's clarification authorizes the missing creep roster, prices, threat budgets, and necessary economy/deployment changes; document the selected MVP values.
- Preserve 15 starting shared lives, hero-death-only life loss, personal gold, existing routing and elimination rules, and preparation-only purchases.
- No cancel/refund system or elaborate custom interface is required.

## Implementation workflow

1. Read the root `AGENTS.md` and the complete `tools/wc3-map-mcp/docs/reference/tool-contracts.md` before MCP use; call `wc3_project_status` first. Inspect Git status and preserve unrelated changes.
2. Inspect existing functionality and record what already works versus what this task must add. Start with `scripts/mcp/systems/information.j`, `sending.j`, `economy.j`, `routing.j`, `waves.j`, and `phases.j`, relative to `tools/wc3-map-mcp`. Trace their actual content definitions and UI bindings before editing.
3. Delegate independent inspection or review when useful. Keep map mutation and publication coordinated by one owner.
4. Use the supported inspect/hash → isolated transaction → apply → diff → validate → build workflow. Keep the golden source immutable and retain a recoverable baseline. Do not use arbitrary archive patches.
5. Look up exact JASS declarations and validate new calls/source before staging. Search and look up unfamiliar object fields before typed edits; preserve unknown fields.
6. Review that data used for preview, charges, budget checks, and deployment has a shared authoritative basis. Avoid display-only copies of game rules and player-local changes to synchronized gameplay state.
7. Update affected documentation in the same implementation. If a public MCP contract changes, update all required contracts, schemas, and agent documentation; avoid unnecessary tool-surface expansion for this gameplay task.

## Automated acceptance

Use targeted tests and supported noninteractive checks to establish:

- Accepted purchases charge exactly once and update the correct personal queue and threat budget.
- Rejections leave gold, queue, and budget unchanged, including exact-budget and insufficient-gold boundaries.
- Preview totals and composition agree with the deployment plan for base-only, base-plus-sends, and applicable filler cases.
- Both teammates' sends are aggregated correctly for the receiving arena without mixing personal balances or duplicating units.
- Preparation-to-combat locking preserves the final preview, and the next round does not show stale queue entries.
- Existing inactive-player and elimination handling does not leave invalid sender/destination labels or stale wave information.
- JASS validation, transaction validation, map build, and archive reinspection pass; review the diff for unintended baseline changes.

Report automated evidence separately from gameplay observations. Static or build success does not prove in-game readability or multiplayer synchronization.

## Publication and user playtest handoff

Inspect the existing `C:\Users\hp\Documents\Warcraft III\Maps\Test\vN` directories at publication time and use the next unused number. It is expected to be v26, but never overwrite or reuse a version folder. Publish the whole validated artifact as `HeroTeamWars_v<version>.w3m`.

Provide exact folder, filename, SHA-256, source baseline, build result, validation results, and a short change summary. Record new-build runtime behavior as unverified until the user reports it. The user performs all Warcraft III interaction; do not automate gameplay verification.

User checklist:

1. Load the new map and confirm existing hero selection and Controller behavior still work.
2. During preparation, identify incoming composition, total threat, your queue, available gold, remaining send budget, and destination without debug commands.
3. Buy different creeps with both teammates; confirm personal charges and queue updates and the corresponding enemy preview.
4. Attempt unaffordable, over-budget, and combat-phase purchases; check the rejection reason and unchanged resources.
5. Watch the entire staggered wave arrive and compare it with the final preparation preview.
6. Play five consecutive waves; check timer, lives, levels, queue resets, and readable updates through deaths and revival. Check elimination labels when a team is eliminated.

After relevant checks, stage only intended files, commit with a concise description, push to the configured upstream, and verify final status and pushed commit. Report any exact push blocker. Completion of implementation means a validated artifact and manual-test handoff, not an invented runtime pass.
