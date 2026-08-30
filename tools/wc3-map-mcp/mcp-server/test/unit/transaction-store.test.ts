import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { afterEach, describe, expect, it } from "vitest";
import { resolveProject, type ResolvedProject } from "../../src/config/resolve-project.js";
import type { Wc3Config } from "../../src/config/schema.js";
import { sha256File } from "../../src/services/artifact-service.js";
import { withProjectLock } from "../../src/storage/project-lock.js";
import { TransactionStore } from "../../src/storage/transaction-store.js";

const roots: string[] = [];

afterEach(() => {
  while (roots.length > 0) {
    const root = roots.pop();
    if (root) rmSync(root, { recursive: true, force: true });
  }
});

describe("transaction store", () => {
  it("creates an isolated source snapshot and complete manifest", () => {
    const { root, project } = fixture();
    const source = join(root, "maps", "source.w3m");
    const store = new TransactionStore();
    const hash = sha256File(source);
    const created = store.create(project, source, hash, { schema_version: "1.0", metadata: [], players: [], forces: [], regions: [] }, { label: "unit", versions: { server: "test", engine: "test", schema: "1.0", worker_protocol: "1.0", dependencies: {} } });
    const loaded = store.read(project, created.id);

    expect(sha256File(loaded.paths.sourceMap).sha256).toBe(hash.sha256);
    expect(loaded.manifest.source.path).toBe(source);
    expect(loaded.manifest.label).toBe("unit");
    expect(loaded.manifest.revision_hashes["0"]).toBe(loaded.manifest.canonical_sha256);
    expect(readFileSync(loaded.paths.manifest, "utf8")).toContain('"operation_revisions"');
  });

  it("rejects invalid state transitions and discards one exact transaction with an audit tombstone", () => {
    const { root, project } = fixture();
    const source = join(root, "maps", "source.w3m");
    const store = new TransactionStore();
    const created = store.create(project, source, sha256File(source), { schema_version: "1.0", metadata: [], players: [], forces: [], regions: [] });
    const loaded = store.read(project, created.id);

    expect(() => store.update(loaded.paths, { ...loaded.manifest, state: "built" })).toThrow(/Invalid transaction state transition/);
    const discarded = store.discard(project, created.id, loaded.manifest.source.sha256, "00000000-0000-4000-8000-000000000001");

    expect(discarded.discarded).toBe(true);
    expect(existsSync(loaded.paths.directory)).toBe(false);
    const tombstone = JSON.parse(readFileSync(String(discarded.tombstone_path), "utf8")) as { status: string; state_before_discard: string };
    expect(tombstone.status).toBe("discarded");
    expect(tombstone.state_before_discard).toBe("staged");
  });

  it("rejects a junction/symlink transaction directory", () => {
    const { root, project } = fixture();
    const outside = mkdtempSync(join(tmpdir(), "wc3-map-mcp-transaction-outside-"));
    roots.push(outside);
    const id = "c0a80101-0000-4000-8000-000000000010";
    const link = join(project.stagingRoot, id);
    mkdirSync(project.stagingRoot, { recursive: true });
    try {
      symlinkSync(outside, link, "junction");
    } catch (error) {
      const code = (error as NodeJS.ErrnoException).code;
      expect(["EACCES", "EPERM", "ENOTSUP", "UNKNOWN"], `symlink creation unsupported: ${code ?? String(error)}`).toContain(code);
      return;
    }

    expect(() => new TransactionStore().read(project, id)).toThrow(/real directory|direct child/);
  });
});

describe("project mutation lock", () => {
  it("serializes concurrent callbacks", async () => {
    const { project } = fixture();
    const events: string[] = [];
    let firstStarted = false;
    const first = withProjectLock(project, "first", async () => {
      firstStarted = true;
      events.push("first-start");
      await new Promise(resolve => setTimeout(resolve, 75));
      events.push("first-end");
    });
    while (!firstStarted) await new Promise(resolve => setTimeout(resolve, 1));
    const second = withProjectLock(project, "second", async () => events.push("second"));
    await Promise.all([first, second]);

    expect(events).toEqual(["first-start", "first-end", "second"]);
  });
});

function fixture(): { root: string; project: ResolvedProject } {
  const root = mkdtempSync(join(tmpdir(), "wc3-map-mcp-transaction-"));
  roots.push(root);
  mkdirSync(join(root, "maps"), { recursive: true });
  writeFileSync(join(root, "maps", "source.w3m"), "fixture source");
  const config: Wc3Config = {
    schema_version: "1.0",
    engine: { executable: process.execPath, arguments: [], request_timeout_ms: 1000 },
    projects: {
      fixture: {
        root,
        source_maps: ["maps/source.w3m"],
        read_roots: ["maps"],
        staging_root: "staging/transactions",
        artifact_root: "artifacts",
        build_root: "builds",
        log_root: "logs",
        test_output_root: "test-output",
        enabled_tools: [],
        write_policy: "writes",
        max_map_bytes: 1024 * 1024,
        max_operation_count: 10
      }
    }
  };
  return { root, project: resolveProject(config, "fixture") };
}
