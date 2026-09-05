# Engineering audit — 2026-09-05

The audit focused on defects that mislead agents, lose evidence or make tested
code differ from deployed tooling. It changes tooling and documentation, not
gameplay source or playable maps. Warcraft III was not launched or controlled.

## Fixed

| Problem | Correction and regression evidence |
|---|---|
| Preparation accepted expected manifest/module hashes but the tool discarded them. | Forward and enforce both before staging; STDIO regression checks mismatches leave revision 0 unchanged. |
| Module-file edits after begin were ignored when the manifest text stayed unchanged. | Compare file identities with the hash-verified initial snapshot; reject drift before apply. |
| Typed gameplay edits removed the manifest field required by subsequent prepare calls. | Use the initial revision for original input identity, and the current canonical model for composition. |
| A process-only or editor session could produce `runtime_verified=true` for gameplay. | Require matching user-recorded gameplay evidence on an exact game session. Load failure may be recorded as a verified failure; successful load alone is insufficient. |
| Re-recording a chunk overwrote its earlier failure; scenario IDs were used as filenames. | Unique UUID artifact for every attempt; scenario ID remains data. |
| Duplicate operation detection used the truthiness of `Set.add`, which always succeeds. | Case-insensitive `Set.has` check before worker execution. |
| Generated JASS and derived team changes happened after diff collection. | Include post-batch derived changes in semantic diffs with the final operation ID. Full source remains in the canonical revision. |
| Non-object engine batch entries were filtered out rather than rejected. | Reject empty/malformed batches before ordering or mutation. |
| `script_policy=disabled` still composed and replaced gameplay at begin; typed variable/module edits bypassed its check. | Preserve raw script and region data for script-disabled begin; gate all explicit source-generating operations. Native archive validation remains active, with logical profile validation when that model exists. |
| Read-only or script-disabled projects advertised mutation as enabled. | Capability flags use actual write/allow-list/script policies; project status reuses the tool catalog instead of a second handwritten list. |
| A missing engine comparison was interpreted as zero differences. | Fail with `ENGINE_PROTOCOL_ERROR`; build reads also verify size and reinspection identity. |
| Build manifests retained a deleted temporary output path. | Persist the final artifact path in the nested engine result. |
| MCP tests could execute an old published engine after .NET source tests passed. | Official test runner republishes current sources and verifies source preservation even on failure. |
| Four duplicated STDIO clients could hang on exit 0, malformed output or unbounded waits. | One shared client with request timeouts, bounded stderr and rejection of pending requests on exit/error. |
| Agents were directed to obsolete v15/v16 work and a promotion example skipped required milestones. | Remove the completed Step 15 packet, mark the v16 investigation historical, link current compatibility evidence, correct the two-session workflow and reduce mandatory onboarding duplication. |

Public tool names and request schemas remain unchanged. Contract descriptions
and artifact-path/evidence semantics are updated together. Older chunk-result
files and map artifacts remain untouched.

## Remaining problems, in priority order

1. **Implicit gameplay seeding still exists for script-enabled transactions.**
   `TransactionService.begin` overlays the configured manifest, regions, teams
   and regenerated JASS into revision 0 without typed operation records. A
   metadata-only request can therefore inherit unrelated gameplay changes;
   its revision diff starts after those changes. The disabled-policy bypass
   is fixed, but removing seeding for the normal authoring workflow needs an
   explicit initialization operation, preservation of map-bound values and
   migration of preparation/typed-authoring callers. Until then, inspect
   initial script hashes and compare the built map with its source archive.

2. **The current playable map has no single reproducible source recipe.**
   The v24 repair consumes the exact v23 inspection and applies four corrected
   definitions, while `scripts/mcp/object-data/v21-controller-abilities.json`
   still contains older zero-based repeated scopes and old Channel metadata.
   Do not use that historical file to reconstruct v24. The next implementation
   should establish a current authoring fixture that passes catalog validation
   for every field, including helper abilities, plus an end-to-end source-to-map
   rebuild comparison. This audit does not silently migrate object values.

3. **Scenario tests do not execute generated JASS.**
   `GameplayService.runScenarioBuild` calls `ScenarioRunner` with profile/chunk/
   scenario IDs, not the map's program. The report is hash-linked to a build,
   but its results test a separate C# rule model. Keep `static_only`; future
   tests should include generated-source integration or user observations of
   each exact map. A passing model scenario cannot catch binary load failures.

4. **Promotion relies on a monotonic success status.**
   `markRuntimeStatus` only advances and `promote` checks the build's status.
   Later failures are retained in sessions but do not revoke an earlier smoke
   success. Promotion should derive eligibility from linked session history,
   with a documented policy for later failures and source drift. Chunk-result
   fixes do not repair that separate build-level decision.

5. **Composition and serialization remain large, tightly coupled units.**
   `OperationApplier`, `ValidationPipeline` and `GameplaySourceComposer` mix
   many concerns; the composer also carries profile/runtime generation and
   source parsing. Avoid a large cosmetic split while build reproducibility
   is unsettled. Extract small tested components when implementing the fixes
   above; retain unknown-field and exact-source preservation gates.

GUI-trigger writing, terrain/pathing authoring and runtime automation are not
unfinished promises to enable casually: the first two remain intentionally
unsupported without serializer evidence; the user owns runtime verification.

## Verification

Baseline: 119 .NET tests and 47 MCP tests passed before changes. Final
`scripts/test.ps1` run passed **120 .NET tests and 65 MCP tests** across 15 MCP
test files, with the current engine published before integration tests.
The no-op integration now verifies the original script hash and compares the
built map with the actual source. Gameplay integration also prepares, validates
and builds after a typed variable edit while preserving that edit.

All 30 registered tools match the host allow-list and catalog. All 12 versioned
JSON schemas parse, worker-operation enums and generated object-field schemas
pass the contract suite, and 32 maintained Markdown files contain no broken
local links. `git diff --check` passed. No machine prerequisites were installed
or upgraded.

Immutable source: `map/HeroTeamWars_M0_2Arena.w3m`, 38,323 bytes, SHA-256
`027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834`.
No new playable version is published because this is a tooling audit. Existing
v24 runtime status remains unverified until the user reports exact-artifact
observations.
