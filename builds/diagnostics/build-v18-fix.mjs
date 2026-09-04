import { createHash } from "node:crypto";
import { spawnSync } from "node:child_process";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { buildMapVariant, publishPlayableMap } from "./build-matrix-runner.mjs";

const root = "C:\\Users\\hp\\Documents\\Warcraft III\\Hero Team Wars";
const expectedSourceHash = "027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834";
const expectedVersion = 18;
const expectedRawcodes = ["H001", "H002", "H003", "H004", "n0AL"];
const expectedHeroes = {
  H001: { parent: "Hpal", name: "HTW Guardian" },
  H002: { parent: "Hmkg", name: "HTW Striker" },
  H003: { parent: "Hamg", name: "HTW Controller" },
  H004: { parent: "Hblm", name: "HTW Support" }
};
const expectedAltar = {
  parent: "ntav",
  name: "HTW Hero Altar",
  tip: "A shared altar where every player selects one hero.",
  sold: "H001,H002,H003,H004"
};

const objectsPath = join(root, "tools", "wc3-map-mcp", "scripts", "mcp", "object-data", "v8-hero-objects.json");

function sha256File(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex").toUpperCase();
}

function powershellLiteral(value) {
  return `'${String(value).replaceAll("'", "''")}'`;
}

// Extract one member through the same War3Net MPQ library used by the map
// engine. No archive bytes are written or modified; this is only an exact
// post-build format assertion for the generated object-data member.
function readArchiveMemberBytes(mapPath, memberName) {
  const mpqAssembly = join(root, "tools", "wc3-map-mcp", "map-engine", "publish", "War3Net.IO.Mpq.dll");
  const script = `
Add-Type -Path ${powershellLiteral(mpqAssembly)}
$archive = [War3Net.IO.Mpq.MpqArchive]::Open(${powershellLiteral(mapPath)}, $true)
try {
  $stream = $archive.OpenFile(${powershellLiteral(memberName)})
  try {
    $memory = New-Object System.IO.MemoryStream
    try {
      $stream.CopyTo($memory)
      [Convert]::ToBase64String($memory.ToArray())
    } finally { $memory.Dispose() }
  } finally { $stream.Dispose() }
} finally { $archive.Dispose() }
`;
  const result = spawnSync("pwsh.exe", ["-NoProfile", "-NonInteractive", "-Command", script], {
    encoding: "utf8",
    windowsHide: true
  });
  assert(result.status === 0, `PowerShell/War3Net member extraction failed: ${result.stderr?.trim() || `exit ${result.status}`}`);
  const encoded = result.stdout.trim();
  assert(encoded.length > 0, `War3Net returned no bytes for ${memberName}`);
  return Buffer.from(encoded, "base64");
}

function assert(condition, message) {
  if (!condition) throw new Error(`v18 static check failed: ${message}`);
}

function modificationsById(object) {
  const modifications = object?.modifications;
  assert(Array.isArray(modifications), `${object?.rawcode ?? "object"} has no modifications array`);
  const byId = new Map();
  for (const modification of modifications) {
    assert(typeof modification.id === "string", `${object.rawcode} has a modification without an id`);
    assert(!byId.has(modification.id), `${object.rawcode} contains duplicate '${modification.id}' modifications`);
    byId.set(modification.id, modification);
  }
  return byId;
}

function assertModification(modifications, id, type, value, rawcode) {
  const modification = modifications.get(id);
  assert(modification, `${rawcode} is missing ${id}`);
  assert(modification.type === type, `${rawcode}.${id} must be ${type}, got ${modification.type}`);
  assert(modification.value === value, `${rawcode}.${id} must equal ${JSON.stringify(value)}, got ${JSON.stringify(modification.value)}`);
}

function assertFixture(objects) {
  assert(Array.isArray(objects), "fixture root.objects must be an array");
  assert(objects.length === 5, `live fixture must contain exactly 5 custom objects, got ${objects.length}`);

  const byRawcode = new Map();
  for (const object of objects) {
    assert(object.category === "unit", `${object.rawcode} must be a unit object`);
    assert(object.object_kind === "custom", `${object.rawcode} must be a custom object`);
    assert(typeof object.rawcode === "string" && !byRawcode.has(object.rawcode), `duplicate or missing rawcode in fixture`);
    byRawcode.set(object.rawcode, object);
  }

  assert(JSON.stringify([...byRawcode.keys()].sort()) === JSON.stringify([...expectedRawcodes].sort()), "fixture rawcodes must be H001..H004 and n0AL");

  for (const [rawcode, expected] of Object.entries(expectedHeroes)) {
    const hero = byRawcode.get(rawcode);
    assert(hero, `fixture is missing ${rawcode}`);
    assert(hero.base_rawcode === expected.parent, `${rawcode} must inherit ${expected.parent}`);
    const modifications = modificationsById(hero);
    assertModification(modifications, "unam", "String", expected.name, rawcode);
    assertModification(modifications, "ugol", "Int", 0, rawcode);
    assertModification(modifications, "ulum", "Int", 0, rawcode);
    assertModification(modifications, "usst", "Int", 0, rawcode);
    assertModification(modifications, "usrg", "Int", 1, rawcode);
    assert(!modifications.has("uhst"), `${rawcode} must not contain obsolete uhst`);
  }

  const altar = byRawcode.get("n0AL");
  assert(altar, "fixture is missing n0AL");
  assert(altar.base_rawcode === expectedAltar.parent, "n0AL must inherit ntav");
  const altarModifications = modificationsById(altar);
  assertModification(altarModifications, "unam", "String", expectedAltar.name, "n0AL");
  assertModification(altarModifications, "utip", "String", expectedAltar.tip, "n0AL");
  assertModification(altarModifications, "useu", "String", expectedAltar.sold, "n0AL");

  return byRawcode;
}

function assertInspectedObjects(inspect, expectedObjects) {
  const outputObjects = Array.isArray(inspect.object_data) ? inspect.object_data : [];
  assert(outputObjects.length === expectedRawcodes.length, `reopened output must contain exactly 5 custom objects, got ${outputObjects.length}`);
  const outputByRawcode = new Map(outputObjects.map((object) => [object.rawcode, object]));
  assert(JSON.stringify([...outputByRawcode.keys()].sort()) === JSON.stringify([...expectedRawcodes].sort()), "reopened output rawcodes differ from fixture");

  for (const [rawcode, expectedObject] of expectedObjects) {
    const outputObject = outputByRawcode.get(rawcode);
    assert(outputObject, `reopened output is missing ${rawcode}`);
    assert(outputObject.base_rawcode === expectedObject.base_rawcode, `${rawcode} base rawcode changed during build`);
    const outputModifications = modificationsById(outputObject);
    const expectedModifications = modificationsById(expectedObject);
    for (const [id, modification] of expectedModifications) {
      assertModification(outputModifications, id, modification.type, modification.value, rawcode);
    }
    assert(!outputModifications.has("uhst"), `${rawcode} output contains obsolete uhst`);
  }

  const member = (inspect.object_data_members ?? []).find((item) => item.archive_path?.toLowerCase() === "war3map.w3u");
  assert(member, "reopened output is missing the war3map.w3u member");
  assert(member.capability === "roundtrip_verified", `war3map.w3u must reopen as roundtrip_verified, got ${member.capability}`);
  assert((inspect.parse_warnings ?? []).length === 0, "reopened output must have no parse warnings");

  // The exact serialized format word is checked immediately after this
  // decoded inspection through readArchiveMemberBytes().
  return {
    format_version: 2,
    object_count: outputObjects.length,
    rawcodes: [...outputByRawcode.keys()],
    archive_path: member.archive_path,
    archive_sha256: (inspect.archive_members ?? []).find((item) => item.path?.toLowerCase() === "war3map.w3u")?.sha256 ?? null,
    codec_version: member.codec_version
  };
}

async function main() {
  const fixture = JSON.parse(readFileSync(objectsPath, "utf8"));
  const expectedObjects = assertFixture(fixture.objects);
  const targetDir = `C:\\Users\\hp\\Documents\\Warcraft III\\Maps\\Test\\v${expectedVersion}`;
  assert(!existsSync(targetDir), `target version directory already exists: ${targetDir}`);

  const summary = await buildMapVariant({
    variantId: "v18-combined-altar-fix",
    versionLabel: "v18",
    description: "Combined HTW altar fix: live v8 hero fixture with v2 object data, n0AL useu roster, zero hero start delay, one-second replenishment, free hero pick, and composed hero-selection JASS.",
    heroObjects: fixture.objects
  });

  assert(summary.source_sha256 === expectedSourceHash, `build summary source hash changed: ${summary.source_sha256}`);
  assert(summary.source_unchanged === true, "golden source must remain unchanged after build");
  assert(summary.reopened === true, "map engine must confirm a clean reopen");
  assert(summary.objects.count === 5, `build summary object count must be 5, got ${summary.objects.count}`);
  assert(summary.objects.has_w3u === true, "build summary must contain war3map.w3u");

  const outputInspect = JSON.parse(readFileSync(join(summary.diagnostics, "output-inspect.json"), "utf8"));
  const objectData = assertInspectedObjects(outputInspect, new Map(fixture.objects.map((object) => [object.rawcode, object])));
  const w3uBytes = readArchiveMemberBytes(summary.output_path, "war3map.w3u");
  assert(w3uBytes.length >= 4, "war3map.w3u must contain a format-version word");
  assert(w3uBytes.readInt32LE(0) === 2, `war3map.w3u format version must be v2, got ${w3uBytes.readInt32LE(0)}`);
  const inspectedW3u = (outputInspect.archive_members ?? []).find((item) => item.path?.toLowerCase() === "war3map.w3u");
  assert(inspectedW3u?.sha256 === createHash("sha256").update(w3uBytes).digest("hex").toUpperCase(), "war3map.w3u extracted hash differs from inspection hash");
  objectData.format_version = w3uBytes.readInt32LE(0);
  objectData.archive_sha256 = inspectedW3u.sha256;

  const published = publishPlayableMap(summary, expectedVersion);
  assert(published.published_path === join(targetDir, "HeroTeamWars_v18.w3m"), "published path must be the v18 playable filename");
  assert(published.published_sha256 === summary.output_sha256, "published copy hash must equal build output hash");
  assert(sha256File(published.published_path) === summary.output_sha256, "published copy rehash must equal build output hash");
  assert(sha256File(join(root, "map", "HeroTeamWars_M0_2Arena.w3m")) === expectedSourceHash, "golden source hash changed after publication");

  const report = {
    experiment: "v18-combined-altar-fix",
    version_label: "v18",
    build_id: summary.build_id,
    summary,
    object_data: objectData,
    published,
    static_checks: {
      source_sha256: expectedSourceHash,
      source_unchanged: true,
      object_data_format_version: 2,
      custom_object_count: 5,
      custom_rawcodes: expectedRawcodes,
      altar_useu: expectedAltar.sold,
      hero_stock_start_delay: 0,
      hero_stock_replenishment_interval: 1,
      hero_gold_cost: 0,
      hero_lumber_cost: 0,
      output_hash_matches_published_copy: true,
      runtime_status: "untested"
    }
  };
  const reportPath = join(root, "builds", "diagnostics", "v18-build-report.json");
  writeFileSync(reportPath, JSON.stringify(report, null, 2));

  console.log("=== v18 Combined Altar Fix Build Successful ===");
  console.log("Output Path:", summary.output_path);
  console.log("Published Path:", published.published_path);
  console.log("SHA-256:", published.published_sha256);
  console.log("Report:", reportPath);
}

if (process.argv[1]?.endsWith("build-v18-fix.mjs")) {
  main().catch((error) => {
    console.error("v18 build failed:", error);
    process.exit(1);
  });
}

export { assertFixture, assertInspectedObjects, main };
