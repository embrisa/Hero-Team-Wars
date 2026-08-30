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
  projects.validateStartup();
  const inspections = new InspectionService(projects, worker);
  const transactions = new TransactionService(projects, worker);
  const builds = new BuildService(projects, worker, transactions);
  const launches = new LaunchService(builds);
  const server = new McpServer({ name: "wc3-map-mcp", version: "0.1.0" }, { instructions: "Call wc3_project_status first, then inspect the exact source hash. The original map is immutable: mutations require a transaction tied to that hash. Review wc3_transaction_diff and validate the exact revision before any build. Unsupported values remain unknown, and destructive actions may remove only one confirmed MCP-owned transaction directory." });
  registerTools(server, { config, projects, inspections, transactions, builds, launches });
  return server;
}
