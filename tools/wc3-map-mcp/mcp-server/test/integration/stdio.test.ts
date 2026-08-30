import { existsSync, readFileSync } from "node:fs";
import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const serverRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const projectRoot = resolve(serverRoot, "../../..");
const sourcePath = resolve(projectRoot, "map/HeroTeamWars_M0_2Arena.w3m");

function hashSource(): string {
  return createHash("sha256").update(readFileSync(sourcePath)).digest("hex").toUpperCase();
}

function requestProcess(): Promise<{ lines: string[]; stderr: string }> {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(process.execPath, [resolve(serverRoot, "dist/index.js")], { cwd: serverRoot, stdio: ["pipe", "pipe", "pipe"] });
    const lines: string[] = [];
    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", chunk => { stdout += String(chunk); });
    child.stderr.on("data", chunk => { stderr += String(chunk); });
    child.on("error", rejectPromise);
    child.on("close", code => code === 0 ? resolvePromise({ lines: stdout.split(/\r?\n/).filter(Boolean).map(line => JSON.parse(line) as string), stderr }) : rejectPromise(new Error(`server exited ${code}: ${stderr}`)));
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 1, method: "initialize", params: { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "test", version: "1" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized", params: {} })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 2, method: "tools/list", params: {} })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 3, method: "tools/call", params: { name: "wc3_project_status", arguments: { project_id: "hero-team-wars" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 4, method: "tools/call", params: { name: "wc3_inspect_map", arguments: { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", section: "metadata" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 5, method: "tools/call", params: { name: "wc3_list_archive_files", arguments: { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", max_items: 50 } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 6, method: "tools/call", params: { name: "wc3_get_component", arguments: { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", component: "regions", max_items: 20 } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 7, method: "tools/call", params: { name: "wc3_get_component", arguments: { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", component: "variables" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 8, method: "tools/call", params: { name: "wc3_validate_map", arguments: { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 9, method: "tools/call", params: { name: "wc3_compare_maps", arguments: { project_id: "hero-team-wars", left: "map/HeroTeamWars_M0_2Arena.w3m", right: "map/HeroTeamWars_M0_2Arena.w3m" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 10, method: "tools/call", params: { name: "wc3_list_archive_files", arguments: { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", max_items: 5 } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 11, method: "tools/call", params: { name: "wc3_list_archive_files", arguments: { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", cursor: "not-a-valid-cursor", max_items: 5 } } })}\n`);
    child.stdin.end();
  });
}

describe("MCP STDIO", () => {
  it("keeps stdout protocol-clean and exposes read-only status", async () => {
    expect(existsSync(resolve(serverRoot, "dist/index.js"))).toBe(true);
    const before = hashSource();
    const result = await requestProcess();
    const after = hashSource();
    expect(after).toBe(before);
    expect(result.lines.every(line => typeof line === "object")).toBe(true);
    const list = result.lines.find(line => (line as any).id === 2) as any;
    expect(list.result.tools.some((tool: any) => tool.name === "wc3_project_status")).toBe(true);
    const call = result.lines.find(line => (line as any).id === 3) as any;
    expect(call.result.structuredContent.ok).toBe(true);
    const inspection = result.lines.find(line => (line as any).id === 4) as any;
    expect(inspection.result.structuredContent.ok).toBe(true);
    expect(inspection.result.structuredContent.data.data.metadata.length).toBeGreaterThan(0);
    const archive = result.lines.find(line => (line as any).id === 5) as any;
    expect(archive.result.structuredContent.ok).toBe(true);
    expect(archive.result.structuredContent.data.members.length).toBe(17);
    expect(archive.result.structuredContent.data.members.some((member: any) => member.path === "war3map.w3i" && member.capability === "parsed_read_only")).toBe(true);
    const regions = result.lines.find(line => (line as any).id === 6) as any;
    expect(regions.result.structuredContent.ok).toBe(true);
    expect(regions.result.structuredContent.data.values.length).toBe(10);
    const opaque = result.lines.find(line => (line as any).id === 7) as any;
    expect(opaque.result.isError).toBe(true);
    expect(opaque.result.structuredContent.error.code).toBe("UNSUPPORTED_COMPONENT");
    const validation = result.lines.find(line => (line as any).id === 8) as any;
    expect(validation.result.structuredContent.ok).toBe(true);
    const comparison = result.lines.find(line => (line as any).id === 9) as any;
    expect(comparison.result.structuredContent.ok).toBe(true);
    const paged = result.lines.find(line => (line as any).id === 10) as any;
    expect(paged.result.structuredContent.ok).toBe(true);
    expect(paged.result.structuredContent.data.members.length).toBe(5);
    expect(typeof paged.result.structuredContent.data.next_cursor).toBe("string");
    const staleCursor = result.lines.find(line => (line as any).id === 11) as any;
    expect(staleCursor.result.isError).toBe(true);
    expect(staleCursor.result.structuredContent.error.code).toBe("CURSOR_STALE");
  });
});
