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

The checked-in example configuration is `read_only` and allow-lists exactly seven tools: `wc3_project_status`, `wc3_inspect_map`, `wc3_list_archive_files`, `wc3_get_component`, `wc3_get_script_source`, `wc3_validate_map`, and `wc3_compare_maps`. In that mode mutation, build, launch, evidence, promotion, and discard tools are not registered. A separately approved project configuration with `write_policy: "writes"` can enable the transaction tools; set `script_policy: "mcp_owned_jass"` to enable gameplay-source mutation, while explicit allow-lists remain enforced per project.

Gameplay-source workflow: call `wc3_get_script_source` to obtain the current complete `war3map.j` text and SHA-256, begin a transaction with that map hash, then apply a `set_script_source` operation whose `target` is `{ "archive_path": "war3map.j" }`, whose `expected` is the script SHA-256, and whose `value` is `{ "language": "jass", "source": "<complete source>" }`. Validate and build the transaction before launching or promoting its uniquely named artifact. The source map is never overwritten.
