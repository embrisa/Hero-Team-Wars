import { randomUUID } from "node:crypto";
import * as schemas from "../schemas/tools.js";
import type { LaunchService } from "../services/launch-service.js";
import { safeCall } from "./response.js";
import type { RegisterTool } from "./builds.js";

/** Register editor/game launch and explicit observation tools. */
export function registerLaunchTools(register: RegisterTool, launches: LaunchService): void {
  register("wc3_launch_editor", {
    description: "Launch exactly one hash-checked build in the configured World Editor. The process-start result is not editor-open evidence; record wc3_record_test_result after observing the editor.",
    inputSchema: schemas.launchSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = randomUUID();
    return safeCall(id, async () => launches.launchEditor(input.project_id, input.build_id, input.expected_build_hash, id));
  });

  register("wc3_launch_test_map", {
    description: "Copy and launch exactly one hash-checked build in Warcraft III using the configured test root. Existing processes cause a conflict and are never terminated.",
    inputSchema: schemas.launchSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = randomUUID();
    return safeCall(id, async () => launches.launchGame(input.project_id, input.build_id, input.expected_build_hash, id));
  });

  register("wc3_record_test_result", {
    description: "Attach one explicitly observed editor/game/smoke/playtest milestone to a matching session. Milestones are ordered, failures remain failures, and process start cannot be upgraded automatically.",
    inputSchema: schemas.recordTestResultSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = randomUUID();
    return safeCall(id, async () => launches.record(input.project_id, input.session_id, input.expected_build_hash, input.milestone, input.result, input.recorder, input.notes, input.artifacts, id));
  });

  register("wc3_get_test_session", {
    description: "Read a persisted hash-linked test session and re-verify its build, test copy, and evidence artifact hashes.",
    inputSchema: schemas.getTestSessionSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async input => {
    const id = randomUUID();
    return safeCall(id, async () => launches.get(input.project_id, input.session_id));
  });
}
