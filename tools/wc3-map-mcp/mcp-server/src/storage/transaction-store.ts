import {
  closeSync,
  copyFileSync,
  existsSync,
  fsyncSync,
  lstatSync,
  mkdirSync,
  openSync,
  readFileSync,
  realpathSync,
  renameSync,
  rmSync,
  writeSync
} from "node:fs";
import { randomUUID } from "node:crypto";
import { basename, dirname, extname, join, resolve } from "node:path";
import type { ResolvedProject } from "../config/resolve-project.js";
import { isWithin } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";
import { sha256File, type ArtifactRef } from "../services/artifact-service.js";

export type TransactionState = "created" | "staged" | "modified" | "validated" | "built" | "tested" | "promoted" | "discarded" | "failed";

export interface TransactionVersions {
  server: string;
  engine: string;
  schema: string;
  worker_protocol: string;
  dependencies: Record<string, string>;
}

export interface TransactionOperationRecord {
  operation_id: string;
  revision: number;
  type: string;
  target: Record<string, unknown>;
  rationale: string;
  design_reference?: string;
}

export interface TransactionManifest {
  schema_version: "1.0";
  transaction_id: string;
  project_id: string;
  label?: string;
  state: TransactionState;
  revision: number;
  source: { path: string; size_bytes: number; modified_utc: string; sha256: string };
  staged_copy_sha256: string;
  canonical_sha256: string;
  revision_hashes: Record<string, string>;
  created_utc: string;
  updated_utc: string;
  versions: TransactionVersions;
  operation_ids: string[];
  operation_revisions: Record<string, number>;
  operation_records: TransactionOperationRecord[];
  validation_reports: string[];
  build_ids: string[];
  test_session_ids: string[];
  failure?: { code: string; message: string; correlation_id: string };
}

export interface TransactionPaths {
  directory: string;
  manifest: string;
  sourceMap: string;
  canonical: string;
  revisions: string;
  reports: string;
  logs: string;
}

export interface StagedSource {
  id: string;
  sourcePath: string;
  paths: TransactionPaths;
  finalPaths: TransactionPaths;
  sourceHash: ReturnType<typeof sha256File>;
  stagedHash: ReturnType<typeof sha256File>;
}

export interface TransactionCreateOptions {
  label?: string;
  versions?: TransactionVersions;
}

const TRANSACTION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const SHA256 = /^[0-9A-F]{64}$/;
const STATES: TransactionState[] = ["created", "staged", "modified", "validated", "built", "tested", "promoted", "discarded", "failed"];

export class TransactionStore {
  public paths(project: ResolvedProject, transactionId: string, extension = ".w3m"): TransactionPaths {
    assertTransactionId(transactionId);
    const normalizedExtension = extension.toLowerCase();
    if (normalizedExtension !== ".w3m" && normalizedExtension !== ".w3x") {
      throw new AppError("INVALID_ARGUMENT", "Transaction source extension must be .w3m or .w3x.");
    }

    return this.pathsForDirectory(join(project.stagingRoot, transactionId), normalizedExtension);
  }

  public stageSource(project: ResolvedProject, sourcePath: string, sourceHash: ReturnType<typeof sha256File>): StagedSource {
    ensureDirectory(project.stagingRoot);
    const id = randomUUID();
    const extension = extname(sourcePath).toLowerCase() === ".w3x" ? ".w3x" : ".w3m";
    const finalPaths = this.paths(project, id, extension);
    const temporaryDirectory = join(project.stagingRoot, `.transaction-${id}.tmp`);
    const paths = this.pathsForDirectory(temporaryDirectory, extension);

    try {
      mkdirSync(temporaryDirectory, { recursive: false });
      for (const directory of [dirname(paths.sourceMap), dirname(paths.canonical), paths.revisions, paths.reports, paths.logs]) {
        mkdirSync(directory, { recursive: true });
      }

      copyFileAtomic(sourcePath, paths.sourceMap);
      const stagedHash = sha256File(paths.sourceMap);
      if (stagedHash.sha256 !== sourceHash.sha256 || stagedHash.size_bytes !== sourceHash.size_bytes) {
        throw new AppError("SOURCE_CHANGED", "The staged source copy did not match the captured source hash.", false, {
          expected_sha256: sourceHash.sha256,
          actual_sha256: stagedHash.sha256,
          expected_size_bytes: sourceHash.size_bytes,
          actual_size_bytes: stagedHash.size_bytes
        });
      }

      return { id, sourcePath, paths, finalPaths, sourceHash, stagedHash };
    } catch (error) {
      cleanupTemporaryDirectory(temporaryDirectory);
      throw error;
    }
  }

  public finalize(
    project: ResolvedProject,
    staged: StagedSource,
    canonical: unknown,
    options: TransactionCreateOptions = {}
  ): { id: string; paths: TransactionPaths; manifest: TransactionManifest; snapshot: ArtifactRef } {
    assertTemporaryTransactionDirectory(project, staged.paths.directory, staged.id);
    const finalPaths = staged.finalPaths;
    if (existsSync(finalPaths.directory)) {
      throw new AppError("INTERNAL_ERROR", `Transaction directory already exists: ${staged.id}`);
    }

    try {
      writeJsonAtomic(staged.paths.canonical, canonical);
      writeJsonAtomic(join(staged.paths.revisions, "0000-initial.json"), canonical);
      const canonicalHash = sha256File(staged.paths.canonical).sha256;
      const now = new Date().toISOString();
      const manifest: TransactionManifest = {
        schema_version: "1.0",
        transaction_id: staged.id,
        project_id: project.id,
        ...(options.label ? { label: options.label } : {}),
        state: "staged",
        revision: 0,
        source: { path: staged.sourcePath, ...staged.sourceHash },
        staged_copy_sha256: staged.stagedHash.sha256,
        canonical_sha256: canonicalHash,
        revision_hashes: { "0": canonicalHash },
        created_utc: now,
        updated_utc: now,
        versions: options.versions ?? defaultVersions(),
        operation_ids: [],
        operation_revisions: {},
        operation_records: [],
        validation_reports: [],
        build_ids: [],
        test_session_ids: []
      };

      writeJsonAtomic(staged.paths.manifest, manifest);
      renameSync(staged.paths.directory, finalPaths.directory);
      return {
        id: staged.id,
        paths: finalPaths,
        manifest,
        snapshot: { kind: "transaction_source_snapshot", path: finalPaths.sourceMap, ...staged.stagedHash }
      };
    } catch (error) {
      cleanupTemporaryDirectory(staged.paths.directory);
      throw error;
    }
  }

  public cleanupStagedSource(staged: StagedSource): void {
    cleanupTemporaryDirectory(staged.paths.directory);
  }

  /**
   * Backwards-compatible synchronous helper for unit callers. The service
   * uses stageSource/finalize so it can inspect the copied source before the
   * transaction becomes visible.
   */
  public create(
    project: ResolvedProject,
    sourcePath: string,
    sourceHash: ReturnType<typeof sha256File>,
    canonical: unknown,
    options: TransactionCreateOptions = {}
  ): { id: string; paths: TransactionPaths; manifest: TransactionManifest; snapshot: ArtifactRef } {
    const staged = this.stageSource(project, sourcePath, sourceHash);
    return this.finalize(project, staged, canonical, options);
  }

  public read(project: ResolvedProject, transactionId: string): { paths: TransactionPaths; manifest: TransactionManifest } {
    assertTransactionId(transactionId);
    const directory = join(project.stagingRoot, transactionId);
    assertManagedTransactionDirectory(project, directory, transactionId);
    const manifestPath = join(directory, "manifest.json");
    assertManagedFile(manifestPath, "transaction manifest");

    const manifest = parseManifest(readFileSync(manifestPath, "utf8"), transactionId, project.id);
    const sourceExtension = existingSourceExtension(directory);
    const paths = this.paths(project, transactionId, sourceExtension);
    const currentRevisionPath = manifest.revision === 0
      ? join(paths.revisions, "0000-initial.json")
      : join(paths.revisions, `${manifest.revision.toString().padStart(4, "0")}-after-operations.json`);
    for (const path of [paths.sourceMap, paths.canonical, join(paths.revisions, "0000-initial.json"), currentRevisionPath]) {
      assertManagedFile(path, "transaction artifact");
    }

    const stagedHash = sha256File(paths.sourceMap);
    if (stagedHash.sha256 !== manifest.staged_copy_sha256 || stagedHash.size_bytes !== manifest.source.size_bytes) {
      throw new AppError("SOURCE_CHANGED", "The isolated transaction source copy no longer matches its manifest.", false, {
        transaction_id: transactionId,
        expected_sha256: manifest.staged_copy_sha256,
        actual_sha256: stagedHash.sha256,
        expected_size_bytes: manifest.source.size_bytes,
        actual_size_bytes: stagedHash.size_bytes
      });
    }

    const canonicalHash = sha256File(paths.canonical).sha256;
    if (canonicalHash !== manifest.canonical_sha256) {
      throw new AppError("SOURCE_CHANGED", "The staged canonical state no longer matches its transaction manifest.", false, {
        transaction_id: transactionId,
        expected_sha256: manifest.canonical_sha256,
        actual_sha256: canonicalHash
      });
    }
    const currentRevisionHash = sha256File(currentRevisionPath).sha256;
    if (currentRevisionHash !== manifest.revision_hashes[String(manifest.revision)]) {
      throw new AppError("SOURCE_CHANGED", "The current revision artifact no longer matches its transaction manifest.", false, {
        transaction_id: transactionId,
        revision: manifest.revision,
        expected_sha256: manifest.revision_hashes[String(manifest.revision)],
        actual_sha256: currentRevisionHash
      });
    }
    return { paths, manifest };
  }

  public update(paths: TransactionPaths, manifest: TransactionManifest): void {
    assertManagedTransactionDirectoryByPath(paths.directory);
    const current = parseManifest(readFileSync(paths.manifest, "utf8"), manifest.transaction_id, manifest.project_id);
    assertTransition(current.state, manifest.state);
    validateManifest(manifest, manifest.transaction_id, manifest.project_id);
    manifest.updated_utc = new Date().toISOString();
    writeJsonAtomic(paths.manifest, manifest);
  }

  public markFailed(paths: TransactionPaths, manifest: TransactionManifest, failure: { code: string; message: string; correlation_id: string }): void {
    const next = { ...manifest, state: "failed" as const, failure };
    try {
      this.update(paths, next);
    } catch {
      // Preserve the original failure. A damaged transaction is safer to
      // leave in place for manual recovery than to delete automatically.
    }
  }

  public discard(project: ResolvedProject, transactionId: string, sourceHash: string, correlationId: string = randomUUID()): Record<string, unknown> {
    const { paths, manifest } = this.read(project, transactionId);
    if (manifest.source.sha256 !== sourceHash.toUpperCase()) {
      throw new AppError("SOURCE_CHANGED", "The expected source hash does not match the transaction manifest.", false, {
        expected_sha256: sourceHash.toUpperCase(),
        actual_sha256: manifest.source.sha256
      });
    }
    if (["built", "tested", "promoted", "discarded"].includes(manifest.state) || manifest.build_ids.length > 0 || manifest.test_session_ids.length > 0) {
      throw new AppError("DISCARD_FAILED", `Transaction '${transactionId}' has downstream artifacts and cannot be discarded.`, false, {
        state: manifest.state,
        build_ids: manifest.build_ids,
        test_session_ids: manifest.test_session_ids
      });
    }

    assertManagedTransactionDirectory(project, paths.directory, transactionId);
    const tombstone = join(project.artifactRoot, "audit", `discard-${transactionId}.json`);
    const pending = {
      schema_version: "1.0",
      transaction_id: transactionId,
      project_id: project.id,
      source_sha256: sourceHash.toUpperCase(),
      discarded_utc: new Date().toISOString(),
      state_before_discard: manifest.state,
      correlation_id: correlationId,
      status: "pending"
    };
    writeJsonAtomic(tombstone, pending);

    try {
      rmSync(paths.directory, { recursive: true, force: false });
      if (existsSync(paths.directory)) {
        throw new AppError("DISCARD_FAILED", "The transaction directory still exists after deletion.");
      }
      writeJsonAtomic(tombstone, { ...pending, status: "discarded" });
    } catch (error) {
      try {
        writeJsonAtomic(tombstone, { ...pending, status: "failed", error: error instanceof Error ? error.message : String(error) });
      } catch {
        // Keep the pending tombstone if the failure report itself cannot be written.
      }
      throw error;
    }

    return { transaction_id: transactionId, discarded: true, tombstone_path: tombstone };
  }

  private pathsForDirectory(directory: string, extension: string): TransactionPaths {
    return {
      directory,
      manifest: join(directory, "manifest.json"),
      sourceMap: join(directory, "source", `source-map${extension}`),
      canonical: join(directory, "working", "canonical-map.json"),
      revisions: join(directory, "revisions"),
      reports: join(directory, "reports"),
      logs: join(directory, "logs")
    };
  }
}

function defaultVersions(): TransactionVersions {
  return { server: "0.1.0", engine: "unknown", schema: "1.0", worker_protocol: "1.0", dependencies: {} };
}

function assertTransactionId(value: string): void {
  if (!TRANSACTION_ID.test(value)) throw new AppError("INVALID_ARGUMENT", "Transaction ID must be a UUID.");
}

function ensureDirectory(path: string): void {
  mkdirSync(path, { recursive: true });
  const stat = lstatSync(path);
  if (!stat.isDirectory() || stat.isSymbolicLink()) throw new AppError("PATH_OUTSIDE_ROOT", `Configured transaction root is not a real directory: ${path}`);
}

function assertManagedTransactionDirectoryByPath(directory: string, kind: "transaction" | "temporary" = "transaction"): void {
  const parent = dirname(directory);
  const target = resolve(directory);
  if (!isWithin(parent, target) || resolve(parent) === target) {
    throw new AppError("DISCARD_FAILED", "The transaction directory is not a strict child of its configured root.");
  }
  const name = basename(target);
  if (kind === "transaction" ? !TRANSACTION_ID.test(name) : !/^\.transaction-[0-9a-f-]{36}\.tmp$/i.test(name)) {
    throw new AppError("PATH_OUTSIDE_ROOT", "The transaction directory name is not MCP-managed.");
  }
  const stat = lstatSync(target);
  if (!stat.isDirectory() || stat.isSymbolicLink()) throw new AppError("DISCARD_FAILED", "The transaction directory must be a real directory.");
}

function assertManagedTransactionDirectory(project: ResolvedProject, directory: string, transactionId: string): void {
  const root = resolve(project.stagingRoot);
  const target = resolve(directory);
  if (!isWithin(root, target) || target === root || dirname(target).toLowerCase() !== root.toLowerCase()) {
    throw new AppError("PATH_OUTSIDE_ROOT", "The transaction directory is not a direct child of the configured transaction root.", false, { transaction_id: transactionId });
  }
  assertManagedTransactionDirectoryByPath(target);

  const realRoot = existsSync(root) ? realpathSync(root) : root;
  const realTarget = realpathSync(target);
  if (realTarget === realRoot || dirname(realTarget).toLowerCase() !== realRoot.toLowerCase()) {
    throw new AppError("PATH_OUTSIDE_ROOT", "The transaction directory resolves outside the configured transaction root.", false, { transaction_id: transactionId });
  }
}

function assertTemporaryTransactionDirectory(project: ResolvedProject, directory: string, transactionId: string): void {
  const root = resolve(project.stagingRoot);
  const target = resolve(directory);
  if (!isWithin(root, target) || dirname(target).toLowerCase() !== root.toLowerCase() || !target.endsWith(`.transaction-${transactionId}.tmp`)) {
    throw new AppError("PATH_OUTSIDE_ROOT", "The temporary transaction directory is outside the configured transaction root.");
  }
  assertManagedTransactionDirectoryByPath(target, "temporary");
}

function assertManagedFile(path: string, label: string): void {
  if (!existsSync(path)) throw new AppError("FILE_NOT_FOUND", `The ${label} does not exist: ${path}`);
  const stat = lstatSync(path);
  if (!stat.isFile() || stat.isSymbolicLink()) throw new AppError("PATH_OUTSIDE_ROOT", `The ${label} must be a regular file: ${path}`);
}

function existingSourceExtension(directory: string): ".w3m" | ".w3x" {
  const w3m = join(directory, "source", "source-map.w3m");
  const w3x = join(directory, "source", "source-map.w3x");
  const hasW3m = existsSync(w3m);
  const hasW3x = existsSync(w3x);
  if (hasW3m === hasW3x) throw new AppError("INTERNAL_ERROR", "A transaction must contain exactly one staged .w3m or .w3x source copy.");
  return hasW3x ? ".w3x" : ".w3m";
}

function parseManifest(text: string, transactionId: string, projectId: string): TransactionManifest {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text) as unknown;
  } catch (error) {
    throw new AppError("INTERNAL_ERROR", `Transaction manifest is invalid: ${transactionId}`, false, {}, { cause: error });
  }
  if (!parsed || typeof parsed !== "object") throw new AppError("INTERNAL_ERROR", "Transaction manifest must be an object.");
  const manifest = parsed as Partial<TransactionManifest>;
  validateManifest(manifest, transactionId, projectId);
  return manifest as TransactionManifest;
}

function validateManifest(value: Partial<TransactionManifest>, transactionId: string, projectId: string): void {
  if (value.schema_version !== "1.0" || value.transaction_id !== transactionId || value.project_id !== projectId) {
    throw new AppError("INTERNAL_ERROR", "Transaction manifest identity or schema does not match the requested transaction.");
  }
  if (!STATES.includes(value.state as TransactionState) || !Number.isInteger(value.revision) || (value.revision ?? -1) < 0) {
    throw new AppError("INTERNAL_ERROR", "Transaction manifest state or revision is invalid.");
  }
  if (!value.source || typeof value.source.path !== "string" || !Number.isInteger(value.source.size_bytes) || typeof value.source.modified_utc !== "string" || !SHA256.test(value.source.sha256)) {
    throw new AppError("INTERNAL_ERROR", "Transaction manifest source identity is invalid.");
  }
  if (!SHA256.test(value.staged_copy_sha256 ?? "") || !SHA256.test(value.canonical_sha256 ?? "")) {
    throw new AppError("INTERNAL_ERROR", "Transaction manifest hashes are invalid.");
  }
  if (!value.versions || typeof value.versions.server !== "string" || typeof value.versions.engine !== "string" || typeof value.versions.schema !== "string" || typeof value.versions.worker_protocol !== "string" || !value.versions.dependencies || typeof value.versions.dependencies !== "object") {
    throw new AppError("INTERNAL_ERROR", "Transaction manifest version metadata is invalid.");
  }
  for (const key of ["operation_ids", "operation_records", "validation_reports", "build_ids", "test_session_ids"] as const) {
    if (!Array.isArray(value[key])) throw new AppError("INTERNAL_ERROR", `Transaction manifest field '${key}' is invalid.`);
  }
  if (!value.revision_hashes || typeof value.revision_hashes !== "object") throw new AppError("INTERNAL_ERROR", "Transaction revision hashes are invalid.");
}

function assertTransition(from: TransactionState, to: TransactionState): void {
  if (from === to) return;
  const allowed: Record<TransactionState, TransactionState[]> = {
    created: ["staged", "failed"],
    staged: ["modified", "validated", "failed"],
    modified: ["modified", "validated", "failed"],
    validated: ["modified", "validated", "built", "failed"],
    built: ["tested", "promoted", "failed"],
    tested: ["promoted", "failed"],
    promoted: [],
    discarded: [],
    failed: []
  };
  if (!allowed[from].includes(to)) throw new AppError("TRANSACTION_STATE", `Invalid transaction state transition '${from}' -> '${to}'.`);
}

function cleanupTemporaryDirectory(directory: string): void {
  if (existsSync(directory)) rmSync(directory, { recursive: true, force: true });
}

export function writeJsonAtomic(path: string, value: unknown): void {
  mkdirSync(dirname(path), { recursive: true });
  const temp = `${path}.${randomUUID()}.tmp`;
  const text = `${JSON.stringify(value, null, 2)}\n`;
  try {
    writeFileDurably(temp, text);
    JSON.parse(readFileSync(temp, "utf8")) as unknown;
    replaceFile(temp, path);
  } finally {
    if (existsSync(temp)) rmSync(temp, { force: true });
  }
}

export function copyFileAtomic(source: string, destination: string): void {
  mkdirSync(dirname(destination), { recursive: true });
  const temp = `${destination}.${randomUUID()}.tmp`;
  try {
    copyFileSync(source, temp);
    durableFile(temp);
    if (existsSync(destination)) throw new AppError("OUTPUT_EXISTS", `The destination already exists: ${destination}`);
    renameSync(temp, destination);
  } finally {
    if (existsSync(temp)) rmSync(temp, { force: true });
  }
}

export function replaceFileAtomic(source: string, destination: string): void {
  mkdirSync(dirname(destination), { recursive: true });
  const temp = `${destination}.${randomUUID()}.tmp`;
  const backup = `${destination}.${randomUUID()}.bak`;
  try {
    copyFileSync(source, temp);
    durableFile(temp);
    if (existsSync(destination)) renameSync(destination, backup);
    try {
      renameSync(temp, destination);
    } catch (error) {
      if (!existsSync(destination) && existsSync(backup)) renameSync(backup, destination);
      throw error;
    }
    if (existsSync(backup)) rmSync(backup, { force: true });
  } finally {
    if (existsSync(temp)) rmSync(temp, { force: true });
    if (existsSync(backup) && existsSync(destination)) rmSync(backup, { force: true });
  }
}

function writeFileDurably(path: string, text: string): void {
  const descriptor = openSync(path, "wx");
  try {
    const bytes = Buffer.from(text, "utf8");
    writeSync(descriptor, bytes, 0, bytes.length, 0);
    tryFsync(descriptor);
  } finally {
    closeSync(descriptor);
  }
}

function durableFile(path: string): void {
  const descriptor = openSync(path, "r");
  try {
    tryFsync(descriptor);
  } finally {
    closeSync(descriptor);
  }
}

function tryFsync(descriptor: number): void {
  try {
    fsyncSync(descriptor);
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code;
    if (code !== "EPERM" && code !== "ENOTSUP" && code !== "EINVAL") throw error;
    // Some Windows filesystems reject FlushFileBuffers on a read-only
    // handle. The uniquely named, same-directory temp-file rename still
    // provides atomic visibility in that environment.
  }
}

function replaceFile(temp: string, destination: string): void {
  if (!existsSync(destination)) {
    renameSync(temp, destination);
    return;
  }
  const backup = `${destination}.${randomUUID()}.bak`;
  renameSync(destination, backup);
  try {
    renameSync(temp, destination);
    rmSync(backup, { force: true });
  } catch (error) {
    if (!existsSync(destination) && existsSync(backup)) renameSync(backup, destination);
    throw error;
  }
}
