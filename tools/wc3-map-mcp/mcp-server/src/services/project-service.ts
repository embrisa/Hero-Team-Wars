import { existsSync, statSync } from "node:fs";
import { extname } from "node:path";
import type { Wc3Config } from "../config/schema.js";
import { readPath, relativeProjectPath, resolveConfiguredPath, resolveProject, sourcePath, type ResolvedProject } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";
import { sha256File } from "./artifact-service.js";
import { WorkerClient } from "../transport/worker-client.js";

export class ProjectService {
  public constructor(private readonly config: Wc3Config, private readonly worker: WorkerClient) {}

  public validateStartup(): void {
    for (const projectId of Object.keys(this.config.projects)) {
      const project = this.project(projectId);
      for (const root of project.config.read_roots) resolveConfiguredPath(project.root, root);
    }
  }

  public project(projectId: string): ResolvedProject {
    return resolveProject(this.config, projectId);
  }

  public source(projectId: string, map: string): string {
    return this.assertReadableMap(this.project(projectId), sourcePath(this.project(projectId), map), map);
  }

  public map(projectId: string, map: string): string {
    const project = this.project(projectId);
    const resolved = project.config.source_maps.some(source => source.toLowerCase() === map.toLowerCase())
      ? sourcePath(project, map)
      : readPath(project, map);
    return this.assertReadableMap(project, resolved, map);
  }

  public async status(projectId: string): Promise<Record<string, unknown>> {
    const project = this.project(projectId);
    const source = project.sourceMaps[0];
    if (!source) {
      throw new AppError("INVALID_ARGUMENT", `Project '${projectId}' has no configured source map.`);
    }
    const sourceExists = existsSync(source) && statSync(source).isFile();
    const sourceHash = sourceExists ? sha256File(source) : undefined;
    const configuredFiles = Object.fromEntries(Object.entries({
      world_editor: project.config.world_editor,
      warcraft: project.config.warcraft,
      test_map_root: project.config.test_map_root
    }).filter((entry): entry is [string, string] => typeof entry[1] === "string"));
    const engine = await this.worker.request<Record<string, unknown>>("environment_status", { configured_files: configuredFiles });
    const expectedHash = project.config.baseline_sha256?.toUpperCase();
    const actualHash = sourceHash?.sha256.toUpperCase();
    const readOnlyTools = ["wc3_project_status", "wc3_inspect_map", "wc3_list_archive_files", "wc3_get_component", "wc3_validate_map", "wc3_compare_maps"];
    return {
      schema_version: "1.0",
      project_id: project.id,
      root: { label: "project_root", path: project.root },
      source_map: { label: "configured_source_map", path: relativeProjectPath(project, source), exists: sourceExists, ...(sourceHash ?? { size_bytes: null, modified_utc: null, sha256: null }) },
      baseline: { expected_sha256: expectedHash ?? null, actual_sha256: actualHash ?? null, drift: expectedHash && actualHash ? expectedHash !== actualHash : "unknown" },
      baseline_drift: expectedHash && actualHash ? expectedHash !== actualHash : "unknown",
      server: { name: "wc3-map-mcp", version: "0.1.0", runtime: `Node.js ${process.version}` },
      engine,
      configured: (engine.configured_files as Record<string, unknown> | undefined) ?? {},
      capability_summary: { inspection: "enabled", comparison: "enabled", validation: "read_only", mutation: "disabled", build: "disabled", launch: "disabled", deletion: "disabled" },
      enabled_tools: project.config.write_policy === "read_only"
        ? readOnlyTools.filter(tool => project.config.enabled_tools.length === 0 || project.config.enabled_tools.includes(tool))
        : project.config.enabled_tools,
      disabled_tools: ["wc3_begin_transaction", "wc3_apply_operations", "wc3_transaction_diff", "wc3_validate_transaction", "wc3_build_map", "wc3_build_report", "wc3_launch_editor", "wc3_launch_test_map", "wc3_record_test_result", "wc3_get_test_session", "wc3_promote_build", "wc3_discard_transaction"],
      disabled_until_evidence: ["script_source_mutation", "generic_archive_patch", "autonomous_promotion"],
      roots: { staging: relativeProjectPath(project, project.stagingRoot), artifacts: relativeProjectPath(project, project.artifactRoot), builds: relativeProjectPath(project, project.buildRoot) }
    };
  }

  public resolveReadArtifact(projectId: string, candidate: string): string {
    const project = this.project(projectId);
    if (project.config.source_maps.some(map => map.toLowerCase() === candidate.toLowerCase())) {
      return this.assertReadableArtifact(project, sourcePath(project, candidate), candidate);
    }

    return this.assertReadableArtifact(project, readPath(project, candidate), candidate);
  }

  private assertReadableMap(project: ResolvedProject, resolved: string, label: string): string {
    if (![".w3m", ".w3x"].includes(extname(resolved).toLowerCase())) {
      throw new AppError("INVALID_ARGUMENT", `Map '${label}' must be a .w3m or .w3x file.`);
    }
    return this.assertReadableFile(project, resolved, label);
  }

  private assertReadableArtifact(project: ResolvedProject, resolved: string, label: string): string {
    if (![".w3m", ".w3x", ".json"].includes(extname(resolved).toLowerCase())) {
      throw new AppError("INVALID_ARGUMENT", `Read artifact '${label}' must be a .w3m, .w3x, or canonical .json file.`);
    }
    return this.assertReadableFile(project, resolved, label);
  }

  private assertReadableFile(project: ResolvedProject, resolved: string, label: string): string {
    const stat = statSync(resolved);
    if (!stat.isFile()) throw new AppError("INVALID_ARGUMENT", `Configured map/artifact '${label}' is not a file.`);
    if (stat.size > project.config.max_map_bytes) throw new AppError("INVALID_ARGUMENT", `Configured map/artifact '${label}' exceeds max_map_bytes.`, false, { size_bytes: stat.size, max_map_bytes: project.config.max_map_bytes });
    return resolved;
  }
}
