# WC3 Map MCP Agent Guide

This is the short orientation for agents working on the live MCP. Read the
repository `AGENTS.md` first, then this guide and the contract documents linked
from the [documentation index](README.md).

## Current implementation

The MCP has a TypeScript STDIO server and a .NET/War3Net map engine. It exposes
read-only inspection, canonical jassdoc lookup and validation, typed map
operations, MCP-owned gameplay composition, deterministic scenarios, builds,
launch/evidence recording, promotion, and transaction discard. The source map
is immutable; changes are staged into uniquely identified transaction/build
artifacts.

## Required change sequence

For any map or gameplay change, inspect the project and source hash first. Use
an isolated transaction, apply typed operations with expected prior values,
inspect the diff, validate the transaction, build a uniquely named artifact,
and only then hand it off for separately observed editor/game verification.
Promote only the exact artifact that passed the required gates, or discard the
confirmed transaction. Never treat process start, parser success, or build
success as gameplay evidence.

## Hard gates

- Use `wc3_project_status` first and read the complete
  [`tool-contracts.md`](reference/tool-contracts.md) before calling tools.
- Never overwrite the source map or use arbitrary archive patching.
- Keep project-relative paths, source hashes, revisions, artifact hashes, and
  observation-session links explicit.
- Use the pinned local jassdoc dataset for JASS search, lookup, call validation,
  and source validation; never guess native names or signatures.
- Preserve unknown archive data and use typed serializers and schemas.
- Do not infer lobby, load, smoke, camera, combat, or playtest success. The user
  owns manual Warcraft III verification, and agents must report those gates as
  unverified until the user observes them.

## Current work and known limitations

Take task scope from the current user request. Versioned compatibility reports
describe individual artifacts; inspect Git, the current configuration and
playable folders before choosing a baseline. The latest recorded repair is
[v24](compatibility/v24-controller-load-repair.md), still runtime-unverified in
that report. Old work items are historical evidence, not standing task orders.

Read the [engineering audit](engineering-audit.md) for remaining structural
risks, including implicit gameplay seeding in script-enabled transactions,
stale object fixtures and scenarios that model rules without executing JASS.
Use `scripts/test.ps1` to test the current published engine and server together.

Gameplay preparation checks requested manifest/module hashes and module drift
against the initial transaction snapshot. Edit source files before beginning
the transaction; use typed operations for later staged edits. Chunk results
are separate immutable attempts, and user-observed results require matching
gameplay observations on an exact game session.

## Keeping this guide current

Any implementation, schema, registration, configuration, response, error,
policy, path, serializer, or evidence change must update this guide when its
workflow or constraints change, plus the affected contract/reference docs and
README files in the same commit.

## Object field knowledge and authoring

Use `wc3_object_field_search` and `wc3_object_field_lookup` before editing
unfamiliar field IDs. The shared offline catalog powers readable field names,
annotated inspection/diffs and enforced field semantics. Unknown legacy fields
remain unchanged; newly authored fields must match verified metadata. See
[object-field authoring](reference/object-field-authoring.md) for exact input, scope, evidence,
preservation and regeneration rules. This is a tooling change; existing maps
and historical authoring fixtures are not automatically migrated.
