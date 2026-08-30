import { randomUUID } from "node:crypto";
import { asAppError } from "../errors/app-error.js";

export function success(data: unknown, correlationId: string = randomUUID(), warnings: unknown[] = [], artifacts: unknown[] = []): Record<string, unknown> {
  return { structuredContent: { ok: true, correlation_id: correlationId, data, warnings, artifacts }, content: [{ type: "text", text: summarize(data) }] };
}

export function failure(error: unknown, correlationId: string = randomUUID()): Record<string, unknown> {
  const appError = asAppError(error);
  const structuredContent = { ok: false, correlation_id: correlationId, error: { code: appError.code, message: appError.message, retryable: appError.retryable, details: appError.details } };
  return { isError: true, structuredContent, content: [{ type: "text", text: `${appError.code}: ${appError.message}` }] };
}

export async function safeCall<T>(correlationId: string, callback: () => Promise<T>): Promise<Record<string, unknown>> {
  try {
    const data = await callback();
    const artifacts = collectArtifacts(data);
    return success(data, correlationId, Array.isArray((data as Record<string, unknown>)?.warnings) ? (data as Record<string, unknown>).warnings as unknown[] : [], artifacts);
  } catch (error) {
    return failure(error, correlationId);
  }
}

function collectArtifacts(value: unknown): unknown[] {
  if (!value || typeof value !== "object") return [];
  return Object.entries(value as Record<string, unknown>).filter(([key, item]) => key.includes("artifact") && item && typeof item === "object").map(([, item]) => item);
}

function summarize(value: unknown): string {
  if (!value || typeof value !== "object") return String(value);
  const record = value as Record<string, unknown>;
  const copy = Object.fromEntries(Object.entries(record).filter(([key]) => !["data", "result", "canonical_map"].includes(key)));
  return JSON.stringify(copy).slice(0, 8000);
}
