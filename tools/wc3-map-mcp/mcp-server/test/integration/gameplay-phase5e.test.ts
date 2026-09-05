import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { McpClient } from "../helpers/mcp-client.js";
import { createHash, randomUUID } from "node:crypto";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { tmpdir } from "node:os";
import { afterAll, afterEach, describe, expect, it } from "vitest";

const serverRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const projectRoot = resolve(serverRoot, "../../..");
const sourcePath = resolve(projectRoot, "map/HeroTeamWars_M0_2Arena.w3m");
const manifestPath = "tools/wc3-map-mcp/scripts/mcp/manifest.json";
const configRoot = mkdtempSync(join(tmpdir(), "wc3-map-mcp-phase5e-test-"));
const configPath = join(configRoot, "writes.json");
const transactionRoot = join(projectRoot, "tools/wc3-map-mcp/snapshots/transactions");
const buildRoot = join(projectRoot, "builds/mcp/hero-team-wars");
const resultRoot = join(projectRoot, "tools/wc3-map-mcp/artifacts/gameplay/results");

writeFileSync(configPath, JSON.stringify({
  schema_version: "1.0",
  engine: { executable: resolve(projectRoot, "tools/wc3-map-mcp/map-engine/publish/Wc3MapEngine.Cli.exe"), arguments: [], request_timeout_ms: 120000 },
  projects: {
    "hero-team-wars": {
      root: projectRoot,
      source_maps: ["map/HeroTeamWars_M0_2Arena.w3m"],
      baseline_sha256: sourceHash(),
      read_roots: ["map", "builds", "tools/wc3-map-mcp/artifacts"],
      staging_root: "tools/wc3-map-mcp/snapshots/transactions",
      artifact_root: "tools/wc3-map-mcp/artifacts",
      build_root: "builds/mcp",
      log_root: "tools/wc3-map-mcp/logs",
      test_output_root: "tools/wc3-map-mcp/artifacts/tests",
      gameplay_source_roots: ["tools/wc3-map-mcp/scripts/mcp"],
      gameplay_manifest: manifestPath,
      enabled_tools: [], write_policy: "writes", script_policy: "mcp_owned_jass", max_map_bytes: 536870912, max_operation_count: 100
    }
  }
}, null, 2), "utf8");


function sourceHash(): string {
  return createHash("sha256").update(readFileSync(sourcePath)).digest("hex").toUpperCase();
}

describe("Phase 5E gameplay transaction workflow", () => {
  let client: McpClient | undefined;
  const transactionIds: string[] = [];
  const buildIds: string[] = [];
  const resultPaths: string[] = [];

  afterEach(() => {
    client?.close();
    for (const transactionId of transactionIds.splice(0)) rmSync(join(transactionRoot, transactionId), { recursive: true, force: true });
    for (const buildId of buildIds.splice(0)) {
      rmSync(join(buildRoot, buildId), { recursive: true, force: true });
      rmSync(join(projectRoot, "tools/wc3-map-mcp/artifacts/builds", buildId), { recursive: true, force: true });
    }
    for (const resultPath of resultPaths.splice(0)) rmSync(resolve(projectRoot, resultPath), { force: true });
  });

  it("builds HTW-01 through HTW-05 from source and records hash-bound static results", async () => {
    const before = sourceHash();
    client = new McpClient(serverRoot, configPath);
    await client.initialize();
    const chunks: Record<string, string[]> = {
      "HTW-01": ["fresh_initialization", "preparation_combat_resolution"],
      "HTW-02": ["timeout_cleanup"],
      "HTW-03": ["single_hero_death", "both_heroes_dead_three_life_penalty", "duplicate_deferred_death_callback", "elimination_victory_draw"],
      "HTW-04": ["personal_purchase_isolation", "opposing_arena_destination"],
      "HTW-05": ["two_arena_repeatability"]
    };

    for (const [chunkId, scenarioIds] of Object.entries(chunks)) {
      const begin = await client.call("wc3_begin_transaction", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", expected_source_hash: before, label: `phase5e-${chunkId}` });
      expect(begin.structuredContent.ok).toBe(true);
      const transactionId = begin.structuredContent.data.transaction_id as string;
      transactionIds.push(transactionId);
      let revision = 0;

      if (chunkId === "HTW-01") {
        for (const precondition of [{ expected_manifest_sha256: "F".repeat(64) }, { expected_module_hashes: { main: "F".repeat(64) } }]) {
          const stale = await client.call("wc3_prepare_gameplay_chunk", { project_id: "hero-team-wars", transaction_id: transactionId,
            expected_revision: 0, chunk_id: chunkId, manifest_path: manifestPath, profile: "mvp_2arena", ...precondition });
          expect(stale.structuredContent.ok).toBe(false);
          expect(stale.structuredContent.error.code).toBe("PRECONDITION_FAILED");
          const manifest = JSON.parse(readFileSync(join(transactionRoot, transactionId, "manifest.json"), "utf8"));
          expect(manifest.revision).toBe(0);
        }
        const canonical = JSON.parse(readFileSync(resolve(projectRoot, begin.structuredContent.data.paths.canonical), "utf8"));
        const phase = canonical.gameplay_variables.find((item: any) => item.id === "phase");
        const changed = await client.call("wc3_apply_operations", { project_id: "hero-team-wars", transaction_id: transactionId, expected_revision: revision,
          operations: [{ operation_id: randomUUID(), type: "update_variable", target: { id: "phase" }, expected: phase, value: { initial: 2 }, rationale: "Verify prepare retains a staged variable edit." }] });
        expect(changed.structuredContent.ok).toBe(true);
        expect(changed.structuredContent.data.diff.changes.some((item: any) => item.component === "scripts")).toBe(true);
        revision = changed.structuredContent.data.revision;
      }

      const prepared = await client.call("wc3_prepare_gameplay_chunk", {
        project_id: "hero-team-wars", transaction_id: transactionId, expected_revision: revision,
        chunk_id: chunkId, manifest_path: manifestPath, profile: "mvp_2arena"
      });
      expect(prepared.structuredContent.ok).toBe(true);
      expect(prepared.structuredContent.data.operation.value.source_strategy).toBe("composed");
      expect(prepared.structuredContent.data.applied.revision).toBe(revision + 1);
      revision = prepared.structuredContent.data.applied.revision;
      if (chunkId === "HTW-01") expect(prepared.structuredContent.data.operation.value.source).toContain("set HTW_Phase = 2");

      const validation = await client.call("wc3_validate_transaction", { project_id: "hero-team-wars", transaction_id: transactionId, revision });
      expect(validation.structuredContent.ok).toBe(true);
      expect(validation.structuredContent.data.report.buildable).toBe(true);

      const run = await client.call("wc3_run_scenario_build", {
        project_id: "hero-team-wars", transaction_id: transactionId, revision,
        expected_source_hash: before, chunk_id: chunkId, scenario_ids: scenarioIds, profile: "mvp_2arena"
      });
      expect(run.structuredContent.ok).toBe(true);
      const report = run.structuredContent.data.scenario_report;
      expect(report.evidence_level).toBe("static_only");
      expect(report.runtime_verified).toBe(false);
      expect(report.scenarios.repeat_count).toBe(2);
      expect(report.scenarios.passed_count).toBe(report.scenarios.scenario_count);
      expect(report.scenarios.failed_count).toBe(0);
      expect(report.scenarios.results.every((result: any) => result.result === "pass" && result.marker.includes(`chunk=${chunkId}`))).toBe(true);

      const buildId = report.build_id as string;
      const buildHash = report.build_sha256 as string;
      buildIds.push(buildId);
      const recorded = await client.call("wc3_record_chunk_result", {
        project_id: "hero-team-wars", chunk_id: chunkId, scenario_id: scenarioIds[0],
        transaction_id: transactionId, revision, build_id: buildId, expected_build_hash: buildHash,
        result: "pass", evidence_level: "static_only", notes: "Deterministic model scenario passed twice from a fresh transaction."
      });
      expect(recorded.structuredContent.ok).toBe(true);
      expect(recorded.structuredContent.data.build_sha256).toBe(buildHash);
      expect(recorded.structuredContent.data.runtime_verified).toBe(false);
      resultPaths.push(recorded.structuredContent.data.artifact.path as string);
    }

    expect(sourceHash()).toBe(before);
    expect(existsSync(resultRoot)).toBe(true);
  }, 120_000);
});

afterAll(() => rmSync(configRoot, { recursive: true, force: true }));
