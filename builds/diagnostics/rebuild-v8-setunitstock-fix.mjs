import { spawn } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const root = "C:\\Users\\hp\\Documents\\Warcraft III\\Hero Team Wars";
const expectedSourceHash = "027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834";
const enginePath = join(root, "tools", "wc3-map-mcp", "map-engine", "publish", "Wc3MapEngine.Cli.exe");
const sourcePath = join(root, "map", "HeroTeamWars_M0_2Arena.w3m");
const manifestPath = join(root, "tools", "wc3-map-mcp", "scripts", "mcp", "manifest.json");
const objectsPath = join(root, "tools", "wc3-map-mcp", "scripts", "mcp", "object-data", "v8-hero-objects.json");
const buildId = randomUUID();
const diagDir = join(root, "builds", "diagnostics", `v8-n0al-load-fix-${buildId}`);
const outDir = join(root, "builds", "mcp", "hero-team-wars", buildId);
const outputPath = join(outDir, `HeroTeamWars_v8-n0al-load-fix_${buildId}.w3m`);

mkdirSync(diagDir, { recursive: true });
mkdirSync(outDir, { recursive: true });

function sha256File(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex").toUpperCase();
}

function createEngine() {
  const child = spawn(enginePath, ["--stdio"], { stdio: ["pipe", "pipe", "pipe"] });
  let buffer = "";
  const pending = new Map();
  child.stdout.setEncoding("utf8");
  child.stdout.on("data", (chunk) => {
    buffer += chunk;
    let newline;
    while ((newline = buffer.indexOf("\n")) >= 0) {
      const line = buffer.slice(0, newline).trim();
      buffer = buffer.slice(newline + 1);
      if (!line) continue;
      const response = JSON.parse(line);
      const waiter = pending.get(response.request_id);
      if (!waiter) throw new Error(`Unexpected engine response ${response.request_id}`);
      pending.delete(response.request_id);
      waiter(response);
    }
  });
  let stderr = "";
  child.stderr.setEncoding("utf8");
  child.stderr.on("data", (chunk) => { stderr += chunk; });
  child.on("exit", (code) => {
    if (pending.size > 0) {
      for (const waiter of pending.values()) waiter({ ok: false, error: { code: "ENGINE_EXIT", message: `Engine exited ${code}: ${stderr}` } });
      pending.clear();
    }
  });
  return {
    request(operation, payload) {
      const requestId = randomUUID();
      const request = { protocol_version: "1.0", request_id: requestId, operation, payload };
      return new Promise((resolve, reject) => {
        pending.set(requestId, (response) => {
          if (!response.ok) reject(new Error(`${response.error?.code}: ${response.error?.message}`));
          else resolve(response.result);
        });
        child.stdin.write(`${JSON.stringify(request)}\n`);
      });
    },
    close() {
      child.stdin.end();
    }
  };
}

function objectOperations(objects) {
  return objects.map((definition) => {
    const id = `war3map.w3u:new:${definition.base_rawcode}:${definition.custom_rawcode}`;
    return {
      operation_id: randomUUID(),
      type: "create_object_definition",
      target: { id, category: definition.category, rawcode: definition.rawcode },
      rationale: "v8 custom heroes and shared altar after SetUnitStock compile fix",
      value: {
        object_kind: definition.object_kind,
        category: definition.category,
        base_rawcode: definition.base_rawcode,
        custom_rawcode: definition.custom_rawcode,
        rawcode: definition.rawcode,
        display_name: definition.display_name,
        dependencies: [],
        references: {},
        unknown_ids: [],
        modifications: definition.modifications
      }
    };
  });
}

const engine = createEngine();
try {
  const sourceHash = sha256File(sourcePath);
  if (sourceHash !== expectedSourceHash) {
    throw new Error(`Golden source hash mismatch: ${sourceHash}`);
  }

  const inspect = await engine.request("inspect_map", { map_path: sourcePath });
  delete inspect.output_path;
  writeFileSync(join(diagDir, "source-inspect.json"), JSON.stringify(inspect));
  writeFileSync(join(diagDir, "source-hash.json"), JSON.stringify({ path: sourcePath, sha256: sourceHash, size_bytes: inspect.source.size_bytes }, null, 2));
  const script = inspect.scripts.find((item) => String(item.archive_path).toLowerCase() === "war3map.j");
  if (!script) throw new Error("Inspected source map has no war3map.j");

  const composition = await engine.request("compose_gameplay_source", { manifest_path: manifestPath, profile: "mvp_2arena" });
  writeFileSync(join(diagDir, "compose-manifest.json"), JSON.stringify({
    source_sha256: composition.source_sha256,
    manifest_sha256: composition.manifest_sha256,
    module_order: composition.module_order,
    main_count: composition.main_count,
    static_validation: composition.static_validation
  }, null, 2));
  writeFileSync(join(diagDir, "composed-source.j"), composition.source);
  if (composition.source.includes("SetUnitStock")) throw new Error("Composed JASS still contains SetUnitStock");
  if (!composition.source.includes("AddUnitToStock") || !composition.source.includes("RemoveUnitFromStock")) {
    throw new Error("Composed JASS is missing AddUnitToStock/RemoveUnitFromStock");
  }

  const canonical = inspect;
  Object.assign(canonical, composition.canonical_model, {
    trigger_mode: "mcp_native_jass",
    gameplay_source: {}
  });
  const bound = await engine.request("compose_gameplay_source", { canonical_model: canonical, profile: "mvp_2arena" });
  writeFileSync(join(diagDir, "composed-bound-source.j"), bound.source);
  if (bound.source.includes("SetUnitStock")) throw new Error("Bound JASS still contains SetUnitStock");
  canonical.gameplay_source = {
    schema_version: "1.0",
    composer_version: bound.composer_version,
    mode: bound.mode,
    profile: bound.profile,
    source_sha256: bound.source_sha256,
    manifest_sha256: composition.manifest_sha256,
    source_manifest_sha256: bound.source_manifest_sha256,
    source_manifest: bound.source_manifest,
    static_validation: bound.static_validation,
    provenance: "intended_design",
    capability: "staged_typed_write"
  };
  script.source = bound.source;
  script.source_sha256 = bound.source_sha256;
  script.sha256 = bound.source_sha256;
  script.size_bytes = Buffer.byteLength(bound.source, "utf8");
  script.provenance = "intended_design";
  script.capability = "staged_typed_write";

  const stagedPath = join(diagDir, "canonical-composed.json");
  writeFileSync(stagedPath, JSON.stringify(canonical));
  const objects = JSON.parse(readFileSync(objectsPath, "utf8")).objects;
  const operations = objectOperations(objects);
  writeFileSync(join(diagDir, "object-operations.json"), JSON.stringify(operations, null, 2));
  const applied = await engine.request("apply_operations", {
    canonical_path: stagedPath,
    operations,
    output_path: join(diagDir, "canonical-after-objects.json")
  });
  writeFileSync(join(diagDir, "apply-result.json"), JSON.stringify({ applied_operation_ids: applied.applied_operation_ids, change_count: applied.diff?.changes?.length }, null, 2));

  const validation = await engine.request("validate_canonical", {
    canonical_path: join(diagDir, "canonical-after-objects.json"),
    source_map_path: sourcePath,
    validation_context: { project_id: "hero-team-wars", profile: "mvp_2arena" }
  });
  writeFileSync(join(diagDir, "validation.json"), JSON.stringify(validation, null, 2));
  if (validation.buildable !== true) throw new Error("Canonical map is not buildable");

  const build = await engine.request("build_map", {
    source_map_path: sourcePath,
    canonical_path: join(diagDir, "canonical-after-objects.json"),
    output_path: outputPath,
    profile: "debug",
    validation_context: { project_id: "hero-team-wars", profile: "mvp_2arena" }
  });
  writeFileSync(join(diagDir, "build-result.json"), JSON.stringify(build, null, 2));

  const afterSourceHash = sha256File(sourcePath);
  if (afterSourceHash !== expectedSourceHash) throw new Error(`Golden source changed during rebuild: ${afterSourceHash}`);

  const outputHash = sha256File(outputPath);
  const inspectOut = await engine.request("inspect_map", { map_path: outputPath, output_path: join(diagDir, "output-inspect.json") });
  const scriptOut = await engine.request("read_script_source", { map_path: outputPath, archive_path: "war3map.j" });
  writeFileSync(join(diagDir, "output-war3map.j"), scriptOut.source);
  const members = await engine.request("list_archive_members", { map_path: outputPath });
  writeFileSync(join(diagDir, "output-archive.json"), JSON.stringify(members, null, 2));
  const validateOut = await engine.request("validate_map", { map_path: outputPath });
  writeFileSync(join(diagDir, "output-validate.json"), JSON.stringify(validateOut, null, 2));

  if (scriptOut.source.includes("SetUnitStock")) throw new Error("Built war3map.j still contains SetUnitStock");
  if (!scriptOut.source.includes("AddUnitToStock") || !scriptOut.source.includes("RemoveUnitFromStock")) {
    throw new Error("Built war3map.j is missing AddUnitToStock/RemoveUnitFromStock");
  }

  const definitions = inspectOut.object_data ?? [];
  const guardian = definitions.find((item) => item.rawcode === "H001");
  const altar = definitions.find((item) => item.rawcode === "n0AL");
  if (!guardian || guardian.base_rawcode !== "Hpal") throw new Error(`H001 parent is ${guardian?.base_rawcode}, expected Hpal`);
  if (!altar) throw new Error("Built map is missing n0AL altar");
  const sold = (altar?.modifications ?? []).filter((item) => item.id === "useu").map((item) => item.value);
  if (sold.length !== 1 || sold[0] !== "H001,H002,H003,H004") throw new Error(`Altar useu is ${JSON.stringify(sold)}`);
  if (scriptOut.source.includes("'H0AL'")) throw new Error("Built war3map.j still creates H0AL");
  if (!scriptOut.source.includes("'n0AL'")) throw new Error("Built war3map.j is missing n0AL");
  const w3u = (members.members ?? []).find((item) => item.path === "war3map.w3u");
  if (!w3u) throw new Error("Built archive is missing war3map.w3u");

  const summary = {
    build_id: buildId,
    source_path: sourcePath,
    source_sha256: sourceHash,
    source_unchanged: afterSourceHash === expectedSourceHash,
    output_path: outputPath,
    output_sha256: outputHash,
    output_size_bytes: inspectOut.source.size_bytes,
    engine_sha256: build.sha256,
    reopened: build.reopened,
    opaque_members_preserved: build.opaque_members_preserved,
    runtime_status: "untested",
    script: {
      sha256: scriptOut.sha256,
      size_bytes: scriptOut.size_bytes,
      contains_setunitstock: scriptOut.source.includes("SetUnitStock"),
      contains_addunittostock: scriptOut.source.includes("AddUnitToStock"),
      contains_removeunitfromstock: scriptOut.source.includes("RemoveUnitFromStock")
    },
    objects: {
      h001_base: guardian.base_rawcode,
      n0al_useu: sold,
      w3u_sha256: w3u.sha256,
      w3u_size_bytes: w3u.size_bytes
    },
    diagnostics: diagDir
  };
  writeFileSync(join(diagDir, "summary.json"), JSON.stringify(summary, null, 2));
  writeFileSync(join(outDir, "rebuild-summary.json"), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify(summary, null, 2));
} finally {
  engine.close();
}
