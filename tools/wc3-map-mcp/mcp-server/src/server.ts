import { McpServer } from "@modelcontextprotocol/server";
import { loadConfig } from "./config/load-config.js";
import type { Wc3Config } from "./config/schema.js";
import { WorkerClient } from "./transport/worker-client.js";
import { ProjectService } from "./services/project-service.js";
import { InspectionService } from "./services/inspection-service.js";
import { TransactionService } from "./services/transaction-service.js";
import { BuildService } from "./services/build-service.js";
import { LaunchService } from "./services/launch-service.js";
import { registerTools } from "./tools/register-tools.js";

export function createServer(config: Wc3Config = loadConfig()): McpServer {
  const worker = new WorkerClient(config);
  const projects = new ProjectService(config, worker);
  const inspections = new InspectionService(projects, worker);
  const transactions = new TransactionService(projects, worker);
  const builds = new BuildService(projects, worker, transactions);
  const launches = new LaunchService(builds);
  const server = new McpServer({ name: "wc3-map-mcp", version: "0.1.0" }, { instructions: "Inspect before changing. The original map is immutable. Mutations require a transaction tied to the inspected source hash. Call transaction_diff and validate_transaction before build_map. A build is untested until a separate editor/game result is recorded. Never infer unknown map values or team identity from player color. Destructive tools may remove only an MCP-owned transaction directory." });
  registerTools(server, { config, projects, inspections, transactions, builds, launches });
  return server;
}
