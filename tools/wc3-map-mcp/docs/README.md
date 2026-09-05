# WC3 Map MCP Documentation

This directory is the maintained documentation set for the live MCP. Historical
kickoff and phase packets are not part of the active agent contract; Git history
retains them when their context is needed.

## Start here

- [`agent-guide.md`](agent-guide.md) — current workflow, safety gates, and reading order.
- [`reference/object-field-authoring.md`](reference/object-field-authoring.md) — field lookup, readable authoring, semantic gates and unknown-field preservation.
- [`reference/object-fields.generated.md`](reference/object-fields.generated.md) — generated field IDs, readable names, types, scope and source evidence.
- [`reference/tool-contracts.md`](reference/tool-contracts.md) — every exposed tool, schema, policy gate, response, and evidence contract.
- [`reference/architecture.md`](reference/architecture.md) — process and component boundaries.
- [`reference/map-model-and-contracts.md`](reference/map-model-and-contracts.md) — typed map model and mutation contracts.
- [`reference/safety-recovery-and-audit.md`](reference/safety-recovery-and-audit.md) — hashes, transactions, recovery, and audit requirements.
- [`reference/testing-and-definition-of-done.md`](reference/testing-and-definition-of-done.md) — verification and evidence gates.
- [`reference/packaging-and-codex-connection.md`](reference/packaging-and-codex-connection.md) — build, launch, and Codex connection behavior.
- [`reference/mcp-and-tooling.md`](reference/mcp-and-tooling.md) and [`reference/wc3-map-formats.md`](reference/wc3-map-formats.md) — protocol/tooling and archive-format references.
- [`decisions/`](decisions/) — durable architecture decisions.
- [`compatibility/`](compatibility/) and [`troubleshooting/`](troubleshooting/) — current compatibility findings and recovery lessons.
- [`compatibility/v21-controller-spells.md`](compatibility/v21-controller-spells.md) — Controller spell definitions, preservation checks, and manual v21 acceptance.
- [`compatibility/v22-h003-ability-attachment.md`](compatibility/v22-h003-ability-attachment.md) — H003 hero-ability field correction from runtime feedback.
- [`compatibility/v23-controller-ability-metadata.md`](compatibility/v23-controller-ability-metadata.md) — Controller hero flag, icons, and Channel field type correction.
- [`compatibility/v24-controller-load-repair.md`](compatibility/v24-controller-load-repair.md) — native boolean encoding repair and exact runtime-unverified artifact.
- [`engineering-audit.md`](engineering-audit.md) — fixed defects, remaining risks and verification from the September audit.
- [`../work-items/`](../work-items/) — historical scoped investigations; current task scope comes from the user.

## Sources of truth

The implementation and schemas remain authoritative for behavior: tool
registrations under `mcp-server/src/tools/`, Zod schemas under
`mcp-server/src/schemas/`, versioned schemas under `contracts/schemas/`, and
the project MCP configuration in `.codex/config.toml`. This documentation
explains the contract agents must follow and must not silently diverge from
those sources.

## Maintenance contract

When code changes a tool name, request/response schema, error, policy gate,
serializer, path, evidence state, configuration, or workflow, update this
index and every affected reference, README, decision/compatibility note,
versioned schema, and Codex allow-list in the same change. Finish with a
registration/schema/documentation consistency check. Documentation-only
changes must explicitly state that no runtime contract changed.
