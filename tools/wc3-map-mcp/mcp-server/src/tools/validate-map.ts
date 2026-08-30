import type { McpServer } from "@modelcontextprotocol/server";
import { correlationId } from "../schemas/common.js";
import { validateMapSchema } from "../schemas/tools.js";
import type { InspectionService } from "../services/inspection-service.js";
import { safeCall } from "./response.js";

export function registerValidateMap(server: McpServer, inspections: InspectionService): void {
  const registerTool = server.registerTool.bind(server) as (...args: any[]) => void;
  registerTool("wc3_validate_map", {
    description: "Validate an allowed map read-only and return severity-coded findings, provenance, remediation, an exact map hash, and a JSON report artifact.",
    inputSchema: validateMapSchema as never,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => inspections.validateMap(input.project_id, input.map, id));
  });
}
