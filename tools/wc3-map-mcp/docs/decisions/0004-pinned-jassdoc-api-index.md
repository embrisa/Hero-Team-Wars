# ADR 0004: Pinned local jassdoc API index

## Context

Generated map JASS must not depend on model memory for function names or signatures. The canonical upstream requested for this project is [`lep/jassdoc`](https://github.com/lep/jassdoc), whose build produces `jass.db` from documented `common.j`, `Blizzard.j`, and related inputs.

The repository currently has no license file, and its own `jassdoc.nix` declares the license as `unknown`. Committing or redistributing its source or generated database would therefore assume rights that have not been established. Adding SQLite plus the upstream Haskell/SQLite build chain solely for runtime lookup would also be unnecessary for this .NET worker.

## Decision

- Pin exactly `lep/jassdoc@deddec452ec16ea355ca0aa47046b88d416dbc65`.
- Commit a deterministic importer and bootstrap script, but do not commit upstream source or generated API data.
- `scripts/sync-jassdoc.ps1` verifies the exact Git commit and produces the ignored `map-engine/data/jassdoc/jass-api.json` index from `common.j`, `Blizzard.j`, and `builtin-types.j`.
- The generated index contains exact declarations, ordered parameter names/types, return types, native/function/global/type kind, source file/line, documentation, annotations, and type inheritance. It contains no timestamp or host path.
- The .NET map engine loads and caches this one local index. Normal MCP tool calls never use the network and expose no version selector.
- Exact lookup, ranked search, call/source validation, generated-source composition, transaction staging, canonical validation, and build validation share this repository and validator.

## Alternatives considered

- **Commit `jass.db`:** rejected until redistribution permission is clear.
- **Download WurstScript's mutable `latest` artifact:** rejected because it is not a stable source pin.
- **Maintain a hand-written native list:** rejected because it recreates the hallucination problem and drifts from the canonical API.
- **Query hosted documentation at runtime:** rejected because lookup and validation must be deterministic and offline.

## Consequences

`bootstrap.ps1` requires Git and network access once to generate the local dataset. Builds, tests, and runtime operations are offline after that. A fresh checkout without the generated file fails closed with an instruction to run `sync-jassdoc.ps1`. If upstream licensing is clarified later, the generated index may be vendored in a separate decision without changing the service API.

## Rollback

Remove the JASS service/tools and generated-data checks, restore the previous syntax-only `ScriptOwnership` behavior, and remove the local dataset. Do not restore a hardcoded function-name exception.
