# Hero Team Wars Compatibility Baseline

Generated UTC: 2026-08-30T11:30:05.9655841Z

## Source identity

- Path: `C:\Users\hp\Documents\Warcraft III\Hero Team Wars\map\HeroTeamWars_M0_2Arena.w3m`
- Size: `38323` bytes
- Modified UTC: `2026-08-25T13:11:16.3515059Z`
- SHA-256: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`
- Source hash after probe: `027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`

## Environment

``json
{
  "engine_version": "1.0.0.0",
  "engine_commit": "local",
  "runtime": "10.0.11",
  "os": "Microsoft Windows NT 10.0.26200.0",
  "architecture": "X64",
  "war3net_io_mpq": "6.0.3.0",
  "war3net_build_core": "6.0.3.0"
}
``

## Archive and parser coverage

- Archive members observed: 17
- Parser probe entries: 17
- Rebuilt member content-hash comparison: **PASS**

The JSON report contains the complete stable member inventory, parser capability matrix, canonical metadata, players, forces, regions, and unknown/opaque classifications.

## No-op rebuild

- Output: `C:\Users\hp\Documents\Warcraft III\Hero Team Wars\tools\wc3-map-mcp\builds\mcp\phase0\HeroTeamWars_M0_2Arena_MCP_P0_noop_20260830T113005Z.w3m`
- Output SHA-256: `C93C2A9F3DC25FED406D04A5902ADADFF2869BAD13C0BB488CDD2F2659C61128`
- Reopened and re-inspected by the engine: `True`
- Editor observed: `false`
- Warcraft III observed: `false`
- Evidence level: `built_reopened_by_engine_only`

This probe did not claim World Editor or game compatibility. Those are explicit manual gates for the exact output above.

## HTW-00 state report

The candidate report is at `artifacts/hero-team-wars/htw-00-candidate.json` and `.md`. It separates archive observations, editor/game verification needs, ledger conflicts, and unsupported or absent values. `design/07-editor-state.yaml` was not modified automatically.

## Recommendation

`GO_READONLY_AND_NOOP_BUILD_WITH_MANUAL_RUNTIME_GATE`
