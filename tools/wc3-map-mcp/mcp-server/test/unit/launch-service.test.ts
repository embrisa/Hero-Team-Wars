import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { afterEach, describe, expect, it, vi } from "vitest";
import { LaunchService } from "../../src/services/launch-service.js";
import type { BuildManifest, BuildService } from "../../src/services/build-service.js";
import type { ResolvedProject } from "../../src/config/resolve-project.js";
import type { ProcessRunner, ProcessStartRequest } from "../../src/platform/windows/process-runner.js";
import type { ExecutableInfo, WarcraftPaths } from "../../src/platform/windows/warcraft-paths.js";

const buildId = "11111111-1111-4111-8111-111111111111";
const transactionId = "22222222-2222-4222-8222-222222222222";
const editorSessionId = "33333333-3333-4333-8333-333333333333";
const gameSessionId = "44444444-4444-4444-8444-444444444444";

const roots: string[] = [];

afterEach(() => {
  while (roots.length > 0) {
    const root = roots.pop();
    if (root) rmSync(root, { recursive: true, force: true });
  }
});

describe("LaunchService", () => {
  it("launches an exact build with an argument array and persists process-start evidence", () => {
    const fixture = createFixture();
    const runner = fakeRunner();
    const service = createService(fixture, runner, editorSessionId);

    const result = service.launchEditor("hero-team-wars", buildId, fixture.hash);
    const session = result.session as any;
    const request = runner.start.mock.calls[0]?.[0] as ProcessStartRequest;

    expect(request.executable).toBe(fixture.editorPath);
    expect(request.arguments).toEqual(["-launch", "-loadfile", fixture.buildPath]);
    expect(request.arguments).not.toContain("--");
    expect(session).toEqual(expect.objectContaining({
      session_id: editorSessionId,
      transaction_id: transactionId,
      revision: 4,
      build_id: buildId,
      build_sha256: fixture.hash,
      build_path: fixture.relativeBuildPath,
      target: "editor",
      executable_version: "2.0.4.23745",
      pid: 9876,
      evidence_level: "process_started",
      exit_state: "unknown"
    }));
    expect(JSON.parse(readFileSync(join(fixture.testOutputRoot, editorSessionId, "test-session.json"), "utf8"))).toEqual(session);
    expect(fixture.builds.attachTestSession).toHaveBeenCalledWith("hero-team-wars", buildId, editorSessionId);
  });

  it("blocks a hash mismatch before starting a process or creating a session", () => {
    const fixture = createFixture();
    const runner = fakeRunner();
    const service = createService(fixture, runner, editorSessionId);

    expect(() => service.launchEditor("hero-team-wars", buildId, "A".repeat(64))).toThrow(/expected build hash/i);
    expect(runner.start).not.toHaveBeenCalled();
    expect(existsSync(fixture.testOutputRoot)).toBe(false);
  });

  it("copies a game build into the configured root and records the copy hash", () => {
    const fixture = createFixture();
    const runner = fakeRunner();
    const service = createService(fixture, runner, gameSessionId);

    const result = service.launchGame("hero-team-wars", buildId, fixture.hash);
    const session = result.session as any;
    const request = runner.start.mock.calls[0]?.[0] as ProcessStartRequest;
    const copyPath = session.test_copy_path as string;

    expect(request.arguments).toEqual(["-loadfile", copyPath]);
    expect(copyPath.startsWith(fixture.testMapRoot)).toBe(true);
    expect(existsSync(copyPath)).toBe(true);
    expect(session.test_copy_sha256).toBe(fixture.hash);
    expect(createHash("sha256").update(readFileSync(copyPath)).digest("hex").toUpperCase()).toBe(fixture.hash);
  });

  it("refuses an existing target process without terminating it", () => {
    const fixture = createFixture();
    const runner = fakeRunner(true);
    const service = createService(fixture, runner, editorSessionId);

    expect(() => service.launchEditor("hero-team-wars", buildId, fixture.hash)).toThrow(/already running/i);
    expect(runner.start).not.toHaveBeenCalled();
    expect(runner.terminate).not.toHaveBeenCalled();
  });

  it("keeps failed observations from becoming passed evidence", () => {
    const fixture = createFixture();
    const runner = fakeRunner();
    const service = createService(fixture, runner, editorSessionId);
    service.launchEditor("hero-team-wars", buildId, fixture.hash);

    const failed = service.record("hero-team-wars", editorSessionId, fixture.hash, "editor_opened", "fail", "user_observation", "Editor did not show the map.");
    expect((failed.session as any).evidence_level).toBe("process_started");
    expect(fixture.builds.markRuntimeStatus).not.toHaveBeenCalled();
    expect(() => service.record("hero-team-wars", editorSessionId, fixture.hash, "editor_opened", "pass", "user_observation", "Retried.")).toThrow(/already been recorded/i);
    expect((service.get("hero-team-wars", editorSessionId).session as any).evidence_level).toBe("process_started");
  });

  it("requires ordered game milestones and retains the highest observed level after a failure", () => {
    const fixture = createFixture();
    const runner = fakeRunner();
    const service = createService(fixture, runner, gameSessionId);
    service.launchGame("hero-team-wars", buildId, fixture.hash);

    expect(() => service.record("hero-team-wars", gameSessionId, fixture.hash, "smoke_test", "pass", "user_observation", "Skipped load.")).toThrow(/out of order/i);
    service.record("hero-team-wars", gameSessionId, fixture.hash, "game_loaded", "pass", "user_observation", "Exact copy loaded.");
    service.record("hero-team-wars", gameSessionId, fixture.hash, "smoke_test", "fail", "user_observation", "Smoke marker missing.");
    expect((service.get("hero-team-wars", gameSessionId).session as any).evidence_level).toBe("game_loaded");
    expect(() => service.record("hero-team-wars", gameSessionId, fixture.hash, "playtest", "pass", "user_observation", "Cannot skip failed smoke.")).toThrow(/out of order/i);
  });

  it("hashes evidence artifacts and rejects drift during a later read", () => {
    const fixture = createFixture();
    const evidence = join(fixture.artifactRoot, "evidence.txt");
    writeFileSync(evidence, "observed");
    const runner = fakeRunner();
    const service = createService(fixture, runner, editorSessionId);
    service.launchEditor("hero-team-wars", buildId, fixture.hash);

    const result = service.record("hero-team-wars", editorSessionId, fixture.hash, "editor_opened", "pass", "user_observation", "Opened exact build.", ["artifacts/evidence.txt"]);
    expect((result.session as any).artifacts).toEqual([expect.objectContaining({ kind: "test_evidence", path: "artifacts/evidence.txt" })]);
    expect((service.get("hero-team-wars", editorSessionId) as any).verified).toBe(true);
    writeFileSync(evidence, "changed");
    expect(() => service.get("hero-team-wars", editorSessionId)).toThrow(/no longer matches/i);
  });
});

function createFixture(): {
  root: string;
  artifactRoot: string;
  testOutputRoot: string;
  testMapRoot: string;
  editorPath: string;
  gamePath: string;
  buildPath: string;
  relativeBuildPath: string;
  hash: string;
  project: ResolvedProject;
  builds: Record<string, any>;
} {
  const root = mkdtempSync(join(tmpdir(), "wc3-map-mcp-launch-test-"));
  roots.push(root);
  const artifactRoot = join(root, "artifacts");
  const testOutputRoot = join(artifactRoot, "tests");
  const testMapRoot = join(root, "test-map-root");
  const buildRoot = join(root, "builds", "mcp");
  const buildPath = join(buildRoot, "hero-team-wars", buildId, "HeroTeamWars_test.w3m");
  const editorPath = join(root, "World Editor.exe");
  const gamePath = join(root, "Warcraft III.exe");
  mkdirSync(join(buildRoot, "hero-team-wars", buildId), { recursive: true });
  mkdirSync(artifactRoot, { recursive: true });
  writeFileSync(buildPath, "test build bytes");
  writeFileSync(editorPath, "fake executable");
  writeFileSync(gamePath, "fake executable");
  const hash = createHash("sha256").update(readFileSync(buildPath)).digest("hex").toUpperCase();
  const relativeBuildPath = "builds/mcp/hero-team-wars/11111111-1111-4111-8111-111111111111/HeroTeamWars_test.w3m";
  const config = {
    root,
    source_maps: ["map/source.w3m"],
    read_roots: ["artifacts"],
    staging_root: "staging",
    artifact_root: "artifacts",
    build_root: "builds/mcp",
    log_root: "logs",
    test_output_root: "artifacts/tests",
    world_editor: editorPath,
    warcraft: gamePath,
    test_map_root: testMapRoot,
    enabled_tools: [],
    write_policy: "writes" as const,
    max_map_bytes: 1024 * 1024,
    max_operation_count: 10
  };
  const project = {
    id: "hero-team-wars",
    config,
    root,
    sourceMaps: [],
    stagingRoot: join(root, "staging"),
    artifactRoot,
    buildRoot,
    logRoot: join(root, "logs"),
    testOutputRoot
  } as ResolvedProject;
  const manifest = {
    schema_version: "1.0",
    build_id: buildId,
    project_id: "hero-team-wars",
    transaction_id: transactionId,
    revision: 4,
    profile: "debug",
    source_sha256: "B".repeat(64),
    output_path: relativeBuildPath,
    output_sha256: hash,
    output_size_bytes: readFileSync(buildPath).byteLength,
    writer_version: "test-writer",
    validator_version: "test-validator",
    validation_report: { path: "staging/report.json", sha256: "C".repeat(64) },
    archive_comparison: {},
    reinspection: { performed: true, semantic_differences: [], output_hash: hash },
    warnings: [],
    runtime_status: "untested",
    created_utc: "2026-08-30T12:00:00.000Z",
    engine_result: {}
  } as BuildManifest;
  const builds = {
    load: vi.fn(() => ({ project, manifest, manifestPath: "" })),
    project: vi.fn(() => project),
    markRuntimeStatus: vi.fn((_: string, __: string, status: string) => ({ build_id: buildId, runtime_status: status })),
    attachTestSession: vi.fn()
  };
  return { root, artifactRoot, testOutputRoot, testMapRoot, editorPath, gamePath, buildPath, relativeBuildPath, hash, project, builds };
}

function fakeRunner(running = false): ProcessRunner & { start: ReturnType<typeof vi.fn>; terminate: ReturnType<typeof vi.fn> } {
  return {
    isRunning: vi.fn(() => running),
    start: vi.fn(() => ({ pid: 9876, started_utc: "2026-08-30T12:34:56.000Z", exit_state: "unknown" as const })),
    terminate: vi.fn()
  };
}

function createService(fixture: ReturnType<typeof createFixture>, runner: ProcessRunner, sessionId: string): LaunchService {
  const paths: WarcraftPaths = {
    executable: (configuredPath, target): ExecutableInfo => ({ target, path: configuredPath ?? "", version: "2.0.4.23745", version_verified: true }),
    testRoot: configuredPath => {
      if (!configuredPath) throw new Error("missing test root");
      mkdirSync(configuredPath, { recursive: true });
      return configuredPath;
    },
    testCopyPath: (root, selectedBuildId, selectedSessionId, extension) => join(root, `MCP_${selectedBuildId}_${selectedSessionId}${extension}`)
  };
  return new LaunchService(fixture.builds as unknown as BuildService, runner, paths, () => sessionId, () => "2026-08-30T12:34:56.000Z");
}
