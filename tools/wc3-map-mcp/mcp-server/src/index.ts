import { serveStdio } from "@modelcontextprotocol/server/stdio";
import { createServer } from "./server.js";
import { loadConfig } from "./config/load-config.js";

try {
  const config = loadConfig();
  void serveStdio(() => createServer(config));
  console.error("wc3-map-mcp listening on stdio");
} catch (error) {
  console.error(`wc3-map-mcp startup failed: ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
}
