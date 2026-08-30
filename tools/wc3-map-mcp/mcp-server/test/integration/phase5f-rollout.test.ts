import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { tmpdir } from "node:os";
import { afterAll, afterEach, describe, expect, it } from "vitest";

const serverRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const projectRoot = resolve(serverRoot, "../../..");
const sourcePath = resolve(projectRoot, "map/HeroTeamWars_M0_2Arena.w3m");
const manifestPath = "tools/wc3-map-mcp/scripts/mcp/manifest.json";
const configRoot = mkdtempSync(join(tmpdir(), "wc3-map-mcp-phase5f-test-"));
const configPath = join(configRoot, "writes.json");
const transactionRoot = join(projectRoot, "tools/wc3-map-mcp/snapshots/transactions");
const buildRoot = join(projectRoot, "builds/mcp/hero-team-wars");

writeFileSync(configPath, JSON.stringify({
  schema_version: "1.0",
  engine: { executable: resolve(projectRoot, "tools/wc3-map-mcp/map-engine/publish/Wc3MapEngine.Cli.exe"), arguments: [], request_timeout_ms: 120000 },
  projects: {
    "hero-team-wars": {
      root: projectRoot, source_maps: ["map/HeroTeamWars_M0_2Arena.w3m"], baseline_sha256: sourceHash(),
      read_roots: ["map", "builds", "tools/wc3-map-mcp/artifacts"], staging_root: "tools/wc3-map-mcp/snapshots/transactions",
      artifact_root: "tools/wc3-map-mcp/artifacts", build_root: "builds/mcp", log_root: "tools/wc3-map-mcp/logs",
      test_output_root: "tools/wc3-map-mcp/artifacts/tests", gameplay_source_roots: ["tools/wc3-map-mcp/scripts/mcp"],
      gameplay_manifest: manifestPath, enabled_tools: [], write_policy: "writes", script_policy: "mcp_owned_jass",
      profile: "mvp_2arena", max_map_bytes: 536870912, max_operation_count: 100
    }
  }
}, null, 2), "utf8");

class McpClient {
  private readonly child: ChildProcessWithoutNullStreams;
  private readonly pending = new Map<number, { resolve: (value: any) => void; reject: (error: Error) => void }>();
  private nextId = 1;
  private stdout = "";

  public constructor() {
    this.child = spawn(process.execPath, [resolve(serverRoot, "dist/index.js")], { cwd: serverRoot, env: { ...process.env, WC3_MAP_MCP_CONFIG: configPath }, stdio: ["pipe", "pipe", "pipe"] });
    this.child.stdout.setEncoding("utf8");
    this.child.stdout.on("data", chunk => this.consume(String(chunk)));
    this.child.on("error", error => this.rejectAll(error));
    this.child.on("close", code => { if (code !== 0) this.rejectAll(new Error(`server exited ${code}`)); });
  }

  public async initialize(): Promise<void> {
    await this.request("initialize", { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "phase5f-test", version: "1" } });
    this.child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized", params: {} })}\n`);
  }

  public call(name: string, args: Record<string, unknown>): Promise<any> { return this.request("tools/call", { name, arguments: args }); }
  public close(): void { this.child.stdin.end(); this.child.kill(); }

  private request(method: string, params: Record<string, unknown>): Promise<any> {
    const id = this.nextId++;
    return new Promise((resolvePromise, rejectPromise) => {
      this.pending.set(id, { resolve: resolvePromise, reject: rejectPromise });
      this.child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id, method, params })}\n`);
    });
  }

  private consume(chunk: string): void {
    this.stdout += chunk;
    const lines = this.stdout.split(/\r?\n/);
    this.stdout = lines.pop() ?? "";
    for (const line of lines.filter(Boolean)) {
      const message = JSON.parse(line) as { id?: number; result?: unknown; error?: { message?: string } };
      if (message.id === undefined) continue;
      const waiter = this.pending.get(message.id);
      if (!waiter) continue;
      this.pending.delete(message.id);
      if (message.error) waiter.reject(new Error(message.error.message ?? "JSON-RPC request failed"));
      else waiter.resolve(message.result);
    }
  }

  private rejectAll(error: Error): void {
    for (const waiter of this.pending.values()) waiter.reject(error);
    this.pending.clear();
  }
}

function sourceHash(): string { return createHash("sha256").update(readFileSync(sourcePath)).digest("hex").toUpperCase(); }

function operation(type: string, target: Record<string, unknown>, expected: unknown, value: unknown): Record<string, unknown> {
  return { operation_id: randomUUID(), type, target, ...(expected === undefined ? {} : { expected }), ...(value === undefined ? {} : { value }), rationale: "Phase 5F cross-feature rollout integration." };
}

describe("Phase 5F full MCP rollout", () => {
  let client: McpClient | undefined;
  let transactionId: string | undefined;
  let buildId: string | undefined;

  afterEach(() => {
    client?.close();
    if (transactionId) rmSync(join(transactionRoot, transactionId), { recursive: true, force: true });
    if (buildId) {
      rmSync(join(buildRoot, buildId), { recursive: true, force: true });
      rmSync(join(projectRoot, "tools/wc3-map-mcp/artifacts/builds", buildId), { recursive: true, force: true });
    }
    transactionId = undefined;
    buildId = undefined;
  });

  it("reports capabilities and performs a dependency-ordered cross-feature build", async () => {
    const before = sourceHash();
    client = new McpClient();
    await client.initialize();

    const status = await client.call("wc3_project_status", { project_id: "hero-team-wars" });
    expect(status.structuredContent.ok).toBe(true);
    expect(status.structuredContent.data.capability_matrix.active_profile).toBe("mvp_2arena");
    expect(status.structuredContent.data.capability_matrix.members.some((item: any) => item.member === "war3map.w3i" && item.profile_status.mvp_2arena.enabled)).toBe(true);
    expect(status.structuredContent.data.capability_matrix.gui_trigger_compatibility.enabled).toBe(false);

    const begin = await client.call("wc3_begin_transaction", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", expected_source_hash: before, label: "phase5f-cross-feature" });
    expect(begin.structuredContent.ok).toBe(true);
    transactionId = begin.structuredContent.data.transaction_id as string;
    const canonical = JSON.parse(readFileSync(join(projectRoot, begin.structuredContent.data.paths.canonical), "utf8"));
    const player = canonical.players.find((item: any) => item.id === 1);
    const force = canonical.forces.find((item: any) => item.index === 0);

    // Deliberately reverse the dependency order. The engine reports and
    // applies the stable order: players/forces -> regions -> object data ->
    // placements -> gameplay triggers/source generation.
    const trigger = { id: "phase5f_rollout_trigger", name: "Phase 5F Rollout", folder_path: "Phase 5F", enabled: true, initially_on: true, events: [{ type: "map_initialization" }], conditions: [], actions: [{ type: "message", text: "Phase 5F" }] };
    const createObject = operation("create_object_definition", { id: "war3map.w3u:new:hfoo:Z5F1", category: "unit", rawcode: "Z5F1" }, undefined, { id: "war3map.w3u:new:hfoo:Z5F1", category: "unit", object_kind: "custom", base_rawcode: "hfoo", custom_rawcode: "Z5F1", rawcode: "Z5F1", unknown_ids: [], modifications: [] });
    const createPlacement = operation("place_object", {}, undefined, { kind: "unit", rawcode: "Z5F1", owner_id: 1, position: { x: 1200, y: -3000, z: 0 } });
    const createRegion = operation("create_region", {}, undefined, { name: "MCP_Phase5F_Region", min_x: 1200, min_y: -3000, max_x: 1216, max_y: -2984 });
    const updateForce = operation("set_force", { index: 0 }, force, { name: "Team 1 Phase 5F" });
    const updatePlayer = operation("set_player_slot", { id: 1 }, player, { name: "Player 1 Phase 5F" });
    const createTrigger = operation("create_trigger", { id: trigger.id }, undefined, trigger);
    const applied = await client.call("wc3_apply_operations", {
      project_id: "hero-team-wars", transaction_id: transactionId, expected_revision: 0,
      operations: [createTrigger, createPlacement, createObject, createRegion, updateForce, updatePlayer]
    });
    expect(applied.structuredContent.ok).toBe(true);
    expect(applied.structuredContent.data.applied_operation_ids).toEqual([
      updatePlayer.operation_id, updateForce.operation_id, createRegion.operation_id,
      createObject.operation_id, createPlacement.operation_id, createTrigger.operation_id
    ]);
    expect(applied.structuredContent.data.diff.dependency_order.map((item: any) => item.type)).toEqual([
      "set_player_slot", "set_force", "create_region", "create_object_definition", "place_object", "create_trigger"
    ]);
    expect(applied.structuredContent.data.diff.groups.some((group: any) => group.component === "players")).toBe(true);
    expect(applied.structuredContent.data.diff.groups.some((group: any) => group.component === "gameplay_triggers")).toBe(true);

    const validation = await client.call("wc3_validate_transaction", { project_id: "hero-team-wars", transaction_id: transactionId, revision: 1 });
    expect(validation.structuredContent.ok).toBe(true);
    expect(validation.structuredContent.data.report.buildable).toBe(true);

    const built = await client.call("wc3_build_map", { project_id: "hero-team-wars", transaction_id: transactionId, revision: 1, expected_source_hash: before, profile: "debug", label: "phase5f-cross-feature" });
    expect(built.structuredContent.ok).toBe(true);
    buildId = built.structuredContent.data.build.build_id as string;
    expect(built.structuredContent.data.build.capability_profile).toBe("mvp_2arena");
    expect(built.structuredContent.data.build.archive_comparison.changed_member_hashes).toEqual(expect.arrayContaining([
      expect.objectContaining({ path: "war3map.w3i" }), expect.objectContaining({ path: "war3map.w3r" }),
      expect.objectContaining({ path: "war3map.w3u" }), expect.objectContaining({ path: "war3mapUnits.doo" }),
      expect.objectContaining({ path: "war3map.j" })
    ]));
    expect(built.structuredContent.data.build.archive_comparison.opaque_members_preserved).toBe(true);
    const transactionManifest = JSON.parse(readFileSync(join(projectRoot, begin.structuredContent.data.manifest_path), "utf8"));
    expect(transactionManifest.operation_records).toHaveLength(6);
    for (const record of transactionManifest.operation_records) {
      expect(record.transaction_id).toBe(transactionId);
      expect(record.source_sha256).toBe(before);
      expect(record.semantic_diff_artifact.sha256).toMatch(/^[A-F0-9]{64}$/);
      expect(record.validation_report_artifacts).toEqual(expect.arrayContaining([expect.objectContaining({ path: validation.structuredContent.data.report_path })]));
      expect(record.build_artifacts).toEqual(expect.arrayContaining([expect.objectContaining({ build_id: buildId, output_sha256: built.structuredContent.data.build.output_sha256 })]));
      expect(record.test_session_ids).toEqual([]);
    }
    expect(sourceHash()).toBe(before);
  }, 120_000);

  it("keeps HTW-06 behind the full six-team profile", async () => {
    const before = sourceHash();
    client = new McpClient();
    await client.initialize();
    const begin = await client.call("wc3_begin_transaction", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", expected_source_hash: before, label: "phase5f-profile-gate" });
    transactionId = begin.structuredContent.data.transaction_id as string;
    const gated = await client.call("wc3_run_scenario_build", {
      project_id: "hero-team-wars", transaction_id: transactionId, revision: 0, expected_source_hash: before,
      chunk_id: "HTW-06", profile: "mvp_2arena", scenario_ids: ["six_team_route_offset"]
    });
    expect(gated.structuredContent.ok).toBe(false);
    expect(gated.structuredContent.error.code).toBe("CAPABILITY_GATED");
    expect(sourceHash()).toBe(before);
  }, 120_000);
});

afterAll(() => rmSync(configRoot, { recursive: true, force: true }));
