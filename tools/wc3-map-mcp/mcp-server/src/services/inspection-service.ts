import { join } from "node:path";
import type { ResolvedProject } from "../config/resolve-project.js";
import { relativeProjectPath } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";
import { type ArtifactRef, writeJsonArtifact } from "./artifact-service.js";
import { ProjectService } from "./project-service.js";
import { WorkerClient } from "../transport/worker-client.js";

export const componentNames = ["metadata", "players", "forces", "regions", "triggers", "variables", "scripts", "object_data", "placed_objects", "terrain_summary", "imports", "archive_members", "capabilities", "opaque_members"] as const;
export type ComponentName = typeof componentNames[number];

export class InspectionService {
  public constructor(private readonly projects: ProjectService, private readonly worker: WorkerClient) {}

  public async inspect(projectId: string, map: string, section: string | undefined, includeProvenance: boolean, maxItems: number, correlationId: string): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    const mapPath = this.projects.source(projectId, map);
    const full = await this.worker.request<Record<string, unknown>>("inspect_map", { map_path: mapPath }, correlationId);
    const artifact = this.writeArtifact(project, "inspect", full, correlationId);
    const data = section ? { schema_version: full.schema_version, source: full.source, [section]: limitSection(full[section], maxItems, includeProvenance) } : limitTopLevel(full, maxItems, includeProvenance);
    return { data, artifact, map_hash: (full.source as Record<string, unknown> | undefined)?.sha256, warnings: full.parse_warnings ?? [] };
  }

  public async listArchiveFiles(projectId: string, map: string, correlationId: string): Promise<Record<string, unknown>> {
    const mapPath = this.projects.source(projectId, map);
    return this.worker.request<Record<string, unknown>>("list_archive_members", { map_path: mapPath }, correlationId);
  }

  public async getComponent(projectId: string, map: string, component: ComponentName, filter: string | undefined, maxItems: number, correlationId: string): Promise<Record<string, unknown>> {
    const inspection = await this.worker.request<Record<string, unknown>>("inspect_map", { map_path: this.projects.source(projectId, map) }, correlationId);
    const value = inspection[component];
    if (value === undefined) {
      throw new AppError("UNSUPPORTED_COMPONENT", `Component '${component}' is not present in the canonical map.`);
    }
    if (component === "variables" && typeof value === "object" && value !== null && (value as Record<string, unknown>).capability === "preserved_opaque") {
      throw new AppError("UNSUPPORTED_COMPONENT", `Component '${component}' is opaque for this map.`, false, { component, reason: (value as Record<string, unknown>).reason });
    }

    const filtered = filterValue(value, filter);
    return { map_hash: (inspection.source as Record<string, unknown> | undefined)?.sha256, component, capability: capabilityOf(value), provenance: includeProvenance(value), values: limitSection(filtered, maxItems, true) };
  }

  public async validateMap(projectId: string, map: string, correlationId: string): Promise<Record<string, unknown>> {
    const project = this.projects.project(projectId);
    const mapPath = this.projects.source(projectId, map);
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
