# Phase 0 - Discovery and Compatibility Baseline

## Objective

Identify the installed Warcraft III/World Editor environment and prove which parts of the existing `.w3m` can be read before selecting write support.

## Implement

- Environment probe for configured game/editor executables and versions.
- SHA-256 inventory for source maps.
- Archive-member listing with sizes and hashes.
- Parser capability matrix for every discovered `war3map.*` member.
- Dependency spike comparing War3Net behavior against the current map.
- Fixture copy of the current map stored outside test outputs; do not commit copyrighted imported assets if this project later becomes public.
- Baseline JSON report and human-readable Markdown report.

## Required investigations

- Exact Warcraft III and World Editor versions.
- Whether the map contains classic GUI triggers, custom-text triggers, JASS, or Lua.
- MPQ archive behavior and preservation of unknown members.
- Whether the current editor can open a no-op rebuilt copy.
- Whether the game can test-launch that copy.
- Which map components can be round-tripped byte-for-byte versus semantically.

## Deliverables

- `compatibility-report.json`
- `compatibility-report.md`
- supported/unsupported component table
- original hash and snapshot manifest
- go/no-go decision for Phase 1

## Acceptance criteria

- Discovery performs no writes to the source map.
- The source hash before and after discovery is identical.
- Every archive member is classified as parsed, preserved opaque, or unsupported.
- Unsupported components block corresponding write tools but not safe inspection.
