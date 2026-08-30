import { readFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { join } from "node:path";
import { AppError } from "../errors/app-error.js";
import { isWithin, relativeProjectPath, resolveConfiguredPath, resolveContained, type ResolvedProject } from "../config/resolve-project.js";
import { writeJsonArtifact, writeTextArtifact } from "./artifact-service.js";
import { ProjectService } from "./project-service.js";
import { TransactionService } from "./transaction-service.js";
import { BuildService } from "./build-service.js";
import { LaunchService } from "./launch-service.js";
import { WorkerClient } from "../transport/worker-client.js";

const HASH = /^[0-9A-F]{64}$/i;
const CHUNK = /^HTW-[0-9]{2}$/;

export class GameplayService {
  public constructor(
    private readonly projects: ProjectService,
    private readonly worker: WorkerClient,
    private readonly transactions: TransactionService,
    private readonly builds: BuildService,
    private readonly launches: LaunchService
  ) {}

  public async compose(projectId: string, manifestPath: string, profile: string | undefined, correlationId: string): Promise<Record<string, unknown>> {
    this.projects.assertToolAvailable(projectId, "wc3_compose_gameplay_source");
    const project = this.projectManifest(projectId, manifestPath);
    const result = await this.worker.request<Record<string, unknown>>("compose_gameplay_source", {
      manifest_path: project.manifest,
      ...(profile ? { profile } : {})
    }, correlationId);
    return this.persistComposition(project.project, result);
  }

  public async validate(projectId: string, manifestPath: string, profile: string | undefined, correlationId: string): Promise<Record<string, unknown>> {
    this.projects.assertToolAvailable(projectId, "wc3_validate_gameplay_source");
    const project = this.projectManifest(projectId, manifestPath);
    const result = await this.worker.request<Record<string, unknown>>("validate_gameplay_source", {
      manifest_path: project.manifest,
      ...(profile ? { profile } : {})
    }, correlationId);
    const report = withoutSource(result);
    const sourceHash = String(result.source_sha256 ?? "").toUpperCase();
    const artifact = writeJsonArtifact(project.project, artifactPath(project.project, `gameplay/validation/${sourceHash || "invalid"}.json`), report, "gameplay_validation");
    return { ...report, validation_artifact: artifact };
  }

  public async prepare(
    projectId: string,
    transactionId: string,
    expectedRevision: number,
    chunkId: string,
    manifestPath: string,
    profile: string | undefined,
    correlationId: string
  ): Promise<Record<string, unknown>> {
    if (!CHUNK.test(chunkId)) throw new AppError("INVALID_ARGUMENT", "chunk_id must use an HTW-## design chunk identifier.");
    this.projects.assertScriptMutationAllowed(projectId);
    const composed = await this.compose(projectId, manifestPath, profile, correlationId);
    const source = String(composed.source ?? "");
    const loaded = this.transactions.get(projectId, transactionId);
    const canonical = JSON.parse(readFileSync(loaded.loaded.paths.canonical, "utf8")) as { scripts?: Array<Record<string, unknown>> };
    const script = canonical.scripts?.find(item => String(item.archive_path ?? "").toLowerCase() === "war3map.j");
    const expected = String(script?.source_sha256 ?? script?.sha256 ?? "").toUpperCase();
    if (!HASH.test(expected)) throw new AppError("PRECONDITION_REQUIRED", "The transaction canonical model has no hash for the existing war3map.j source.");
    const operation = {
      operation_id: randomUUID(),
      type: "set_script_source",
      target: { archive_path: "war3map.j" },
      expected,
      value: { language: "jass", source },
      rationale: `Compose MCP-native gameplay source for ${chunkId}.`,
      design_reference: chunkId,
      expected_revision: expectedRevision
    };
    const applied = await this.transactions.apply(projectId, transactionId, expectedRevision, [operation], false, correlationId);
    return { chunk_id: chunkId, transaction_id: transactionId, composition: withoutSource(composed), source_artifact: composed.source_artifact, operation, applied };
  }

  public async runScenarioBuild(
    projectId: string,
    transactionId: string,
    revision: number,
    expectedSourceHash: string,
    chunkId: string,
    scenarioIds: string[] | undefined,
    profile: string | undefined,
    correlationId: string
  ): Promise<Record<string, unknown>> {
    if (!CHUNK.test(chunkId)) throw new AppError("INVALID_ARGUMENT", "chunk_id must use an HTW-## design chunk identifier.");
    const built = await this.builds.build(projectId, transactionId, revision, expectedSourceHash, "debug", `scenario-${chunkId}`, correlationId);
    const build = built.build as Record<string, unknown>;
    const buildId = String(build.build_id ?? "");
    const buildHash = String(build.output_sha256 ?? "").toUpperCase();
    const scenarioPayload: Record<string, unknown> = {
      profile: profile ?? "mvp_2arena",
      chunk_id: chunkId
    };
    if (scenarioIds && scenarioIds.length > 0) scenarioPayload.scenario_ids = scenarioIds;
    const scenarios = await this.worker.request<Record<string, unknown>>("run_scenario", scenarioPayload, correlationId);
    const report = {
      schema_version: "1.0",
      chunk_id: chunkId,
      transaction_id: transactionId,
      revision,
      build_id: buildId,
      build_sha256: buildHash,
      evidence_level: "static_only",
      runtime_verified: false,
      scenarios
    };
    const resolvedProject = this.projects.project(projectId);
    const artifact = writeJsonArtifact(resolvedProject, artifactPath(resolvedProject, `gameplay/scenarios/${buildId}-${chunkId}.json`), report, "scenario_report");
    return { build: built, scenario_report: report, scenario_artifact: artifact };
  }

  public async recordChunkResult(input: {
    project_id: string; chunk_id: string; scenario_id: string; transaction_id: string; revision: number;
    build_id: string; expected_build_hash: string; result: "pass" | "fail"; evidence_level: "static_only" | "user_observed";
    notes: string; test_session_id?: string;
  }): Promise<Record<string, unknown>> {
    if (!CHUNK.test(input.chunk_id)) throw new AppError("INVALID_ARGUMENT", "chunk_id must use an HTW-## design chunk identifier.");
    this.projects.assertMutationAllowed(input.project_id, "wc3_record_chunk_result");
    const loaded = this.builds.load(input.project_id, input.build_id);
    if (loaded.manifest.output_sha256.toUpperCase() !== input.expected_build_hash.toUpperCase()) throw new AppError("SOURCE_CHANGED", "The expected build hash does not match the exact build artifact.");
    let session: unknown;
    if (input.test_session_id) {
      session = await this.launches.get(input.project_id, input.test_session_id);
      const sessionRecord = session as { session?: { build_id?: string; build_sha256?: string } };
      if (sessionRecord.session?.build_id !== input.build_id || sessionRecord.session.build_sha256?.toUpperCase() !== input.expected_build_hash.toUpperCase()) throw new AppError("PRECONDITION_FAILED", "The test session does not reference the expected build.");
    }
    const project = this.projects.project(input.project_id);
    const value = {
      schema_version: "1.0",
      chunk_id: input.chunk_id,
      scenario_id: input.scenario_id,
      transaction_id: input.transaction_id,
      revision: input.revision,
      build_id: input.build_id,
      build_sha256: input.expected_build_hash.toUpperCase(),
      ...(input.test_session_id ? { test_session_id: input.test_session_id } : {}),
      result: input.result,
      evidence_level: input.evidence_level,
      runtime_verified: input.evidence_level === "user_observed" && Boolean(input.test_session_id),
      notes: input.notes,
      recorded_utc: new Date().toISOString()
    };
    const artifact = writeJsonArtifact(project, artifactPath(project, `gameplay/results/${input.build_id}-${input.chunk_id}-${input.scenario_id}.json`), value, "chunk_result");
    return { ...value, artifact, ...(session ? { test_session: session } : {}) };
  }

  private projectManifest(projectId: string, manifestPath: string): { project: ResolvedProject; manifest: string } {
    const project = this.projects.project(projectId);
    const manifest = resolveContained(project.root, manifestPath, "gameplay manifest", { allowMissing: false });
    const roots = project.config.gameplay_source_roots.map(root => resolveConfiguredPath(project.root, root));
    if (!roots.some(root => isWithin(root, manifest))) throw new AppError("PATH_OUTSIDE_ROOT", "Gameplay manifests must be inside a configured gameplay_source_roots directory.");
    return { project, manifest };
  }

  private persistComposition(project: ResolvedProject, result: Record<string, unknown>): Record<string, unknown> {
    const source = String(result.source ?? "");
    const sourceHash = String(result.source_sha256 ?? "").toUpperCase();
    if (!source || !HASH.test(sourceHash)) throw new AppError("ENGINE_PROTOCOL_ERROR", "The composer returned no valid source hash.");
    const sourceArtifact = writeTextArtifact(project, artifactPath(project, `gameplay/source/${sourceHash}/war3map.j`), source, "gameplay_source");
    const report = withoutSource(result);
    const manifestArtifact = writeJsonArtifact(project, artifactPath(project, `gameplay/source/${sourceHash}/composition.json`), report, "gameplay_manifest");
    return { ...report, source_artifact: sourceArtifact, manifest_artifact: manifestArtifact, source };
  }
}

function artifactPath(project: ResolvedProject, path: string): string {
  return relativeProjectPath(project, join(project.artifactRoot, path));
}

function withoutSource(value: Record<string, unknown>): Record<string, unknown> {
  const copy = { ...value };
  delete copy.source;
  return copy;
}
