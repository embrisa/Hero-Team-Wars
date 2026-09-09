# WC3 Map MCP Tool Contracts

Object-data serialization: logical `Bool` values use native Int tag 0 and
four-byte 0/1 values. Inspection exposes catalog-known native 0/1 boolean
fields as logical `Bool`; unknown integers stay unchanged. Legacy tag-4
records are readable but only repaired when rewritten. Public tool names and
request schemas are unchanged. See [object-field authoring](object-field-authoring.md).

This is the agent-facing catalog of the implemented MCP surface. Read it
before calling a tool or changing a tool schema. The executable source of
truth is `tools/wc3-map-mcp/mcp-server/src/tools/` plus
`tools/wc3-map-mcp/mcp-server/src/schemas/tools.ts` and
`tools/wc3-map-mcp/mcp-server/src/schemas/operations.ts`. Update this catalog
when the public surface changes.

## Maintenance contract

This catalog is a maintained contract, not a historical design note. Any code
or configuration change that affects a tool name, request field, default,
response field, artifact/hash behavior, error, policy gate, serializer,
capability, evidence state, or workflow must update this file and every related
README, versioned JSON schema, decision/compatibility note, and Codex allow-list in the
same change. Breaking wire changes require the appropriate schema-version
decision. Before finishing, compare tool registrations, executable schemas,
`contracts/schemas/`, `.codex/config.toml`, and this catalog; do not knowingly
leave them inconsistent.

The current server exposes 30 tools: 7 map read tools, 4 global JASS/jassdoc
knowledge tools, 2 global object-field tools, 5 transaction tools, 3 build tools, 4 launch/evidence tools,
and 5 gameplay-source tools.

## Contract rules shared by every tool

- Request objects are closed/strict JSON schemas; unknown fields are errors.
- `project_id` is a configured project identifier. Map and artifact paths are
  project-relative and are resolved against configured roots; arbitrary
  absolute paths, `..` escapes, and wildcards are not valid tool inputs.
- A successful call returns `structuredContent` with
  `{ ok: true, correlation_id, data, warnings, artifacts }` plus a bounded text
  summary. Artifact references contain `kind`, project-relative `path`,
  `size_bytes`, and `sha256`.
- A failed domain call returns `isError: true` and
  `{ ok: false, correlation_id, error: { code, message, retryable, details } }`.
  Correct the reported precondition or input; do not guess around an error.
- The source map is read-only. All writes go to MCP-owned staging, artifact,
  test, or build roots. Every transaction/build/evidence record carries the
  relevant UUIDs and hashes.
- Tool annotations are hints, not authorization. The server still enforces
  project policy, path containment, locks, hashes, revisions, capabilities,
  and evidence gates.

Common schema values:

```text
project_id       non-empty string, max 100 characters
map              non-empty project-relative map/artifact reference, max 400
UUID             RFC UUID string
SHA-256          exactly 64 hexadecimal characters
```

## Required workflow

For ordinary map work, use this order:

```text
wc3_project_status
  -> inspect/hash with read tools
  -> wc3_begin_transaction
  -> wc3_apply_operations (dry_run=true first when useful)
  -> wc3_transaction_diff
  -> wc3_validate_transaction
  -> wc3_build_map
  -> wc3_build_report
  -> optionally launch and record observed evidence
  -> promote or discard
```

Do not skip a hash, diff, validation, or exact-build check because a previous
call succeeded. A build is not editor-open, game-loaded, smoke-test, or
playtest evidence. Runtime evidence must be separately observed and recorded.

## Map inspection and comparison tools

These tools are read-only and do not create a transaction.

| Tool | Request contract | Result contract |
|---|---|---|
| `wc3_project_status` | `{ project_id }` | Configuration/dependency status, current source hash and drift, configured paths, active profile, capability matrix, and enabled/disabled tools. Call first. |
| `wc3_inspect_map` | `{ project_id, map, section?, include_provenance?, max_items_per_section? }`; defaults are `include_provenance=true`, `max_items_per_section=100`. | Exact map hash plus bounded canonical summary and a full inspection artifact. `section` may be one of the component names listed below. |
| `wc3_list_archive_files` | `{ project_id, map, prefix?, cursor?, max_items? }`; `max_items` defaults to 200. | Stable MPQ member metadata, member hashes, and parser capabilities. No member bytes or arbitrary filesystem paths. Cursors are tied to map hash and prefix; stale cursors return `CURSOR_STALE`. |
| `wc3_get_component` | `{ project_id, map, component, filter?, cursor?, max_items? }`; `max_items` defaults to 100. | One enumerated canonical component with typed values, provenance, capability, and pagination. Unsupported/opaque components return `UNSUPPORTED_COMPONENT` with the reason. |
| `wc3_get_script_source` | `{ project_id, map, archive_path?, expected_script_hash? }`; `archive_path` defaults to and must match `war3map.j`. | Exact JASS source text and its hash. `expected_script_hash` is an optimistic concurrency check and mismatches return `SOURCE_CHANGED`. |
| `wc3_validate_map` | `{ project_id, map }` | Read-only severity-coded findings, provenance/remediation, exact map hash, and a validation report artifact. |
| `wc3_compare_maps` | `{ project_id, left, right }` | Differences between two allowed maps/canonical JSON artifacts, separating archive-member changes from semantic gameplay changes. |

The `section`/`component` enum is:

```text
metadata, players, forces, teams, team_registry, regions, region_roles,
cameras, triggers, variables, gameplay_triggers, gameplay_variables,
gameplay_modules, trigger_mode, scripts, object_data, placed_objects,
terrain_summary, imports, archive_members, capabilities, component_status,
profiles, profile, opaque_members
```

## Global JASS and jassdoc tools

These four tools are project-independent, read-only, and always registered by
the server because they query the one local canonical dataset. They do not
mutate a map. Never invent a native/function name or signature from model
memory.

| Tool | Request contract | Result contract |
|---|---|---|
| `jass_search` | `{ query, limit? }`; query is 1-200 characters and limit is 1-50, default 10. | Ranked matches from canonical jassdoc by name, CamelCase tokens, parameters, docs, annotations, and source. Use when the exact API name is uncertain. |
| `jass_lookup` | `{ name }`; name must be a JASS identifier. | Exact native/Blizzard.j symbol metadata: declaration, kind, parameters/types, return type, docs, annotations, source, and related metadata. Unknown names return `found=false` and suggestions; suggestions are not proof of a valid name. |
| `jass_validate_call` | `{ function, arguments, local_source? }`; function is a JASS identifier and arguments are source-order expressions, at most 128. | `valid`, error/warning counts, structured issues, and resolved function metadata where available. Checks existence, argument count, confident type mismatches, annotations, canonical symbols, and optional local declarations. |
| `jass_validate_source` | `{ source, context_source? }`; each source string is 1-16 MiB. | Source-level `valid`, error/warning counts, and line/function/parameter issues against canonical jassdoc plus declarations in the supplied source/context. |

The local index is generated by
`tools/wc3-map-mcp/scripts/sync-jassdoc.ps1` from the pinned `lep/jassdoc`
commit. If it is absent, the server fails closed with `DEPENDENCY_MISSING`;
sync/bootstrap it rather than substituting a hand-written list. The generated
JSON and upstream checkout are intentionally ignored and must not be
committed. Script-producing transaction paths run the same validator before
publishing a revision.

## Global object-field tools

These two tools are project-independent, always registered, read-only and
backed by the engine's embedded offline catalog. Use search then exact lookup
before authoring unfamiliar object fields. See
[`object-field-authoring.md`](object-field-authoring.md) and the
[generated field reference](object-fields.generated.md).

| Tool | Request contract | Result contract |
|---|---|---|
| `wc3_object_field_lookup` | `{ field, category?, base_rawcode? }`; field is 1-200 nonblank characters, an exact case-sensitive ID or readable name. | `catalog_version`, `catalog_sha256`, `found`, `ambiguous`, `identifier_kind`, `field` (null unless uniquely resolved), `matches`, `suggestions`. Entries include native type/scope, base applicability, enum/flag values and pinned source evidence. Prefixes/object IDs are never silently resolved as fields. |
| `wc3_object_field_search` | `{ query, category?, base_rawcode?, limit? }`; query is 1-200 nonblank characters; limit is 1-50, default 10. | Ranked bounded `matches`, `total_matches`, `catalog_version`, `catalog_sha256`. Case-insensitive discovery does not change case-sensitive authoring. |

`category` is unit, ability, item, buff, upgrade, doodad or destructable.
`base_rawcode` is exactly four printable ASCII characters and filters direct
catalog applicability; it does not resolve custom ancestry without a map.
Catalog hash identifies the embedded metadata content. Evidence remains static.

Object inspection additionally returns separate bounded `object_fields`
annotations (category/rawcode/fields with id/type/value/scope and readable
name/description/expected_type/known), without changing canonical artifacts or
expected records. Apply and transaction diffs add `diff.object_fields` with
annotated before/after objects; revision-range output concatenates these in
revision order. Null before/after denotes creation/deletion.

## Transaction tools

Transaction tools are project-scoped and require a writes-enabled project unless
noted otherwise. The original source map is never modified.

| Tool | Request contract | Result and state contract |
|---|---|---|
| `wc3_begin_transaction` | `{ project_id, map, expected_source_hash, label? }` | Rehashes the allowed source, copies it into an isolated MCP-owned transaction, inspects that copy, and returns `transaction_id`, revision `0`, snapshot/source hashes, manifest paths, and versions. A stale source hash fails. |
| `wc3_apply_operations` | `{ project_id, transaction_id, expected_revision, operations, dry_run? }`; 1-100 operations, `dry_run` defaults false. | Applies a bounded atomic batch. Dry-run returns a diff without changing revision/canonical/manifest state. A real call creates the next revision, semantic diff artifact, applied operation IDs, and canonical hash. Stale revisions, duplicate operation IDs, source drift, schema errors, and failed operations leave the prior revision intact. |
| `wc3_transaction_diff` | `{ project_id, transaction_id, from_revision?, to_revision? }` | Reads grouped semantic changes and reference rewrites over the requested revision range. It does not change the transaction. |
| `wc3_validate_transaction` | `{ project_id, transaction_id, revision }` | Validates exactly that staged revision, writes a validation report, and sets state to `validated` only when `buildable=true`. Errors leave it `modified` and prevent building. |
| `wc3_discard_transaction` | `{ project_id, transaction_id, expected_source_hash, confirmation: true }` | Destructively removes only the matching MCP-owned transaction directory beneath the configured staging root and retains an audit tombstone. It cannot delete the source map, an arbitrary directory, or an accepted build. |

With `script_policy="disabled"`, begin preserves the inspected script/regions
and does not invoke gameplay composition. Native archive validation still
runs; logical profile validation is added when a staged profile/team/source
model exists. With `mcp_owned_jass`, begin currently seeds the configured
gameplay model and regenerated source into revision 0. That known limitation
means revision 0 is not necessarily a no-op relative to the source archive;
inspect its script hash before authoring expected values. See the
[engineering audit](../engineering-audit.md).

Duplicate operation IDs (case-insensitive) are rejected before worker execution.
Gameplay variable types include `framehandle` alongside `multiboard` and
`fogmodifier`; the supported JASS type allow-list remains closed.
Typed input accepts `array` (boolean) and `array_size` (1-8191), matching engine
source manifests; arrays cannot have scalar initial values. `framehandle`
supports scalar and array declarations under the existing handle validation
rules. Array size is manifest metadata: a size-5 HUD root emits
`framehandle array HTW_HudRoot`, without an inline `[5]` or initializer.
Every generated scalar global has a safe literal initializer: integer `0`,
real `0.`, boolean `false`, string `""`, or supported handle `null`. This includes
built-in/profile scalars and custom-event state. Authored scalar initial values
are still applied by `HTW_MCP_InitializeVariables` before bootstrap. No native
calls or handle creation run in global initializers. This changes composed
source/hash, not input schemas, handle input rules or transaction gates. Arrays
retain their existing declaration syntax and native default elements. See the
[v29 startup repair](../compatibility/v29-startup-initialization.md).
JASS source validation recognizes grouped lowercase `and`/`or` expressions while still
validating nested calls and canonical `And`/`Or` natives. These additions do not
change tool names, source-hash gates, or runtime-evidence requirements.
All source-generating module/trigger/variable operations require script policy,
including dry runs. Diffs include generated script hashes and derived registry
changes performed after the typed batch, attributed to its final operation
with `provenance="derived"`; full source stays in revision artifacts.

### `wc3_apply_operations` envelope

Every operation is a strict object with:

```json
{
  "operation_id": "UUID",
  "type": "closed operation enum",
  "target": {},
  "expected_revision": 0,
  "expected": "complete prior value or hash when required",
  "value": "typed requested value when required",
  "rationale": "non-empty explanation",
  "design_reference": "HTW-##"
}
```

`expected_revision` and `design_reference` are optional. `rationale` is
required and is limited to 2,000 characters. Create operations generally
require an absent `expected` value; updates/deletes require the complete
expected prior record or source hash. The closed typed operation families are:

- Map/player/force/team: `set_map_metadata`,
  `create_player_slot`, `set_player_slot`, `delete_player_slot`,
  `create_force`, `set_force`, `delete_force`, `create_team`, `set_team`,
  `delete_team`, `set_team_arena`, `set_team_members`.
- Regions: `create_region`, `update_region`, `rename_region`,
  `delete_region`, `reorder_regions`, `set_region_role`.
- Object definitions/references: `create_object_definition`,
  `update_object_definition`, `delete_object_definition`,
  `set_object_data`, `set_object_reference`.
- Placements: `place_object`, `place_unit`, `move_object`, `move_unit`,
  `update_placed_object`, `remove_placed_object`, `remove_placed_unit`.
- MCP-owned JASS and typed gameplay: `set_script_source`,
  `upsert_script_module`, `remove_script_module`, `set_trigger_mode`,
  `create_trigger`, `update_trigger`, `move_trigger`, `delete_trigger`,
  `create_variable`, `update_variable`, `delete_variable`.

Important typed preconditions include:

- `set_script_source` targets only `war3map.j`, expects its current SHA-256,
  and takes `{ language: "jass", source, source_strategy?: "composed" }`.
  Script mutation also requires project `script_policy="mcp_owned_jass"`.
- The v21 Controller kit uses level-scoped typed ability definitions and a
  typed helper unit, with MCP-owned spell dispatch. The conservative standard
  ability compatibility set includes `ANcl` (Channel) and `Aslo` (Slow).
  This extends supported object parents without changing operation schemas,
  tool names, transaction gates, or runtime-evidence requirements. See
  [`../compatibility/v21-controller-spells.md`](../compatibility/v21-controller-spells.md).
  The v22 attachment correction stores Controller skills in the typed `uhab`
  hero-ability field; see [`../compatibility/v22-h003-ability-attachment.md`](../compatibility/v22-h003-ability-attachment.md).
  The v23 metadata correction adds explicit hero flags, existing-game icons,
  and typed Channel fields; see [`../compatibility/v23-controller-ability-metadata.md`](../compatibility/v23-controller-ability-metadata.md).
- Object modification input is either raw `{ id, type, value, ...scope }` or
  named `{ field, value, ...scope }`. Named input resolves exact names/IDs and
  types from the catalog; raw and named selectors cannot be mixed. Named enum
  strings, flag-name arrays and rawcode-list arrays resolve to native values.
  Native level/variation is explicit; named pointers default from metadata.
  Updates replace the entire modification array, preserving unrelated records.
  Changed fields must match catalog category/type/base/scope/value rules;
  unknown fields must remain unchanged. Hero attachment checks run after the
  complete batch. Before building, source-relative validation repeats the gates.
  See the authoring contract for precise preservation and legacy behavior.
- Rawcodes are exactly four printable ASCII characters. Player IDs are 1-24;
  force indexes are 0-23; logical team IDs are `team_N`; region IDs are
  `region:N`; and placement targets use a stable ID or native creation number.
- Region renames require an explicit reference-rewrite plan. Existing region
  names must not be changed casually or inferred from colors/coordinates.
- Object, placement, player/force/team, trigger, variable, and module values
  must use their closed typed schemas. Do not emulate a generic archive patch.

## Build tools

| Tool | Request contract | Result and gate |
|---|---|---|
| `wc3_build_map` | `{ project_id, transaction_id, revision, expected_source_hash, profile?, label? }`; profile is `debug`, `release`, or `noop`, default `debug`. | Requires the exact transaction revision to be `validated`, source hash to be unchanged, and all staged/build preconditions to pass. Produces a unique map artifact, manifest, output hash, archive comparison, and reopen/reinspection evidence. Runtime status is always initially `untested`. |
| `wc3_build_report` | `{ project_id, build_id }` | Reopens the persisted build manifest, rehashes the exact output, and returns `verified=true` only when references still match. |
| `wc3_promote_build` | `{ project_id, build_id, expected_build_hash, destination_id: "test_map_root", destination_name }` | Requires the exact build hash and recorded `smoke_passed` or `playtest_passed` status. Copies to the configured test-map root under a simple `.w3m`/`.w3x` filename, refuses overwrite/source identity conflicts, rehashes the copy, and writes an audit artifact. |

Build output, reinspection, and model scenarios do not prove Warcraft III
runtime compatibility.

A missing/non-array semantic comparison is `ENGINE_PROTOCOL_ERROR`, not an
empty diff. Reports verify output size, hash and persisted reinspection identity.
Canonical object-definition lists compare by unique stable `id`, so regrouping
units/abilities by archive member on reopen is not a semantic change. Object
values and modification order still compare normally; missing/duplicate IDs
retain positional comparison. Archive order remains in archive evidence.
`engine_result.output_path` identifies the final artifact, not its removed
temporary directory.

## Launch and evidence tools

These tools do not automate user input inside World Editor or Warcraft III.
They launch a hash-checked artifact and persist an observation record.

| Tool | Request contract | Result and evidence contract |
|---|---|---|
| `wc3_launch_editor` | `{ project_id, build_id, expected_build_hash }` | Starts exactly the selected build in the configured editor, refuses an already-running target, and returns a hash-linked session with `evidence_level="process_started"`. Process start is not editor-open evidence. |
| `wc3_launch_test_map` | `{ project_id, build_id, expected_build_hash }` | Copies the selected build to the configured test root, verifies the copy hash, starts Warcraft III with the exact test copy, and returns a session at `process_started`. Existing processes are never killed. |
| `wc3_record_test_result` | `{ project_id, session_id, expected_build_hash, milestone, result, recorder, notes, artifacts? }`; milestone is `editor_opened`, `game_loaded`, `smoke_test`, or `playtest`; result is `pass`/`fail`; recorder is `user_observation`/`agent_log_observation`. | Attaches one ordered result to the matching session/build. Passing milestones advance evidence; failures remain failures. The exact build/session/hash must match. The editor sequence permits `editor_opened`; the game sequence is `game_loaded -> smoke_test -> playtest`. |
| `wc3_get_test_session` | `{ project_id, session_id }` | Revalidates the persisted session, selected build, test copy, launch argument array, and evidence artifact hashes before returning `verified=true`. |

Required manual acceptance gates are separate: normal-menu lobby slot
observation, game load, on-camera gameplay/smoke behavior, and camera movement
must not be inferred from process state, static parsing, or a launch call.

## Gameplay-source and scenario tools

These tools implement MCP-native JASS gameplay for the `mvp_2arena` and
`full_6team` profiles. `gui_compatible` remains gated. They preserve the same
transaction, source-hash, revision, build, and evidence contracts.

| Tool | Request contract | Result and gate |
|---|---|---|
| `wc3_compose_gameplay_source` | `{ project_id, manifest_path, profile?, expected_manifest_sha256?, expected_module_hashes? }`; manifest must be inside a configured gameplay source root. | Read-only deterministic composition. Returns generated source/hash, module and manifest hashes, symbol/trigger/variable metadata, static validation, and source/manifest artifacts. It does not mutate a map. |
| `wc3_validate_gameplay_source` | Same request shape as compose. | Read-only validation of module paths/dependencies, symbols, typed triggers/variables/references, selected profile, and generated JASS. Returns a buildable/static result; it does not claim runtime evidence. |
| `wc3_prepare_gameplay_chunk` | `{ project_id, manifest_path, profile?, expected_manifest_sha256?, expected_module_hashes?, transaction_id, expected_revision, chunk_id }`; `chunk_id` must be `HTW-##`. | Composes reviewed source, verifies current transaction canonical/source hashes, and stages one `set_script_source` operation into the requested next revision. Returns transaction/revision IDs, operation, input hashes, and generated-source artifacts. |
| `wc3_run_scenario_build` | `{ project_id, transaction_id, revision, expected_source_hash, chunk_id, scenario_ids?, profile? }`; chunk is `HTW-##`, profile is `mvp_2arena` or `full_6team`, default `mvp_2arena`. | Builds the exact validated revision and runs deterministic model-level scenarios tied to the build hash. Returns build and scenario-report artifacts with `evidence_level="static_only"` and `runtime_verified=false`. `HTW-06` requires `full_6team`. |
| `wc3_record_chunk_result` | `{ project_id, chunk_id, scenario_id, transaction_id, revision, build_id, expected_build_hash, result, evidence_level?, test_session_id?, notes }`; evidence level defaults to `static_only` and may be `user_observed`. | Verifies the exact build/transaction/revision and writes linked chunk evidence. `user_observed` requires an exact `test_session_id` for that build; only then can `runtime_verified` be true. |

Preparation forwards `expected_manifest_sha256` and `expected_module_hashes`;
mismatches return `PRECONDITION_FAILED` before staging. It also compares current
manifest/module-file identities to the hash-verified initial transaction
revision and returns `SOURCE_CHANGED` for disk drift. Typed edits to staged
modules/variables remain valid even after regenerated metadata drops the
original manifest field. A stale requested revision is `PRECONDITION_FAILED`.

User-observed chunk evidence requires an exact **game** session whose latest
milestone is a matching pass/fail `user_observation` of `smoke_test` or
`playtest`, or a failed `game_loaded` observation. Process-only sessions,
editor sessions, successful load alone, log-only observations and conflicting
results return `PRECONDITION_FAILED`. The service also rejects a missing
session below the schema boundary with `PRECONDITION_REQUIRED`. A verified
failed observation retains `result="fail"`; verification does not mean pass.

Each result is a new artifact at
`gameplay/results/<build_id>-<chunk_id>-<UUID>.json` beneath the artifact root.
Use the returned artifact path; retries never replace earlier failures, and
`scenario_id` is descriptive data rather than part of the filename.

Composition and scenario artifacts are useful static evidence, but never
upgrade themselves to editor-open, game-loaded, smoke, or playtest evidence.

## Availability and policy gates

- `.codex/config.toml` must allow-list all 30 names for Codex visibility. The
  four `jass_*` tools and two `wc3_object_field_*` tools are global and bypass
  project map allow-list entries. JASS requires the local jassdoc dataset;
  object fields use the embedded catalog.
- `write_policy="read_only"` exposes only read/validation tools. Writes,
  builds, launches, evidence mutation, promotion, and discard require a
  writes-enabled project configuration.
- MCP-owned script changes require `script_policy="mcp_owned_jass"`.
  Capability member/operation `enabled` flags reflect the write policy and
  apply-tool allow-list; module/trigger/variable operations also reflect script
  policy. Static parser capability is separate from permission to mutate.
  GUI-trigger compatibility is not enabled merely because a trigger is
  inspectable; `war3map.wtg` and `war3map.wct` remain opaque/gated.
- Profile capability is explicit. `mvp_2arena` is the approved two-team
  profile, `full_6team` enables the six-team extension, and `gui_compatible`
  is gated pending exact serializer and editor evidence.
- Errors are stable, structured, and actionable. Important codes include
  `INVALID_ARGUMENT`, `PATH_OUTSIDE_ROOT`, `FILE_NOT_FOUND`, `SOURCE_CHANGED`,
  `UNSUPPORTED_COMPONENT`, `SCRIPT_MUTATION_DISABLED`, `PARSE_FAILED`,
  `VALIDATION_FAILED`, `BUILD_FAILED`, `BUILD_UNSUPPORTED`,
  `BUILD_REOPEN_MISMATCH`, `ENGINE_UNAVAILABLE`, `ENGINE_PROTOCOL_ERROR`,
  `TRANSACTION_STATE`, `PRECONDITION_FAILED`, `PRECONDITION_REQUIRED`,
  `CURSOR_STALE`, `LOCKED`, `LAUNCH_FAILED`, `PROMOTION_FAILED`,
  `CAPABILITY_GATED`, `DISCARD_FAILED`, `OUTPUT_EXISTS`, and `INTERNAL_ERROR`.
  Object-field errors include `OBJECT_FIELD_UNKNOWN`,
  `OBJECT_FIELD_TYPE_MISMATCH`, `OBJECT_FIELD_BASE_MISMATCH`,
  `OBJECT_FIELD_SCOPE_INVALID`, `OBJECT_FIELD_VALUE_INVALID`,
  `OBJECT_FIELD_DUPLICATE`, and `OBJECT_HERO_SKILL_INVALID`.

For exact field-level validation, inspect the closed Zod schemas and the
versioned JSON schemas under `tools/wc3-map-mcp/contracts/schemas/`. For
runtime/manual boundaries, follow `../../AGENTS.md`, `../agent-guide.md`, and
`safety-recovery-and-audit.md`.

Controller v25 uses the expanded 60-field offline catalog for QWER cast/learn
hotkeys, button positions, learn icons/tooltips, rank and technology requirements,
and Channel disable-other-abilities. Hotkeys serialize as native strings; tool
names, request schemas, and transaction/evidence gates are unchanged.
