import { existsSync, readFileSync } from "node:fs";
import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const serverRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const projectRoot = resolve(serverRoot, "../../..");
const sourcePath = resolve(projectRoot, "map/HeroTeamWars_M0_2Arena.w3m");
const configPath = resolve(serverRoot, "../config/wc3-map-mcp.example.json");

function hashSource(): string {
  return createHash("sha256").update(readFileSync(sourcePath)).digest("hex").toUpperCase();
}

function requestProcess(): Promise<{ lines: string[]; stderr: string }> {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(process.execPath, [resolve(serverRoot, "dist/index.js")], { cwd: serverRoot, env: { ...process.env, WC3_MAP_MCP_CONFIG: configPath }, stdio: ["pipe", "pipe", "pipe"] });
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
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 12, method: "tools/call", params: { name: "wc3_inspect_map", arguments: { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", section: "metadata" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 13, method: "tools/call", params: { name: "wc3_project_status", arguments: {} } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 14, method: "tools/call", params: { name: "wc3_project_status", arguments: { project_id: "not-a-project" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 15, method: "tools/call", params: { name: "wc3_inspect_map", arguments: { project_id: "hero-team-wars", map: "../map/HeroTeamWars_M0_2Arena.w3m" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 16, method: "tools/call", params: { name: "wc3_inspect_map", arguments: { project_id: "hero-team-wars", map: sourcePath } } } )}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 17, method: "tools/call", params: { name: "wc3_inspect_map", arguments: { project_id: "hero-team-wars", map: "map" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 18, method: "tools/call", params: { name: "wc3_inspect_map", arguments: { project_id: "hero-team-wars", map: "design/07-editor-state.yaml" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 19, method: "tools/call", params: { name: "wc3_get_script_source", arguments: { project_id: "hero-team-wars", map: "map/HeroTeamWars_M0_2Arena.w3m", archive_path: "war3map.j" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 20, method: "tools/call", params: { name: "wc3_compose_gameplay_source", arguments: { project_id: "hero-team-wars", manifest_path: "tools/wc3-map-mcp/scripts/mcp/manifest.json", profile: "mvp_2arena" } } })}\n`);
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 21, method: "tools/call", params: { name: "wc3_validate_gameplay_source", arguments: { project_id: "hero-team-wars", manifest_path: "tools/wc3-map-mcp/scripts/mcp/manifest.json", profile: "mvp_2arena" } } })}\n`);
    child.stdin.end();
  });
}

function response(lines: string[], id: number): any {
  const result = lines.find(line => (line as any).id === id) as any;
  expect(result, `missing JSON-RPC response for id ${id}`).toBeDefined();
  return result;
}

function expectDomainError(lines: string[], id: number, code: string): any {
  const result = response(lines, id);
  expect(result.result?.isError).toBe(true);
  expect(result.result?.structuredContent?.ok).toBe(false);
  expect(result.result?.structuredContent?.error?.code).toBe(code);
  expect(result.result?.content).toEqual(expect.arrayContaining([expect.objectContaining({ type: "text" })]));
  return result;
}

describe("MCP STDIO", () => {
  it("keeps stdout protocol-clean and exposes read-only status", async () => {
    expect(existsSync(resolve(serverRoot, "dist/index.js"))).toBe(true);
    const before = hashSource();
    const result = await requestProcess();
    const after = hashSource();
    expect(after).toBe(before);
    expect(result.lines.every(line => typeof line === "object")).toBe(true);
    expect(result.stderr).toMatch(/wc3-map-mcp listening on stdio/);

    const list = response(result.lines, 2);
    const toolNames = list.result.tools.map((tool: any) => tool.name);
    expect(toolNames).toEqual([
      "wc3_project_status",
      "wc3_inspect_map",
      "wc3_list_archive_files",
      "wc3_get_component",
      "wc3_get_script_source",
      "wc3_validate_map",
      "wc3_compare_maps",
      "wc3_compose_gameplay_source",
      "wc3_validate_gameplay_source"
    ]);
    expect(list.result.tools.every((tool: any) => tool.annotations?.readOnlyHint === true && tool.annotations?.destructiveHint === false)).toBe(true);
    expect(toolNames.some((name: string) => /begin|apply|transaction|build|launch|promote|discard/i.test(name))).toBe(false);

    const call = response(result.lines, 3);
    expect(call.result.structuredContent.ok).toBe(true);
    expect(call.result.content).toEqual(expect.arrayContaining([expect.objectContaining({ type: "text", text: expect.any(String) })]));
    expect(call.result.structuredContent.correlation_id).toEqual(expect.any(String));

    const inspection = response(result.lines, 4);
    expect(inspection.result.structuredContent.ok).toBe(true);
    expect(inspection.result.structuredContent.data.data.metadata.length).toBeGreaterThan(0);
    expect(inspection.result.content).toEqual(expect.arrayContaining([expect.objectContaining({ type: "text" })]));

    const repeatedInspection = response(result.lines, 12);
    expect(repeatedInspection.result.structuredContent.ok).toBe(true);
    expect(repeatedInspection.result.structuredContent.data.data).toEqual(inspection.result.structuredContent.data.data);
    const firstCanonical = JSON.parse(readFileSync(resolve(projectRoot, inspection.result.structuredContent.data.artifact.path), "utf8"));
    const repeatedCanonical = JSON.parse(readFileSync(resolve(projectRoot, repeatedInspection.result.structuredContent.data.artifact.path), "utf8"));
    expect(repeatedCanonical).toEqual(firstCanonical);

    const archive = response(result.lines, 5);
    expect(archive.result.structuredContent.ok).toBe(true);
    expect(archive.result.structuredContent.data.members.length).toBe(17);
    expect(archive.result.structuredContent.data.members.some((member: any) => member.path === "war3map.w3i" && member.capability === "parsed_read_only")).toBe(true);
    const regions = response(result.lines, 6);
    expect(regions.result.structuredContent.ok).toBe(true);
    expect(regions.result.structuredContent.data.values.length).toBe(10);
    expectDomainError(result.lines, 7, "UNSUPPORTED_COMPONENT");
    const validation = response(result.lines, 8);
    expect(validation.result.structuredContent.ok).toBe(true);
    const comparison = response(result.lines, 9);
    expect(comparison.result.structuredContent.ok).toBe(true);
    const paged = response(result.lines, 10);
    expect(paged.result.structuredContent.ok).toBe(true);
    expect(paged.result.structuredContent.data.members.length).toBe(5);
    expect(typeof paged.result.structuredContent.data.next_cursor).toBe("string");
    expectDomainError(result.lines, 11, "CURSOR_STALE");

    const invalidSchema = response(result.lines, 13);
    const invalidSchemaPayload = invalidSchema.error ?? invalidSchema.result;
    expect(invalidSchemaPayload).toBeDefined();
    expect(JSON.stringify(invalidSchemaPayload)).toMatch(/project_id|invalid|required|argument/i);

    expectDomainError(result.lines, 14, "INVALID_ARGUMENT");
    expectDomainError(result.lines, 15, "PATH_OUTSIDE_ROOT");
    expectDomainError(result.lines, 16, "PATH_OUTSIDE_ROOT");
    expectDomainError(result.lines, 17, "INVALID_ARGUMENT");
    expectDomainError(result.lines, 18, "PATH_OUTSIDE_ROOT");
    const script = response(result.lines, 19);
    expect(script.result.structuredContent.ok).toBe(true);
    expect(script.result.structuredContent.data.language).toBe("jass");
    expect(script.result.structuredContent.data.source).toMatch(/function main takes nothing returns nothing/);
    const composition = response(result.lines, 20);
    expect(composition.result.structuredContent.ok).toBe(true);
    expect(composition.result.structuredContent.data.static_validation.evidence_level).toBe("static_only");
    expect(composition.result.structuredContent.data.source_artifact.path).toMatch(/^tools\/wc3-map-mcp\/artifacts\/gameplay\/source\//);
    const validationReport = response(result.lines, 21);
    expect(validationReport.result.structuredContent.ok).toBe(true);
    expect(validationReport.result.structuredContent.data.static_validation.evidence_level).toBe("static_only");
  });
});
