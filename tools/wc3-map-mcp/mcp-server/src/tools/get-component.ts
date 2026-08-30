import type { McpServer } from "@modelcontextprotocol/server";
import { correlationId } from "../schemas/common.js";
import { getComponentSchema } from "../schemas/tools.js";
import type { InspectionService } from "../services/inspection-service.js";
import { safeCall } from "./response.js";

export function registerGetComponent(server: McpServer, inspections: InspectionService): void {
  const registerTool = server.registerTool.bind(server) as (...args: any[]) => void;
  registerTool("wc3_get_component", {
    description: "Read one enumerated canonical component with capability and provenance. Opaque components return UNSUPPORTED_COMPONENT and a reason.",
    inputSchema: getComponentSchema as never,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => inspections.getComponent(input.project_id, input.map, input.component, input.filter, input.cursor, input.max_items, id));
  });
}
