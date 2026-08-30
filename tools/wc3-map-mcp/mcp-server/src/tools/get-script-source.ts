import type { McpServer } from "@modelcontextprotocol/server";
import { correlationId } from "../schemas/common.js";
import { getScriptSourceSchema } from "../schemas/tools.js";
import type { InspectionService } from "../services/inspection-service.js";
import { safeCall } from "./response.js";

export function registerGetScriptSource(server: McpServer, inspections: InspectionService): void {
  const registerTool = server.registerTool.bind(server) as (...args: any[]) => void;
  registerTool("wc3_get_script_source", {
    description: "Read the exact MCP-managed JASS source from an allowed map. Returns the source text and hash; pass expected_script_hash to fail if the map changed.",
    inputSchema: getScriptSourceSchema as never,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => inspections.getScriptSource(input.project_id, input.map, input.archive_path, input.expected_script_hash, id));
  });
}
