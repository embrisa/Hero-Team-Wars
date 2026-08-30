import { existsSync, readFileSync, rmSync } from "node:fs";
import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, describe, expect, it } from "vitest";

const serverRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const projectRoot = resolve(serverRoot, "../../..");
const sourcePath = resolve(projectRoot, "map/HeroTeamWars_M0_2Arena.w3m");

class McpClient {
  private readonly child: ChildProcessWithoutNullStreams;
  private readonly pending = new Map<number, { resolve: (value: any) => void; reject: (error: Error) => void }>();
  private nextId = 1;
  private stdout = "";

  public constructor() {
    this.child = spawn(process.execPath, [resolve(serverRoot, "dist/index.js")], { cwd: serverRoot, stdio: ["pipe", "pipe", "pipe"] });
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
});
