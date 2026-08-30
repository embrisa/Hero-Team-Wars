import { correlationId } from "../schemas/common.js";
import * as schemas from "../schemas/tools.js";
import type { BuildService } from "../services/build-service.js";
import type { LaunchService } from "../services/launch-service.js";
import { safeCall } from "./response.js";

type RegisterTool = (
  name: string,
  config: Record<string, unknown>,
  handler: (input: any) => Promise<Record<string, unknown>>
) => void;

/** Register the build/evidence/promotion surface as one auditable module. */
export function registerBuildTools(register: RegisterTool, builds: BuildService, launches: LaunchService): void {
  register("wc3_build_map", {
    description: "Build a uniquely named map artifact from an exact validated transaction revision. The engine reopens and compares the output; runtime status remains untested.",
    inputSchema: schemas.buildMapSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = correlationId();
    return safeCall(id, () => builds.build(input.project_id, input.transaction_id, input.revision, input.expected_source_hash, input.profile, input.label, id));
  });

  register("wc3_build_report", {
    description: "Read a persisted build manifest and rehash its exact generated output before using it.",
    inputSchema: schemas.buildReportSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async input => {
    const id = correlationId();
    return safeCall(id, async () => builds.report(input.project_id, input.build_id));
  });

  register("wc3_launch_editor", {
    description: "Launch exactly one hash-checked build in the configured World Editor. Process start is not editor-open evidence.",
    inputSchema: schemas.launchSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = correlationId();
    return safeCall(id, async () => launches.launchEditor(input.project_id, input.build_id, input.expected_build_hash));
  });

  register("wc3_launch_test_map", {
    description: "Copy and launch exactly one hash-checked build in Warcraft III using the configured test root; never terminates an existing process.",
    inputSchema: schemas.launchSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = correlationId();
    return safeCall(id, async () => launches.launchGame(input.project_id, input.build_id, input.expected_build_hash));
  });

  register("wc3_record_test_result", {
    description: "Attach an explicitly observed editor/game/smoke/playtest milestone to a matching build session; process start alone cannot pass it.",
    inputSchema: schemas.recordTestResultSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = correlationId();
    return safeCall(id, async () => launches.record(input.project_id, input.session_id, input.expected_build_hash, input.milestone, input.result, input.recorder, input.notes));
  });

  register("wc3_get_test_session", {
    description: "Read a persisted hash-linked test session and its current evidence level.",
    inputSchema: schemas.getTestSessionSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async input => {
    const id = correlationId();
    return safeCall(id, async () => launches.get(input.project_id, input.session_id));
  });

  register("wc3_promote_build", {
    description: "Copy a selected build to one explicitly configured destination only after recorded smoke/playtest evidence; refuses source drift and destination overwrite.",
    inputSchema: schemas.promoteSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = correlationId();
    return safeCall(id, async () => builds.promote(input.project_id, input.build_id, input.expected_build_hash, input.destination_id, input.destination_name));
  });
}
