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
import { registerGetScriptSource } from "./get-script-source.js";
import { registerValidateMap } from "./validate-map.js";
import { registerCompareMaps } from "./compare-maps.js";
import { registerTransactionTools } from "./transactions.js";
import { registerBuildTools } from "./builds.js";
import { registerLaunchTools } from "./launches.js";
import { registerGameplayTools } from "./gameplay.js";
import { GameplayService } from "../services/gameplay-service.js";
import { isToolEnabledForProject } from "../services/capability-catalog.js";

export interface ToolServices {
  config: Wc3Config;
  projects: ProjectService;
  inspections: InspectionService;
  transactions: TransactionService;
  builds: BuildService;
  launches: LaunchService;
  gameplay: GameplayService;
}

export function registerTools(server: McpServer, services: ToolServices): void {
  // MCP tools are registered at server scope, while project/profile is a
  // request field. Register only the intersection supported by every
  // configured project; otherwise one project's capability would make a
  // tool appear callable for another project's profile.
  const enabled = (name: string): boolean => Object.values(services.config.projects).length > 0
    && Object.values(services.config.projects).every(project => isToolEnabledForProject(project, name));
  const register = (name: string, config: Record<string, unknown>, handler: (input: any) => Promise<Record<string, unknown>>): void => {
    if (enabled(name)) server.registerTool(name, config as never, handler as never);
  };

  if (enabled("wc3_project_status")) registerProjectStatus(server, services.projects);
  if (enabled("wc3_inspect_map")) registerInspectMap(server, services.inspections);
  if (enabled("wc3_list_archive_files")) registerListArchiveFiles(server, services.inspections);
  if (enabled("wc3_get_component")) registerGetComponent(server, services.inspections);
  if (enabled("wc3_get_script_source")) registerGetScriptSource(server, services.inspections);
  if (enabled("wc3_validate_map")) registerValidateMap(server, services.inspections);
  if (enabled("wc3_compare_maps")) registerCompareMaps(server, services.inspections);

  registerTransactionTools(server, services.transactions, enabled);
  registerBuildTools(register, services.builds);
  registerLaunchTools(register, services.launches);
  registerGameplayTools(services.gameplay, register);
}
