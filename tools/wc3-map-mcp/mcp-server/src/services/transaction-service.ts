import { copyFileSync, existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import type { ResolvedProject } from "../config/resolve-project.js";
import { relativeProjectPath } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";
import { sha256File, writeJsonArtifact } from "./artifact-service.js";
import { ProjectService } from "./project-service.js";
import { TransactionStore, type TransactionManifest } from "../storage/transaction-store.js";
import { withProjectLock } from "../storage/project-lock.js";
import { WorkerClient } from "../transport/worker-client.js";

export class TransactionService {
  public constructor(private readonly projects: ProjectService, private readonly worker: WorkerClient, private readonly store = new TransactionStore()) {}

  public async begin(projectId: string, map: string, expectedSourceHash: string, correlationId: string): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    return withProjectLock(project, "begin_transaction", async () => {
      const sourcePath = this.projects.source(projectId, map);
      const sourceHash = sha256File(sourcePath);
      assertHash(expectedSourceHash, sourceHash.sha256);
      const canonical = await this.worker.request<Record<string, unknown>>("inspect_map", { map_path: sourcePath }, correlationId);
      const created = this.store.create(project, sourcePath, sourceHash, canonical);
      return { transaction_id: created.id, revision: 0, source: created.manifest.source, staged_source_hash: created.manifest.staged_copy_sha256, manifest_path: relativeProjectPath(project, join(project.stagingRoot, created.id, "manifest.json")), snapshot: created.snapshot };
    });
  }

  public async apply(projectId: string, transactionId: string, expectedRevision: number, operations: unknown[], dryRun: boolean, correlationId: string): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    return withProjectLock(project, "apply_operations", async () => {
      const loaded = this.store.read(project, transactionId);
      this.assertActive(loaded.manifest);
      this.assertSourceUnchanged(project, loaded.manifest, loaded.paths.sourceMap);
      if (loaded.manifest.revision !== expectedRevision) throw new AppError("PRECONDITION_FAILED", `Transaction revision ${loaded.manifest.revision} does not match expected revision ${expectedRevision}.`);
      if (operations.length === 0) throw new AppError("INVALID_ARGUMENT", "At least one operation is required.");
      if (dryRun) return { transaction_id: transactionId, revision: expectedRevision, dry_run: true, result: await this.worker.request("apply_operations", { canonical_path: loaded.paths.canonical, operations }, correlationId) };

      const nextRevision = expectedRevision + 1;
      const revisionPath = join(loaded.paths.revisions, `${nextRevision.toString().padStart(4, "0")}-after-operations.json`);
      const result = await this.worker.request<Record<string, unknown>>("apply_operations", { canonical_path: loaded.paths.canonical, output_path: revisionPath, operations }, correlationId);
      copyFileSync(revisionPath, loaded.paths.canonical);
      const diffPath = join(loaded.paths.reports, `diff-${nextRevision.toString().padStart(4, "0")}.json`);
      const diff = result.diff ?? { schema_version: "1.0", changes: [] };
      const artifact = writeJsonArtifact(project, relativeProjectPath(project, diffPath), diff, "semantic_diff");
      const operationIds = Array.isArray(result.applied_operation_ids) ? result.applied_operation_ids.filter((x): x is string => typeof x === "string") : [];
      const manifest = loaded.manifest;
      manifest.revision = nextRevision;
      manifest.state = "modified";
      manifest.operation_ids.push(...operationIds);
      this.store.update(loaded.paths, manifest);
      return { transaction_id: transactionId, revision: nextRevision, dry_run: false, applied_operation_ids: operationIds, diff: diff, diff_artifact: artifact, canonical_path: relativeProjectPath(project, loaded.paths.canonical) };
    });
  }

  public async diff(projectId: string, transactionId: string, fromRevision: number | undefined, toRevision: number | undefined): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    const loaded = this.store.read(project, transactionId);
    const from = fromRevision ?? 0;
    const to = toRevision ?? loaded.manifest.revision;
    if (from < 0 || to < from || to > loaded.manifest.revision) throw new AppError("INVALID_ARGUMENT", "Revision range is invalid.");
    const reports = [];
    for (let revision = from + 1; revision <= to; revision++) {
      const report = join(loaded.paths.reports, `diff-${revision.toString().padStart(4, "0")}.json`);
      if (existsSync(report)) reports.push({ path: report, value: JSON.parse(readFileSync(report, "utf8")) as Record<string, unknown> });
    }
    const changes = reports.flatMap(item => Array.isArray(item.value.changes) ? item.value.changes : []);
    const singleReport = reports.length === 1 ? reports[0] : undefined;
    return { transaction_id: transactionId, from_revision: from, to_revision: to, diff: { schema_version: "1.0", changes }, ...(singleReport ? { artifact_path: relativeProjectPath(project, singleReport.path) } : {}), ...(changes.length === 0 ? { note: "No semantic changes were recorded for this revision range." } : {}) };
  }

  public async validate(projectId: string, transactionId: string, revision: number, correlationId: string): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    return withProjectLock(project, "validate_transaction", async () => {
      const loaded = this.store.read(project, transactionId);
      this.assertActive(loaded.manifest);
      this.assertSourceUnchanged(project, loaded.manifest, loaded.paths.sourceMap);
      if (loaded.manifest.revision !== revision) throw new AppError("PRECONDITION_FAILED", `Transaction revision ${loaded.manifest.revision} does not match expected revision ${revision}.`);
      const report = await this.worker.request<Record<string, unknown>>("validate_canonical", { canonical_path: loaded.paths.canonical }, correlationId);
      const reportPath = join(loaded.paths.reports, `validation-${revision.toString().padStart(4, "0")}.json`);
      const artifact = writeJsonArtifact(project, relativeProjectPath(project, reportPath), report, "validation_report");
      const manifest = loaded.manifest;
      manifest.validation_reports.push(artifact.path);
      if (report.buildable === true) manifest.state = "validated";
      else manifest.state = "modified";
      this.store.update(loaded.paths, manifest);
      if (report.buildable !== true) throw new AppError("VALIDATION_FAILED", "The transaction contains validation errors.", false, { report_path: artifact.path });
      return { transaction_id: transactionId, revision, report, artifact };
    });
  }

  public async discard(projectId: string, transactionId: string, expectedSourceHash: string, confirmation: boolean): Promise<Record<string, unknown>> {
    if (!confirmation) throw new AppError("INVALID_ARGUMENT", "Discard requires explicit confirmation=true.");
    const project = this.projects.project(projectId);
    return withProjectLock(project, "discard_transaction", async () => this.store.discard(project, transactionId, expectedSourceHash.toUpperCase()));
  }

  public get(projectId: string, transactionId: string): { project: ResolvedProject; loaded: ReturnType<TransactionStore["read"]> } {
    const project = this.projects.project(projectId);
    return { project, loaded: this.store.read(project, transactionId) };
  }

  private assertActive(manifest: TransactionManifest): void {
    if (!["staged", "modified", "validated"].includes(manifest.state)) throw new AppError("TRANSACTION_STATE", `Transaction is in state '${manifest.state}' and cannot be changed in this operation.`);
  }

  private assertSourceUnchanged(project: ResolvedProject, manifest: TransactionManifest, stagedPath: string): void {
    if (!project.sourceMaps.some(source => source.toLowerCase() === manifest.source.path.toLowerCase())) {
      throw new AppError("SOURCE_CHANGED", "The transaction manifest does not point to an allowed source map.");
    }
    const current = sha256File(manifest.source.path).sha256;
    if (current !== manifest.source.sha256) throw new AppError("SOURCE_CHANGED", "The source map changed since this transaction was created.", false, { expected_sha256: manifest.source.sha256, actual_sha256: current });
    const staged = sha256File(stagedPath).sha256;
    if (staged !== manifest.staged_copy_sha256) throw new AppError("SOURCE_CHANGED", "The isolated transaction source copy changed after staging.", false, { expected_sha256: manifest.staged_copy_sha256, actual_sha256: staged });
  }
}

function assertHash(expected: string, actual: string): void {
  if (!/^[0-9A-F]{64}$/i.test(expected) || expected.toUpperCase() !== actual.toUpperCase()) throw new AppError("SOURCE_CHANGED", "The expected source hash does not match the current source map.", false, { expected_sha256: expected, actual_sha256: actual });
}
