import type { McpServer } from "@modelcontextprotocol/server";
import { correlationId } from "../schemas/common.js";
import { inspectMapSchema } from "../schemas/tools.js";
import type { InspectionService } from "../services/inspection-service.js";
import { safeCall } from "./response.js";

export function registerInspectMap(server: McpServer, inspections: InspectionService): void {
  const registerTool = server.registerTool.bind(server) as (...args: any[]) => void;
  registerTool("wc3_inspect_map", {
    description: "Inspect an allowed source map at its exact hash and return a bounded canonical summary plus a full JSON artifact. Call wc3_project_status first.",
    inputSchema: inspectMapSchema as never,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => inspections.inspect(input.project_id, input.map, input.section, input.include_provenance, input.max_items_per_section, id));
  });
}
