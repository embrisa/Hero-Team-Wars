import type { McpServer } from "@modelcontextprotocol/server";
import { correlationId } from "../schemas/common.js";
import { compareMapsSchema } from "../schemas/tools.js";
import type { InspectionService } from "../services/inspection-service.js";
import { safeCall } from "./response.js";

export function registerCompareMaps(server: McpServer, inspections: InspectionService): void {
  const registerTool = server.registerTool.bind(server) as (...args: any[]) => void;
  registerTool("wc3_compare_maps", {
    description: "Compare two allowed map/canonical artifacts and report member-content differences separately from semantic gameplay differences.",
    inputSchema: compareMapsSchema as never,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => inspections.compareMaps(input.project_id, input.left, input.right, id));
  });
}
