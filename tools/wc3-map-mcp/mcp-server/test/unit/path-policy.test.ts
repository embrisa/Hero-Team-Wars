import { mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { afterEach, describe, expect, it } from "vitest";
import { ProjectService } from "../../src/services/project-service.js";
import { assertSafeRelative, isWithin, readPath, resolveProject, type ResolvedProject } from "../../src/config/resolve-project.js";
import type { Wc3Config } from "../../src/config/schema.js";

const fixtureRoots: string[] = [];

function fixture(): { root: string; config: Wc3Config; project: ResolvedProject } {
  const root = mkdtempSync(join(tmpdir(), "wc3-map-mcp-path-"));
  fixtureRoots.push(root);
  mkdirSync(join(root, "maps"), { recursive: true });
  mkdirSync(join(root, "allowed"), { recursive: true });
  writeFileSync(join(root, "maps", "source.w3m"), "fixture map");

  const config = fixtureConfigFor(root);
  return { root, config, project: resolveProject(config, "fixture") };
}

afterEach(() => {
  while (fixtureRoots.length > 0) {
    const root = fixtureRoots.pop();
    if (root) rmSync(root, { recursive: true, force: true });
  }
});

describe("path policy", () => {
  it("rejects traversal, rooted paths, and wildcard syntax", () => {
    expect(() => assertSafeRelative("../outside.w3m")).toThrow();
    expect(() => assertSafeRelative("..\\outside.w3m")).toThrow();
    expect(() => assertSafeRelative("C:/outside.w3m")).toThrow();
    expect(() => assertSafeRelative("\\\\server\\share\\outside.w3m")).toThrow();
    expect(() => assertSafeRelative("safe/*.w3m", true)).toThrow();
  });

  it("uses segment-aware, case-insensitive containment", () => {
    expect(isWithin("C:/safe/root", "C:/safe/root/file.w3m")).toBe(true);
    expect(isWithin("C:/safe/root", "C:/safe/root-evil/file.w3m")).toBe(false);
    expect(isWithin("C:/safe/root", "c:/SAFE/ROOT/file.w3m")).toBe(true);
  });

  it("rejects unknown project IDs before resolving paths", () => {
    expect(() => resolveProject({ projects: {} } as Wc3Config, "missing")).toThrow(/Unknown project/);
  });

  it("rejects existing files outside the configured read roots", () => {
    const { root, project, config } = fixture();
    writeFileSync(join(root, "not-readable.w3m"), "not allowed");
    expect(() => readPath(project, "not-readable.w3m")).toThrow(/outside the configured read roots/);

    const service = new ProjectService(config, undefined as never);
    expect(() => service.map("fixture", "not-readable.w3m")).toThrow(/outside the configured read roots/);
  });

  it("rejects directories when a map file is required", () => {
    const { root, config } = fixture();
    mkdirSync(join(root, "maps", "directory.w3m"));
    const service = new ProjectService(config, undefined as never);
    expect(() => service.map("fixture", "maps/directory.w3m")).toThrow(/not a file/);
  });

  it("rejects symlink escapes when the platform permits symlink creation", () => {
    const { root, project } = fixture();
    const outside = mkdtempSync(join(tmpdir(), "wc3-map-mcp-outside-"));
    fixtureRoots.push(outside);
    writeFileSync(join(outside, "escaped.w3m"), "outside root");
    const link = join(root, "maps", "escape");

    try {
      symlinkSync(outside, link, "junction");
    } catch (error) {
      const code = (error as NodeJS.ErrnoException).code;
      expect(["EACCES", "EPERM", "ENOTSUP", "UNKNOWN"], `symlink creation unsupported: ${code ?? String(error)}`).toContain(code);
      return;
    }

    expect(() => readPath(project, "maps/escape/escaped.w3m")).toThrow(/resolves outside its configured root/);
  });
});

function fixtureConfigFor(root: string): Wc3Config {
  return {
    schema_version: "1.0",
    engine: { executable: process.execPath, arguments: [], request_timeout_ms: 1000 },
    projects: {
      fixture: {
        root,
        source_maps: ["maps/source.w3m"],
        read_roots: ["maps", "allowed"],
        staging_root: "staging",
        artifact_root: "artifacts",
        build_root: "builds",
        log_root: "logs",
        test_output_root: "test-output",
        enabled_tools: [],
        write_policy: "read_only",
        max_map_bytes: 1024 * 1024,
        max_operation_count: 10
      }
    }
  } as Wc3Config;
}
