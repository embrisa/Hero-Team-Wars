import { closeSync, existsSync, fsyncSync, mkdirSync, openSync, readFileSync, unlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { randomUUID } from "node:crypto";
import type { ResolvedProject } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";

const LOCK_WAIT_TIMEOUT_MS = 30_000;
const LOCK_POLL_MS = 25;
const PROCESS_STARTED_UTC = new Date().toISOString();

interface LockRecord {
  lock_id: string;
  pid: number;
  process_started_utc: string;
  operation: string;
  correlation_id: string;
  created_utc: string;
}

/**
 * Acquire the per-project mutation lock. A competing mutation waits for the
 * current owner instead of racing the transaction files. Stale locks are
 * never deleted automatically: after the bounded wait the caller receives a
 * retryable LOCKED error with the recorded owner information.
 */
export async function withProjectLock<T>(
  project: ResolvedProject,
  operation: string,
  callback: () => Promise<T>,
  options: { timeout_ms?: number; poll_ms?: number } = {}
): Promise<T> {
  const lockPath = join(project.stagingRoot, "project.lock");
  mkdirSync(project.stagingRoot, { recursive: true });
  const lock: LockRecord = {
    lock_id: randomUUID(),
    pid: process.pid,
    process_started_utc: PROCESS_STARTED_UTC,
    operation,
    correlation_id: randomUUID(),
    created_utc: new Date().toISOString()
  };
  const timeoutMs = options.timeout_ms ?? LOCK_WAIT_TIMEOUT_MS;
  const pollMs = options.poll_ms ?? LOCK_POLL_MS;
  const started = Date.now();
  let acquired = false;

  while (!acquired) {
    let descriptor: number | undefined;
    try {
      descriptor = openSync(lockPath, "wx");
      const text = `${JSON.stringify(lock)}\n`;
      writeFileSync(descriptor, text, { encoding: "utf8" });
      try {
        fsyncSync(descriptor);
      } catch (error) {
        const code = (error as NodeJS.ErrnoException).code;
        if (code !== "EPERM" && code !== "ENOTSUP" && code !== "EINVAL") throw error;
      }
      closeSync(descriptor);
      descriptor = undefined;
      acquired = true;
    } catch (error) {
      if (descriptor !== undefined) {
        try { closeSync(descriptor); } catch { /* best effort */ }
        try { unlinkSync(lockPath); } catch { /* best effort */ }
      }
      if ((error as NodeJS.ErrnoException).code !== "EEXIST") {
        throw new AppError("LOCKED", `Unable to acquire the project mutation lock: ${error instanceof Error ? error.message : String(error)}`, true, { lock_path: lockPath }, { cause: error });
      }
      if (Date.now() - started >= timeoutMs) {
        const existing = existsSync(lockPath) ? readFileSync(lockPath, "utf8").slice(-4000) : "owner disappeared while waiting";
        throw new AppError("LOCKED", `Project mutation remained locked after ${timeoutMs} ms. Existing lock: ${existing}`, true, { lock_path: lockPath, timeout_ms: timeoutMs });
      }
      await delay(Math.min(pollMs, Math.max(1, timeoutMs - (Date.now() - started))));
    }
  }

  try {
    return await callback();
  } finally {
    // Only remove the lock if it still contains this owner token. This avoids
    // deleting a replacement lock if an external process interfered with the
    // lock file after acquisition.
    let owned = false;
    try {
      owned = JSON.parse(readFileSync(lockPath, "utf8"))?.lock_id === lock.lock_id;
    } catch {
      owned = false;
    }
    if (owned) {
      try { unlinkSync(lockPath); } catch (error) {
        if ((error as NodeJS.ErrnoException).code !== "ENOENT") throw error;
      }
    }
  }
}

function delay(milliseconds: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}
