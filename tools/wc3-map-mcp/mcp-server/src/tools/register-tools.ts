import type { McpServer } from "@modelcontextprotocol/server";
import { InspectionService } from "../services/inspection-service.js";
import { ProjectService } from "../services/project-service.js";
import { TransactionService } from "../services/transaction-service.js";
import { BuildService } from "../services/build-service.js";
import { LaunchService } from "../services/launch-service.js";
import type { Wc3Config } from "../config/schema.js";
import { registerProjectStatus } from "./project-status.js";
import { registerInspectMap } from "./inspect-map.js";
import { registerListArchiveFiles } from "./list-archive-files.js";
import { registerGetComponent } from "./get-component.js";
import { registerValidateMap } from "./validate-map.js";
import { registerCompareMaps } from "./compare-maps.js";
import { registerTransactionTools } from "./transactions.js";
import { registerBuildTools } from "./builds.js";

export interface ToolServices {
  config: Wc3Config;
  projects: ProjectService;
  inspections: InspectionService;
  transactions: TransactionService;
  builds: BuildService;
  launches: LaunchService;
}

export function registerTools(server: McpServer, services: ToolServices): void {
  const phaseOneReadOnly = new Set(["wc3_project_status", "wc3_inspect_map", "wc3_list_archive_files", "wc3_get_component", "wc3_validate_map", "wc3_compare_maps"]);
  const enabled = (name: string): boolean => Object.values(services.config.projects).some(project => {
    if (project.write_policy === "read_only" && !phaseOneReadOnly.has(name)) return false;
    return project.enabled_tools.length === 0 || project.enabled_tools.includes(name);
  });
  const register = (name: string, config: Record<string, unknown>, handler: (input: any) => Promise<Record<string, unknown>>): void => {
    if (enabled(name)) server.registerTool(name, config as never, handler as never);
  };

  if (enabled("wc3_project_status")) registerProjectStatus(server, services.projects);
  if (enabled("wc3_inspect_map")) registerInspectMap(server, services.inspections);
  if (enabled("wc3_list_archive_files")) registerListArchiveFiles(server, services.inspections);
  if (enabled("wc3_get_component")) registerGetComponent(server, services.inspections);
  if (enabled("wc3_validate_map")) registerValidateMap(server, services.inspections);
  if (enabled("wc3_compare_maps")) registerCompareMaps(server, services.inspections);

  registerTransactionTools(server, services.transactions, enabled);
  registerBuildTools(register, services.builds, services.launches);
}
