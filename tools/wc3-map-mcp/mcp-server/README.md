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

Read-only tools are always available. Mutation, build, launch, evidence, promotion, and discard tools are registered only when the configuration is not `read_only`.
