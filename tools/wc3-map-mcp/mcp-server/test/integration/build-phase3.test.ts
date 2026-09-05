import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { McpClient } from "../helpers/mcp-client.js";
import { createHash } from "node:crypto";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { tmpdir } from "node:os";
import { afterAll, afterEach, describe, expect, it } from "vitest";

const serverRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const projectRoot = resolve(serverRoot, "../../..");
const sourcePath = resolve(projectRoot, "map/HeroTeamWars_M0_2Arena.w3m");
const configRoot = mkdtempSync(join(tmpdir(), "wc3-map-mcp-phase3-test-"));
const configPath = join(configRoot, "writes.json");
const transactionRoot = join(projectRoot, "tools/wc3-map-mcp/snapshots/transactions");
const buildRoot = join(projectRoot, "builds/mcp/hero-team-wars");

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
      gameplay_manifest: "tools/wc3-map-mcp/scripts/mcp/manifest.json",
      enabled_tools: [], write_policy: "writes", max_map_bytes: 536870912, max_operation_count: 100
    }
  }
}, null, 2), "utf8");


function sourceHash(): string {
  return createHash("sha256").update(readFileSync(sourcePath)).digest("hex").toUpperCase();
}

describe("Phase 3 build contract", () => {
  let client: McpClient | undefined;
  let transactionId: string | undefined;
  let buildId: string | undefined;

  afterEach(() => {
    client?.close();
    if (transactionId && /^[0-9a-f-]{36}$/i.test(transactionId)) rmSync(join(transactionRoot, transactionId), { recursive: true, force: true });
    if (buildId && /^[0-9a-f-]{36}$/i.test(buildId)) {
      rmSync(join(buildRoot, buildId), { recursive: true, force: true });
      rmSync(join(projectRoot, "tools/wc3-map-mcp/artifacts/builds", buildId), { recursive: true, force: true });
    }
  });

  it("builds an exact no-op revision with reinspection, opaque preservation, and a verified manifest", async () => {
    const before = sourceHash();
    client = new McpClient(serverRoot, configPath);
    await client.initialize();

    const begin = await client.call("wc3_begin_transaction", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", expected_source_hash: before, label: "phase3-noop" });
    expect(begin.structuredContent.ok).toBe(true);
    transactionId = begin.structuredContent.data.transaction_id as string;
    const originalScript = await client.call("wc3_get_script_source", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m" });
    const canonical = JSON.parse(readFileSync(resolve(projectRoot, begin.structuredContent.data.paths.canonical), "utf8"));
    const script = canonical.scripts.find((item: any) => item.archive_path === "war3map.j");
    expect(originalScript.structuredContent.ok).toBe(true);
    expect(script.source_sha256 ?? script.sha256).toBe(originalScript.structuredContent.data.sha256);

    const validation = await client.call("wc3_validate_transaction", { project_id: "hero-team-wars", transaction_id: transactionId, revision: 0 });
    const validationReportPath = validation.structuredContent.error?.details?.report_path;
    expect(validation.structuredContent.ok, validationReportPath ? readFileSync(resolve(projectRoot, validationReportPath), "utf8") : JSON.stringify(validation.structuredContent)).toBe(true);
    expect(validation.structuredContent.data.report_path).toMatch(/^tools\/wc3-map-mcp\/snapshots\/transactions\//);
    expect(validation.structuredContent.data.report_sha256).toMatch(/^[0-9A-F]{64}$/);
    expect(validation.structuredContent.data.report.buildable).toBe(true);

    const built = await client.call("wc3_build_map", { project_id: "hero-team-wars", transaction_id: transactionId, revision: 0, expected_source_hash: before, profile: "noop", label: "phase3" });
    expect(built.structuredContent.ok).toBe(true);
    buildId = built.structuredContent.data.build.build_id as string;
    expect(built.structuredContent.data.runtime_status).toBe("untested");
    expect(built.structuredContent.data.build.archive_comparison.opaque_members_preserved).toBe(true);
    expect(built.structuredContent.data.build.reinspection).toEqual(expect.objectContaining({ performed: true, semantic_differences: [] }));
    expect(built.structuredContent.data.build.output_path).not.toMatch(/^[A-Za-z]:[\\/]|^[\\/]/);
    expect(existsSync(resolve(projectRoot, built.structuredContent.data.build.output_path))).toBe(true);
    const comparison = await client.call("wc3_compare_maps", { project_id: "hero-team-wars", left: "map/HeroTeamWars_M0_2Arena.w3m", right: built.structuredContent.data.build.output_path });
    expect(comparison.structuredContent.ok).toBe(true);
    expect(comparison.structuredContent.data.result.semantic_differences).toEqual([]);

    const report = await client.call("wc3_build_report", { project_id: "hero-team-wars", build_id: buildId });
    expect(report.structuredContent.ok).toBe(true);
    expect(report.structuredContent.data.verified).toBe(true);
    expect(report.structuredContent.data.build.output_sha256).toMatch(/^[0-9A-F]{64}$/);
    expect(sourceHash()).toBe(before);

    const leftovers = existsSync(buildRoot)
      ? readdirSync(buildRoot).filter(name => name.startsWith(".build-") && name.endsWith(".tmp"))
      : [];
    expect(leftovers).toEqual([]);
  }, 120_000);
});

afterAll(() => rmSync(configRoot, { recursive: true, force: true }));
