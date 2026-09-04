# MCP Server

TypeScript MCP STDIO process responsible for tool schemas, project/path policy, transaction coordination, worker lifecycle, and normalized responses. Binary WC3 parsing remains in the .NET worker.

Expected source layout:

```text
src/
  index.ts
  server.ts
  config/
  tools/
  services/
  schemas/
  transport/
  errors/
```

No Warcraft III binary parsing belongs in this component. Run `npm run build`, then start the built entry point with `node dist/index.js`; configure it with `WC3_MAP_MCP_CONFIG` or the checked-in example configuration.

The engine's v21 Controller kit supports Channel (`ANcl`) and Slow (`Aslo`)
as typed ability parents. No MCP request/response schema, tool registration,
or promotion evidence gate changed. See
[`../docs/compatibility/v21-controller-spells.md`](../docs/compatibility/v21-controller-spells.md)
for kit definitions and the exact-artifact verification procedure.
The v22 follow-up stores those skills in H003's hero-ability field; see
[`../docs/compatibility/v22-h003-ability-attachment.md`](../docs/compatibility/v22-h003-ability-attachment.md).

The four global JASS knowledge tools are always registered because they query the single local canonical dataset rather than a project map. `jass_search` discovers real APIs by concept, `jass_lookup` returns exact signatures and documentation, and the two validation tools check calls/source without mutation. Unknown names remain errors or unknown lookup results; fuzzy matches are suggestions only.

The checked-in example configuration is `read_only` and allow-lists exactly seven tools: `wc3_project_status`, `wc3_inspect_map`, `wc3_list_archive_files`, `wc3_get_component`, `wc3_get_script_source`, `wc3_validate_map`, and `wc3_compare_maps`. In that mode mutation, build, launch, evidence, promotion, and discard tools are not registered. A separately approved project configuration with `write_policy: "writes"` can enable the transaction tools; set `script_policy: "mcp_owned_jass"` to enable gameplay-source mutation, while explicit allow-lists remain enforced per project.

Gameplay-source workflow: call `wc3_get_script_source` to obtain the current complete `war3map.j` text and SHA-256, begin a transaction with that map hash, then apply a `set_script_source` operation whose `target` is `{ "archive_path": "war3map.j" }`, whose `expected` is the script SHA-256, and whose `value` is `{ "language": "jass", "source": "<complete source>" }`. Validate and build the transaction before launching or promoting its uniquely named artifact. The source map is never overwritten.

Never invent JASS API names or rely on memory for signatures. Generated and direct JASS replacements are checked against jassdoc plus declarations in the complete staged source before transaction state changes. Correct validation errors and retry; the server never silently substitutes a fuzzy match.

Keep the agent-facing contract catalog current when this server changes:
`../docs/reference/tool-contracts.md`. Changes to registrations,
Zod schemas, normalized responses, errors, policy gates, or worker behavior must
update the catalog and any affected versioned contract schema/README in the
same change, followed by a registration/schema/documentation consistency check.
