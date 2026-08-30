import { existsSync, realpathSync } from "node:fs";
import { isAbsolute, join, normalize, relative, resolve, sep } from "node:path";
import type { ProjectConfig, Wc3Config } from "./schema.js";
import { AppError } from "../errors/app-error.js";

export interface ResolvedProject {
  id: string;
  config: ProjectConfig;
  root: string;
  sourceMaps: string[];
  stagingRoot: string;
  artifactRoot: string;
  buildRoot: string;
  logRoot: string;
  testOutputRoot: string;
}

export function resolveProject(config: Wc3Config, projectId: string): ResolvedProject {
  const project = config.projects[projectId];
  if (!project) {
    throw new AppError("INVALID_ARGUMENT", `Unknown project '${projectId}'.`, false, { project_id: projectId });
  }

  const root = resolve(project.root);
  return {
    id: projectId,
    config: project,
    root,
    sourceMaps: project.source_maps.map(value => resolveContained(root, value, "source map", { allowMissing: true })),
    stagingRoot: resolveConfiguredPath(root, project.staging_root),
    artifactRoot: resolveConfiguredPath(root, project.artifact_root),
    buildRoot: resolveConfiguredPath(root, project.build_root),
    logRoot: resolveConfiguredPath(root, project.log_root),
    testOutputRoot: resolveConfiguredPath(root, project.test_output_root)
  };
}

export function resolveContained(root: string, candidate: string, label: string, options: { allowMissing?: boolean; destructive?: boolean } = {}): string {
  assertSafeRelative(candidate, options.destructive ?? false);
  const resolved = resolve(root, candidate);
  if (!isWithin(root, resolved) || resolved === root) {
    throw new AppError("PATH_OUTSIDE_ROOT", `The ${label} path '${candidate}' is outside its configured root.`, false, { root, candidate });
  }

  if (!options.allowMissing && !existsSync(resolved)) {
    throw new AppError("FILE_NOT_FOUND", `The ${label} does not exist: ${candidate}`, false, { path: candidate });
  }

  const canonicalRoot = existsSync(root) ? realpathSync(root) : resolve(root);
  const canonicalCandidate = resolveExistingPath(resolved, options.allowMissing ?? false);
  if (!isWithin(canonicalRoot, canonicalCandidate) || canonicalCandidate === canonicalRoot) {
    throw new AppError("PATH_OUTSIDE_ROOT", `The ${label} path '${candidate}' resolves outside its configured root.`, false, { root: canonicalRoot, candidate: canonicalCandidate });
  }
  return canonicalCandidate;
}

export function resolveConfiguredPath(root: string, configured: string): string {
  return isAbsolute(configured) ? resolve(configured) : resolveContained(root, configured, "configured path", { allowMissing: true });
}

export function assertSafeRelative(value: string, destructive = false): void {
  if (!value || isAbsolute(value) || value.includes("\0") || /(^|[\\/])\.\.([\\/]|$)/.test(value) || /[?*]/.test(value)) {
    throw new AppError("PATH_OUTSIDE_ROOT", `Unsafe project-relative path '${value}'.`);
  }

  if (/[<>:"|]/.test(value) || value.endsWith(".") || value.endsWith(" ")) {
    throw new AppError("INVALID_ARGUMENT", `Unsupported path syntax '${value}'.`);
  }

  if (destructive && value.includes(":")) {
    throw new AppError("INVALID_ARGUMENT", `Destructive paths cannot contain alternate data stream syntax: '${value}'.`);
  }
}

export function isWithin(root: string, target: string): boolean {
  const rootResolved = normalize(resolve(root));
  const targetResolved = normalize(resolve(target));
  const rel = relative(rootResolved, targetResolved);
  return rel === "" || (rel !== ".." && !rel.startsWith(`..${sep}`) && !isAbsolute(rel));
}

export function resolveExistingPath(candidate: string, allowMissing: boolean): string {
  if (existsSync(candidate)) {
    return realpathSync(candidate);
  }

  if (!allowMissing) {
    throw new AppError("FILE_NOT_FOUND", `Path does not exist: ${candidate}`);
  }

  const parent = resolveExistingPath(join(candidate, ".."), false);
  return join(parent, candidate.slice(parent.length + 1));
}

export function sourcePath(project: ResolvedProject, map: string): string {
  const candidate = resolveContained(project.root, map, "map", { allowMissing: false });
  if (!project.sourceMaps.some(source => source.toLowerCase() === candidate.toLowerCase())) {
    throw new AppError("PATH_OUTSIDE_ROOT", `Map '${map}' is not an allowed source map for project '${project.id}'.`);
  }
  return candidate;
}

export function readPath(project: ResolvedProject, candidate: string): string {
  const resolved = resolveContained(project.root, candidate, "read path", { allowMissing: false });
  const roots = [project.stagingRoot, project.artifactRoot, project.buildRoot, ...project.config.read_roots.map(root => resolveConfiguredPath(project.root, root))];
  if (!roots.some(root => isWithin(root, resolved))) {
    throw new AppError("PATH_OUTSIDE_ROOT", `Read path '${candidate}' is outside the configured read roots.`);
  }
  return resolved;
}

export function relativeProjectPath(project: ResolvedProject, absolutePath: string): string {
  if (!isWithin(project.root, absolutePath)) {
    return absolutePath;
  }
  return relative(project.root, absolutePath).replaceAll("\\", "/");
}
