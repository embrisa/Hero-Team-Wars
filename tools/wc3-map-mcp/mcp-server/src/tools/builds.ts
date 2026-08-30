import { correlationId } from "../schemas/common.js";
import * as schemas from "../schemas/tools.js";
import type { BuildService } from "../services/build-service.js";
import { safeCall } from "./response.js";

export type RegisterTool = (
  name: string,
  config: Record<string, unknown>,
  handler: (input: any) => Promise<Record<string, unknown>>
) => void;

/** Register the build and approval surface as one auditable module. */
export function registerBuildTools(register: RegisterTool, builds: BuildService): void {
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

  register("wc3_promote_build", {
    description: "Copy a selected build to one explicitly configured destination only after recorded smoke/playtest evidence; refuses source drift and destination overwrite.",
    inputSchema: schemas.promoteSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = correlationId();
    return safeCall(id, async () => builds.promote(input.project_id, input.build_id, input.expected_build_hash, input.destination_id, input.destination_name));
  });
}
