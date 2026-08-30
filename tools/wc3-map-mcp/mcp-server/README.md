# MCP Server

Planned TypeScript process responsible for MCP STDIO transport, tool schemas, project/path policy, transaction coordination, worker lifecycle, and normalized responses.

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

No Warcraft III binary parsing belongs in this component.
