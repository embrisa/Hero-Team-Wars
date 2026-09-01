import { McpServer } from "@modelcontextprotocol/server";
import { loadConfig } from "./config/load-config.js";
import type { Wc3Config } from "./config/schema.js";
import { WorkerClient } from "./transport/worker-client.js";
import { ProjectService } from "./services/project-service.js";
import { InspectionService } from "./services/inspection-service.js";
import { TransactionService } from "./services/transaction-service.js";
import { BuildService } from "./services/build-service.js";
import { LaunchService } from "./services/launch-service.js";
import { GameplayService } from "./services/gameplay-service.js";
import { JassService } from "./services/jass-service.js";
import { registerTools } from "./tools/register-tools.js";

export function createServer(config: Wc3Config = loadConfig()): McpServer {
  const worker = new WorkerClient(config);
  const projects = new ProjectService(config, worker);
  projects.validateStartup();
  const inspections = new InspectionService(projects, worker);
  const transactions = new TransactionService(projects, worker);
  const builds = new BuildService(projects, worker, transactions);
  const launches = new LaunchService(builds);
  const gameplay = new GameplayService(projects, worker, transactions, builds, launches);
  const jass = new JassService(worker);
  const server = new McpServer({ name: "wc3-map-mcp", version: "0.1.0" }, { instructions: "Inspect status and source hash first. The original map is immutable; mutations require a hash-tied transaction. Review the transaction diff and validate before building. For JASS, never invent names/signatures: use jass_search when unsure and jass_lookup for exact declarations/docs; jassdoc is canonical. Generated JASS is validated before map writes—correct failures and retry. Unsupported values stay unknown; destructive actions are limited to one confirmed MCP-owned transaction directory." });
  registerTools(server, { config, projects, inspections, transactions, builds, launches, gameplay, jass });
  return server;
}
