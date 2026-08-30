export type ErrorCode =
  | "INVALID_ARGUMENT"
  | "PATH_OUTSIDE_ROOT"
  | "FILE_NOT_FOUND"
  | "SOURCE_CHANGED"
  | "UNSUPPORTED_COMPONENT"
  | "PARSE_FAILED"
  | "VALIDATION_FAILED"
  | "BUILD_FAILED"
  | "BUILD_UNSUPPORTED"
  | "BUILD_REOPEN_MISMATCH"
  | "ENGINE_UNAVAILABLE"
  | "ENGINE_PROTOCOL_ERROR"
  | "TRANSACTION_STATE"
  | "PRECONDITION_FAILED"
  | "PRECONDITION_REQUIRED"
  | "CURSOR_STALE"
  | "LOCKED"
  | "LAUNCH_FAILED"
  | "PROMOTION_FAILED"
  | "DISCARD_FAILED"
  | "OUTPUT_EXISTS"
  | "INTERNAL_ERROR";

export class AppError extends Error {
  public readonly code: ErrorCode;
  public readonly retryable: boolean;
  public readonly details: Record<string, unknown>;

  public constructor(code: ErrorCode, message: string, retryable = false, details: Record<string, unknown> = {}, options?: ErrorOptions) {
    super(message, options);
    this.name = "AppError";
    this.code = code;
    this.retryable = retryable;
    this.details = details;
  }
}

export function asAppError(error: unknown, fallbackCode: ErrorCode = "INTERNAL_ERROR"): AppError {
  if (error instanceof AppError) {
    return error;
  }
  return new AppError(fallbackCode, error instanceof Error ? error.message : String(error), false, {}, { cause: error });
}
