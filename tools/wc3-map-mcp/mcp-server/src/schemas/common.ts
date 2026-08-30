import * as z from "zod/v4";
import { randomUUID } from "node:crypto";

export const uuidSchema = z.string().uuid();
export const sha256Schema = z.string().regex(/^[0-9a-f]{64}$/i, "Expected a SHA-256 hex digest.");
export const projectMapSchema = z.object({ project_id: z.string().min(1).max(100), map: z.string().min(1).max(400) }).strict();

export type ArtifactRef = { kind: string; path: string; size_bytes: number; sha256: string };

export function correlationId(): string {
  return randomUUID();
}
