import { copyFileSync, existsSync, mkdirSync, readFileSync } from "node:fs";
import { spawn, execFileSync } from "node:child_process";
import { basename, extname, join } from "node:path";
import { randomUUID } from "node:crypto";
import { AppError } from "../errors/app-error.js";
import { relativeProjectPath, isWithin } from "../config/resolve-project.js";
import { sha256File, writeJsonArtifact, type ArtifactRef } from "./artifact-service.js";
import { BuildService } from "./build-service.js";

type Milestone = "editor_opened" | "game_loaded" | "smoke_test" | "playtest";
type Evidence = "process_started" | "editor_opened" | "game_loaded" | "smoke_passed" | "playtest_passed";

interface TestSession {
  schema_version: "1.0";
  session_id: string;
  project_id: string;
  build_id: string;
  build_sha256: string;
  target: "editor" | "game";
  executable: string;
  arguments: string[];
  test_copy_path?: string;
  test_copy_sha256?: string;
  pid?: number;
  started_utc: string;
  exit_state: "running" | "unknown" | "exited";
  evidence_level: Evidence;
  milestones: Array<{ milestone: Milestone; result: "pass" | "fail"; recorder: string; notes: string; observed_utc: string }>;
}

export class LaunchService {
  public constructor(private readonly builds: BuildService) {}

  public launchEditor(projectId: string, buildId: string, expectedHash: string): Record<string, unknown> {
    const loaded = this.builds.load(projectId, buildId);
    assertHash(loaded.manifest.output_sha256, expectedHash);
    const executable = loaded.project.config.world_editor;
    if (!executable || !existsSync(executable)) throw new AppError("LAUNCH_FAILED", "World Editor is not configured or does not exist.");
    assertNotRunning(executable);
    return this.start(loaded.project, loaded.manifest.build_id, loaded.manifest.output_sha256, "editor", executable, ["-loadfile", loaded.manifest.output_path]);
  }

  public launchGame(projectId: string, buildId: string, expectedHash: string): Record<string, unknown> {
    const loaded = this.builds.load(projectId, buildId);
    assertHash(loaded.manifest.output_sha256, expectedHash);
    const executable = loaded.project.config.warcraft;
    const testRoot = loaded.project.config.test_map_root;
    if (!executable || !existsSync(executable) || !testRoot) throw new AppError("LAUNCH_FAILED", "Warcraft III and test_map_root must be configured and exist before launching a test map.");
    mkdirSync(testRoot, { recursive: true });
    assertNotRunning(executable);
    const sessionId = randomUUID();
    const extension = extname(loaded.manifest.output_path) || ".w3m";
    const copyPath = join(testRoot, `MCP_${loaded.manifest.build_id}_${sessionId}${extension}`);
    const session = this.copyAndStart(loaded.project, sessionId, loaded.manifest.build_id, loaded.manifest.output_sha256, executable, ["-loadfile", copyPath], loaded.manifest.output_path, copyPath, "game");
    return session;
  }

  public record(projectId: string, sessionId: string, expectedHash: string, milestone: Milestone, result: "pass" | "fail", recorder: "user_observation" | "agent_log_observation", notes: string): Record<string, unknown> {
    const project = this.builds.project(projectId);
    const path = join(project.artifactRoot, "tests", sessionId, "test-session.json");
    if (!existsSync(path)) throw new AppError("FILE_NOT_FOUND", `Test session '${sessionId}' was not found.`);
    const session = JSON.parse(readFileSync(path, "utf8")) as TestSession;
    if (session.build_sha256.toUpperCase() !== expectedHash.toUpperCase()) throw new AppError("SOURCE_CHANGED", "Test result hash does not match the session build hash.");
    const order: Milestone[] = session.target === "editor" ? ["editor_opened"] : ["game_loaded", "smoke_test", "playtest"];
    const expectedIndex = order.indexOf(milestone);
    if (expectedIndex < 0 || (expectedIndex > 0 && !session.milestones.some(item => item.milestone === order[expectedIndex - 1] && item.result === "pass"))) throw new AppError("PRECONDITION_FAILED", `Milestone '${milestone}' is out of order for this ${session.target} session.`);
    if (session.milestones.some(item => item.milestone === milestone)) throw new AppError("PRECONDITION_FAILED", `Milestone '${milestone}' has already been recorded for this session.`);
    session.milestones.push({ milestone, result, recorder, notes, observed_utc: new Date().toISOString() });
    if (result === "pass" && milestone === "editor_opened") session.evidence_level = "editor_opened";
    else if (result === "pass" && milestone === "game_loaded") session.evidence_level = "game_loaded";
    else if (result === "pass" && milestone === "smoke_test") session.evidence_level = "smoke_passed";
    else if (result === "pass" && milestone === "playtest") session.evidence_level = "playtest_passed";
    const artifact = writeJsonArtifact(project, relativeProjectPath(project, path), session, "test_session");
    const buildEvidence = result === "pass" ? this.builds.markRuntimeStatus(projectId, session.build_id, session.evidence_level) : undefined;
    return { session, artifact, ...(buildEvidence ? { build_evidence: buildEvidence } : {}) };
  }

  public get(projectId: string, sessionId: string): Record<string, unknown> {
    const project = this.builds.project(projectId);
    const path = join(project.artifactRoot, "tests", sessionId, "test-session.json");
    if (!existsSync(path)) throw new AppError("FILE_NOT_FOUND", `Test session '${sessionId}' was not found.`);
    return { session: JSON.parse(readFileSync(path, "utf8")) as TestSession, session_path: relativeProjectPath(project, path) };
  }

  private start(project: ReturnType<BuildService["load"]>["project"], buildId: string, buildHash: string, target: "editor" | "game", executable: string, args: string[]): Record<string, unknown> {
    const sessionId = randomUUID();
    return this.copyAndStart(project, sessionId, buildId, buildHash, executable, args, undefined, undefined, target);
  }

  private copyAndStart(project: ReturnType<BuildService["load"]>["project"], sessionId: string, buildId: string, buildHash: string, executable: string, args: string[], source: string | undefined, copyPath: string | undefined, target: "editor" | "game"): Record<string, unknown> {
    if (source && copyPath) {
      if (!isWithin(project.config.test_map_root ?? "", copyPath)) throw new AppError("PATH_OUTSIDE_ROOT", "Test map destination escaped the configured test root.");
      copyFileSync(source, copyPath);
      const copied = sha256File(copyPath);
      if (copied.sha256 !== buildHash.toUpperCase()) throw new AppError("LAUNCH_FAILED", "The test copy hash did not match the build.");
    }
    const child = spawn(executable, args, { detached: true, stdio: "ignore", windowsHide: false });
    const session: TestSession = { schema_version: "1.0", session_id: sessionId, project_id: project.id, build_id: buildId, build_sha256: buildHash, target, executable, arguments: args, started_utc: new Date().toISOString(), exit_state: "unknown", evidence_level: "process_started", milestones: [], ...(copyPath ? { test_copy_path: copyPath, test_copy_sha256: sha256File(copyPath).sha256 } : {}), ...(child.pid !== undefined ? { pid: child.pid } : {}) };
    const artifact = writeJsonArtifact(project, relativeProjectPath(project, join(project.artifactRoot, "tests", sessionId, "test-session.json")), session, "test_session");
    child.unref();
    return { session, artifact };
  }

}

function assertHash(actual: string, expected: string): void {
  if (!/^[0-9A-F]{64}$/i.test(expected) || actual.toUpperCase() !== expected.toUpperCase()) throw new AppError("SOURCE_CHANGED", "The expected build hash does not match the manifest.");
}

function assertNotRunning(executable: string): void {
  const image = basename(executable);
  try {
    const output = execFileSync("tasklist.exe", ["/FI", `IMAGENAME eq ${image}`], { encoding: "utf8", windowsHide: true });
    if (output.toLowerCase().includes(image.toLowerCase())) throw new AppError("LAUNCH_FAILED", `The target process '${image}' is already running; fail_if_running prevented launch.`);
  } catch (error) {
    if (error instanceof AppError) throw error;
  }
}
