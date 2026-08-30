import { copyFileSync, existsSync, mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { randomUUID } from "node:crypto";
import type { ResolvedProject } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";
import { sha256File, type ArtifactRef } from "../services/artifact-service.js";

export type TransactionState = "created" | "staged" | "modified" | "validated" | "built" | "tested" | "promoted" | "discarded" | "failed";

export interface TransactionManifest {
  schema_version: "1.0";
  transaction_id: string;
  project_id: string;
  state: TransactionState;
  revision: number;
  source: { path: string; size_bytes: number; modified_utc: string; sha256: string };
  staged_copy_sha256: string;
  created_utc: string;
  updated_utc: string;
  operation_ids: string[];
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

export class TransactionStore {
  public paths(project: ResolvedProject, transactionId: string): TransactionPaths {
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(transactionId)) {
      throw new AppError("INVALID_ARGUMENT", "Transaction ID must be a UUID.");
    }
    const directory = join(project.stagingRoot, transactionId);
    return { directory, manifest: join(directory, "manifest.json"), sourceMap: join(directory, "source", "source-map.w3m"), canonical: join(directory, "working", "canonical-map.json"), revisions: join(directory, "revisions"), reports: join(directory, "reports"), logs: join(directory, "logs") };
  }

  public create(project: ResolvedProject, sourcePath: string, sourceHash: ReturnType<typeof sha256File>, canonical: unknown): { id: string; paths: TransactionPaths; manifest: TransactionManifest; snapshot: ArtifactRef } {
    const id = randomUUID();
    const paths = this.paths(project, id);
    for (const path of [paths.directory, dirname(paths.sourceMap), dirname(paths.canonical), paths.revisions, paths.reports, paths.logs]) mkdirSync(path, { recursive: true });
    copyFileSync(sourcePath, paths.sourceMap);
    const stagedHash = sha256File(paths.sourceMap);
    if (stagedHash.sha256 !== sourceHash.sha256 || stagedHash.size_bytes !== sourceHash.size_bytes) {
      rmSync(paths.directory, { recursive: true, force: true });
      throw new AppError("INTERNAL_ERROR", "The staged source copy did not match the source hash.");
    }
    writeJsonAtomic(paths.canonical, canonical);
    writeJsonAtomic(join(paths.revisions, "0000-initial.json"), canonical);
    const now = new Date().toISOString();
    const manifest: TransactionManifest = { schema_version: "1.0", transaction_id: id, project_id: project.id, state: "staged", revision: 0, source: { path: sourcePath, ...sourceHash }, staged_copy_sha256: stagedHash.sha256, created_utc: now, updated_utc: now, operation_ids: [], validation_reports: [], build_ids: [], test_session_ids: [] };
    writeJsonAtomic(paths.manifest, manifest);
    return { id, paths, manifest, snapshot: { kind: "transaction_source_snapshot", path: paths.sourceMap, ...stagedHash } };
  }

  public read(project: ResolvedProject, transactionId: string): { paths: TransactionPaths; manifest: TransactionManifest } {
    const paths = this.paths(project, transactionId);
    if (!existsSync(paths.manifest)) throw new AppError("FILE_NOT_FOUND", `Transaction '${transactionId}' was not found.`);
    let manifest: TransactionManifest;
    try { manifest = JSON.parse(readFileSync(paths.manifest, "utf8")) as TransactionManifest; } catch (error) { throw new AppError("INTERNAL_ERROR", `Transaction manifest is invalid: ${transactionId}`, false, {}, { cause: error }); }
    if (manifest.transaction_id !== transactionId || manifest.project_id !== project.id) throw new AppError("INTERNAL_ERROR", "Transaction manifest identity does not match the requested project or ID.");
    return { paths, manifest };
  }

  public update(paths: TransactionPaths, manifest: TransactionManifest): void {
    manifest.updated_utc = new Date().toISOString();
    writeJsonAtomic(paths.manifest, manifest);
  }

  public discard(project: ResolvedProject, transactionId: string, sourceHash: string): Record<string, unknown> {
    const { paths, manifest } = this.read(project, transactionId);
    if (manifest.source.sha256 !== sourceHash) throw new AppError("SOURCE_CHANGED", "The expected source hash does not match the transaction manifest.");
    if (["built", "tested", "promoted", "discarded"].includes(manifest.state) || manifest.build_ids.length > 0 || manifest.test_session_ids.length > 0) {
      throw new AppError("DISCARD_FAILED", `Transaction '${transactionId}' has downstream artifacts and cannot be discarded.`, false, { state: manifest.state, build_ids: manifest.build_ids, test_session_ids: manifest.test_session_ids });
    }
    if (!existsSync(paths.directory) || dirname(paths.directory).toLowerCase() !== project.stagingRoot.toLowerCase()) throw new AppError("DISCARD_FAILED", "The transaction directory is not a direct child of the configured transaction root.");
    const tombstone = join(project.artifactRoot, "audit", `discard-${transactionId}.json`);
    writeJsonAtomic(tombstone, { schema_version: "1.0", transaction_id: transactionId, project_id: project.id, source_sha256: sourceHash, discarded_utc: new Date().toISOString(), state_before_discard: manifest.state });
    rmSync(paths.directory, { recursive: true, force: false });
    if (existsSync(paths.directory)) throw new AppError("DISCARD_FAILED", "The transaction directory still exists after deletion.");
    return { transaction_id: transactionId, discarded: true, tombstone_path: tombstone };
  }
}

function writeJsonAtomic(path: string, value: unknown): void {
  mkdirSync(dirname(path), { recursive: true });
  const temp = `${path}.${randomUUID()}.tmp`;
  try {
    writeFileSync(temp, `${JSON.stringify(value, null, 2)}\n`, "utf8");
    JSON.parse(readFileSync(temp, "utf8")) as unknown;
    renameSync(temp, path);
  } finally {
    if (existsSync(temp)) rmSync(temp, { force: true });
  }
}
