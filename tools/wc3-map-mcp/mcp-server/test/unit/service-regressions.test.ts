import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { afterEach, describe, expect, it, vi } from "vitest";
import { configSchema } from "../../src/config/schema.js";
import { ProjectService } from "../../src/services/project-service.js";
import { TransactionService } from "../../src/services/transaction-service.js";
import { TransactionStore } from "../../src/storage/transaction-store.js";
import { GameplayService } from "../../src/services/gameplay-service.js";
import { BuildService } from "../../src/services/build-service.js";
import { sha256File } from "../../src/services/artifact-service.js";

const roots: string[] = [];
afterEach(() => { for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true }); });
const projectId = "hero-team-wars";

function fixture(scriptPolicy: "disabled" | "mcp_owned_jass" = "mcp_owned_jass") {
  const root = mkdtempSync(join(tmpdir(), "wc3-service-regression-"));
  roots.push(root);
  mkdirSync(join(root, "maps"));
  mkdirSync(join(root, "gameplay"));
  writeFileSync(join(root, "maps/source.w3m"), "isolated test source");
  writeFileSync(join(root, "gameplay/manifest.json"), "{}");
  const config = configSchema.parse({ schema_version: "1.0", engine: { executable: "unused" }, projects: {
    [projectId]: { root, source_maps: ["maps/source.w3m"], staging_root: "staging", artifact_root: "artifacts", build_root: "builds",
      log_root: "logs", test_output_root: "tests", gameplay_source_roots: ["gameplay"], gameplay_manifest: "gameplay/manifest.json", script_policy: scriptPolicy }
  } });
  const worker = { request: vi.fn() };
  const projects = new ProjectService(config, worker as any);
  const project = projects.project(projectId);
  const store = new TransactionStore();
  const module = { id: "main", source_sha256: "B".repeat(64) };
  const canonical: any = { schema_version: "1.0", scripts: [{ archive_path: "war3map.j", sha256: "C".repeat(64) }],
    gameplay_modules: [module], gameplay_source: { manifest_sha256: "A".repeat(64) } };
  const source = join(root, "maps/source.w3m");
  const created = store.create(project, source, sha256File(source), canonical);
  const loaded = store.read(project, created.id);
  return { root, project, projects, worker, store, canonical, loaded, module };
}

describe("transaction preflight", () => {
  it("rejects case-insensitive duplicate IDs before invoking the engine", async () => {
    const f = fixture();
    const service = new TransactionService(f.projects, f.worker as any);
    const op = { operation_id: randomUUID(), type: "set_map_metadata", target: { field: "title" }, expected: "old", value: "new", rationale: "test" };
    await expect(service.apply(projectId, f.loaded.manifest.transaction_id, 0, [op, { ...op, operation_id: op.operation_id.toUpperCase() }], false, randomUUID()))
      .rejects.toMatchObject({ code: "INVALID_ARGUMENT" });
    expect(f.worker.request).not.toHaveBeenCalled();
    expect(f.store.read(f.project, f.loaded.manifest.transaction_id).manifest.revision).toBe(0);
  });

  it("does not compose or replace source when script mutation is disabled", async () => {
    const f = fixture("disabled");
    const original = { schema_version: "1.0", scripts: [{ archive_path: "war3map.j", source: "original bytes" }], regions: [{ name: "Actual map region" }] };
    f.worker.request.mockImplementation(async operation => {
      if (operation === "inspect_map") return structuredClone(original);
      if (operation === "environment_status") return {};
      throw new Error(`Unexpected worker operation: ${operation}`);
    });
    const service = new TransactionService(f.projects, f.worker as any);
    const result = await service.begin(projectId, "maps/source.w3m", sha256File(join(f.root, "maps/source.w3m")).sha256, undefined, randomUUID());
    const staged = JSON.parse(readFileSync(join(f.root, String((result.paths as any).canonical)), "utf8"));
    expect(staged.scripts).toEqual(original.scripts);
    expect(staged.regions).toEqual(original.regions);
    expect(f.worker.request.mock.calls.map(call => call[0])).toEqual(["inspect_map", "environment_status"]);
  });

  it("enforces script policy for generated variable edits too", async () => {
    const f = fixture("disabled");
    const service = new TransactionService(f.projects, f.worker as any);
    await expect(service.apply(projectId, f.loaded.manifest.transaction_id, 0, [{ operation_id: randomUUID(), type: "create_variable",
      target: { id: "phase" }, value: { id: "phase", name: "HTW_Phase", type: "integer", initial: 0 }, rationale: "test" }], true, randomUUID()))
      .rejects.toMatchObject({ code: "SCRIPT_MUTATION_DISABLED" });
    expect(f.worker.request).not.toHaveBeenCalled();
  });

  it("rejects indirectly regenerated JASS from older staged models under disabled policy", async () => {
    const f = fixture("disabled");
    const operationId = randomUUID();
    f.worker.request.mockResolvedValue({ applied_operation_ids: [operationId], diff: { changes: [{ component: "scripts", path: "$.scripts[0].source_sha256",
      before: "A".repeat(64), after: "B".repeat(64), change_type: "changed", operation_id: operationId, provenance: "derived", target: {} }] } });
    const service = new TransactionService(f.projects, f.worker as any);
    await expect(service.apply(projectId, f.loaded.manifest.transaction_id, 0, [{ operation_id: operationId, type: "set_map_metadata",
      target: { field: "title" }, expected: "old", value: "new", rationale: "test" }], true, randomUUID())).rejects.toMatchObject({ code: "SCRIPT_MUTATION_DISABLED" });
    expect(f.store.read(f.project, f.loaded.manifest.transaction_id).manifest.revision).toBe(0);
  });
});

describe("gameplay preparation", () => {
  function setup() {
    const f = fixture();
    const transactions = { get: () => ({ project: f.project, loaded: f.loaded }), apply: vi.fn(async () => ({ revision: 1 })) };
    const service = new GameplayService(f.projects, f.worker as any, transactions as any, {} as any, {} as any);
    const composition = { manifest_sha256: "A".repeat(64), modules: [f.module] };
    const compose = vi.spyOn(service, "compose").mockResolvedValue(composition);
    return { ...f, transactions, service, compose, composition };
  }

  it("forwards expected hashes before any staging work", async () => {
    const f = setup();
    f.compose.mockRejectedValue(Object.assign(new Error("stale manifest"), { code: "SOURCE_CHANGED" }));
    const correlation = randomUUID();
    const hashes = { main: "D".repeat(64) };
    await expect(f.service.prepare(projectId, f.loaded.manifest.transaction_id, 0, "HTW-01", "gameplay/manifest.json", undefined, correlation, "E".repeat(64), hashes))
      .rejects.toMatchObject({ code: "SOURCE_CHANGED" });
    expect(f.compose).toHaveBeenCalledWith(projectId, "gameplay/manifest.json", undefined, correlation, "E".repeat(64), hashes);
    expect(f.transactions.apply).not.toHaveBeenCalled();
  });

  it("rejects changed module files even when the manifest text is unchanged", async () => {
    const f = setup();
    f.compose.mockResolvedValue({ ...f.composition, modules: [{ ...f.module, source_sha256: "D".repeat(64) }] });
    await expect(f.service.prepare(projectId, f.loaded.manifest.transaction_id, 0, "HTW-01", "gameplay/manifest.json", undefined, randomUUID()))
      .rejects.toMatchObject({ code: "SOURCE_CHANGED" });
    expect(f.worker.request).not.toHaveBeenCalled();
    expect(f.transactions.apply).not.toHaveBeenCalled();
  });

  it("uses the initial manifest identity after a typed edit regenerated gameplay metadata", async () => {
    const f = setup();
    delete f.canonical.gameplay_source.manifest_sha256;
    writeFileSync(f.loaded.paths.canonical, JSON.stringify(f.canonical));
    f.worker.request.mockRejectedValue(new Error("reached canonical composer"));
    await expect(f.service.prepare(projectId, f.loaded.manifest.transaction_id, 0, "HTW-01", "gameplay/manifest.json", undefined, randomUUID()))
      .rejects.toThrow("reached canonical composer");
    expect(f.worker.request).toHaveBeenCalledWith("compose_gameplay_source", { canonical_model: f.canonical }, expect.any(String));
  });
});

describe("chunk evidence", () => {
  function setup() {
    const f = fixture();
    const buildId = randomUUID();
    const input: any = { project_id: projectId, chunk_id: "HTW-01", scenario_id: "scenario/with:descriptive text", transaction_id: f.loaded.manifest.transaction_id,
      revision: 0, build_id: buildId, expected_build_hash: "D".repeat(64), result: "pass", evidence_level: "user_observed", test_session_id: randomUUID(), notes: "explicit observation" };
    const session: any = { build_id: buildId, build_sha256: input.expected_build_hash, target: "game", milestones: [] };
    const builds = { load: () => ({ manifest: { transaction_id: input.transaction_id, revision: 0, output_sha256: input.expected_build_hash } }), attachTestSession: vi.fn() };
    const launches = { get: vi.fn(() => ({ session, verified: true })) };
    const service = new GameplayService(f.projects, f.worker as any, {} as any, builds as any, launches as any);
    return { ...f, input, session, builds, service };
  }

  it.each([
    ["game", undefined],
    ["editor", { milestone: "editor_opened", result: "pass", recorder: "user_observation" }],
    ["game", { milestone: "game_loaded", result: "pass", recorder: "user_observation" }],
    ["game", { milestone: "smoke_test", result: "pass", recorder: "agent_log_observation" }],
    ["game", { milestone: "smoke_test", result: "fail", recorder: "user_observation" }]
  ])("rejects insufficient %s session evidence %j", async (target, milestone) => {
    const f = setup();
    f.session.target = target;
    f.session.milestones = milestone ? [milestone] : [];
    await expect(f.service.recordChunkResult(f.input)).rejects.toMatchObject({ code: "PRECONDITION_FAILED" });
    expect(f.builds.attachTestSession).not.toHaveBeenCalled();
  });

  it("requires a session even when called below the tool schema", async () => {
    const f = setup();
    delete f.input.test_session_id;
    await expect(f.service.recordChunkResult(f.input)).rejects.toMatchObject({ code: "PRECONDITION_REQUIRED" });
  });

  it("retains failures and subsequent passes as separate artifacts", async () => {
    const f = setup();
    f.input.result = "fail";
    f.session.milestones = [{ milestone: "game_loaded", result: "fail", recorder: "user_observation" }];
    const failed: any = await f.service.recordChunkResult(f.input);
    f.input.result = "pass";
    f.session.milestones = [{ milestone: "smoke_test", result: "pass", recorder: "user_observation" }];
    const passed: any = await f.service.recordChunkResult(f.input);
    expect(failed.runtime_verified).toBe(true);
    expect(passed.runtime_verified).toBe(true);
    expect(passed.artifact.path).not.toBe(failed.artifact.path);
    expect(JSON.parse(readFileSync(join(f.root, failed.artifact.path), "utf8")).result).toBe("fail");
    expect(JSON.parse(readFileSync(join(f.root, passed.artifact.path), "utf8")).result).toBe("pass");
  });
});

describe("build evidence", () => {
  async function buildFixture(comparison: Record<string, unknown>) {
    const f = fixture();
    const reportPath = join(f.loaded.paths.reports, "validation-0000.json");
    writeFileSync(reportPath, JSON.stringify({ buildable: true }));
    f.loaded.manifest.validation_reports = [reportPath];
    f.loaded.manifest.state = "validated";
    f.store.update(f.loaded.paths, f.loaded.manifest);
    const transactions = { get: () => ({ loaded: f.store.read(f.project, f.loaded.manifest.transaction_id) }) };
    f.worker.request.mockImplementation(async (operation, payload) => {
      if (operation === "build_map") {
        writeFileSync(payload.output_path, "built map bytes");
        return { output_path: payload.output_path, reopened: true, sha256: sha256File(payload.output_path).sha256,
          opaque_members_preserved: true, archive_comparison: { opaque_members_preserved: true } };
      }
      if (operation === "inspect_map") return {};
      if (operation === "compare_maps") return comparison;
      throw new Error(`Unexpected operation ${operation}`);
    });
    const service = new BuildService(f.projects, f.worker as any, transactions as any, f.store);
    const result = service.build(projectId, f.loaded.manifest.transaction_id, 0, f.loaded.manifest.source.sha256, "debug", undefined, randomUUID());
    return { ...f, service, result };
  }

  it("does not interpret a missing comparison as successful reinspection", async () => {
    const f = await buildFixture({});
    await expect(f.result).rejects.toMatchObject({ code: "ENGINE_PROTOCOL_ERROR" });
    expect(f.store.read(f.project, f.loaded.manifest.transaction_id).manifest.state).toBe("validated");
  });

  it("records the finalized output path and detects inconsistent persisted evidence", async () => {
    const f = await buildFixture({ semantic_differences: [] });
    const result: any = await f.result;
    expect(result.build.engine_result.output_path).toBe(result.build.output_path);
    expect(result.build.output_path).not.toContain(".tmp");
    expect(f.service.report(projectId, result.build.build_id).verified).toBe(true);
    const path = join(f.root, result.manifest_artifact.path);
    const manifest = JSON.parse(readFileSync(path, "utf8"));
    manifest.output_size_bytes++;
    writeFileSync(path, JSON.stringify(manifest));
    expect(() => f.service.report(projectId, manifest.build_id)).toThrow(/hash no longer matches/);
    manifest.output_size_bytes--;
    manifest.reinspection.output_hash = "F".repeat(64);
    writeFileSync(path, JSON.stringify(manifest));
    expect(() => f.service.report(projectId, manifest.build_id)).toThrow(/reinspection evidence/);
  });
});
