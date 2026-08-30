import type { McpServer } from "@modelcontextprotocol/server";
import { AppError } from "../errors/app-error.js";
import { correlationId } from "../schemas/common.js";
import { listArchiveFilesSchema } from "../schemas/tools.js";
import type { InspectionService } from "../services/inspection-service.js";
import { safeCall } from "./response.js";

export function registerListArchiveFiles(server: McpServer, inspections: InspectionService): void {
  const registerTool = server.registerTool.bind(server) as (...args: any[]) => void;
  registerTool("wc3_list_archive_files", {
    description: "List MPQ member metadata, hashes, and parser capability in stable order. Member bytes and arbitrary filesystem paths are never returned.",
    inputSchema: listArchiveFilesSchema as never,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, async () => {
      const result = await inspections.listArchiveFiles(input.project_id, input.map, id);
      const members = Array.isArray(result.members) ? result.members.filter((item: any) => !input.prefix || String(item.path).toLowerCase().startsWith(input.prefix.toLowerCase())) : [];
      const mapHash = String(result.map_sha256 ?? "");
      const offset = decodeArchiveCursor(input.cursor, mapHash, input.prefix ?? "");
      result.members = members.slice(offset, offset + input.max_items);
      if (offset + input.max_items < members.length) result.next_cursor = encodeArchiveCursor(mapHash, input.prefix ?? "", offset + input.max_items);
      return result;
    });
  });
}

function encodeArchiveCursor(mapSha256: string, prefix: string, offset: number): string {
  return Buffer.from(JSON.stringify({ schema_version: "1.0", map_sha256: mapSha256, prefix, offset }), "utf8").toString("base64url");
}

function decodeArchiveCursor(cursor: string | undefined, mapSha256: string, prefix: string): number {
  if (!cursor) return 0;
  try {
    const value = JSON.parse(Buffer.from(cursor, "base64url").toString("utf8")) as { schema_version?: string; map_sha256?: string; prefix?: string; offset?: number };
    if (value.schema_version !== "1.0" || value.map_sha256?.toUpperCase() !== mapSha256.toUpperCase() || value.prefix !== prefix || !Number.isInteger(value.offset) || (value.offset ?? -1) < 0) throw new Error("cursor identity mismatch");
    return value.offset ?? 0;
  } catch (error) {
    throw new AppError("CURSOR_STALE", "The archive cursor is invalid or belongs to a different map/filter.", false, { cause: error instanceof Error ? error.message : String(error) });
  }
}
