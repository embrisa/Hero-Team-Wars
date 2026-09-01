import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { tmpdir } from "node:os";
import { afterAll, afterEach, describe, expect, it } from "vitest";

const serverRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const projectRoot = resolve(serverRoot, "../../..");
const sourcePath = resolve(projectRoot, "map/HeroTeamWars_M0_2Arena.w3m");
const configRoot = mkdtempSync(join(tmpdir(), "wc3-map-mcp-writes-test-"));
const configPath = join(configRoot, "writes.json");
writeFileSync(configPath, JSON.stringify({
  schema_version: "1.0",
  engine: { executable: resolve(projectRoot, "tools/wc3-map-mcp/map-engine/publish/Wc3MapEngine.Cli.exe"), arguments: [], request_timeout_ms: 120000 },
  projects: {
    "hero-team-wars": {
      root: projectRoot,
      source_maps: ["map/HeroTeamWars_M0_2Arena.w3m"],
      baseline_sha256: "027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834",
      read_roots: ["map", "builds", "tools/wc3-map-mcp/artifacts"],
      staging_root: "tools/wc3-map-mcp/snapshots/transactions",
      artifact_root: "tools/wc3-map-mcp/artifacts",
      build_root: "builds/mcp",
      log_root: "tools/wc3-map-mcp/logs",
      test_output_root: "tools/wc3-map-mcp/artifacts/tests",
      gameplay_source_roots: ["tools/wc3-map-mcp/scripts/mcp"],
      gameplay_manifest: "tools/wc3-map-mcp/scripts/mcp/manifest.json",
      enabled_tools: [], write_policy: "writes", script_policy: "mcp_owned_jass", max_map_bytes: 536870912, max_operation_count: 100
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
    await this.request("initialize", { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "transaction-test", version: "1" } });
    this.child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized", params: {} })}\n`);
  }

  public call(name: string, args: Record<string, unknown>): Promise<any> {
    return this.request("tools/call", { name, arguments: args });
  }

  public close(): void {
    this.child.stdin.end();
    this.child.kill();
  }

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

function sourceHash(): string {
  return createHash("sha256").update(readFileSync(sourcePath)).digest("hex").toUpperCase();
}

function operation(type: string, target: Record<string, unknown>, expected: unknown, value: unknown): Record<string, unknown> {
  return { operation_id: randomUUID(), type, target, expected, value, rationale: "Integration-test metadata round trip." };
}

describe("MCP transaction and build workflow", () => {
  let client: McpClient | undefined;
  let transactionId: string | undefined;
  let buildId: string | undefined;

  afterEach(() => {
    client?.close();
    if (transactionId && /^[0-9a-f-]{36}$/i.test(transactionId)) rmSync(join(projectRoot, "tools/wc3-map-mcp/snapshots/transactions", transactionId), { recursive: true, force: true });
    if (buildId && /^[0-9a-f-]{36}$/i.test(buildId)) {
      rmSync(join(projectRoot, "tools/wc3-map-mcp/builds/mcp/hero-team-wars", buildId), { recursive: true, force: true });
      rmSync(join(projectRoot, "tools/wc3-map-mcp/artifacts/builds", buildId), { recursive: true, force: true });
    }
  });

  it("rejects a source hash mismatch before creating a staging directory", async () => {
    const before = sourceHash();
    const transactionRoot = join(projectRoot, "tools/wc3-map-mcp/snapshots/transactions");
    const existingTransactions = new Set(existsSync(transactionRoot)
      ? readdirSync(transactionRoot, { withFileTypes: true }).filter(entry => entry.isDirectory() && /^[0-9a-f-]{36}$/i.test(entry.name)).map(entry => entry.name)
      : []);
    client = new McpClient();
    await client.initialize();

    const result = await client.call("wc3_begin_transaction", {
      project_id: "hero-team-wars",
      map: "map/HeroTeamWars_M0_2Arena.w3m",
      expected_source_hash: "0000000000000000000000000000000000000000000000000000000000000000"
    });

    expect(result.structuredContent.ok).toBe(false);
    expect(result.structuredContent.error.code).toBe("SOURCE_CHANGED");
    expect(sourceHash()).toBe(before);
    const afterTransactions = new Set(readdirSync(transactionRoot, { withFileTypes: true }).filter(entry => entry.isDirectory() && /^[0-9a-f-]{36}$/i.test(entry.name)).map(entry => entry.name));
    expect(afterTransactions).toEqual(existingTransactions);
  }, 120_000);

  it("stages, diffs, validates, builds, and rehashes a changed copy", async () => {
    const before = sourceHash();
    client = new McpClient();
    await client.initialize();

    const begin = await client.call("wc3_begin_transaction", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", expected_source_hash: before });
    expect(begin.structuredContent.ok).toBe(true);
    transactionId = begin.structuredContent.data.transaction_id as string;
    expect(transactionId).toMatch(/^[0-9a-f-]{36}$/i);

    const dryRun = await client.call("wc3_apply_operations", {
      project_id: "hero-team-wars",
      transaction_id: transactionId,
      expected_revision: 0,
      dry_run: true,
      operations: [operation("set_map_metadata", { field: "title" }, "Hero Team Wars - Two Arena MVP", "Hero Team Wars - MCP Test")]
    });
    expect(dryRun.structuredContent.ok).toBe(true);
    expect(dryRun.structuredContent.data.revision).toBe(0);

    const apply = await client.call("wc3_apply_operations", {
      project_id: "hero-team-wars",
      transaction_id: transactionId,
      expected_revision: 0,
      operations: [operation("set_map_metadata", { field: "title" }, "Hero Team Wars - Two Arena MVP", "Hero Team Wars - MCP Test")]
    });
    expect(apply.structuredContent.ok).toBe(true);
    expect(apply.structuredContent.data.revision).toBe(1);
    expect(apply.structuredContent.data.diff.changes.length).toBeGreaterThan(0);
    expect(apply.structuredContent.data.diff.changes[0]).toEqual(expect.objectContaining({
      component: "metadata",
      operation_id: expect.any(String),
      provenance: "intended_design",
      target: { field: "title" },
      change_type: "changed"
    }));

    const diff = await client.call("wc3_transaction_diff", { project_id: "hero-team-wars", transaction_id: transactionId });
    expect(diff.structuredContent.ok).toBe(true);
    expect(diff.structuredContent.data.diff.changes.length).toBeGreaterThan(0);

    const validation = await client.call("wc3_validate_transaction", { project_id: "hero-team-wars", transaction_id: transactionId, revision: 1 });
    expect(validation.structuredContent.ok).toBe(true);
    expect(validation.structuredContent.data.report.buildable).toBe(true);

    const build = await client.call("wc3_build_map", { project_id: "hero-team-wars", transaction_id: transactionId, revision: 1, expected_source_hash: before, profile: "noop", label: "integration" });
    expect(build.structuredContent.ok).toBe(true);
    buildId = build.structuredContent.data.build.build_id as string;
    expect(build.structuredContent.data.runtime_status).toBe("untested");
    expect(existsSync(resolve(projectRoot, build.structuredContent.data.build.output_path))).toBe(true);

    const report = await client.call("wc3_build_report", { project_id: "hero-team-wars", build_id: buildId });
    expect(report.structuredContent.ok).toBe(true);
    expect(report.structuredContent.data.verified).toBe(true);
    expect(sourceHash()).toBe(before);
  }, 120_000);

  it("reads, stages, validates, and builds MCP-owned JASS source", async () => {
    const before = sourceHash();
    client = new McpClient();
    await client.initialize();

    const source = await client.call("wc3_get_script_source", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", archive_path: "war3map.j" });
    expect(source.structuredContent.ok).toBe(true);
    expect(source.structuredContent.data.language).toBe("jass");
    expect(source.structuredContent.data.sha256).toMatch(/^[0-9A-F]{64}$/);
    const updatedSource = `${source.structuredContent.data.source}\n// MCP-owned JASS integration test.\n`;

    const begin = await client.call("wc3_begin_transaction", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", expected_source_hash: before, label: "jass-source" });
    expect(begin.structuredContent.ok).toBe(true);
    transactionId = begin.structuredContent.data.transaction_id as string;
    const stagedCanonical = JSON.parse(readFileSync(join(projectRoot, begin.structuredContent.data.paths.canonical), "utf8"));
    const stagedScriptHash = stagedCanonical.scripts.find((item: any) => item.archive_path === "war3map.j").sha256;
    expect(stagedScriptHash).toMatch(/^[0-9A-F]{64}$/i);

    const apply = await client.call("wc3_apply_operations", {
      project_id: "hero-team-wars",
      transaction_id: transactionId,
      expected_revision: 0,
      operations: [{
        operation_id: randomUUID(),
        type: "set_script_source",
        target: { archive_path: "war3map.j" },
        expected: stagedScriptHash,
        value: { language: "jass", source: updatedSource },
        rationale: "Verify MCP-owned gameplay source replacement."
      }]
    });
    expect(apply.structuredContent.ok).toBe(true);
    expect(apply.structuredContent.data.diff.changes).toEqual(expect.arrayContaining([expect.objectContaining({ component: "scripts" })]));

    const validation = await client.call("wc3_validate_transaction", { project_id: "hero-team-wars", transaction_id: transactionId, revision: 1 });
    expect(validation.structuredContent.ok).toBe(true);
    expect(validation.structuredContent.data.report.buildable).toBe(true);

    const build = await client.call("wc3_build_map", { project_id: "hero-team-wars", transaction_id: transactionId, revision: 1, expected_source_hash: before, profile: "debug", label: "jass-source" });
    expect(build.structuredContent.ok).toBe(true);
    buildId = build.structuredContent.data.build.build_id as string;
    const rebuilt = await client.call("wc3_get_script_source", { project_id: "hero-team-wars", map: build.structuredContent.data.build.output_path, archive_path: "war3map.j" });
    expect(rebuilt.structuredContent.ok).toBe(true);
    expect(rebuilt.structuredContent.data.source).toBe(updatedSource);
    expect(sourceHash()).toBe(before);
  }, 120_000);

  it("rejects an unknown JASS API before changing the transaction revision", async () => {
    const before = sourceHash();
    client = new McpClient();
    await client.initialize();

    const begin = await client.call("wc3_begin_transaction", {
      project_id: "hero-team-wars",
      map: "map/HeroTeamWars_M0_2Arena.w3m",
      expected_source_hash: before,
      label: "jass-validation-gate"
    });
    expect(begin.structuredContent.ok).toBe(true);
    transactionId = begin.structuredContent.data.transaction_id as string;
    const transactionRoot = join(projectRoot, "tools/wc3-map-mcp/snapshots/transactions", transactionId);
    const canonicalPath = join(transactionRoot, "working/canonical-map.json");
    const initialCanonical = readFileSync(canonicalPath, "utf8");
    const canonical = JSON.parse(initialCanonical);
    const stagedScriptHash = canonical.scripts.find((item: any) => item.archive_path === "war3map.j").sha256;
    const invalidSource = "function main takes nothing returns nothing\n    call SetUnitStock(null, 'H001', 1)\nendfunction\nfunction config takes nothing returns nothing\n    call SetPlayers(1)\nendfunction\n";

    const rejected = await client.call("wc3_apply_operations", {
      project_id: "hero-team-wars",
      transaction_id: transactionId,
      expected_revision: 0,
      operations: [{
        operation_id: randomUUID(),
        type: "set_script_source",
        target: { archive_path: "war3map.j" },
        expected: stagedScriptHash,
        value: { language: "jass", source: invalidSource },
        rationale: "Prove unknown canonical API calls fail before staging."
      }]
    });

    expect(rejected.structuredContent.ok).toBe(false);
    expect(rejected.structuredContent.error.code).toBe("INVALID_ARGUMENT");
    expect(rejected.structuredContent.error.message).toMatch(/SetUnitStock/);
    expect(JSON.stringify(rejected.structuredContent.error)).toMatch(/AddUnitToStock/);
    expect(readFileSync(canonicalPath, "utf8")).toBe(initialCanonical);
    expect(existsSync(join(transactionRoot, "revisions/0001-after-operations.json"))).toBe(false);

    const validSource = "function main takes nothing returns nothing\n    call AddUnitToStock(null, 'H001', 1, 2)\nendfunction\nfunction config takes nothing returns nothing\n    call SetPlayers(1)\nendfunction\n";
    const accepted = await client.call("wc3_apply_operations", {
      project_id: "hero-team-wars",
      transaction_id: transactionId,
      expected_revision: 0,
      operations: [{
        operation_id: randomUUID(),
        type: "set_script_source",
        target: { archive_path: "war3map.j" },
        expected: stagedScriptHash,
        value: { language: "jass", source: validSource },
        rationale: "Prove a canonical jassdoc-backed API call can be staged."
      }]
    });
    expect(accepted.structuredContent.ok).toBe(true);
    expect(accepted.structuredContent.data.revision).toBe(1);
    expect(sourceHash()).toBe(before);
  }, 120_000);

  it("keeps failed batches and dry runs at the previous revision", async () => {
    const before = sourceHash();
    client = new McpClient();
    await client.initialize();
    const begin = await client.call("wc3_begin_transaction", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", expected_source_hash: before, label: "atomicity" });
    expect(begin.structuredContent.ok).toBe(true);
    transactionId = begin.structuredContent.data.transaction_id as string;
    const transactionRoot = join(projectRoot, "tools/wc3-map-mcp/snapshots/transactions", transactionId);
    const canonicalPath = join(transactionRoot, "working/canonical-map.json");
    const initialCanonical = readFileSync(canonicalPath, "utf8");

    const dryRun = await client.call("wc3_apply_operations", {
      project_id: "hero-team-wars",
      transaction_id: transactionId,
      expected_revision: 0,
      dry_run: true,
      operations: [operation("set_map_metadata", { field: "title" }, "Hero Team Wars - Two Arena MVP", "Dry run title")]
    });
    expect(dryRun.structuredContent.ok).toBe(true);
    expect(dryRun.structuredContent.data.revision).toBe(0);
    expect(readFileSync(canonicalPath, "utf8")).toBe(initialCanonical);
    expect(existsSync(join(transactionRoot, "revisions/0001-after-operations.json"))).toBe(false);

    const failed = await client.call("wc3_apply_operations", {
      project_id: "hero-team-wars",
      transaction_id: transactionId,
      expected_revision: 0,
      operations: [
        operation("set_map_metadata", { field: "title" }, "Hero Team Wars - Two Arena MVP", "Must roll back"),
        operation("update_region", { name: "not-present" }, { name: "not-present" }, { min_x: 1 })
      ]
    });
    expect(failed.structuredContent.ok).toBe(false);
    expect(failed.structuredContent.error.code).toBe("INVALID_ARGUMENT");
    expect(readFileSync(canonicalPath, "utf8")).toBe(initialCanonical);
    expect(existsSync(join(transactionRoot, "revisions/0001-after-operations.json"))).toBe(false);

    const stale = await client.call("wc3_apply_operations", {
      project_id: "hero-team-wars",
      transaction_id: transactionId,
      expected_revision: 1,
      operations: [operation("set_map_metadata", { field: "title" }, "Hero Team Wars - Two Arena MVP", "Stale request")]
    });
    expect(stale.structuredContent.ok).toBe(false);
    expect(stale.structuredContent.error.code).toBe("PRECONDITION_FAILED");
    expect(sourceHash()).toBe(before);
  }, 120_000);

  it("discards only a confirmed transaction and leaves the source unchanged", async () => {
    const before = sourceHash();
    client = new McpClient();
    await client.initialize();
    const begin = await client.call("wc3_begin_transaction", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", expected_source_hash: before });
    transactionId = begin.structuredContent.data.transaction_id as string;
    const discarded = await client.call("wc3_discard_transaction", { project_id: "hero-team-wars", transaction_id: transactionId, expected_source_hash: before, confirmation: true });

    expect(discarded.structuredContent.ok).toBe(true);
    expect(discarded.structuredContent.data.discarded).toBe(true);
    expect(existsSync(join(projectRoot, "tools/wc3-map-mcp/snapshots/transactions", transactionId))).toBe(false);
    expect(JSON.parse(readFileSync(join(projectRoot, "tools/wc3-map-mcp/artifacts/audit", `discard-${transactionId}.json`), "utf8")).status).toBe("discarded");
    expect(sourceHash()).toBe(before);
  }, 120_000);
});

afterAll(() => rmSync(configRoot, { recursive: true, force: true }));
