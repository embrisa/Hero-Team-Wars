import * as z from "zod/v4";
import { uuidSchema } from "./common.js";

/**
 * The private TypeScript/.NET worker protocol is deliberately strict.  A
 * response is consumed only after it has passed this schema so that malformed
 * or partially written engine output cannot reach an MCP tool handler.
 */
export const workerProtocolVersionSchema = z.literal("1.0");

export const workerOperationSchema = z.enum([
  "environment_status",
  "hash_file",
  "list_archive_members",
  "probe_map",
  "inspect_map",
  "read_script_source",
  "validate_map",
  "validate_canonical",
  "apply_operations",
  "build_map",
  "compare_maps",
  "compose_gameplay_source",
  "validate_gameplay_source",
  "run_scenario"
]);

export const workerErrorSchema = z.object({
  code: z.string().regex(/^[A-Z][A-Z0-9_]+$/),
  message: z.string(),
  retryable: z.boolean(),
  details: z.record(z.string(), z.unknown()).optional()
}).strict();

const workerResponseBase = {
  protocol_version: workerProtocolVersionSchema,
  request_id: uuidSchema
} as const;

export const workerSuccessResponseSchema = z.object({
  ...workerResponseBase,
  ok: z.literal(true),
  result: z.unknown()
}).strict();

export const workerFailureResponseSchema = z.object({
  ...workerResponseBase,
  ok: z.literal(false),
  error: workerErrorSchema
}).strict();

export const workerResponseSchema = z.discriminatedUnion("ok", [
  workerSuccessResponseSchema,
  workerFailureResponseSchema
]);

/** Alias matching the cross-process contract terminology. */
export const engineResponseSchema = workerResponseSchema;
export const engineErrorSchema = workerErrorSchema;

export const workerRequestSchema = z.object({
  protocol_version: workerProtocolVersionSchema,
  request_id: uuidSchema,
  operation: workerOperationSchema,
  payload: z.record(z.string(), z.unknown())
}).strict();

export const engineRequestSchema = workerRequestSchema;

export type WorkerError = z.infer<typeof workerErrorSchema>;
export type WorkerSuccessResponse = z.infer<typeof workerSuccessResponseSchema>;
export type WorkerFailureResponse = z.infer<typeof workerFailureResponseSchema>;
export type WorkerResponse = z.infer<typeof workerResponseSchema>;
export type EngineResponse = WorkerResponse;
export type WorkerRequest = z.infer<typeof workerRequestSchema>;
export type EngineRequest = WorkerRequest;
