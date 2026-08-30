import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { AppError, asAppError } from "../errors/app-error.js";
import { mapEngineError } from "../errors/map-error.js";
import { workerResponseSchema } from "../schemas/worker.js";
import { encodeNdjson, parseNdjsonLine } from "./ndjson.js";
import type { Wc3Config } from "../config/schema.js";

const MAX_DIAGNOSTIC_CHARS = 4000;

function diagnosticTail(value: string): string {
  return value.slice(-MAX_DIAGNOSTIC_CHARS);
}

function transportDetails(
  operation: string,
  correlationId: string,
  requestId: string,
  stdout: string,
  stderr: string,
  extra: Record<string, unknown> = {}
): Record<string, unknown> {
  return {
    operation,
    correlation_id: correlationId,
    request_id: requestId,
    ...extra,
    stdout: diagnosticTail(stdout),
    stderr: diagnosticTail(stderr)
  };
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
      let timeout: NodeJS.Timeout | undefined;

      const finish = (callback: () => void): void => {
        if (settled) return;
        settled = true;
        if (timeout !== undefined) clearTimeout(timeout);
        callback();
      };

      child.stdout.setEncoding("utf8");
      child.stderr.setEncoding("utf8");
      child.stdout.on("data", chunk => { stdout += String(chunk); });
      child.stderr.on("data", chunk => { stderr += String(chunk); });

      child.on("error", error => finish(() => rejectPromise(new AppError(
        "ENGINE_UNAVAILABLE",
        `Unable to start the map engine: ${error.message}`,
        true,
        transportDetails(operation, correlationId, requestId, stdout, stderr)
      ))));

      // An early process exit can surface as EPIPE on stdin instead of (or
      // before) the child's close event. Treat it as an unavailable engine,
      // while retaining the close-event crash classification when available.
      child.stdin.on("error", error => finish(() => rejectPromise(new AppError(
        "ENGINE_UNAVAILABLE",
        `Unable to send the request to the map engine: ${error.message}`,
        true,
        transportDetails(operation, correlationId, requestId, stdout, stderr)
      ))));

      child.on("close", (code, signal) => finish(() => {
        const lines = stdout
          .split(/\r?\n/)
          .map(value => value.trim())
          .filter(value => value.length > 0);

        if (code !== 0 || signal !== null) {
          rejectPromise(new AppError(
            "ENGINE_UNAVAILABLE",
            `Map engine exited unexpectedly during '${operation}'.`,
            true,
            transportDetails(operation, correlationId, requestId, stdout, stderr, { code, signal })
          ));
          return;
        }

        if (lines.length === 0) {
          rejectPromise(new AppError(
            "ENGINE_PROTOCOL_ERROR",
            `Map engine returned no response for '${operation}'.`,
            true,
            transportDetails(operation, correlationId, requestId, stdout, stderr, { code, signal })
          ));
          return;
        }

        if (lines.length !== 1) {
          rejectPromise(new AppError(
            "ENGINE_PROTOCOL_ERROR",
            `Map engine returned multiple stdout responses for '${operation}'.`,
            false,
            transportDetails(operation, correlationId, requestId, stdout, stderr, { response_count: lines.length })
          ));
          return;
        }

        const line = lines[0]!;
        let parsed: unknown;
        try {
          parsed = parseNdjsonLine(line);
        } catch (error) {
          rejectPromise(new AppError(
            "ENGINE_PROTOCOL_ERROR",
            `Map engine returned malformed JSON for '${operation}'.`,
            false,
            transportDetails(operation, correlationId, requestId, stdout, stderr, { response: line.slice(0, MAX_DIAGNOSTIC_CHARS) }),
            { cause: error }
          ));
          return;
        }

        const validated = workerResponseSchema.safeParse(parsed);
        if (!validated.success) {
          rejectPromise(new AppError(
            "ENGINE_PROTOCOL_ERROR",
            `Map engine returned an invalid response for '${operation}'.`,
            false,
            transportDetails(operation, correlationId, requestId, stdout, stderr, {
              issues: validated.error.issues
            })
          ));
          return;
        }

        const response = validated.data;
        if (response.request_id !== requestId) {
          rejectPromise(new AppError(
            "ENGINE_PROTOCOL_ERROR",
            "Map engine response identity or protocol version did not match the request.",
            false,
            transportDetails(operation, correlationId, requestId, stdout, stderr, {
              response_request_id: response.request_id,
              expected_request_id: requestId,
              protocol_version: response.protocol_version
            })
          ));
          return;
        }

        if (!response.ok) {
          const engineError = response.error;
          rejectPromise(mapEngineError({
            code: engineError.code,
            message: engineError.message,
            retryable: engineError.retryable,
            ...(engineError.details === undefined ? {} : { details: engineError.details })
          }));
          return;
        }

        resolvePromise(response.result as T);
      }));

      timeout = setTimeout(() => finish(() => {
        child.kill();
        rejectPromise(new AppError(
          "ENGINE_UNAVAILABLE",
          `Map engine timed out during '${operation}'.`,
          true,
          transportDetails(operation, correlationId, requestId, stdout, stderr)
        ));
      }), this.config.engine.request_timeout_ms);

      child.stdin.write(encodeNdjson({ protocol_version: "1.0", request_id: requestId, operation, payload }));
      child.stdin.end();
    }).catch(error => { throw asAppError(error); });
  }
}
