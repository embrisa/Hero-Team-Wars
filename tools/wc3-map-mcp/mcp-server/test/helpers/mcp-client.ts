import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { resolve } from "node:path";

/** Shared bounded STDIO client. Any exit rejects pending calls, including exit 0. */
export class McpClient {
  private readonly child: ChildProcessWithoutNullStreams;
  private readonly pending = new Map<number, { resolve: (value: any) => void; reject: (error: Error) => void; timer: NodeJS.Timeout }>();
  private nextId = 1;
  private stdout = "";
  private stderr = "";
  private failure: Error | undefined;

  public constructor(serverRoot: string, configPath: string, private readonly timeoutMs = 120_000) {
    this.child = spawn(process.execPath, [resolve(serverRoot, "dist/index.js")], {
      cwd: serverRoot, env: { ...process.env, WC3_MAP_MCP_CONFIG: configPath }, stdio: ["pipe", "pipe", "pipe"], windowsHide: true
    });
    this.child.stdout.setEncoding("utf8");
    this.child.stderr.setEncoding("utf8");
    this.child.stdout.on("data", chunk => this.consume(String(chunk)));
    this.child.stderr.on("data", chunk => { this.stderr = (this.stderr + String(chunk)).slice(-4000); });
    this.child.stdin.on("error", error => this.rejectAll(error));
    this.child.on("error", error => this.rejectAll(error));
    this.child.on("close", (code, signal) => this.rejectAll(new Error(`MCP server exited (${code}, ${signal}): ${this.stderr}`)));
  }

  public async initialize(): Promise<void> {
    await this.request("initialize", { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "wc3-integration-tests", version: "1" } });
    this.child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized", params: {} })}\n`);
  }

  public call(name: string, args: Record<string, unknown>): Promise<any> { return this.request("tools/call", { name, arguments: args }); }
  public listTools(): Promise<any> { return this.request("tools/list", {}); }
  public close(): void {
    this.rejectAll(new Error("MCP client closed"));
    this.child.stdin.end();
    this.child.kill();
  }

  private request(method: string, params: Record<string, unknown>): Promise<any> {
    if (this.failure) return Promise.reject(this.failure);
    const id = this.nextId++;
    return new Promise((resolvePromise, rejectPromise) => {
      const timer = setTimeout(() => {
        this.rejectAll(new Error(`MCP ${method} timed out after ${this.timeoutMs} ms: ${this.stderr}`));
        this.child.kill();
      }, this.timeoutMs);
      this.pending.set(id, { resolve: resolvePromise, reject: rejectPromise, timer });
      this.child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id, method, params })}\n`);
    });
  }

  private consume(chunk: string): void {
    this.stdout += chunk;
    const lines = this.stdout.split(/\r?\n/);
    this.stdout = lines.pop() ?? "";
    try {
      for (const line of lines.filter(Boolean)) {
        const message = JSON.parse(line) as { id?: number; result?: unknown; error?: { message?: string } };
        if (message.id === undefined) continue;
        const waiter = this.pending.get(message.id);
        if (!waiter) continue;
        this.pending.delete(message.id);
        clearTimeout(waiter.timer);
        if (message.error) waiter.reject(new Error(message.error.message ?? "JSON-RPC request failed"));
        else waiter.resolve(message.result);
      }
    } catch (error) {
      this.rejectAll(new Error(`Malformed MCP response: ${String(error)}; stderr: ${this.stderr}`));
      this.child.kill();
    }
  }

  private rejectAll(error: Error): void {
    this.failure ??= error;
    for (const waiter of this.pending.values()) { clearTimeout(waiter.timer); waiter.reject(error); }
    this.pending.clear();
  }
}
