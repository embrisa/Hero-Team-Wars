import { correlationId } from "../schemas/common.js";
import * as schemas from "../schemas/tools.js";
import type { GameplayService } from "../services/gameplay-service.js";
import { safeCall } from "./response.js";
import type { RegisterTool } from "./builds.js";

export function registerGameplayTools(gameplay: GameplayService, register: RegisterTool): void {
  register("wc3_compose_gameplay_source", {
    description: "Compose deterministic MCP-native JASS from a project-relative module manifest and emit source and hash artifacts. This is static evidence only and does not mutate a map.",
    inputSchema: schemas.gameplayManifestSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => gameplay.compose(input.project_id, input.manifest_path, input.profile, id));
  });

  register("wc3_validate_gameplay_source", {
    description: "Validate gameplay module paths, dependencies, symbols, trigger/variable manifests, and generated JASS syntax without claiming runtime evidence.",
    inputSchema: schemas.gameplayManifestSchema,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => gameplay.validate(input.project_id, input.manifest_path, input.profile, id));
  });

  register("wc3_prepare_gameplay_chunk", {
    description: "Compose and place reviewed MCP-native gameplay source into an exact transaction revision through the existing source-hash and revision gates.",
    inputSchema: schemas.prepareGameplayChunkSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => gameplay.prepare(input.project_id, input.transaction_id, input.expected_revision, input.chunk_id, input.manifest_path, input.profile, id));
  });

  register("wc3_run_scenario_build", {
    description: "Build an exact validated transaction revision and run deterministic model-level scenarios tied to the resulting build hash; Warcraft III runtime evidence remains separate.",
    inputSchema: schemas.runScenarioBuildSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = correlationId();
    return safeCall(id, () => gameplay.runScenarioBuild(input.project_id, input.transaction_id, input.revision, input.expected_source_hash, input.chunk_id, input.scenario_ids, input.profile, id));
  });

  register("wc3_record_chunk_result", {
    description: "Record pass/fail evidence for one HTW chunk and scenario while preserving exact transaction, revision, build, and optional observed test-session references.",
    inputSchema: schemas.recordChunkResultSchema,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async input => {
    const id = correlationId();
    return safeCall(id, () => gameplay.recordChunkResult(input));
  });
}
