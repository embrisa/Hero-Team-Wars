import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { join, parse } from "node:path";
import { tmpdir } from "node:os";
import { afterEach, describe, expect, it } from "vitest";
import { WindowsWarcraftPaths } from "../../src/platform/windows/warcraft-paths.js";
import { NativeProcessRunner } from "../../src/platform/windows/process-runner.js";

const roots: string[] = [];

afterEach(() => {
  while (roots.length > 0) {
    const root = roots.pop();
    if (root) rmSync(root, { recursive: true, force: true });
  }
});

describe("Windows launch adapters", () => {
  it("creates a direct, UUID-addressed test destination beneath the configured root", () => {
    const root = mkdtempSync(join(tmpdir(), "wc3-map-mcp-platform-test-"));
    roots.push(root);
    const paths = new WindowsWarcraftPaths();
    const result = paths.testCopyPath(root, "11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", ".w3m");

    expect(result).toBe(join(root, "MCP_11111111-1111-4111-8111-111111111111_22222222-2222-4222-8222-222222222222.w3m"));
    expect(parse(result).dir.toLowerCase()).toBe(root.toLowerCase());
  });

  it("rejects a filesystem root and an existing test destination", () => {
    const paths = new WindowsWarcraftPaths();
    expect(() => paths.testRoot(parse(process.cwd()).root)).toThrow(/too broad/i);

    const root = mkdtempSync(join(tmpdir(), "wc3-map-mcp-platform-test-"));
    roots.push(root);
    const name = "MCP_11111111-1111-4111-8111-111111111111_22222222-2222-4222-8222-222222222222.w3m";
    writeFileSync(join(root, name), "already here");
    expect(() => paths.testCopyPath(root, "11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", ".w3m")).toThrow(/already exists/i);
  });

  it("uses spawn with an argument array and keeps the process detached", () => {
    const runner = new NativeProcessRunner();
    const executable = process.execPath;
    const started = runner.start({ executable, arguments: ["-e", ""], working_directory: process.cwd() });

    expect(started.pid).toBeGreaterThan(0);
    expect(started.exit_state).toBe("unknown");
  });
});
