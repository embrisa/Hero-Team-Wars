import { existsSync, statSync } from "node:fs";
import { extname } from "node:path";
import type { Wc3Config } from "../config/schema.js";
import { readPath, relativeProjectPath, resolveConfiguredPath, resolveProject, sourcePath, type ResolvedProject } from "../config/resolve-project.js";
import { AppError } from "../errors/app-error.js";
import { sha256File } from "./artifact-service.js";
import { WorkerClient } from "../transport/worker-client.js";
import { capabilityMatrix, isToolEnabledForProject, isToolSupportedByProfile, type CapabilityProfile } from "./capability-catalog.js";

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

  public assertToolAvailable(projectId: string, toolName: string): void {
    const project = this.project(projectId);
    if (!isToolEnabledForProject(project.config, toolName)) {
      throw new AppError("INVALID_ARGUMENT", `Tool '${toolName}' is not enabled for project '${projectId}'.`);
    }
  }

  public assertProfile(projectId: string, requested: string | undefined, operation: string): CapabilityProfile {
    const project = this.project(projectId);
    const profile = requested ?? project.config.profile;
    if (!isToolSupportedByProfile(project.config.profile, operation)) {
      throw new AppError("CAPABILITY_GATED", `Operation '${operation}' is not enabled for project profile '${project.config.profile}'.`, false, { project_id: projectId, profile: project.config.profile, operation });
    }
    if (profile !== project.config.profile) {
      throw new AppError("CAPABILITY_GATED", `Requested profile '${profile}' does not match project profile '${project.config.profile}'.`, false, { project_id: projectId, requested_profile: profile, project_profile: project.config.profile });
    }
    if (profile === "gui_compatible") {
      throw new AppError("CAPABILITY_GATED", `Operation '${operation}' is gated for gui_compatible until exact GUI trigger evidence exists.`, false, { project_id: projectId, profile });
    }
    return profile as CapabilityProfile;
  }

  public assertMutationAllowed(projectId: string, toolName: string): void {
    const project = this.project(projectId);
    if (project.config.write_policy === "read_only") {
      throw new AppError("INVALID_ARGUMENT", `Project '${projectId}' is read-only; '${toolName}' is disabled.`);
    }
    this.assertToolAvailable(projectId, toolName);
  }

  public assertScriptMutationAllowed(projectId: string): void {
    const project = this.project(projectId);
    this.assertMutationAllowed(projectId, "wc3_apply_operations");
    if (project.config.script_policy !== "mcp_owned_jass") {
      throw new AppError("SCRIPT_MUTATION_DISABLED", "MCP-owned JASS mutation is disabled for this project. Set script_policy to 'mcp_owned_jass' in the writes-enabled local configuration after reviewing ADR 0002.", false, {
        project_id: projectId,
        script_policy: project.config.script_policy
      });
    }
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
    const readOnlyTools = ["wc3_project_status", "wc3_inspect_map", "wc3_list_archive_files", "wc3_get_component", "wc3_get_script_source", "wc3_validate_map", "wc3_compare_maps", "wc3_compose_gameplay_source", "wc3_validate_gameplay_source", "jass_lookup", "jass_search", "jass_validate_call", "jass_validate_source"];
    const transactionTools = ["wc3_begin_transaction", "wc3_apply_operations", "wc3_transaction_diff", "wc3_validate_transaction", "wc3_discard_transaction"];
    const gameplayTools = ["wc3_compose_gameplay_source", "wc3_validate_gameplay_source", "wc3_prepare_gameplay_chunk", "wc3_run_scenario_build", "wc3_record_chunk_result"];
    const laterTools = ["wc3_build_map", "wc3_build_report", "wc3_launch_editor", "wc3_launch_test_map", "wc3_record_test_result", "wc3_get_test_session", "wc3_promote_build", ...gameplayTools];
    const allTools = [...new Set([...readOnlyTools, ...transactionTools, ...laterTools])];
    const enabledTools = allTools.filter(tool => isToolEnabledForProject(project.config, tool));
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
      capability_summary: { inspection: "enabled", comparison: "enabled", validation: project.config.write_policy === "read_only" ? "read_only" : "transactional", mutation: project.config.write_policy === "read_only" ? "disabled" : "typed_write_enabled", script_source: project.config.write_policy === "read_only" ? "disabled" : project.config.script_policy, build: project.config.write_policy === "read_only" ? "disabled" : "available_after_validation", launch: project.config.write_policy === "read_only" ? "disabled" : "approval_gated", deletion: project.config.write_policy === "read_only" ? "disabled" : "confirmed_transaction_only" },
      profile: project.config.profile,
      gameplay_capabilities: { source_composition: "mcp_native_jass", gui_trigger_compatibility: "gated_pending_exact_fixture_and_editor_evidence", scenarios: "static_only", runtime_evidence: "explicit_observation_only" },
      capability_matrix: capabilityMatrix(project.config),
      enabled_tools: enabledTools,
      disabled_tools: allTools.filter(tool => !enabledTools.includes(tool)),
      disabled_until_evidence: [...(project.config.script_policy === "disabled" ? ["script_source_mutation"] : []), "generic_archive_patch", "autonomous_promotion"],
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
