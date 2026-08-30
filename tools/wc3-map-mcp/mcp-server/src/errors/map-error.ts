import { AppError } from "./app-error.js";

export function mapEngineError(error: { code?: string; message?: string; retryable?: boolean; details?: Record<string, unknown> }): AppError {
  const supported = new Set([
    "INVALID_ARGUMENT", "PATH_OUTSIDE_ROOT", "FILE_NOT_FOUND", "SOURCE_CHANGED", "UNSUPPORTED_COMPONENT", "SCRIPT_MUTATION_DISABLED",
    "PARSE_FAILED", "VALIDATION_FAILED", "BUILD_FAILED", "BUILD_UNSUPPORTED", "PRECONDITION_FAILED",
    "PRECONDITION_REQUIRED", "CURSOR_STALE", "TRANSACTION_STATE", "BUILD_REOPEN_MISMATCH", "REGION_RENAME_FORBIDDEN",
    "UNSUPPORTED_OPERATION", "OUTPUT_EXISTS", "DUPLICATE_ARCHIVE_MEMBER", "INVALID_JSON", "SCHEMA_VERSION_UNSUPPORTED",
    "PLAYER_ID_INVALID", "REGION_NAME_INVALID", "REGION_BOUNDS_INVALID", "REQUIRED_MEMBER_MISSING", "CAPABILITY_GATED"
  ]);
  const code = error.code && supported.has(error.code) ? error.code : "INTERNAL_ERROR";
  return new AppError(code as never, error.message ?? "The map engine rejected the request.", error.retryable ?? false, error.details ?? {});
}
