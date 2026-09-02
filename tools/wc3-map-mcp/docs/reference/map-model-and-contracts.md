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
  teams[]
  regions[]
  cameras[]
  variables[]
  triggers[]
  scripts[]
  gameplay_source
  object_data[]
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

Capability records must be attached independently to trigger definitions,
scripts, variables, region fields, object categories/fields, placements,
players, teams, and forces. A parsed read-only member must not imply that its
binary serializer is available.

## Identity rules

- Archive members: normalized Warcraft archive path plus original path.
- Warcraft objects: four-character rawcode plus object category.
- Trigger/variable identities: stable MCP ID plus exact editor name/path where
  present; generated JASS symbol identity is recorded separately.
- Placed objects: stable generated ID stored in the canonical model; preserve native creation number where available.
- Regions: exact case-sensitive name and coordinates.
- Players: numeric slot ID.
- Teams: stable logical team ID, member player IDs, force index, arena ID, and
  living/routing state.
- Forces: stable index, exact name, player IDs, mask, alliance, vision, and
  control flags.
- Transactions/builds/tests: generated UUID and content hash.

## Operation envelope

Each change operation contains operation type, target identity, expected prior value or revision, requested value, rationale, and optional design/chunk reference. Batch application is atomic.

## Versioning

Version the MCP tool schema, engine protocol, canonical map schema, transaction manifest, and compatibility report independently. Reject incompatible major versions with a clear upgrade message.

## Canonical map example

```json
{
  "schema_version": "1.0",
  "source": {
    "project_id": "hero-team-wars",
    "path": "map/HeroTeamWars_M0_2Arena.w3m",
    "sha256": "027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834",
    "size_bytes": 38323
  },
  "metadata": {
    "title": {
      "value": "Hero Team Wars - Two Arena MVP",
      "provenance": "observed_archive",
      "capability": "parsed_read_only"
    },
    "suggested_players": {
      "value": 4,
      "provenance": "observed_archive",
      "capability": "parsed_read_only"
    }
  },
  "players": [],
  "forces": [],
  "teams": [],
  "regions": [],
  "triggers": [],
  "scripts": [],
  "gameplay_source": {
    "mode": "unknown",
    "manifest": null,
    "source_sha256": null
  },
  "components": {
    "triggers": {
      "capability": "preserved_opaque",
      "reason": "Example only; use actual Phase 0 result."
    }
  },
  "opaque_members": []
}
```

Do not copy the illustrative capability value into a real report. Phase 0 establishes it.

## Component capability record

```json
{
  "archive_path": "war3map.w3r",
  "logical_component": "regions",
  "capability": "roundtrip_verified",
  "parser": {
    "library": "War3Net.Build.Core",
    "version": "resolved in Phase 0",
    "type": "actual parser type"
  },
  "evidence": {
    "fixture_test": "test name",
    "world_editor_opened": true,
    "game_loaded": true
  }
}
```

## Transaction manifest minimum

```text
schema_version
transaction_id
project_id
state
revision
source path label, size, modified UTC, SHA-256
staged-copy SHA-256
created/updated UTC
server/engine/schema/dependency versions
operation IDs and revision links
validation/build/test references
failure information
```

## Semantic diff record

Each difference contains component, target identity, field/path, before, after, operation ID, change type, and provenance. Binary archive reordering is recorded in an archive diff, not semantic diff.

Generated source changes must additionally record the source manifest hash,
module hashes, trigger/variable IDs, generated symbol rewrites, and the final
`war3map.j` hash. Object and placement diffs must include rawcode category and
stable placement ID.

## Cross-process compatibility

The TypeScript worker client sends its supported engine protocol major/minor. The engine rejects incompatible major versions. Unknown optional fields in a compatible minor version may be ignored only if schemas explicitly allow them. Persist protocol/schema versions in every artifact so later agents can reproduce or migrate old transactions.
