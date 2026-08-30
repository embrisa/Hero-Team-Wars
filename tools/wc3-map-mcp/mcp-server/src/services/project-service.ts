import { existsSync } from "node:fs";
import { join } from "node:path";
import type { Wc3Config } from "../config/schema.js";
import { readPath, relativeProjectPath, resolveProject, sourcePath, type ResolvedProject } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";
import { sha256File } from "./artifact-service.js";
import { WorkerClient } from "../transport/worker-client.js";

export class ProjectService {
  public constructor(private readonly config: Wc3Config, private readonly worker: WorkerClient) {}

  public project(projectId: string): ResolvedProject {
    return resolveProject(this.config, projectId);
  }

  public source(projectId: string, map: string): string {
    return sourcePath(this.project(projectId), map);
  }

  public async status(projectId: string): Promise<Record<string, unknown>> {
    const project = this.project(projectId);
    const source = project.sourceMaps[0];
    if (!source) {
      throw new AppError("INVALID_ARGUMENT", `Project '${projectId}' has no configured source map.`);
    }
    const sourceHash = sha256File(source);
    const engine = await this.worker.request<Record<string, unknown>>("environment_status", {});
    const configured = {
      world_editor: project.config.world_editor ? { path: project.config.world_editor, exists: existsSync(project.config.world_editor) } : { configured: false },
      warcraft: project.config.warcraft ? { path: project.config.warcraft, exists: existsSync(project.config.warcraft) } : { configured: false },
      test_map_root: project.config.test_map_root ? { path: project.config.test_map_root, exists: existsSync(project.config.test_map_root) } : { configured: false }
    };
    return {
      project_id: project.id,
      root: project.root,
      source_map: { path: relativeProjectPath(project, source), ...sourceHash },
      baseline_drift: false,
      engine,
      configured,
      capability_summary: { inspection: "enabled", transaction_staging: "enabled", canonical_build: "enabled_when_validated", launch: project.config.world_editor || project.config.warcraft ? "configured" : "disabled" },
      enabled_tools: project.config.enabled_tools,
      disabled_until_evidence: ["script_source_mutation", "generic_archive_patch", "autonomous_promotion"],
      roots: { staging: relativeProjectPath(project, project.stagingRoot), artifacts: relativeProjectPath(project, project.artifactRoot), builds: relativeProjectPath(project, project.buildRoot) }
    };
  }

  public resolveReadArtifact(projectId: string, candidate: string): string {
    const project = this.project(projectId);
    if (project.config.source_maps.some(map => map.toLowerCase() === candidate.toLowerCase())) {
      return sourcePath(project, candidate);
    }

    return readPath(project, candidate);
  }
}
