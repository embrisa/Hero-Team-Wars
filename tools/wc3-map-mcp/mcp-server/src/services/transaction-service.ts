import { existsSync, readFileSync, rmSync } from "node:fs";
import { join } from "node:path";
import type { ResolvedProject } from "../config/resolve-project.js";
import { relativeProjectPath } from "../config/resolve-project.js";
import { AppError, asAppError } from "../errors/app-error.js";
import { sha256File } from "./artifact-service.js";
import { writeJsonArtifact, type ArtifactRef } from "./artifact-service.js";
import { ProjectService } from "./project-service.js";
import {
  replaceFileAtomic,
  TransactionStore,
  type TransactionManifest,
  type TransactionOperationRecord,
  type TransactionVersions
} from "../storage/transaction-store.js";
import { withProjectLock } from "../storage/project-lock.js";
import { WorkerClient } from "../transport/worker-client.js";
import { operationSchema, type OperationInput } from "../schemas/operations.js";

const SERVER_VERSION = "0.1.0";
const SCHEMA_VERSION = "1.0";
const WORKER_PROTOCOL_VERSION = "1.0";

export class TransactionService {
  public constructor(private readonly projects: ProjectService, private readonly worker: WorkerClient, private readonly store = new TransactionStore()) {}

  public async begin(projectId: string, map: string, expectedSourceHash: string, label: string | undefined, correlationId: string): Promise<Record<string, unknown>> {
    this.projects.assertMutationAllowed(projectId, "wc3_begin_transaction");
    const project = this.projects.project(projectId);
    return withProjectLock(project, "begin_transaction", async () => {
      const sourcePath = this.projects.source(projectId, map);
      const sourceHash = sha256File(sourcePath);
      assertHash(expectedSourceHash, sourceHash.sha256);
      const staged = this.store.stageSource(project, sourcePath, sourceHash);

      try {
        // The canonical model must come from the isolated copy, never from a
        // second read of the mutable source path.
        const canonical = await this.worker.request<Record<string, unknown>>("inspect_map", { map_path: staged.paths.sourceMap }, correlationId);
        const afterCopyHash = sha256File(sourcePath);
        assertHash(expectedSourceHash, afterCopyHash.sha256);
        if (afterCopyHash.size_bytes !== sourceHash.size_bytes) {
          throw new AppError("SOURCE_CHANGED", "The source map size changed while the transaction was being staged.", false, {
            expected_size_bytes: sourceHash.size_bytes,
            actual_size_bytes: afterCopyHash.size_bytes
          });
        }

        const source = canonical.source as Record<string, unknown> | undefined;
        canonical.source = { ...(source ?? {}), path: sourcePath, ...afterCopyHash };
        const environment = await this.worker.request<Record<string, unknown>>("environment_status", { configured_files: {} }, correlationId);
        const created = this.store.finalize(project, staged, canonical, { ...(label ? { label } : {}), versions: transactionVersions(environment) });
        return {
          transaction_id: created.id,
          revision: 0,
          source: created.manifest.source,
          staged_source_hash: created.manifest.staged_copy_sha256,
          manifest_path: relativeProjectPath(project, created.paths.manifest),
          snapshot: created.snapshot,
          paths: {
            source: relativeProjectPath(project, created.paths.sourceMap),
            canonical: relativeProjectPath(project, created.paths.canonical),
            initial_revision: relativeProjectPath(project, join(created.paths.revisions, "0000-initial.json")),
            reports: relativeProjectPath(project, created.paths.reports),
            logs: relativeProjectPath(project, created.paths.logs)
          },
          versions: created.manifest.versions
        };
      } catch (error) {
        this.store.cleanupStagedSource(staged);
        throw error;
      }
    });
  }

  public async apply(projectId: string, transactionId: string, expectedRevision: number, operations: unknown[], dryRun: boolean, correlationId: string): Promise<Record<string, unknown>> {
    this.projects.assertMutationAllowed(projectId, "wc3_apply_operations");
    const project = this.projects.project(projectId);
    const parsedOperations = parseOperations(operations, project.config.max_operation_count, expectedRevision);
    if (parsedOperations.some(operation => operation.type === "set_script_source")) {
      this.projects.assertScriptMutationAllowed(projectId);
    }
    return withProjectLock(project, "apply_operations", async () => {
      const loaded = this.store.read(project, transactionId);
      this.assertActive(loaded.manifest);
      this.assertSourceUnchanged(project, loaded.manifest, loaded.paths.sourceMap);
      if (loaded.manifest.revision !== expectedRevision) {
        throw new AppError("PRECONDITION_FAILED", `Transaction revision ${loaded.manifest.revision} does not match expected revision ${expectedRevision}.`, false, {
          transaction_id: transactionId,
          expected_revision: expectedRevision,
          actual_revision: loaded.manifest.revision
        });
      }

      const operationIds = parsedOperations.map(operation => operation.operation_id);
      const existingIds = new Set(loaded.manifest.operation_ids.map(id => id.toUpperCase()));
      const duplicate = operationIds.find(id => existingIds.has(id.toUpperCase()));
      if (duplicate) throw new AppError("PRECONDITION_FAILED", `Operation '${duplicate}' has already been applied to this transaction.`);

      if (dryRun) {
        const result = await this.worker.request<Record<string, unknown>>("apply_operations", { canonical_path: loaded.paths.canonical, operations: parsedOperations }, correlationId);
        return {
          transaction_id: transactionId,
          revision: expectedRevision,
          dry_run: true,
          applied_operation_ids: stringArray(result.applied_operation_ids),
          diff: semanticDiff(result.diff),
          note: "Dry run only; no revision, canonical state, manifest, or report was written."
        };
      }

      const nextRevision = expectedRevision + 1;
      const revisionPath = join(loaded.paths.revisions, `${nextRevision.toString().padStart(4, "0")}-after-operations.json`);
      if (existsSync(revisionPath)) throw new AppError("TRANSACTION_STATE", `Revision artifact already exists: ${nextRevision}.`);
      const diffPath = join(loaded.paths.reports, `diff-${nextRevision.toString().padStart(4, "0")}.json`);
      if (existsSync(diffPath)) throw new AppError("TRANSACTION_STATE", `Semantic diff artifact already exists: ${nextRevision}.`);
      const previousRevisionPath = expectedRevision === 0
        ? join(loaded.paths.revisions, "0000-initial.json")
        : join(loaded.paths.revisions, `${expectedRevision.toString().padStart(4, "0")}-after-operations.json`);
      if (!existsSync(previousRevisionPath)) throw new AppError("TRANSACTION_STATE", `Current revision artifact is missing: ${expectedRevision}.`);

      let result: Record<string, unknown>;
      try {
        result = await this.worker.request<Record<string, unknown>>("apply_operations", {
          canonical_path: loaded.paths.canonical,
          output_path: revisionPath,
          operations: parsedOperations
        }, correlationId);
        if (!existsSync(revisionPath)) throw new AppError("ENGINE_PROTOCOL_ERROR", "The map engine reported success without writing the requested revision.");
        const appliedIds = stringArray(result.applied_operation_ids);
        if (appliedIds.length !== parsedOperations.length || appliedIds.some((id, index) => id !== parsedOperations[index]?.operation_id)) {
          throw new AppError("ENGINE_PROTOCOL_ERROR", "The map engine returned an incomplete or reordered operation result.");
        }
        const diff = semanticDiff(result.diff);
        writeJsonArtifact(project, relativeProjectPath(project, diffPath), diff, "semantic_diff");
        replaceFileAtomic(revisionPath, loaded.paths.canonical);
        const canonicalHash = sha256File(loaded.paths.canonical).sha256;
        const manifest: TransactionManifest = {
          ...loaded.manifest,
          revision: nextRevision,
          state: "modified",
          canonical_sha256: canonicalHash,
          revision_hashes: { ...loaded.manifest.revision_hashes, [String(nextRevision)]: canonicalHash },
          operation_ids: [...loaded.manifest.operation_ids, ...appliedIds],
          operation_revisions: { ...loaded.manifest.operation_revisions },
          operation_records: [...loaded.manifest.operation_records]
        };
        for (const operation of parsedOperations) {
          manifest.operation_revisions[operation.operation_id] = nextRevision;
          manifest.operation_records.push(operationRecord(operation, nextRevision));
        }
        this.store.update(loaded.paths, manifest);
      } catch (error) {
        try {
          if (existsSync(loaded.paths.canonical) && sha256File(loaded.paths.canonical).sha256 !== loaded.manifest.canonical_sha256) {
            replaceFileAtomic(previousRevisionPath, loaded.paths.canonical);
          }
        } catch {
          // Preserve the original error. The manifest still points at the
          // previous revision and recovery can use its revision artifact.
        }
        if (existsSync(revisionPath)) rmSync(revisionPath, { force: true });
        if (existsSync(diffPath)) rmSync(diffPath, { force: true });
        const appError = asAppError(error);
        if (isFatalTransactionError(appError)) this.store.markFailed(loaded.paths, loaded.manifest, { code: appError.code, message: appError.message, correlation_id: correlationId });
        throw appError;
      }

      const diff = semanticDiff(result.diff);
      const artifact = { kind: "semantic_diff", path: relativeProjectPath(project, diffPath), ...sha256File(diffPath) } satisfies ArtifactRef;
      const appliedIds = stringArray(result.applied_operation_ids);
      const manifest = this.store.read(project, transactionId).manifest;

      return {
        transaction_id: transactionId,
        revision: nextRevision,
        dry_run: false,
        applied_operation_ids: appliedIds,
        diff,
        diff_artifact: artifact,
        canonical_path: relativeProjectPath(project, loaded.paths.canonical),
        revision_path: relativeProjectPath(project, join(loaded.paths.revisions, `${nextRevision.toString().padStart(4, "0")}-after-operations.json`)),
        source_sha256: manifest.source.sha256
      };
    });
  }

  public async diff(projectId: string, transactionId: string, fromRevision: number | undefined, toRevision: number | undefined): Promise<Record<string, unknown>> {
    this.projects.assertToolAvailable(projectId, "wc3_transaction_diff");
    const project = this.projects.project(projectId);
    const loaded = this.store.read(project, transactionId);
    const from = fromRevision ?? 0;
    const to = toRevision ?? loaded.manifest.revision;
    if (!Number.isInteger(from) || !Number.isInteger(to) || from < 0 || to < from || to > loaded.manifest.revision) throw new AppError("INVALID_ARGUMENT", "Revision range is invalid.");

    const reports: Array<{ path: string; value: Record<string, unknown> }> = [];
    for (let revision = from + 1; revision <= to; revision++) {
      const reportPath = join(loaded.paths.reports, `diff-${revision.toString().padStart(4, "0")}.json`);
      if (!existsSync(reportPath)) throw new AppError("FILE_NOT_FOUND", `Semantic diff report for revision ${revision} is missing.`);
      let value: Record<string, unknown>;
      try {
        value = JSON.parse(readFileSync(reportPath, "utf8")) as Record<string, unknown>;
      } catch (error) {
        throw new AppError("INTERNAL_ERROR", `Semantic diff report for revision ${revision} is invalid.`, false, {}, { cause: error });
      }
      reports.push({ path: reportPath, value });
    }

    const changes = reports.flatMap(item => Array.isArray(item.value.changes) ? item.value.changes : []);
    const groups = groupChanges(changes);
    const singleReport = reports.length === 1 ? reports[0] : undefined;
    return {
      transaction_id: transactionId,
      source_sha256: loaded.manifest.source.sha256,
      from_revision: from,
      to_revision: to,
      diff: { schema_version: SCHEMA_VERSION, changes },
      groups,
      ...(singleReport ? { artifact_path: relativeProjectPath(project, singleReport.path) } : {}),
      ...(changes.length === 0 ? { note: "No semantic changes were recorded for this revision range." } : {})
    };
  }

  public async validate(projectId: string, transactionId: string, revision: number, correlationId: string): Promise<Record<string, unknown>> {
    this.projects.assertMutationAllowed(projectId, "wc3_validate_transaction");
    const project = this.projects.project(projectId);
    return withProjectLock(project, "validate_transaction", async () => {
      const loaded = this.store.read(project, transactionId);
      this.assertActive(loaded.manifest);
      this.assertSourceUnchanged(project, loaded.manifest, loaded.paths.sourceMap);
      if (loaded.manifest.revision !== revision) throw new AppError("PRECONDITION_FAILED", `Transaction revision ${loaded.manifest.revision} does not match expected revision ${revision}.`);
      const rawReport = await this.worker.request<Record<string, unknown>>("validate_canonical", {
        canonical_path: loaded.paths.canonical,
        source_map_path: loaded.paths.sourceMap,
        validation_context: {
          project_id: projectId,
          ...(projectId === "hero-team-wars" ? { protected_region_names: ["Arena_A", "Camp_A_Player1"], explicit_teams: [[1, 2], [3, 4]] } : {})
        }
      }, correlationId);
      const reportPath = join(loaded.paths.reports, `validation-${revision.toString().padStart(4, "0")}.json`);
      const report: Record<string, unknown> = {
        ...rawReport,
        transaction_id: transactionId,
        revision,
        source_sha256: loaded.manifest.source.sha256,
        validator_version: String(rawReport.validator_version ?? "unknown"),
        target: String(rawReport.target ?? "transaction_build")
      };
      const artifact = writeJsonArtifact(project, relativeProjectPath(project, reportPath), report, "validation_report");
      const manifest = loaded.manifest;
      manifest.validation_reports = Array.from(new Set([...manifest.validation_reports, artifact.path]));
      manifest.state = report.buildable === true ? "validated" : "modified";
      this.store.update(loaded.paths, manifest);
      if (report.buildable !== true) throw new AppError("VALIDATION_FAILED", "The transaction contains validation errors.", false, { report_path: artifact.path, report_sha256: artifact.sha256 });
      return {
        transaction_id: transactionId,
        revision,
        source_sha256: manifest.source.sha256,
        validator_version: report.validator_version,
        target: report.target,
        buildable: report.buildable,
        report_path: artifact.path,
        report_sha256: artifact.sha256,
        report,
        artifact
      };
    });
  }

  public async discard(projectId: string, transactionId: string, expectedSourceHash: string, confirmation: boolean, correlationId: string): Promise<Record<string, unknown>> {
    this.projects.assertMutationAllowed(projectId, "wc3_discard_transaction");
    if (!confirmation) throw new AppError("INVALID_ARGUMENT", "Discard requires explicit confirmation=true.");
    const project = this.projects.project(projectId);
    return withProjectLock(project, "discard_transaction", async () => this.store.discard(project, transactionId, expectedSourceHash, correlationId));
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
    const current = sha256File(manifest.source.path);
    if (current.sha256 !== manifest.source.sha256 || current.size_bytes !== manifest.source.size_bytes) throw new AppError("SOURCE_CHANGED", "The source map changed since this transaction was created.", false, { expected_sha256: manifest.source.sha256, actual_sha256: current.sha256 });
    const staged = sha256File(stagedPath);
    if (staged.sha256 !== manifest.staged_copy_sha256 || staged.size_bytes !== manifest.source.size_bytes) throw new AppError("SOURCE_CHANGED", "The isolated transaction source copy changed after staging.", false, { expected_sha256: manifest.staged_copy_sha256, actual_sha256: staged.sha256 });
  }
}

function parseOperations(operations: unknown[], maxOperationCount: number, expectedRevision: number): OperationInput[] {
  if (operations.length === 0) throw new AppError("INVALID_ARGUMENT", "At least one operation is required.");
  if (operations.length > maxOperationCount) throw new AppError("INVALID_ARGUMENT", `The operation batch exceeds the configured limit of ${maxOperationCount}.`);
  const parsed: OperationInput[] = [];
  for (const operation of operations) {
    const result = operationSchema.safeParse(operation);
    if (!result.success) throw new AppError("INVALID_ARGUMENT", "One or more typed operations are invalid.", false, { issues: result.error.issues });
    parsed.push(result.data);
  }
  const ids = new Set<string>();
  for (const operation of parsed) {
    if (!ids.add(operation.operation_id.toUpperCase())) throw new AppError("INVALID_ARGUMENT", `Operation '${operation.operation_id}' occurs more than once in the batch.`);
    if (operation.expected_revision !== undefined && operation.expected_revision !== expectedRevision) {
      throw new AppError("PRECONDITION_FAILED", `Operation '${operation.operation_id}' targets revision ${operation.expected_revision}, but the request expects revision ${expectedRevision}.`);
    }
  }
  return parsed;
}

function operationRecord(operation: OperationInput, revision: number): TransactionOperationRecord {
  return {
    operation_id: operation.operation_id,
    revision,
    type: operation.type,
    target: operation.target,
    rationale: operation.rationale,
    ...(operation.design_reference ? { design_reference: operation.design_reference } : {})
  };
}

function semanticDiff(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || !Array.isArray((value as Record<string, unknown>).changes)) {
    throw new AppError("ENGINE_PROTOCOL_ERROR", "The map engine returned an invalid semantic diff.");
  }
  const changes = (value as Record<string, unknown>).changes as unknown[];
  for (const change of changes) {
    if (!change || typeof change !== "object") throw new AppError("ENGINE_PROTOCOL_ERROR", "The map engine returned an invalid semantic diff change.");
    const item = change as Record<string, unknown>;
    const requiredStrings = ["component", "path", "change_type", "operation_id", "provenance"];
    if (requiredStrings.some(key => typeof item[key] !== "string") || !["added", "removed", "changed"].includes(item.change_type as string)) {
      throw new AppError("ENGINE_PROTOCOL_ERROR", "The map engine returned a malformed semantic diff change.");
    }
    if (!Object.prototype.hasOwnProperty.call(item, "before") || !Object.prototype.hasOwnProperty.call(item, "after")) {
      throw new AppError("ENGINE_PROTOCOL_ERROR", "The map engine returned a semantic diff change without before/after values.");
    }
    if (item.target !== null && (typeof item.target !== "object" || Array.isArray(item.target))) {
      throw new AppError("ENGINE_PROTOCOL_ERROR", "The map engine returned a semantic diff change with an invalid target.");
    }
    if (item.design_reference !== undefined && typeof item.design_reference !== "string") {
      throw new AppError("ENGINE_PROTOCOL_ERROR", "The map engine returned an invalid semantic diff design reference.");
    }
  }
  return { schema_version: SCHEMA_VERSION, changes };
}

function stringArray(value: unknown): string[] {
  if (!Array.isArray(value) || value.some(item => typeof item !== "string")) throw new AppError("ENGINE_PROTOCOL_ERROR", "The map engine returned an invalid operation ID list.");
  return value as string[];
}

function groupChanges(changes: unknown[]): unknown[] {
  const groups = new Map<string, { component: string; target: unknown; changes: unknown[] }>();
  for (const change of changes) {
    if (!change || typeof change !== "object") continue;
    const item = change as Record<string, unknown>;
    const component = typeof item.component === "string" ? item.component : "unknown";
    const target = item.target ?? null;
    const key = `${component}:${JSON.stringify(target)}`;
    const group = groups.get(key) ?? { component, target, changes: [] };
    group.changes.push(change);
    groups.set(key, group);
  }
  return Array.from(groups.values());
}

function transactionVersions(environment: Record<string, unknown>): TransactionVersions {
  const dependencies: Record<string, string> = {};
  for (const key of ["war3net_io_mpq", "war3net_build_core"] as const) {
    if (typeof environment[key] === "string") dependencies[key] = environment[key] as string;
  }
  return {
    server: SERVER_VERSION,
    engine: typeof environment.engine_version === "string" ? environment.engine_version : "unknown",
    schema: SCHEMA_VERSION,
    worker_protocol: WORKER_PROTOCOL_VERSION,
    dependencies
  };
}

function assertHash(expected: string, actual: string): void {
  if (!/^[0-9A-F]{64}$/i.test(expected) || expected.toUpperCase() !== actual.toUpperCase()) throw new AppError("SOURCE_CHANGED", "The expected source hash does not match the current source map.", false, { expected_sha256: expected, actual_sha256: actual });
}

function isFatalTransactionError(error: AppError): boolean {
  return ["ENGINE_UNAVAILABLE", "ENGINE_PROTOCOL_ERROR"].includes(error.code);
}
