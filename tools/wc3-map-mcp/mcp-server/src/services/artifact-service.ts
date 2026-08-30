import { createHash, randomUUID } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, renameSync, statSync, unlinkSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import type { ResolvedProject } from "../config/resolve-project.js";
import { relativeProjectPath } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";

export interface ArtifactRef {
  kind: string;
  path: string;
  size_bytes: number;
  sha256: string;
}

export function sha256File(path: string): { size_bytes: number; modified_utc: string; sha256: string } {
  if (!existsSync(path)) {
    throw new AppError("FILE_NOT_FOUND", `File does not exist: ${path}`);
  }
  const bytes = readFileSync(path);
  const stat = statSync(path);
  return { size_bytes: bytes.byteLength, modified_utc: stat.mtime.toISOString(), sha256: createHash("sha256").update(bytes).digest("hex").toUpperCase() };
}

export function writeJsonArtifact(project: ResolvedProject, relativePath: string, value: unknown, kind: string): ArtifactRef {
  const path = join(project.root, relativePath.replaceAll("/", "\\"));
  mkdirSync(dirname(path), { recursive: true });
  const temp = join(dirname(path), `.${path.split(/[\\/]/).pop() ?? "artifact"}.${randomUUID()}.tmp`);
  const text = `${JSON.stringify(value, null, 2)}\n`;
  try {
    writeFileSync(temp, text, { encoding: "utf8", flag: "wx" });
    JSON.parse(readFileSync(temp, "utf8")) as unknown;
    replaceFile(temp, path);
  } catch (error) {
    if (existsSync(temp)) {
      try { unlinkSync(temp); } catch { /* Preserve the useful original error. */ }
    }
    throw new AppError("INTERNAL_ERROR", `Unable to write artifact '${relativePath}': ${error instanceof Error ? error.message : String(error)}`, false, { path: relativePath }, { cause: error });
  }

  const hash = sha256File(path);
  return { kind, path: relativeProjectPath(project, path), size_bytes: hash.size_bytes, sha256: hash.sha256 };
}

function replaceFile(temp: string, destination: string): void {
  if (!existsSync(destination)) {
    renameSync(temp, destination);
    return;
  }

  const backup = join(dirname(destination), `.${destination.split(/[\\/]/).pop() ?? "artifact"}.${randomUUID()}.bak`);
  renameSync(destination, backup);
  try {
    renameSync(temp, destination);
    try { unlinkSync(backup); } catch { /* A recoverable backup is safer than failing after replacement. */ }
  } catch (error) {
    if (!existsSync(destination) && existsSync(backup)) renameSync(backup, destination);
    throw error;
  }
}
