# v29 JASS startup initialization repair

On 2026-09-09, after the v28 handoff, the user reported that the DEV HUD did not
appear and chat commands did nothing. That is a failed user playtest report;
v28's automated pass is not evidence that its developer controls worked in
Warcraft. The report was made outside an MCP-tracked game session, so no
session milestone or promotion evidence is fabricated.
The user clarified that the regular HUD worked and `-dev` was completely
silent. Do not reinterpret that as a reported failure of the regular HUD.

## Cause and correction

The exact v28 archive contains these scalar declarations without values:

```jass
trigger HTW_DevChatTrigger
trigger HTW_DevUILoad
trigger HTW_HudLoadTrigger
```

Their first uses compare them with `null`. Neither map setup nor
`HTW_MCP_InitializeVariables` assigns them first. Reading an uninitialized JASS
scalar can terminate the executing thread. `HTW_Dev_Initialize` therefore has
a failure point before chat registration. The regular HUD also has an
uninitialized trigger guard before its frame loop; v29 makes that guard
explicitly initialized without changing its behavior or layout. These are
source-confirmed initialization defects. The user's regular-HUD observation
is retained; the exact cause in their session has not been instrumented directly.

The old source harness silently assigned default values to missing scalar
initializers, including locals. It therefore masked the missing generated
initialization. The engine's canonical JASS validation checks symbols and types;
it does not prove definite assignment on every execution path.

Composer `mcp-jass-composer-2.5` now gives every generated scalar a safe literal in `globals`:
integer `0`, real `0.`, boolean `false`, string `""`, and supported handles
`null`. This covers generated built-ins, custom-event scalars and typed
manifest variables. It creates no handles and calls no functions at global
scope. Arrays remain bare declarations with native default elements; their
manifest size is metadata. Explicit authored scalar initial values still run
in `HTW_MCP_InitializeVariables`, in the existing order before bootstrap.

The JASS behavior is independently documented by an author's
[reproduction and resolution](https://www.hiveworkshop.com/threads/function-seems-to-just-stop-executing-after-a-few-lines.316280/)
and the [language behavior notes](https://www.hiveworkshop.com/threads/things-to-know-in-jass.240564/).
These explain the language semantics; they are not observations of this map.

The source harness now distinguishes missing values from explicit `null`,
rejects uninitialized scalar/global/local reads, and retains array defaults.
Generated-source startup checks consume actual `war3map.j` declarations and
initialization rather than filling missing values from the manifest. The v29
build driver checks both staged and reopened source. A negative regression
removes the trigger initializers to reproduce v28's startup failure.

The repaired map also prints `Dev: v29 controls ready. Click DEV or type -dev
help.` after solo chat registration. `-dev` reports menu opened/closed in text,
so command acknowledgement can be distinguished from rendered frame behavior.
Command effects, solo eligibility, phase timings and gameplay rules are unchanged.

## Contracts and preservation

This changes generated JASS and its composer version/hash. MCP tool names,
request/response fields, typed variable schemas, handle initial-value input
rules, policy gates and runtime evidence requirements are unchanged. Existing
transaction/source hashes remain immutable; use a fresh transaction with the
newly published engine rather than editing an old snapshot.

`scripts/build-v29-startup-repair.mjs` consumes the exact v28 build. Run it from
the repository root with `stage <fresh-evidence-directory>` under
`tools/wc3-map-mcp/artifacts/`, review saved evidence, then run `build` on that
directory. `rebuild` retries only an already applied revision. The driver checks
all global identities/types/array flags, every new literal initializer, and
all function bodies except the two dev acknowledgement changes. No function
may be added or removed. Object data and all other inspected components must
match v28; only `war3map.j` and `(attributes)` may change in the archive.

Revision 0 still includes implicit composition. Its empty operation diff must
be read alongside the explicit v28-to-composed source checks and final archive
comparison. Golden source and v28 are hash-checked before and after the build.

## Published build and automated evidence

Published on 2026-09-09 after inspecting the existing version folders and
selecting the unused `v29` folder:

- Folder: `C:\Users\hp\Documents\Warcraft III\Maps\Test\v29\`
- Map: `HeroTeamWars_v29.w3m` (84,390 bytes).
- SHA-256: `43D964B6B11C4DAD81A08E70DA9C631CC1681640756321D63BD9122749A16DC3`.
- Build: `961912a1-a70c-43fc-89c0-5f01d211a1a2`, verified build report.
- Transaction: `0b555666-8d4d-4fa8-bf64-982c4c545706`, revision 1.
- Built script SHA-256: `0DB098F93A70E39B12467B1BC6D241D849CC58B94B515C2BE774CB1D5EB2DF11`.
- Evidence directory: `tools/wc3-map-mcp/artifacts/v29-startup-20260909-a/`
  contains the MCP responses, startup checks, `handoff.json` and `publication.json`.

The source suites passed 50/50 tests; the full engine suite passed 131/131;
the MCP suite passed 70/70 against the freshly published engine. Send catalog
generation matched its checked-in files. Canonical JASS validation passed with
zero errors. Transaction/build validation, archive comparison and reinspection
passed. The published copy matches the verified build hash.

The staged and reopened scripts each contain 69 initialized scalars and 41
bare arrays. The generated declarations and initialization passed the strict
mocked-native startup checker: four chat registrations, regular/dev roots,
two menu toggles, and one gold update each through chat and button dispatch.
The exact v28 source was rejected for its missing scalar initializers. Only
`war3map.j` and `(attributes)` changed; all object data and other inspected map
components were preserved. Golden source and the v28 artifact remain unchanged.

These are automated source/build results. The v29 map has not been launched,
promoted or verified inside Warcraft by the agent.

## Manual verification

The user owns Warcraft testing. For the new map, verify the v29 ready message,
regular icon HUD and DEV button; type `-dev` and check both text acknowledgement
and the panel. `-dev gold 5000` should report the addition and update gold.
Then check a phase jump, a button action, hover help, and save/load. Automated
mocked-native tests cannot prove the rendered UI, native event delivery or
save/load behavior. The repaired build remains runtime-unverified until the
user reports it.
