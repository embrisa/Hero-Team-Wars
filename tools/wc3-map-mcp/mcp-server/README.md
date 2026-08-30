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

The checked-in Phase 1 configuration is `read_only` and allow-lists exactly six tools: `wc3_project_status`, `wc3_inspect_map`, `wc3_list_archive_files`, `wc3_get_component`, `wc3_validate_map`, and `wc3_compare_maps`. Mutation, build, launch, evidence, promotion, and discard tools are not registered in that mode.
