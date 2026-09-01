import type { McpServer } from "@modelcontextprotocol/server";
import { correlationId } from "../schemas/common.js";
import * as schemas from "../schemas/tools.js";
import type { JassService } from "../services/jass-service.js";
import { safeCall } from "./response.js";

/** Register global canonical JASS knowledge tools, independent of project allow-lists. */
export function registerJassTools(server: McpServer, jass: JassService): void {
  const registerTool = server.registerTool.bind(server) as (...args: any[]) => void;

  registerTool("jass_lookup", {
    description: "Look up an exact Warcraft III JASS native or Blizzard.j function in the canonical local jassdoc dataset. Unknown symbols are reported as unknown with generic suggestions; never infer or fabricate a signature.",
    inputSchema: schemas.jassLookupSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => jass.lookup(input.name, id));
  });

  registerTool("jass_search", {
    description: "Search the canonical local jassdoc dataset by JASS names, CamelCase tokens, parameters, documentation, annotations, and source. Use this when the exact API name is uncertain.",
    inputSchema: schemas.jassSearchSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => jass.search(input.query, input.limit, id));
  });

  registerTool("jass_validate_call", {
    description: "Validate one JASS call against canonical jassdoc symbols plus optional local declarations, including existence, argument count, confident type mismatches, and relevant warnings.",
    inputSchema: schemas.jassValidateCallSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => jass.validateCall(input.function, input.arguments, input.local_source, id));
  });

  registerTool("jass_validate_source", {
    description: "Validate a JASS source block against canonical jassdoc symbols and declarations found in the supplied source/context. Unknown functions and confident argument errors are reported before map writes.",
    inputSchema: schemas.jassValidateSourceSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => jass.validateSource(input.source, input.context_source, id));
  });
}
