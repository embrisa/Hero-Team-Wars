import { existsSync, copyFileSync, mkdirSync, readFileSync } from "node:fs";
import { join, basename, extname, isAbsolute, resolve } from "node:path";
import { randomUUID } from "node:crypto";
import type { ResolvedProject } from "../config/resolve-project.js";
import { relativeProjectPath } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";
import { sha256File, writeJsonArtifact, type ArtifactRef } from "./artifact-service.js";
import { ProjectService } from "./project-service.js";
import { TransactionService } from "./transaction-service.js";
import { TransactionStore } from "../storage/transaction-store.js";
import { withProjectLock } from "../storage/project-lock.js";
import { WorkerClient } from "../transport/worker-client.js";

export interface BuildManifest {
  schema_version: "1.0";
  build_id: string;
  project_id: string;
  transaction_id: string;
  revision: number;
  source_sha256: string;
  output_path: string;
  output_sha256: string;
  output_size_bytes: number;
  runtime_status: "untested" | "process_started" | "editor_opened" | "game_loaded" | "smoke_passed" | "playtest_passed";
  created_utc: string;
  engine_result: Record<string, unknown>;
}

export class BuildService {
  public constructor(private readonly projects: ProjectService, private readonly worker: WorkerClient, private readonly transactions: TransactionService, private readonly store = new TransactionStore()) {}

  public async build(projectId: string, transactionId: string, revision: number, expectedSourceHash: string, profile: string, label: string | undefined, correlationId: string): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    return withProjectLock(project, "build_map", async () => {
      const { loaded } = this.transactions.get(projectId, transactionId);
      if (loaded.manifest.state !== "validated") throw new AppError("TRANSACTION_STATE", `Transaction must be validated before building; current state is '${loaded.manifest.state}'.`);
      if (loaded.manifest.revision !== revision) throw new AppError("PRECONDITION_FAILED", "Build revision does not match the transaction manifest.");
      if (loaded.manifest.source.sha256.toUpperCase() !== expectedSourceHash.toUpperCase()) throw new AppError("SOURCE_CHANGED", "Build source hash does not match the transaction manifest.");
      if (!project.sourceMaps.some(source => source.toLowerCase() === loaded.manifest.source.path.toLowerCase())) throw new AppError("SOURCE_CHANGED", "The transaction manifest does not point to an allowed source map.");
      if (sha256File(loaded.manifest.source.path).sha256 !== expectedSourceHash.toUpperCase()) throw new AppError("SOURCE_CHANGED", "The accepted source map changed before build.");
      if (sha256File(loaded.paths.sourceMap).sha256 !== loaded.manifest.staged_copy_sha256.toUpperCase()) throw new AppError("SOURCE_CHANGED", "The isolated transaction source copy changed before build.");

      const buildId = randomUUID();
      const outputDirectory = join(project.buildRoot, projectId, buildId);
      mkdirSync(outputDirectory, { recursive: true });
      const extension = extname(loaded.manifest.source.path).toLowerCase() === ".w3x" ? ".w3x" : ".w3m";
      const safeLabel = (label ?? profile ?? "debug").replace(/[^A-Za-z0-9_-]/g, "_").slice(0, 40) || "debug";
      const outputPath = join(outputDirectory, `HeroTeamWars_${safeLabel}_${buildId}${extension}`);
      const result = await this.worker.request<Record<string, unknown>>("build_map", { source_map_path: loaded.paths.sourceMap, canonical_path: loaded.paths.canonical, output_path: outputPath, profile }, correlationId);
      const hash = sha256File(outputPath);
      const manifest: BuildManifest = { schema_version: "1.0", build_id: buildId, project_id: projectId, transaction_id: transactionId, revision, source_sha256: expectedSourceHash.toUpperCase(), output_path: outputPath, output_sha256: hash.sha256, output_size_bytes: hash.size_bytes, runtime_status: "untested", created_utc: new Date().toISOString(), engine_result: result };
      const artifact = writeJsonArtifact(project, relativeProjectPath(project, join(project.artifactRoot, "builds", buildId, "build-manifest.json")), { ...manifest, output_path: relativeProjectPath(project, outputPath) }, "build_manifest");
      loaded.manifest.state = "built";
      loaded.manifest.build_ids.push(buildId);
      this.store.update(loaded.paths, loaded.manifest);
      return { build: { ...manifest, output_path: relativeProjectPath(project, outputPath) }, manifest_artifact: artifact, runtime_status: "untested" };
    });
  }

  public load(projectId: string, buildId: string): { project: ResolvedProject; manifest: BuildManifest; manifestPath: string } {
    const project = this.projects.project(projectId);
    if (!/^[0-9a-f-]{36}$/i.test(buildId)) throw new AppError("INVALID_ARGUMENT", "Build ID must be a UUID.");
    const manifestPath = join(project.artifactRoot, "builds", buildId, "build-manifest.json");
    if (!existsSync(manifestPath)) throw new AppError("FILE_NOT_FOUND", `Build '${buildId}' was not found.`);
    const storedManifest = JSON.parse(readFileSync(manifestPath, "utf8")) as BuildManifest;
    const manifest = { ...storedManifest, output_path: isAbsolute(storedManifest.output_path) ? storedManifest.output_path : resolve(project.root, storedManifest.output_path) };
    if (manifest.build_id !== buildId || manifest.project_id !== projectId) throw new AppError("INTERNAL_ERROR", "Build manifest identity does not match the requested build.");
    if (!existsSync(manifest.output_path)) throw new AppError("FILE_NOT_FOUND", "The build output no longer exists.");
    const hash = sha256File(manifest.output_path);
    if (hash.sha256 !== manifest.output_sha256) throw new AppError("SOURCE_CHANGED", "The build output hash no longer matches its manifest.", false, { expected_sha256: manifest.output_sha256, actual_sha256: hash.sha256 });
    return { project, manifest, manifestPath };
  }

  public report(projectId: string, buildId: string): Record<string, unknown> {
    const loaded = this.load(projectId, buildId);
    return { build: loaded.manifest, verified: true, manifest_path: relativeProjectPath(loaded.project, loaded.manifestPath) };
  }

  public markRuntimeStatus(projectId: string, buildId: string, status: BuildManifest["runtime_status"]): Record<string, unknown> {
    const loaded = this.load(projectId, buildId);
    const rank: Record<BuildManifest["runtime_status"], number> = { untested: 0, process_started: 1, editor_opened: 2, game_loaded: 3, smoke_passed: 4, playtest_passed: 5 };
    if (rank[status] <= rank[loaded.manifest.runtime_status]) return { build_id: buildId, runtime_status: loaded.manifest.runtime_status, changed: false };
    loaded.manifest.runtime_status = status;
    const stored = { ...loaded.manifest, output_path: relativeProjectPath(loaded.project, loaded.manifest.output_path) };
    const artifact = writeJsonArtifact(loaded.project, relativeProjectPath(loaded.project, loaded.manifestPath), stored, "build_manifest");
    return { build_id: buildId, runtime_status: status, changed: true, manifest_artifact: artifact };
  }

  public project(projectId: string): ResolvedProject {
    return this.projects.project(projectId);
  }

  public async promote(projectId: string, buildId: string, expectedHash: string, destinationId: string, destinationName: string): Promise<Record<string, unknown>> {
    const loaded = this.load(projectId, buildId);
    if (loaded.manifest.output_sha256.toUpperCase() !== expectedHash.toUpperCase()) throw new AppError("SOURCE_CHANGED", "Expected build hash does not match the build manifest.");
    if (!["smoke_passed", "playtest_passed"].includes(loaded.manifest.runtime_status)) throw new AppError("PROMOTION_FAILED", "Promotion requires recorded smoke-test or playtest evidence for this exact build.", false, { runtime_status: loaded.manifest.runtime_status });
    if (destinationId !== "test_map_root" || !loaded.project.config.test_map_root) throw new AppError("PROMOTION_FAILED", `Destination '${destinationId}' is not configured.`);
    if (!/^[A-Za-z0-9._-]+\.(w3m|w3x)$/i.test(destinationName)) throw new AppError("INVALID_ARGUMENT", "Promotion destination name must be a simple .w3m or .w3x filename.");
    const destinationRoot = loaded.project.config.test_map_root;
    mkdirSync(destinationRoot, { recursive: true });
    const destination = join(destinationRoot, basename(destinationName));
    if (destination.toLowerCase() === loaded.manifest.output_path.toLowerCase()) throw new AppError("PROMOTION_FAILED", "Promotion destination cannot be the build source.");
    copyFileSync(loaded.manifest.output_path, destination);
    const hash = sha256File(destination);
    if (hash.sha256 !== expectedHash.toUpperCase()) throw new AppError("PROMOTION_FAILED", "Promoted copy hash did not match the selected build.");
    const audit = writeJsonArtifact(loaded.project, relativeProjectPath(loaded.project, join(loaded.project.artifactRoot, "audit", `promote-${buildId}.json`)), { schema_version: "1.0", build_id: buildId, source_sha256: expectedHash.toUpperCase(), destination_id: destinationId, destination_path: destination, destination_sha256: hash.sha256, promoted_utc: new Date().toISOString() }, "promotion_audit");
    return { build_id: buildId, destination_path: destination, destination_sha256: hash.sha256, audit_artifact: audit };
  }
}
