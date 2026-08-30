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
import { registerProjectStatus } from "./project-status.js";
import { registerInspectMap } from "./inspect-map.js";
import { registerListArchiveFiles } from "./list-archive-files.js";
import { registerGetComponent } from "./get-component.js";
import { registerValidateMap } from "./validate-map.js";
import { registerCompareMaps } from "./compare-maps.js";

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

  if (enabled("wc3_project_status")) registerProjectStatus(server, services.projects);
  if (enabled("wc3_inspect_map")) registerInspectMap(server, services.inspections);
  if (enabled("wc3_list_archive_files")) registerListArchiveFiles(server, services.inspections);
  if (enabled("wc3_get_component")) registerGetComponent(server, services.inspections);
  if (enabled("wc3_validate_map")) registerValidateMap(server, services.inspections);
  if (enabled("wc3_compare_maps")) registerCompareMaps(server, services.inspections);

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
