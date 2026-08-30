import { closeSync, existsSync, mkdirSync, openSync, readFileSync, unlinkSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { randomUUID } from "node:crypto";
import type { ResolvedProject } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";

export async function withProjectLock<T>(project: ResolvedProject, operation: string, callback: () => Promise<T>): Promise<T> {
  const lockPath = join(project.stagingRoot, "project.lock");
  mkdirSync(dirname(lockPath), { recursive: true });
  const record = { pid: process.pid, operation, correlation_id: randomUUID(), created_utc: new Date().toISOString() };
  let descriptor: number;
  try {
    descriptor = openSync(lockPath, "wx");
    writeFileSync(descriptor, `${JSON.stringify(record)}\n`, { encoding: "utf8" });
  } catch (error) {
    const existing = existsSync(lockPath) ? readFileSync(lockPath, "utf8") : "unknown";
    throw new AppError("LOCKED", `Project mutation is already locked. Existing lock: ${existing.slice(0, 500)}`, true, { lock_path: lockPath }, { cause: error });
  }

  try {
    return await callback();
  } finally {
    try { closeSync(descriptor); } finally {
      if (existsSync(lockPath)) unlinkSync(lockPath);
    }
  }
}
