import type { McpServer } from "@modelcontextprotocol/server";
import { correlationId } from "../schemas/common.js";
import { projectStatusSchema } from "../schemas/tools.js";
import type { ProjectService } from "../services/project-service.js";
import { safeCall } from "./response.js";

export function registerProjectStatus(server: McpServer, projects: ProjectService): void {
  const registerTool = server.registerTool.bind(server) as (...args: any[]) => void;
  registerTool("wc3_project_status", {
    description: "Call this first. Report read-only project, source-hash, dependency, version, and capability status without parsing or changing a map.",
    inputSchema: projectStatusSchema as never,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => safeCall(correlationId(), () => projects.status(input.project_id)));
}
