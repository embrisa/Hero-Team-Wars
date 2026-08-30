# Map Model and Internal Contracts

## Canonical project model

```text
Project
  configuration
  source_map
  compatibility
  transactions[]
  builds[]
  test_sessions[]

CanonicalMap
  metadata
  players[]
  forces[]
  regions[]
  cameras[]
  variables[]
  triggers
  scripts
  object_data
  placed_objects
  terrain_summary
  imports[]
  opaque_members[]
```

## Value provenance

Every reported value should carry or inherit one provenance class:

- `observed_archive`: parsed directly from the map.
- `observed_editor`: recorded from World Editor inspection.
- `observed_runtime`: recorded from a test launch/playtest.
- `derived`: calculated from observed data; include the rule.
- `intended_design`: read from design documentation.
- `unknown`: not inspected or unsupported.

## Identity rules

- Archive members: normalized Warcraft archive path plus original path.
- Warcraft objects: four-character rawcode plus object category.
- Placed objects: stable generated ID stored in the canonical model; preserve native creation number where available.
- Regions: exact case-sensitive name and coordinates.
- Players: numeric slot ID.
- Transactions/builds/tests: generated UUID and content hash.

## Operation envelope

Each change operation contains operation type, target identity, expected prior value or revision, requested value, rationale, and optional design/chunk reference. Batch application is atomic.

## Versioning

Version the MCP tool schema, engine protocol, canonical map schema, transaction manifest, and compatibility report independently. Reject incompatible major versions with a clear upgrade message.
