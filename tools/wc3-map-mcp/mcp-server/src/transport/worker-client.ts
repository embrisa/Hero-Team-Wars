import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { AppError, asAppError } from "../errors/app-error.js";
import { mapEngineError } from "../errors/map-error.js";
import { encodeNdjson, parseNdjsonLine } from "./ndjson.js";
import type { Wc3Config } from "../config/schema.js";

interface EngineResponse {
  protocol_version: string;
  request_id: string;
  ok: boolean;
  result?: unknown;
  error?: { code?: string; message?: string; retryable?: boolean; details?: Record<string, unknown> };
}

export class WorkerClient {
  public constructor(private readonly config: Wc3Config) {}

  public request<T>(operation: string, payload: Record<string, unknown>, correlationId: string = randomUUID()): Promise<T> {
    return new Promise<T>((resolvePromise, rejectPromise) => {
      const requestId = randomUUID();
      const child = spawn(this.config.engine.executable, [...this.config.engine.arguments, "--stdio"], {
        cwd: process.cwd(),
        stdio: ["pipe", "pipe", "pipe"]
      });
      let stdout = "";
      let stderr = "";
      let settled = false;
      const timeout = setTimeout(() => {
        if (!settled) {
          settled = true;
          child.kill();
          rejectPromise(new AppError("ENGINE_UNAVAILABLE", `Map engine timed out during '${operation}'.`, true, { operation, correlation_id: correlationId }));
        }
      }, this.config.engine.request_timeout_ms);

      const finish = (callback: () => void): void => {
        if (settled) return;
        settled = true;
        clearTimeout(timeout);
        callback();
      };

      child.stdout.setEncoding("utf8");
      child.stderr.setEncoding("utf8");
      child.stdout.on("data", chunk => { stdout += String(chunk); });
      child.stderr.on("data", chunk => { stderr += String(chunk); });
      child.on("error", error => finish(() => rejectPromise(new AppError("ENGINE_UNAVAILABLE", `Unable to start the map engine: ${error.message}`, true, { operation, stderr }))));
      child.on("close", (code, signal) => finish(() => {
        const line = stdout.split(/\r?\n/).map(value => value.trim()).find(value => value.length > 0);
        if (!line) {
          rejectPromise(new AppError("ENGINE_PROTOCOL_ERROR", `Map engine returned no response for '${operation}'.`, true, { code, signal, stderr: stderr.slice(-4000) }));
          return;
        }

        let response: EngineResponse;
        try {
          response = parseNdjsonLine(line) as EngineResponse;
        } catch (error) {
          rejectPromise(new AppError("ENGINE_PROTOCOL_ERROR", `Map engine returned malformed JSON for '${operation}'.`, false, { stdout: line.slice(0, 4000), stderr: stderr.slice(-4000) }, { cause: error }));
          return;
        }

        if (response.request_id !== requestId || response.protocol_version.split(".")[0] !== "1") {
          rejectPromise(new AppError("ENGINE_PROTOCOL_ERROR", "Map engine response identity or protocol version did not match the request.", false, { request_id: response.request_id, expected_request_id: requestId, protocol_version: response.protocol_version }));
          return;
        }

        if (!response.ok) {
          rejectPromise(mapEngineError(response.error ?? {}));
          return;
        }

        resolvePromise(response.result as T);
      }));

      child.stdin.write(encodeNdjson({ protocol_version: "1.0", request_id: requestId, operation, payload }));
      child.stdin.end();
    }).catch(error => { throw asAppError(error); });
  }
}
