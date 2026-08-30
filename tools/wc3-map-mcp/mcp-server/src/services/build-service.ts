import { existsSync, lstatSync, mkdirSync, readFileSync, renameSync, rmSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { extname, join, resolve } from "node:path";
import type { ResolvedProject } from "../config/resolve-project.js";
import { isWithin, relativeProjectPath } from "../config/resolve-project.js";
import { AppError, asAppError } from "../errors/app-error.js";
import { sha256File, writeJsonArtifact, type ArtifactRef } from "./artifact-service.js";
import { ProjectService } from "./project-service.js";
import { TransactionService } from "./transaction-service.js";
import { copyFileAtomic, TransactionStore, type TransactionManifest } from "../storage/transaction-store.js";
import { withProjectLock } from "../storage/project-lock.js";
import { WorkerClient } from "../transport/worker-client.js";

const BUILD_SCHEMA_VERSION = "1.0";
const VALIDATION_CONTEXT = {
  project_id: "hero-team-wars"
};

export type RuntimeStatus = "untested" | "process_started" | "editor_opened" | "game_loaded" | "smoke_passed" | "playtest_passed";

export interface BuildManifest {
  schema_version: "1.0";
  build_id: string;
  project_id: string;
  transaction_id: string;
  revision: number;
  profile: "debug" | "release" | "noop";
  source_sha256: string;
  output_path: string;
  output_sha256: string;
  output_size_bytes: number;
  writer_version: string;
  validator_version: string;
  validation_report: { path: string; sha256: string };
  archive_comparison: Record<string, unknown>;
  reinspection: { performed: true; semantic_differences: unknown[]; output_hash: string };
  warnings: unknown[];
  runtime_status: RuntimeStatus;
  created_utc: string;
  engine_result: Record<string, unknown>;
}

export class BuildService {
  public constructor(
    private readonly projects: ProjectService,
    private readonly worker: WorkerClient,
    private readonly transactions: TransactionService,
    private readonly store = new TransactionStore()
  ) {}

  public async build(
    projectId: string,
    transactionId: string,
    revision: number,
    expectedSourceHash: string,
    profile: "debug" | "release" | "noop",
    label: string | undefined,
    correlationId: string
  ): Promise<Record<string, unknown>> {
    this.projects.assertMutationAllowed(projectId, "wc3_build_map");
    const project = this.projects.project(projectId);
    const buildId = randomUUID();
    const buildParent = join(project.buildRoot, projectId);
    const temporaryDirectory = join(buildParent, `.build-${buildId}.tmp`);
    const finalDirectory = join(buildParent, buildId);
    let finalized = false;
    let transactionUpdated = false;

    return withProjectLock(project, "build_map", async () => {
      try {
        const { loaded } = this.transactions.get(projectId, transactionId);
        assertBuildableTransaction(loaded.manifest, transactionId, revision, expectedSourceHash);
        assertSourceUnchanged(project, loaded.manifest, loaded.paths.sourceMap);

        const validationReport = latestValidationReport(project, loaded.manifest);
        if (!validationReport) {
          throw new AppError("PRECONDITION_REQUIRED", "The transaction has no persisted validation report; validate the exact revision before building.");
        }

        mkdirSync(buildParent, { recursive: true });
        if (existsSync(finalDirectory) || existsSync(temporaryDirectory)) {
          throw new AppError("OUTPUT_EXISTS", `The generated build directory already exists for build '${buildId}'.`);
        }
        mkdirSync(temporaryDirectory, { recursive: false });
        const extension = extname(loaded.manifest.source.path).toLowerCase() === ".w3x" ? ".w3x" : ".w3m";
        const safeLabel = (label ?? profile ?? "debug").replace(/[^A-Za-z0-9_-]/g, "_").slice(0, 40) || "debug";
        const outputFileName = `HeroTeamWars_${safeLabel}_${buildId}${extension}`;
        const temporaryOutput = join(temporaryDirectory, outputFileName);

        const engineResult = await this.worker.request<Record<string, unknown>>("build_map", {
          source_map_path: loaded.paths.sourceMap,
          canonical_path: loaded.paths.canonical,
          output_path: temporaryOutput,
          profile,
          validation_context: { ...VALIDATION_CONTEXT, project_id: projectId, profile: project.config.profile }
        }, correlationId);
        assertEngineBuildResult(engineResult, temporaryOutput);

        // Re-inspect on the MCP side as well as inside the engine. This keeps
        // the artifact contract meaningful if the engine implementation is
        // replaced or its internal checks regress.
        const reopened = await this.worker.request<Record<string, unknown>>("inspect_map", { map_path: temporaryOutput }, correlationId);
        const comparison = await this.worker.request<Record<string, unknown>>("compare_maps", {
          left_path: loaded.paths.canonical,
          right_path: temporaryOutput
        }, correlationId);
        const semanticDifferences = Array.isArray(comparison.semantic_differences) ? comparison.semantic_differences : [];
        if (semanticDifferences.length !== 0) {
          throw new AppError("BUILD_REOPEN_MISMATCH", "The re-inspected output does not match the intended staged canonical model.", false, { semantic_differences: semanticDifferences });
        }

        const hash = sha256File(temporaryOutput);
        const engineHash = String(engineResult.sha256 ?? "").toUpperCase();
        if (engineHash !== hash.sha256) {
          throw new AppError("BUILD_REOPEN_MISMATCH", "The engine-reported output hash does not match the closed output file.", false, { expected_sha256: engineHash, actual_sha256: hash.sha256 });
        }

        const archiveComparison = asRecord(engineResult.archive_comparison);
        if (engineResult.reopened !== true || engineResult.opaque_members_preserved !== true || archiveComparison?.opaque_members_preserved !== true) {
          throw new AppError("BUILD_REOPEN_MISMATCH", "The engine did not prove archive reopen and opaque-member preservation.", false, { engine_result: engineResult });
        }

        const finalOutput = join(finalDirectory, outputFileName);
        renameSync(temporaryDirectory, finalDirectory);
        finalized = true;
        const outputRelative = relativeProjectPath(project, finalOutput);
        const validationPath = validationReport.path;
        const validationHash = sha256File(validationPath).sha256;
        const warnings = validationWarnings(validationReport.value);
        const sanitizedEngineResult = sanitizeEngineResult(project, engineResult);
        const manifest: BuildManifest = {
          schema_version: BUILD_SCHEMA_VERSION,
          build_id: buildId,
          project_id: projectId,
          transaction_id: transactionId,
          revision,
          profile,
          source_sha256: expectedSourceHash.toUpperCase(),
          output_path: outputRelative,
          output_sha256: hash.sha256,
          output_size_bytes: hash.size_bytes,
          writer_version: String(engineResult.writer_version ?? "unknown"),
          validator_version: String(asRecord(engineResult.validation)?.validator_version ?? asRecord(validationReport.value)?.validator_version ?? "unknown"),
          validation_report: { path: relativeProjectPath(project, validationPath), sha256: validationHash },
          archive_comparison: archiveComparison ?? {},
          reinspection: { performed: true, semantic_differences: semanticDifferences, output_hash: hash.sha256 },
          warnings,
          runtime_status: "untested",
          created_utc: new Date().toISOString(),
          engine_result: sanitizedEngineResult
        };
        const manifestArtifact = writeJsonArtifact(project, relativeProjectPath(project, join(project.artifactRoot, "builds", buildId, "build-manifest.json")), manifest, "build_manifest");
        const current = this.store.read(project, transactionId);
        current.manifest.state = "built";
        current.manifest.build_ids.push(buildId);
        this.store.update(current.paths, current.manifest);
        transactionUpdated = true;
        return {
          build: manifest,
          manifest_artifact: manifestArtifact,
          reinspection: { performed: true, semantic_differences: semanticDifferences, output_path: outputRelative },
          reopened,
          runtime_status: "untested"
        };
      } catch (error) {
        if (!finalized) {
          removeDirectory(temporaryDirectory);
          removeDirectory(finalDirectory);
        } else if (!transactionUpdated) {
          // A build without a transaction reference is not promotable.
          removeDirectory(finalDirectory);
        }
        const appError = asAppError(error);
        const failureArtifacts = writeBuildFailure(project, buildId, transactionId, revision, expectedSourceHash, correlationId, appError, temporaryDirectory, finalDirectory);
        throw new AppError(appError.code, appError.message, appError.retryable, { ...appError.details, failure_report: failureArtifacts.report.path, failure_log: failureArtifacts.log.path }, { cause: appError });
      }
    });
  }

  public load(projectId: string, buildId: string): { project: ResolvedProject; manifest: BuildManifest; manifestPath: string } {
    const project = this.projects.project(projectId);
    assertUuid(buildId, "Build ID");
    const manifestPath = join(project.artifactRoot, "builds", buildId, "build-manifest.json");
    if (!existsSync(manifestPath)) throw new AppError("FILE_NOT_FOUND", `Build '${buildId}' was not found.`);
    let manifest: BuildManifest;
    try {
      manifest = JSON.parse(readFileSync(manifestPath, "utf8")) as BuildManifest;
    } catch (error) {
      throw new AppError("INTERNAL_ERROR", `Build manifest '${buildId}' is invalid.`, false, {}, { cause: error });
    }
    validateManifest(manifest, projectId, buildId);
    const outputPath = resolve(project.root, manifest.output_path);
    const expectedDirectory = join(project.buildRoot, projectId, buildId);
    if (!isWithin(expectedDirectory, outputPath) || !existsSync(outputPath)) {
      throw new AppError("PATH_OUTSIDE_ROOT", "The build manifest output is outside its generated build directory.");
    }
    const stat = lstatSync(outputPath);
    if (!stat.isFile() || stat.isSymbolicLink()) throw new AppError("PATH_OUTSIDE_ROOT", "The build output must be a regular file.");
    const hash = sha256File(outputPath);
    if (hash.sha256 !== manifest.output_sha256) {
      throw new AppError("SOURCE_CHANGED", "The build output hash no longer matches its manifest.", false, { expected_sha256: manifest.output_sha256, actual_sha256: hash.sha256 });
    }
    return { project, manifest: { ...manifest, output_path: outputPath }, manifestPath };
  }

  public report(projectId: string, buildId: string): Record<string, unknown> {
    const loaded = this.load(projectId, buildId);
    return { build: { ...loaded.manifest, output_path: relativeProjectPath(loaded.project, loaded.manifest.output_path) }, verified: true, manifest_path: relativeProjectPath(loaded.project, loaded.manifestPath) };
  }

  public markRuntimeStatus(projectId: string, buildId: string, status: RuntimeStatus): Record<string, unknown> {
    const loaded = this.load(projectId, buildId);
    const rank: Record<RuntimeStatus, number> = { untested: 0, process_started: 1, editor_opened: 2, game_loaded: 3, smoke_passed: 4, playtest_passed: 5 };
    if (rank[status] <= rank[loaded.manifest.runtime_status]) return { build_id: buildId, runtime_status: loaded.manifest.runtime_status, changed: false };
    loaded.manifest.runtime_status = status;
    const stored = { ...loaded.manifest, output_path: relativeProjectPath(loaded.project, loaded.manifest.output_path) };
    const artifact = writeJsonArtifact(loaded.project, relativeProjectPath(loaded.project, loaded.manifestPath), stored, "build_manifest");
    if (rank[status] >= rank.game_loaded) {
      try {
        const transaction = this.store.read(loaded.project, loaded.manifest.transaction_id);
        if (["built", "tested"].includes(transaction.manifest.state)) {
          transaction.manifest.state = "tested";
          this.store.update(transaction.paths, transaction.manifest);
        }
      } catch {
        // Runtime evidence remains on the build manifest; transaction recovery
        // can reconcile a missing state transition without losing evidence.
      }
    }
    return { build_id: buildId, runtime_status: status, changed: true, manifest_artifact: artifact };
  }

  /** Persist the session link without changing the build's evidence level. */
  public attachTestSession(projectId: string, buildId: string, sessionId: string): void {
    const loaded = this.load(projectId, buildId);
    assertUuid(sessionId, "Session ID");
    const transaction = this.store.read(loaded.project, loaded.manifest.transaction_id);
    if (transaction.manifest.test_session_ids.includes(sessionId)) return;
    transaction.manifest.test_session_ids.push(sessionId);
    this.store.update(transaction.paths, transaction.manifest);
  }

  public project(projectId: string): ResolvedProject {
    return this.projects.project(projectId);
  }

  public async promote(projectId: string, buildId: string, expectedHash: string, destinationId: string, destinationName: string): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    this.projects.assertMutationAllowed(projectId, "wc3_promote_build");
    return withProjectLock(project, "promote_build", async () => {
      const loaded = this.load(projectId, buildId);
      if (!/^[0-9A-F]{64}$/i.test(expectedHash) || loaded.manifest.output_sha256.toUpperCase() !== expectedHash.toUpperCase()) throw new AppError("SOURCE_CHANGED", "Expected build hash does not match the build manifest.");
      if (!["smoke_passed", "playtest_passed"].includes(loaded.manifest.runtime_status)) throw new AppError("PROMOTION_FAILED", "Promotion requires recorded smoke-test or playtest evidence for this exact build.", false, { runtime_status: loaded.manifest.runtime_status });
      if (destinationId !== "test_map_root" || !project.config.test_map_root) throw new AppError("PROMOTION_FAILED", `Destination '${destinationId}' is not configured.`);
      if (!/^[A-Za-z0-9._-]+\.(w3m|w3x)$/i.test(destinationName)) throw new AppError("INVALID_ARGUMENT", "Promotion destination name must be a simple .w3m or .w3x filename.");

      const destinationRoot = resolve(project.config.test_map_root);
      mkdirSync(destinationRoot, { recursive: true });
      const destination = join(destinationRoot, destinationName);
      if (destination.toLowerCase() === loaded.manifest.output_path.toLowerCase()) throw new AppError("PROMOTION_FAILED", "Promotion destination cannot be the build source.");
      copyFileAtomic(loaded.manifest.output_path, destination);
      const hash = sha256File(destination);
      if (hash.sha256 !== expectedHash.toUpperCase()) throw new AppError("PROMOTION_FAILED", "Promoted copy hash did not match the selected build.");
      const audit = writeJsonArtifact(project, relativeProjectPath(project, join(project.artifactRoot, "audit", `promote-${buildId}.json`)), {
        schema_version: BUILD_SCHEMA_VERSION,
        build_id: buildId,
        source_path: relativeProjectPath(project, loaded.manifest.output_path),
        source_sha256: expectedHash.toUpperCase(),
        destination_id: destinationId,
        destination_path: destination,
        destination_sha256: hash.sha256,
        promoted_utc: new Date().toISOString()
      }, "promotion_audit");
      const transaction = this.store.read(project, loaded.manifest.transaction_id);
      if (["built", "tested"].includes(transaction.manifest.state)) {
        transaction.manifest.state = "promoted";
        this.store.update(transaction.paths, transaction.manifest);
      }
      return { build_id: buildId, destination_path: destination, destination_sha256: hash.sha256, audit_artifact: audit };
    });
  }
}

function assertBuildableTransaction(manifest: TransactionManifest, transactionId: string, revision: number, expectedSourceHash: string): void {
  if (manifest.transaction_id !== transactionId || manifest.state !== "validated") throw new AppError("TRANSACTION_STATE", `Transaction must be validated before building; current state is '${manifest.state}'.`);
  if (manifest.revision !== revision) throw new AppError("PRECONDITION_FAILED", "Build revision does not match the transaction manifest.");
  if (manifest.source.sha256.toUpperCase() !== expectedSourceHash.toUpperCase()) throw new AppError("SOURCE_CHANGED", "Build source hash does not match the transaction manifest.");
}

function assertSourceUnchanged(project: ResolvedProject, manifest: TransactionManifest, stagedPath: string): void {
  if (!project.sourceMaps.some(source => source.toLowerCase() === manifest.source.path.toLowerCase())) throw new AppError("SOURCE_CHANGED", "The transaction manifest does not point to an allowed source map.");
  const source = sha256File(manifest.source.path);
  if (source.sha256 !== manifest.source.sha256 || source.size_bytes !== manifest.source.size_bytes) throw new AppError("SOURCE_CHANGED", "The accepted source map changed before build.", false, { expected_sha256: manifest.source.sha256, actual_sha256: source.sha256 });
  const staged = sha256File(stagedPath);
  if (staged.sha256 !== manifest.staged_copy_sha256 || staged.size_bytes !== manifest.source.size_bytes) throw new AppError("SOURCE_CHANGED", "The isolated transaction source copy changed before build.", false, { expected_sha256: manifest.staged_copy_sha256, actual_sha256: staged.sha256 });
}

function assertEngineBuildResult(result: Record<string, unknown>, expectedOutput: string): void {
  if (result.output_path !== expectedOutput || result.reopened !== true || typeof result.sha256 !== "string" || !/^[0-9A-F]{64}$/i.test(result.sha256)) throw new AppError("ENGINE_PROTOCOL_ERROR", "The map engine returned an incomplete build result.");
}

function latestValidationReport(project: ResolvedProject, manifest: TransactionManifest): { path: string; value: Record<string, unknown> } | undefined {
  const path = manifest.validation_reports.at(-1);
  if (!path) return undefined;
  const absolute = resolve(project.root, path);
  if (!isWithin(project.stagingRoot, absolute) || !existsSync(absolute)) throw new AppError("FILE_NOT_FOUND", "The transaction validation report is missing or outside the staging root.");
  try {
    return { path: absolute, value: JSON.parse(readFileSync(absolute, "utf8")) as Record<string, unknown> };
  } catch (error) {
    throw new AppError("INTERNAL_ERROR", "The transaction validation report is invalid.", false, {}, { cause: error });
  }
}

function validationWarnings(report: Record<string, unknown>): unknown[] {
  const findings = Array.isArray(report.findings) ? report.findings : [];
  return findings.filter(item => item && typeof item === "object" && ["warning", "info"].includes(String((item as Record<string, unknown>).severity)));
}

function sanitizeEngineResult(project: ResolvedProject, result: Record<string, unknown>): Record<string, unknown> {
  const clone = JSON.parse(JSON.stringify(result)) as Record<string, unknown>;
  if (typeof clone.output_path === "string") clone.output_path = relativeProjectPath(project, clone.output_path);
  return clone;
}

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : undefined;
}

function validateManifest(manifest: BuildManifest, projectId: string, buildId: string): void {
  if (manifest.schema_version !== BUILD_SCHEMA_VERSION || manifest.build_id !== buildId || manifest.project_id !== projectId || !/^[0-9A-F]{64}$/.test(manifest.output_sha256) || !/^[0-9A-F]{64}$/.test(manifest.source_sha256)) throw new AppError("INTERNAL_ERROR", "Build manifest identity, schema, or hashes are invalid.");
  if (!["debug", "release", "noop"].includes(manifest.profile) || !["untested", "process_started", "editor_opened", "game_loaded", "smoke_passed", "playtest_passed"].includes(manifest.runtime_status)) throw new AppError("INTERNAL_ERROR", "Build manifest profile or runtime status is invalid.");
  if (!manifest.output_path || !manifest.validation_report?.path || !manifest.writer_version || !manifest.validator_version) throw new AppError("INTERNAL_ERROR", "Build manifest is missing required Phase 3 fields.");
}

function assertUuid(value: string, label: string): void {
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value)) throw new AppError("INVALID_ARGUMENT", `${label} must be a UUID.`);
}

function removeDirectory(path: string): void {
  try {
    if (existsSync(path)) rmSync(path, { recursive: true, force: true });
  } catch {
    // Preserve the original failure. The generated name is unique and the
    // recovery report identifies any leftover path.
  }
}

function writeBuildFailure(project: ResolvedProject, buildId: string, transactionId: string, revision: number, sourceHash: string, correlationId: string, error: AppError, temporaryDirectory: string, finalDirectory: string): { report: ArtifactRef; log: ArtifactRef } {
  const record = {
    schema_version: BUILD_SCHEMA_VERSION,
    build_id: buildId,
    transaction_id: transactionId,
    revision,
    source_sha256: sourceHash.toUpperCase(),
    correlation_id: correlationId,
    code: error.code,
    message: error.message,
    retryable: error.retryable,
    details: error.details,
    temporary_directory: relativeProjectPath(project, temporaryDirectory),
    final_directory: relativeProjectPath(project, finalDirectory),
    failed_utc: new Date().toISOString()
  };
  const report = writeJsonArtifact(project, relativeProjectPath(project, join(project.artifactRoot, "build-failures", `${buildId}.json`)), record, "build_failure");
  const log = writeJsonArtifact(project, relativeProjectPath(project, join(project.logRoot, "build-failures", `${buildId}.json`)), record, "build_failure_log");
  return { report, log };
}
