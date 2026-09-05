import type { McpServer } from "@modelcontextprotocol/server";
import { correlationId } from "../schemas/common.js";
import { objectFieldLookupSchema, objectFieldSearchSchema } from "../schemas/tools.js";
import type { ObjectFieldService } from "../services/object-field-service.js";
import { safeCall } from "./response.js";

export function registerObjectFieldTools(server: McpServer, fields: ObjectFieldService): void {
  const register = server.registerTool.bind(server) as (...args: any[]) => void;
  register("wc3_object_field_lookup", {
    description: "Look up an exact case-sensitive object field ID or readable name in the local pinned catalog. Returns types, native scope, base applicability, enum/flag values and evidence. Object IDs and incomplete prefixes are not field IDs; unknown/ambiguous results never authorize edits.",
    inputSchema: objectFieldLookupSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => { const id = correlationId(); return safeCall(id, () => fields.lookup(input, id)); });
  register("wc3_object_field_search", {
    description: "Search object-data fields by readable names, rawcodes, prefixes and descriptions (for example learnable hero skills or Channel target type). Use lookup on the exact result before writing unfamiliar fields. The catalog is offline and does not claim runtime verification.",
    inputSchema: objectFieldSearchSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => { const id = correlationId(); return safeCall(id, () => fields.search(input, id)); });
}
