# WC3 Map MCP Documentation

This directory is the maintained documentation set for the live MCP. Historical
kickoff and phase packets are not part of the active agent contract; Git history
retains them when their context is needed.

## Start here

- [`agent-guide.md`](agent-guide.md) — current workflow, safety gates, and reading order.
- [`reference/tool-contracts.md`](reference/tool-contracts.md) — every exposed tool, schema, policy gate, response, and evidence contract.
- [`reference/architecture.md`](reference/architecture.md) — process and component boundaries.
- [`reference/map-model-and-contracts.md`](reference/map-model-and-contracts.md) — typed map model and mutation contracts.
- [`reference/safety-recovery-and-audit.md`](reference/safety-recovery-and-audit.md) — hashes, transactions, recovery, and audit requirements.
- [`reference/testing-and-definition-of-done.md`](reference/testing-and-definition-of-done.md) — verification and evidence gates.
- [`reference/packaging-and-codex-connection.md`](reference/packaging-and-codex-connection.md) — build, launch, and Codex connection behavior.
- [`reference/mcp-and-tooling.md`](reference/mcp-and-tooling.md) and [`reference/wc3-map-formats.md`](reference/wc3-map-formats.md) — protocol/tooling and archive-format references.
- [`decisions/`](decisions/) — durable architecture decisions.
- [`compatibility/`](compatibility/) and [`troubleshooting/`](troubleshooting/) — current compatibility findings and recovery lessons.
- [`../work-items/`](../work-items/) — active scoped implementation work, not general agent instructions.

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
