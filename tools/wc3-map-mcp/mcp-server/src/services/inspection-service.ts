import { join } from "node:path";
import type { ResolvedProject } from "../config/resolve-project.js";
import { relativeProjectPath } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";
import { type ArtifactRef, writeJsonArtifact } from "./artifact-service.js";
import { ProjectService } from "./project-service.js";
import { WorkerClient } from "../transport/worker-client.js";

export const componentNames = ["metadata", "players", "forces", "regions", "cameras", "triggers", "variables", "scripts", "object_data", "placed_objects", "terrain_summary", "imports", "archive_members", "capabilities", "opaque_members"] as const;
export type ComponentName = typeof componentNames[number];

export class InspectionService {
  public constructor(private readonly projects: ProjectService, private readonly worker: WorkerClient) {}

  public async inspect(projectId: string, map: string, section: string | undefined, includeProvenance: boolean, maxItems: number, correlationId: string): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    const mapPath = this.projects.map(projectId, map);
    const full = await this.worker.request<Record<string, unknown>>("inspect_map", { map_path: mapPath }, correlationId);
    const artifact = this.writeArtifact(project, "inspect", full, correlationId);
    const data = section ? { schema_version: full.schema_version, source: full.source, [section]: limitSection(full[section], maxItems, includeProvenance) } : limitTopLevel(full, maxItems, includeProvenance);
    return { data, artifact, map_hash: (full.source as Record<string, unknown> | undefined)?.sha256, warnings: full.parse_warnings ?? [] };
  }

  public async listArchiveFiles(projectId: string, map: string, correlationId: string): Promise<Record<string, unknown>> {
    const mapPath = this.projects.map(projectId, map);
    return this.worker.request<Record<string, unknown>>("list_archive_members", { map_path: mapPath }, correlationId);
  }

  public async getScriptSource(projectId: string, map: string, archivePath: string, expectedScriptHash: string | undefined, correlationId: string): Promise<Record<string, unknown>> {
    this.projects.assertToolAvailable(projectId, "wc3_get_script_source");
    const result = await this.worker.request<Record<string, unknown>>("read_script_source", { map_path: this.projects.map(projectId, map), archive_path: archivePath }, correlationId);
    const actualHash = String(result.sha256 ?? "");
    if (expectedScriptHash && actualHash.toUpperCase() !== expectedScriptHash.toUpperCase()) {
      throw new AppError("SOURCE_CHANGED", "The expected script hash does not match the current map script.", false, { expected_sha256: expectedScriptHash.toUpperCase(), actual_sha256: actualHash });
    }
    return result;
  }

  public async getComponent(projectId: string, map: string, component: ComponentName, filter: string | undefined, cursor: string | undefined, maxItems: number, correlationId: string): Promise<Record<string, unknown>> {
    const inspection = await this.worker.request<Record<string, unknown>>("inspect_map", { map_path: this.projects.map(projectId, map) }, correlationId);
    const value = inspection[component];
    if (value === undefined) {
      throw new AppError("UNSUPPORTED_COMPONENT", `Component '${component}' is not present in the canonical map.`);
    }
    const status = componentStatus(inspection, component, value);
    if (!["parsed_read_only", "roundtrip_verified", "typed_write_enabled"].includes(String(status.capability)) && !["archive_members", "capabilities", "opaque_members"].includes(component)) {
      throw new AppError("UNSUPPORTED_COMPONENT", `Component '${component}' is not semantically decoded for this map.`, false, { component, capability: status.capability, reason: status.reason ?? "The Phase 0 parser classified this component as opaque or absent." });
    }

    const filtered = filterValue(value, filter);
    const mapHash = String((inspection.source as Record<string, unknown> | undefined)?.sha256 ?? "");
    const offset = decodeComponentCursor(cursor, mapHash, component, filter ?? "");
    const values = Array.isArray(filtered) ? filtered.slice(offset, offset + maxItems) : filtered;
    if (!Array.isArray(filtered) && cursor) throw new AppError("CURSOR_STALE", `Component '${component}' is not paginated.`);
    const result: Record<string, unknown> = { map_hash: mapHash, component, capability: status.capability, provenance: status.provenance, values };
    if (Array.isArray(filtered) && offset + maxItems < filtered.length) result.next_cursor = encodeComponentCursor(mapHash, component, filter ?? "", offset + maxItems);
    return result;
  }

  public async validateMap(projectId: string, map: string, correlationId: string): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    const mapPath = this.projects.map(projectId, map);
    const report = await this.worker.request<Record<string, unknown>>("validate_map", { map_path: mapPath }, correlationId);
    const artifact = this.writeArtifact(project, "validation", report, correlationId);
    return { report, artifact, map_hash: shaFromReport(report) };
  }

  public async compareMaps(projectId: string, left: string, right: string, correlationId: string): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    const result = await this.worker.request<Record<string, unknown>>("compare_maps", { left_path: this.projects.resolveReadArtifact(projectId, left), right_path: this.projects.resolveReadArtifact(projectId, right) }, correlationId);
    const artifact = this.writeArtifact(project, "compare", result, correlationId);
    return { result, artifact };
  }

  private writeArtifact(project: ResolvedProject, kind: string, value: unknown, correlationId: string): ArtifactRef {
    const path = relativeProjectPath(project, join(project.artifactRoot, "reports", `${kind}-${correlationId}.json`));
    return writeJsonArtifact(project, path, value, kind);
  }
}

function limitTopLevel(value: Record<string, unknown>, maxItems: number, includeProvenance: boolean): Record<string, unknown> {
  return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, limitSection(item, maxItems, includeProvenance)]));
}

function limitSection(value: unknown, maxItems: number, includeProvenance: boolean): unknown {
  if (Array.isArray(value)) {
    return value.slice(0, maxItems).map(item => includeProvenance ? item : stripProvenance(item));
  }
  return includeProvenance ? value : stripProvenance(value);
}

function stripProvenance(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stripProvenance);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).filter(([key]) => key !== "provenance").map(([key, item]) => [key, stripProvenance(item)]));
  return value;
}

function filterValue(value: unknown, filter: string | undefined): unknown {
  if (!filter || !Array.isArray(value)) return value;
  const needle = filter.toLowerCase();
  return value.filter(item => JSON.stringify(item).toLowerCase().includes(needle));
}

function capabilityOf(value: unknown): unknown {
  if (Array.isArray(value)) return value[0] && typeof value[0] === "object" ? (value[0] as Record<string, unknown>).capability : "unknown";
  if (value && typeof value === "object") return (value as Record<string, unknown>).capability ?? "unknown";
  return "unknown";
}

function includeProvenance(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(item => item && typeof item === "object" ? (item as Record<string, unknown>).provenance : undefined);
  return value && typeof value === "object" ? (value as Record<string, unknown>).provenance : undefined;
}

function shaFromReport(report: Record<string, unknown>): unknown {
  return report.map_hash ?? report.source_sha256 ?? undefined;
}

function componentStatus(inspection: Record<string, unknown>, component: ComponentName, value: unknown): Record<string, unknown> {
  const statuses = inspection.component_status;
  if (statuses && typeof statuses === "object" && component in (statuses as Record<string, unknown>)) {
    const status = (statuses as Record<string, unknown>)[component];
    if (status && typeof status === "object") return status as Record<string, unknown>;
  }
  return { capability: capabilityOf(value), provenance: includeProvenance(value) ?? "unknown" };
}

function encodeComponentCursor(mapSha256: string, component: ComponentName, filter: string, offset: number): string {
  return Buffer.from(JSON.stringify({ schema_version: "1.0", map_sha256: mapSha256, component, filter, offset }), "utf8").toString("base64url");
}

function decodeComponentCursor(cursor: string | undefined, mapSha256: string, component: ComponentName, filter: string): number {
  if (!cursor) return 0;
  try {
    const value = JSON.parse(Buffer.from(cursor, "base64url").toString("utf8")) as { schema_version?: string; map_sha256?: string; component?: string; filter?: string; offset?: number };
    if (value.schema_version !== "1.0" || value.map_sha256?.toUpperCase() !== mapSha256.toUpperCase() || value.component !== component || value.filter !== filter || !Number.isInteger(value.offset) || (value.offset ?? -1) < 0) throw new Error("cursor identity mismatch");
    return value.offset ?? 0;
  } catch (error) {
    throw new AppError("CURSOR_STALE", "The component cursor is invalid or belongs to a different map/component/filter.", false, { cause: error instanceof Error ? error.message : String(error) });
  }
}
