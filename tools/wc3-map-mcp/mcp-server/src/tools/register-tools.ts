import type { McpServer } from "@modelcontextprotocol/server";
import { correlationId } from "../schemas/common.js";
import * as schemas from "../schemas/tools.js";
import { InspectionService } from "../services/inspection-service.js";
import { ProjectService } from "../services/project-service.js";
import { TransactionService } from "../services/transaction-service.js";
import { BuildService } from "../services/build-service.js";
import { LaunchService } from "../services/launch-service.js";
import { safeCall } from "./response.js";
import type { Wc3Config } from "../config/schema.js";
import { AppError } from "../errors/app-error.js";

export interface ToolServices {
  config: Wc3Config;
  projects: ProjectService;
  inspections: InspectionService;
  transactions: TransactionService;
  builds: BuildService;
  launches: LaunchService;
}

export function registerTools(server: McpServer, services: ToolServices): void {
  const enabled = (name: string): boolean => services.config.projects[Object.keys(services.config.projects)[0] ?? ""]?.enabled_tools.length === 0 || Object.values(services.config.projects).some(project => project.enabled_tools.includes(name));
  const readOnly = services.config.projects[Object.keys(services.config.projects)[0] ?? ""]?.write_policy === "read_only";
  const register = (name: string, config: Record<string, unknown>, handler: (input: any) => Promise<Record<string, unknown>>): void => {
    if (enabled(name)) server.registerTool(name, config as never, handler as never);
  };

  register("wc3_project_status", { description: "Read-only readiness and source-hash status for a configured WC3 project. Call this first.", inputSchema: schemas.projectStatusSchema, annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true } }, async input => safeCall(correlationId(), () => services.projects.status(input.project_id)));
  register("wc3_inspect_map", { description: "Read-only canonical inventory of an allowed WC3 map. The source map is never changed; use before any transaction.", inputSchema: schemas.inspectMapSchema, annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true } }, async input => { const id = correlationId(); return safeCall(id, () => services.inspections.inspect(input.project_id, input.map, input.section, input.include_provenance, input.max_items_per_section, id)); });
  register("wc3_list_archive_files", { description: "List MPQ archive members, sizes, hashes, and parser capability for an allowed map.", inputSchema: schemas.listArchiveFilesSchema, annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true } }, async input => safeCall(correlationId(), async () => {
    const result = await services.inspections.listArchiveFiles(input.project_id, input.map, correlationId());
    const members = Array.isArray(result.members) ? result.members.filter((item: any) => !input.prefix || String(item.path).toLowerCase().startsWith(input.prefix.toLowerCase())) : [];
    const offset = decodeArchiveCursor(input.cursor, String(result.map_sha256 ?? ""), input.prefix ?? "");
    result.members = members.slice(offset, offset + input.max_items);
    if (offset + input.max_items < members.length) result.next_cursor = encodeArchiveCursor(String(result.map_sha256 ?? ""), input.prefix ?? "", offset + input.max_items);
    return result;
  }));
  register("wc3_get_component", { description: "Read one typed or explicitly opaque map component. Opaque components return an actionable unsupported error.", inputSchema: schemas.getComponentSchema, annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true } }, async input => safeCall(correlationId(), () => services.inspections.getComponent(input.project_id, input.map, input.component, input.filter, input.max_items, correlationId())));
  register("wc3_validate_map", { description: "Validate an allowed map without changing or building it; returns errors, warnings, and opaque-data limitations.", inputSchema: schemas.validateMapSchema, annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true } }, async input => safeCall(correlationId(), () => services.inspections.validateMap(input.project_id, input.map, correlationId())));
  register("wc3_compare_maps", { description: "Compare two allowed map or artifact paths and separate archive differences from semantic differences.", inputSchema: schemas.compareMapsSchema, annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true } }, async input => safeCall(correlationId(), () => services.inspections.compareMaps(input.project_id, input.left, input.right, correlationId())));

  if (readOnly) return;
  register("wc3_begin_transaction", { description: "Stage an isolated transaction from an exact inspected source hash. Never overwrites the source map.", inputSchema: schemas.beginTransactionSchema, annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false } }, async input => safeCall(correlationId(), () => services.transactions.begin(input.project_id, input.map, input.expected_source_hash, correlationId())));
  register("wc3_apply_operations", { description: "Apply a bounded batch of typed semantic operations atomically to a transaction revision. Use dry_run for review.", inputSchema: schemas.applyOperationsSchema, annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false } }, async input => safeCall(correlationId(), () => services.transactions.apply(input.project_id, input.transaction_id, input.expected_revision, input.operations, input.dry_run, correlationId())));
  register("wc3_transaction_diff", { description: "Read the attributable semantic diff recorded for a staged transaction.", inputSchema: schemas.transactionDiffSchema, annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true } }, async input => safeCall(correlationId(), () => services.transactions.diff(input.project_id, input.transaction_id, input.from_revision, input.to_revision)));
  register("wc3_validate_transaction", { description: "Validate an exact transaction revision before building it. A build requires a validation result with no errors.", inputSchema: schemas.validateTransactionSchema, annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true } }, async input => safeCall(correlationId(), () => services.transactions.validate(input.project_id, input.transaction_id, input.revision, correlationId())));
  register("wc3_build_map", { description: "Build a uniquely named map artifact from an exact validated transaction revision. The result starts with runtime status untested.", inputSchema: schemas.buildMapSchema, annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false } }, async input => safeCall(correlationId(), () => services.builds.build(input.project_id, input.transaction_id, input.revision, input.expected_source_hash, input.profile, input.label, correlationId())));
  register("wc3_build_report", { description: "Read and rehash a build manifest before using the artifact.", inputSchema: schemas.buildReportSchema, annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true } }, async input => safeCall(correlationId(), async () => services.builds.report(input.project_id, input.build_id)));
  register("wc3_launch_editor", { description: "Launch exactly one hash-checked build in the configured World Editor. Process start is not editor-open evidence.", inputSchema: schemas.launchSchema, annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false } }, async input => safeCall(correlationId(), async () => services.launches.launchEditor(input.project_id, input.build_id, input.expected_build_hash)));
  register("wc3_launch_test_map", { description: "Copy and launch exactly one hash-checked build in Warcraft III using the configured test root; never terminates an existing process.", inputSchema: schemas.launchSchema, annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false } }, async input => safeCall(correlationId(), async () => services.launches.launchGame(input.project_id, input.build_id, input.expected_build_hash)));
  register("wc3_record_test_result", { description: "Attach an explicitly observed editor/game/smoke/playtest milestone to a matching build session; process start alone cannot pass it.", inputSchema: schemas.recordTestResultSchema, annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false } }, async input => safeCall(correlationId(), async () => services.launches.record(input.project_id, input.session_id, input.expected_build_hash, input.milestone, input.result, input.recorder, input.notes)));
  register("wc3_get_test_session", { description: "Read a persisted hash-linked test session and its current evidence level.", inputSchema: schemas.getTestSessionSchema, annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true } }, async input => safeCall(correlationId(), async () => services.launches.get(input.project_id, input.session_id)));
  register("wc3_promote_build", { description: "Copy a selected hash-checked build to one configured explicit destination and verify the copy hash.", inputSchema: schemas.promoteSchema, annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false } }, async input => safeCall(correlationId(), async () => services.builds.promote(input.project_id, input.build_id, input.expected_build_hash, input.destination_id, input.destination_name)));
  register("wc3_discard_transaction", { description: "Destructively remove only one confirmed MCP-owned transaction directory after manifest/hash checks and write an audit tombstone.", inputSchema: schemas.discardSchema, annotations: { readOnlyHint: false, destructiveHint: true, idempotentHint: false } }, async input => safeCall(correlationId(), async () => services.transactions.discard(input.project_id, input.transaction_id, input.expected_source_hash, input.confirmation)));
}

function encodeArchiveCursor(mapSha256: string, prefix: string, offset: number): string {
  return Buffer.from(JSON.stringify({ schema_version: "1.0", map_sha256: mapSha256, prefix, offset }), "utf8").toString("base64url");
}

function decodeArchiveCursor(cursor: string | undefined, mapSha256: string, prefix: string): number {
  if (!cursor) return 0;
  try {
    const value = JSON.parse(Buffer.from(cursor, "base64url").toString("utf8")) as { schema_version?: string; map_sha256?: string; prefix?: string; offset?: number };
    if (value.schema_version !== "1.0" || value.map_sha256?.toUpperCase() !== mapSha256.toUpperCase() || value.prefix !== prefix || !Number.isInteger(value.offset) || (value.offset ?? -1) < 0) {
      throw new Error("cursor identity mismatch");
    }
    return value.offset ?? 0;
  } catch (error) {
    throw new AppError("CURSOR_STALE", "The archive cursor is invalid or belongs to a different map/filter.", false, { cause: error instanceof Error ? error.message : String(error) });
  }
}
