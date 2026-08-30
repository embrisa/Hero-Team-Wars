import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
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
      enabled_tools: [], write_policy: "writes", max_map_bytes: 536870912, max_operation_count: 100
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
    await this.request("initialize", { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "phase3-build-test", version: "1" } });
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
    client = new McpClient();
    await client.initialize();

    const begin = await client.call("wc3_begin_transaction", { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", expected_source_hash: before, label: "phase3-noop" });
    expect(begin.structuredContent.ok).toBe(true);
    transactionId = begin.structuredContent.data.transaction_id as string;

    const validation = await client.call("wc3_validate_transaction", { project_id: "hero-team-wars", transaction_id: transactionId, revision: 0 });
    expect(validation.structuredContent.ok).toBe(true);
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
