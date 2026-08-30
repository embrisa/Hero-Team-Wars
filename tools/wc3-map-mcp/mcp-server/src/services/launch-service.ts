import { existsSync, lstatSync, readFileSync, rmSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { basename, dirname, extname, join, resolve } from "node:path";
import { AppError } from "../errors/app-error.js";
import { isWithin, relativeProjectPath, resolveContained, type ResolvedProject } from "../config/resolve-project.js";
import { sha256File, writeJsonArtifact, type ArtifactRef } from "./artifact-service.js";
import { BuildService, type BuildManifest } from "./build-service.js";
import { copyFileAtomic } from "../storage/transaction-store.js";
import { NativeProcessRunner, type ProcessRunner, type StartedProcess } from "../platform/windows/process-runner.js";
import { WindowsWarcraftPaths, type ExecutableInfo, type WarcraftPaths } from "../platform/windows/warcraft-paths.js";

export type Milestone = "editor_opened" | "game_loaded" | "smoke_test" | "playtest";
export type EvidenceLevel = "process_started" | "editor_opened" | "game_loaded" | "smoke_passed" | "playtest_passed";
export type TestResult = "pass" | "fail";
export type TestRecorder = "user_observation" | "agent_log_observation";

export interface TestMilestone {
  milestone: Milestone;
  result: TestResult;
  recorder: TestRecorder;
  correlation_id: string;
  notes: string;
  observed_utc: string;
  artifacts: ArtifactRef[];
}

export interface TestSession {
  schema_version: "1.0";
  session_id: string;
  project_id: string;
  correlation_id: string;
  transaction_id: string;
  revision: number;
  build_id: string;
  build_sha256: string;
  build_path: string;
  target: "editor" | "game";
  executable: string;
  executable_version: string;
  executable_version_verified: boolean;
  arguments: string[];
  test_copy_path?: string;
  test_copy_sha256?: string;
  pid: number;
  started_utc: string;
  exit_state: "running" | "unknown" | "exited";
  evidence_level: EvidenceLevel;
  milestones: TestMilestone[];
  artifacts: ArtifactRef[];
  notes: string[];
}

interface LoadedSession {
  project: ResolvedProject;
  path: string;
  session: TestSession;
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const HASH = /^[0-9A-F]{64}$/i;

export class LaunchService {
  public constructor(
    private readonly builds: BuildService,
    private readonly runner: ProcessRunner = new NativeProcessRunner(),
    private readonly paths: WarcraftPaths = new WindowsWarcraftPaths(),
    private readonly idFactory: () => string = randomUUID,
    private readonly now: () => string = () => new Date().toISOString()
  ) {}

  public launchEditor(projectId: string, buildId: string, expectedHash: string, correlationId = randomUUID()): Record<string, unknown> {
    const loaded = this.loadBuild(projectId, buildId, expectedHash);
    const executable = this.paths.executable(loaded.project.config.world_editor, "editor");
    this.assertNotRunning(executable.path);

    const sessionId = this.idFactory();
    assertUuid(sessionId, "Session ID");
    const buildPath = buildPathFor(loaded.project, loaded.manifest);
    const argumentsArray = ["-loadfile", buildPath];
    const started = this.runner.start({ executable: executable.path, arguments: argumentsArray, working_directory: dirname(executable.path) });
    const session = this.createSession(loaded.project, loaded.manifest, sessionId, correlationId, executable, argumentsArray, started, "editor", buildPath);
    return this.persistSession(session);
  }

  public launchGame(projectId: string, buildId: string, expectedHash: string, correlationId = randomUUID()): Record<string, unknown> {
    const loaded = this.loadBuild(projectId, buildId, expectedHash);
    const executable = this.paths.executable(loaded.project.config.warcraft, "game");
    const testRoot = this.paths.testRoot(loaded.project.config.test_map_root);
    this.assertNotRunning(executable.path);

    const sessionId = this.idFactory();
    assertUuid(sessionId, "Session ID");
    const buildPath = buildPathFor(loaded.project, loaded.manifest);
    const copyPath = this.paths.testCopyPath(testRoot, loaded.manifest.build_id, sessionId, extname(buildPath).toLowerCase());
    const copyHash = copyBuildForTest(loaded.manifest.output_sha256, buildPath, copyPath);
    const argumentsArray = ["-loadfile", copyPath];
    const started = this.runner.start({ executable: executable.path, arguments: argumentsArray, working_directory: dirname(executable.path) });
    const session = this.createSession(loaded.project, loaded.manifest, sessionId, correlationId, executable, argumentsArray, started, "game", buildPath, copyPath, copyHash);
    return this.persistSession(session);
  }

  public record(
    projectId: string,
    sessionId: string,
    expectedHash: string,
    milestone: Milestone,
    result: TestResult,
    recorder: TestRecorder,
    notes: string,
    artifactPaths: string[] = [],
    correlationId = randomUUID()
  ): Record<string, unknown> {
    assertUuid(sessionId, "Session ID");
    const loaded = this.readSession(projectId, sessionId);
    const build = this.builds.load(projectId, loaded.session.build_id);
    assertSessionBuild(loaded.session, build.manifest, expectedHash);
    verifySessionReferences(loaded.project, loaded.session, build.manifest);

    const allowed = loaded.session.target === "editor"
      ? ["editor_opened"] as Milestone[]
      : ["game_loaded", "smoke_test", "playtest"] as Milestone[];
    const milestoneIndex = allowed.indexOf(milestone);
    if (milestoneIndex < 0 || (milestoneIndex > 0 && !loaded.session.milestones.some(item => item.milestone === allowed[milestoneIndex - 1] && item.result === "pass"))) {
      throw new AppError("PRECONDITION_FAILED", `Milestone '${milestone}' is out of order for this ${loaded.session.target} session.`);
    }
    if (loaded.session.milestones.some(item => item.milestone === milestone)) {
      throw new AppError("PRECONDITION_FAILED", `Milestone '${milestone}' has already been recorded for this session.`);
    }

    const artifacts = captureArtifacts(loaded.project, artifactPaths);
    loaded.session.milestones.push({ milestone, result, recorder, correlation_id: correlationId, notes, observed_utc: this.now(), artifacts });
    loaded.session.artifacts.push(...artifacts);
    loaded.session.notes.push(notes);
    if (result === "pass") loaded.session.evidence_level = evidenceForMilestone(milestone);

    const artifact = writeJsonArtifact(loaded.project, relativeProjectPath(loaded.project, loaded.path), loaded.session, "test_session");
    const buildEvidence = result === "pass"
      ? this.builds.markRuntimeStatus(projectId, loaded.session.build_id, loaded.session.evidence_level)
      : undefined;
    return { session: loaded.session, artifact, ...(buildEvidence ? { build_evidence: buildEvidence } : {}) };
  }

  public get(projectId: string, sessionId: string): Record<string, unknown> {
    assertUuid(sessionId, "Session ID");
    const loaded = this.readSession(projectId, sessionId);
    const build = this.builds.load(projectId, loaded.session.build_id);
    assertSessionBuild(loaded.session, build.manifest, loaded.session.build_sha256);
    verifySessionReferences(loaded.project, loaded.session, build.manifest);
    return { session: loaded.session, verified: true, session_path: relativeProjectPath(loaded.project, loaded.path) };
  }

  private loadBuild(projectId: string, buildId: string, expectedHash: string): { project: ResolvedProject; manifest: BuildManifest } {
    const loaded = this.builds.load(projectId, buildId);
    assertHash(loaded.manifest.output_sha256, expectedHash);
    return { project: loaded.project, manifest: loaded.manifest };
  }

  private assertNotRunning(executable: string): void {
    if (this.runner.isRunning(executable)) {
      throw new AppError("LAUNCH_FAILED", `The target process '${executable}' is already running; fail_if_running prevented launch.`, true, { executable, policy: "fail_if_running" });
    }
  }

  private createSession(
    project: ResolvedProject,
    manifest: BuildManifest,
    sessionId: string,
    correlationId: string,
    executable: ExecutableInfo,
    argumentsArray: string[],
    started: StartedProcess,
    target: "editor" | "game",
    buildPath: string,
    testCopyPath?: string,
    testCopyHash?: string
  ): TestSession {
    if (!Number.isInteger(started.pid) || started.pid < 1) throw new AppError("LAUNCH_FAILED", "The process runner did not return a valid PID.");
    if (!HASH.test(manifest.output_sha256)) throw new AppError("INTERNAL_ERROR", "The build manifest contains an invalid output hash.");
    return {
      schema_version: "1.0",
      session_id: sessionId,
      project_id: project.id,
      correlation_id: correlationId,
      transaction_id: manifest.transaction_id,
      revision: manifest.revision,
      build_id: manifest.build_id,
      build_sha256: manifest.output_sha256.toUpperCase(),
      build_path: relativeProjectPath(project, buildPath),
      target,
      executable: executable.path,
      executable_version: executable.version,
      executable_version_verified: executable.version_verified,
      arguments: [...argumentsArray],
      started_utc: started.started_utc,
      exit_state: started.exit_state,
      evidence_level: "process_started",
      milestones: [],
      artifacts: [],
      notes: [],
      ...(testCopyPath !== undefined ? { test_copy_path: testCopyPath } : {}),
      ...(testCopyHash !== undefined ? { test_copy_sha256: testCopyHash.toUpperCase() } : {}),
      pid: started.pid
    };
  }

  private persistSession(session: TestSession): Record<string, unknown> {
    const project = this.builds.project(session.project_id);
    const path = sessionPath(project, session.session_id);
    const artifact = writeJsonArtifact(project, relativeProjectPath(project, path), session, "test_session");
    if (typeof this.builds.attachTestSession === "function") this.builds.attachTestSession(session.project_id, session.build_id, session.session_id);
    return { session, artifact };
  }

  private readSession(projectId: string, sessionId: string): LoadedSession {
    const project = this.builds.project(projectId);
    const path = sessionPath(project, sessionId);
    if (!existsSync(path)) throw new AppError("FILE_NOT_FOUND", `Test session '${sessionId}' was not found.`);
    const stat = lstatSync(path);
    if (!stat.isFile() || stat.isSymbolicLink()) throw new AppError("PATH_OUTSIDE_ROOT", "The test session must be a regular MCP-owned file.");
    let value: unknown;
    try {
      value = JSON.parse(readFileSync(path, "utf8")) as unknown;
    } catch (error) {
      throw new AppError("INTERNAL_ERROR", `Test session '${sessionId}' is invalid.`, false, {}, { cause: error });
    }
    validateSession(value, sessionId, project.id);
    return { project, path, session: value as TestSession };
  }
}

function buildPathFor(project: ResolvedProject, manifest: BuildManifest): string {
  const path = resolve(project.root, manifest.output_path);
  if (!isWithin(project.buildRoot, path) || !existsSync(path)) throw new AppError("PATH_OUTSIDE_ROOT", "The build output is no longer beneath the configured build root.");
  const stat = lstatSync(path);
  if (!stat.isFile() || stat.isSymbolicLink()) throw new AppError("PATH_OUTSIDE_ROOT", "The build output must be a regular file before launch.");
  return path;
}

function copyBuildForTest(expectedHash: string, source: string, destination: string): string {
  const current = sha256File(source);
  assertHash(current.sha256, expectedHash);
  copyFileAtomic(source, destination);
  const copied = sha256File(destination);
  try {
    assertHash(copied.sha256, expectedHash);
  } catch (error) {
    try {
      const stat = lstatSync(destination);
      if (stat.isFile() && !stat.isSymbolicLink()) rmSync(destination, { force: true });
    } catch {
      // Preserve the actionable hash mismatch even if cleanup is unavailable.
    }
    throw new AppError("LAUNCH_FAILED", "The test copy hash did not match the selected build.", false, { expected_sha256: expectedHash.toUpperCase(), actual_sha256: copied.sha256 }, { cause: error });
  }
  const after = sha256File(source);
  assertHash(after.sha256, expectedHash);
  return copied.sha256;
}

function sessionPath(project: ResolvedProject, sessionId: string): string {
  assertUuid(sessionId, "Session ID");
  const path = join(project.testOutputRoot, sessionId, "test-session.json");
  if (!isWithin(project.testOutputRoot, path) || path.toLowerCase() === project.testOutputRoot.toLowerCase()) throw new AppError("PATH_OUTSIDE_ROOT", "The test session path escaped the configured test output root.");
  return path;
}

function assertHash(actual: string, expected: string): void {
  if (!HASH.test(expected) || actual.toUpperCase() !== expected.toUpperCase()) {
    throw new AppError("SOURCE_CHANGED", "The expected build hash does not match the selected build.", false, { expected_sha256: expected.toUpperCase(), actual_sha256: actual.toUpperCase() });
  }
}

function assertUuid(value: string, label: string): void {
  if (!UUID.test(value)) throw new AppError("INVALID_ARGUMENT", `${label} must be a UUID.`);
}

function assertSessionBuild(session: TestSession, manifest: BuildManifest, expectedHash: string): void {
  assertHash(session.build_sha256, expectedHash);
  if (session.project_id !== manifest.project_id || session.build_id !== manifest.build_id || session.transaction_id !== manifest.transaction_id || session.revision !== manifest.revision) {
    throw new AppError("SOURCE_CHANGED", "The test session does not reference the selected build revision.", false, { session_build_id: session.build_id, manifest_build_id: manifest.build_id, session_revision: session.revision, manifest_revision: manifest.revision });
  }
  assertHash(manifest.output_sha256, session.build_sha256);
}

function verifySessionReferences(project: ResolvedProject, session: TestSession, manifest: BuildManifest): void {
  const buildPath = buildPathFor(project, manifest);
  if (session.build_path !== relativeProjectPath(project, buildPath)) throw new AppError("SOURCE_CHANGED", "The test session build path does not match the build manifest.");
  const expectedLoadPath = session.target === "editor" ? buildPath : session.test_copy_path;
  if (session.arguments.length !== 2 || session.arguments[0] !== "-loadfile" || session.arguments[1] !== expectedLoadPath) {
    throw new AppError("INTERNAL_ERROR", "The test session argument array does not match its target and exact build path.");
  }
  if (session.target === "game") {
    if (!session.test_copy_path || !session.test_copy_sha256) throw new AppError("INTERNAL_ERROR", "A game test session is missing its test copy identity.");
    verifyTestCopy(project, session);
  }
  for (const artifact of session.artifacts) {
    const path = resolveEvidenceArtifact(project, artifact.path);
    const hash = sha256File(path);
    if (hash.sha256 !== artifact.sha256 || hash.size_bytes !== artifact.size_bytes) throw new AppError("SOURCE_CHANGED", `Test evidence artifact '${artifact.path}' no longer matches its recorded hash.`);
  }
}

function verifyTestCopy(project: ResolvedProject, session: TestSession): void {
  const configuredRoot = project.config.test_map_root;
  if (!configuredRoot || !session.test_copy_path || !session.test_copy_sha256) throw new AppError("INTERNAL_ERROR", "The game test session has no configured test-copy identity.");
  const root = resolve(configuredRoot);
  if (!existsSync(root)) throw new AppError("FILE_NOT_FOUND", "The configured test_map_root no longer exists.");
  const rootStat = lstatSync(root);
  if (!rootStat.isDirectory() || rootStat.isSymbolicLink()) throw new AppError("PATH_OUTSIDE_ROOT", "The configured test_map_root is no longer a real directory.");
  const candidate = resolve(session.test_copy_path);
  if (!isWithin(root, candidate) || dirname(candidate).toLowerCase() !== root.toLowerCase()) throw new AppError("PATH_OUTSIDE_ROOT", "The test copy is outside the configured test_map_root.");
  const realRoot = resolve(root);
  const realParent = resolve(dirname(candidate));
  if (!isWithin(realRoot, realParent) || realParent.toLowerCase() !== realRoot.toLowerCase()) throw new AppError("PATH_OUTSIDE_ROOT", "The test copy resolves outside the configured test_map_root.");
  const expectedName = `MCP_${session.build_id}_${session.session_id}${extname(candidate).toLowerCase()}`;
  if (basename(candidate).toLowerCase() !== expectedName.toLowerCase()) throw new AppError("PATH_OUTSIDE_ROOT", "The test copy name is not the MCP-generated name for this session.");
  if (!existsSync(candidate)) throw new AppError("FILE_NOT_FOUND", "The recorded test copy no longer exists.");
  const stat = lstatSync(candidate);
  if (!stat.isFile() || stat.isSymbolicLink()) throw new AppError("PATH_OUTSIDE_ROOT", "The recorded test copy must be a regular file.");
  const hash = sha256File(candidate);
  if (hash.sha256 !== session.test_copy_sha256.toUpperCase()) throw new AppError("SOURCE_CHANGED", "The recorded test copy no longer matches its session hash.");
}

function captureArtifacts(project: ResolvedProject, paths: string[]): ArtifactRef[] {
  const result: ArtifactRef[] = [];
  for (const candidate of paths) {
    const path = resolveEvidenceArtifact(project, candidate);
    const hash = sha256File(path);
    result.push({ kind: "test_evidence", path: relativeProjectPath(project, path), size_bytes: hash.size_bytes, sha256: hash.sha256 });
  }
  return result;
}

function resolveEvidenceArtifact(project: ResolvedProject, candidate: string): string {
  const path = resolveContained(project.root, candidate, "test evidence artifact", { allowMissing: false });
  const allowedRoots = [project.artifactRoot, project.buildRoot, project.stagingRoot, ...project.config.read_roots.map(root => resolve(project.root, root))];
  if (!allowedRoots.some(root => isWithin(root, path))) throw new AppError("PATH_OUTSIDE_ROOT", `Test evidence artifact '${candidate}' is outside configured artifact/read roots.`);
  const stat = lstatSync(path);
  if (!stat.isFile() || stat.isSymbolicLink()) throw new AppError("PATH_OUTSIDE_ROOT", `Test evidence artifact '${candidate}' must be a regular file.`);
  return path;
}

function validateSession(value: unknown, sessionId: string, projectId: string): void {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new AppError("INTERNAL_ERROR", "Test session must be an object.");
  const session = value as Partial<TestSession>;
  if (session.schema_version !== "1.0" || session.session_id !== sessionId || session.project_id !== projectId || !UUID.test(session.correlation_id ?? "") || !UUID.test(session.transaction_id ?? "") || !UUID.test(session.build_id ?? "") || !Number.isInteger(session.revision) || (session.revision ?? -1) < 0 || !HASH.test(session.build_sha256 ?? "") || typeof session.build_path !== "string" || !Array.isArray(session.arguments) || !session.arguments.every(argument => typeof argument === "string") || typeof session.executable !== "string" || typeof session.executable_version !== "string" || typeof session.executable_version_verified !== "boolean" || !Number.isInteger(session.pid) || (session.pid ?? 0) < 1 || typeof session.started_utc !== "string" || !["running", "unknown", "exited"].includes(session.exit_state ?? "") || !["process_started", "editor_opened", "game_loaded", "smoke_passed", "playtest_passed"].includes(session.evidence_level ?? "") || !Array.isArray(session.milestones) || !Array.isArray(session.artifacts) || !Array.isArray(session.notes)) {
    throw new AppError("INTERNAL_ERROR", `Test session '${sessionId}' is missing required identity or state fields.`);
  }
  if (session.target !== "editor" && session.target !== "game") throw new AppError("INTERNAL_ERROR", `Test session '${sessionId}' has an invalid target.`);

  const allowed = session.target === "editor"
    ? ["editor_opened"] as Milestone[]
    : ["game_loaded", "smoke_test", "playtest"] as Milestone[];
  const milestones = session.milestones as unknown[];
  let expectedEvidence: EvidenceLevel = "process_started";
  let previous: TestMilestone | undefined;
  const seen = new Set<string>();
  for (const value of milestones) {
    if (!value || typeof value !== "object" || Array.isArray(value)) throw new AppError("INTERNAL_ERROR", `Test session '${sessionId}' contains an invalid milestone.`);
    const item = value as Partial<TestMilestone>;
    if (!allowed.includes(item.milestone as Milestone) || (item.result !== "pass" && item.result !== "fail") || (item.recorder !== "user_observation" && item.recorder !== "agent_log_observation") || !UUID.test(item.correlation_id ?? "") || typeof item.notes !== "string" || typeof item.observed_utc !== "string" || !Array.isArray(item.artifacts) || !item.artifacts.every(isArtifact)) {
      throw new AppError("INTERNAL_ERROR", `Test session '${sessionId}' contains an invalid milestone record.`);
    }
    const milestone = item.milestone as Milestone;
    const index = allowed.indexOf(milestone);
    if (seen.has(milestone) || (index !== (previous ? allowed.indexOf(previous.milestone) + 1 : 0)) || (previous && previous.result !== "pass")) {
      throw new AppError("INTERNAL_ERROR", `Test session '${sessionId}' contains out-of-order or duplicate milestones.`);
    }
    seen.add(milestone);
    previous = item as TestMilestone;
    if (item.result === "pass") expectedEvidence = evidenceForMilestone(milestone);
  }
  if (!milestones.every(item => isTestMilestone(item)) || session.evidence_level !== expectedEvidence || !session.artifacts.every(isArtifact) || !session.notes.every(note => typeof note === "string")) {
    throw new AppError("INTERNAL_ERROR", `Test session '${sessionId}' contains inconsistent evidence state.`);
  }
  if ((session.test_copy_path === undefined) !== (session.test_copy_sha256 === undefined)) {
    throw new AppError("INTERNAL_ERROR", `Test session '${sessionId}' contains an incomplete test-copy identity.`);
  }
}

function isArtifact(value: unknown): value is ArtifactRef {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const artifact = value as Partial<ArtifactRef>;
  return typeof artifact.kind === "string" && artifact.kind.length > 0 && typeof artifact.path === "string" && artifact.path.length > 0 && Number.isInteger(artifact.size_bytes) && (artifact.size_bytes ?? -1) >= 0 && typeof artifact.sha256 === "string" && HASH.test(artifact.sha256);
}

function isTestMilestone(value: unknown): value is TestMilestone {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const item = value as Partial<TestMilestone>;
  return typeof item.milestone === "string" && (item.result === "pass" || item.result === "fail") && (item.recorder === "user_observation" || item.recorder === "agent_log_observation") && UUID.test(item.correlation_id ?? "") && typeof item.notes === "string" && typeof item.observed_utc === "string" && Array.isArray(item.artifacts) && item.artifacts.every(isArtifact);
}

function evidenceForMilestone(milestone: Milestone): EvidenceLevel {
  switch (milestone) {
    case "editor_opened": return "editor_opened";
    case "game_loaded": return "game_loaded";
    case "smoke_test": return "smoke_passed";
    case "playtest": return "playtest_passed";
  }
}
