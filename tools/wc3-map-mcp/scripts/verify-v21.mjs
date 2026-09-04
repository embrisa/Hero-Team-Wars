import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, mkdirSync, copyFileSync, constants, existsSync, readdirSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

// Read-only archive inspection inputs come from wc3_inspect_map and
// wc3_get_script_source. Publication copies the entire verified build only.
const root = resolve(fileURLToPath(new URL('../../..', import.meta.url)));
const json = path => JSON.parse(readFileSync(resolve(root, path), 'utf8'));
const sha = path => createHash('sha256').update(readFileSync(path)).digest('hex').toUpperCase();
const expectedSource = '027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834';
const expectedV20 = 'E65E29FFCE8705F6DFB50759DF27C1A904F291AD49DC5CFF8EC4FB196A2DC9E5';
const [inputPath, publishFlag] = process.argv.slice(2);
assert(inputPath, 'Supply a verification input JSON produced from exact MCP results.');
const input = json(inputPath);
const manifest = json(input.build_manifest);
const before = json(input.baseline_inspect);
const after = json(input.output_inspect);
const oldScript = readFileSync(resolve(root, input.baseline_script), 'utf8');
const newScript = readFileSync(resolve(root, input.output_script), 'utf8');
const output = resolve(root, manifest.output_path);
function archiveMember(name) {
  const quote = value => "'" + value.replaceAll("'", "''") + "'";
  const script = `Add-Type -Path ${quote(join(root, 'tools/wc3-map-mcp/map-engine/publish/War3Net.IO.Mpq.dll'))}
$v21Archive = [War3Net.IO.Mpq.MpqArchive]::Open(${quote(output)}, $true)
try {
  $v21Stream = $v21Archive.OpenFile(${quote(name)})
  try {
    $v21Bytes = [System.IO.MemoryStream]::new()
    try { $v21Stream.CopyTo($v21Bytes); [Convert]::ToBase64String($v21Bytes.ToArray()) }
    finally { $v21Bytes.Dispose() }
  } finally { $v21Stream.Dispose() }
} finally { $v21Archive.Dispose() }`;
  const result = spawnSync('pwsh.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { encoding: 'utf8', windowsHide: true });
  assert.equal(result.status, 0, result.stderr);
  return Buffer.from(result.stdout.trim(), 'base64');
}
function decodeObjects(bytes, levelBased) {
  let cursor = 0;
  const int = () => { const n = bytes.readInt32LE(cursor); cursor += 4; return n; };
  const raw = () => { const s = bytes.toString('ascii', cursor, cursor + 4); cursor += 4; return s; };
  const str = () => { const end = bytes.indexOf(0, cursor); assert(end >= cursor); const s = bytes.toString('utf8', cursor, end); cursor = end + 1; return s; };
  assert.equal(int(), 2, 'Generated object data must use the proven v2 format');
  const records = [];
  for (let table = 0; table < 2; table++) {
    const count = int();
    assert(count >= 0 && count < 10000);
    for (let i = 0; i < count; i++) {
      const base = raw(), custom = raw(), mods = [], countMods = int();
      assert(countMods >= 0 && countMods < 10000);
      for (let m = 0; m < countMods; m++) {
        const id = raw(), type = int();
        const level = levelBased ? int() : undefined, pointer = levelBased ? int() : undefined;
        let value;
        if (type === 0) value = int();
        else if (type === 1 || type === 2) { value = bytes.readFloatLE(cursor); cursor += 4; }
        else { assert.equal(type, 3); value = str(); }
        int(); // Object-data modification terminator.
        mods.push({ id, type, value, level, pointer });
      }
      records.push({ base, rawcode: table === 0 ? base : custom, mods });
    }
  }
  assert.equal(cursor, bytes.length, 'Object decoder consumed the exact member');
  return records;
}
const testRoot = resolve(root, '../Maps/Test');
const v20 = join(testRoot, 'v20/HeroTeamWars_v20.w3m');
assert.equal(sha(resolve(root, 'map/HeroTeamWars_M0_2Arena.w3m')), expectedSource);
assert.equal(sha(v20), expectedV20);
assert.equal(before.source.sha256, expectedV20);
assert.equal(sha(output), manifest.output_sha256);
assert.equal(after.source.sha256, manifest.output_sha256);
assert.equal(manifest.capability_profile, 'mvp_2arena');
assert.deepEqual(after.parse_warnings, []);
for (const component of ['players', 'forces', 'regions', 'placed_objects', 'imports']) {
  assert.deepEqual(after[component], before[component], `Unrelated ${component} changed`);
}
const allowedMembers = new Set(['(attributes)', '(listfile)', 'war3map.j', 'war3map.w3u', 'war3map.w3a', 'war3map.w3h']);
for (const member of before.archive_members) {
  if (!allowedMembers.has(member.path)) assert.equal(after.archive_members.find(x => x.path === member.path)?.sha256, member.sha256, member.path);
}
for (const member of after.archive_members) {
  assert(before.archive_members.some(x => x.path === member.path) || allowedMembers.has(member.path), `Unexpected archive member ${member.path}`);
}
const definitions = [
  ...json('tools/wc3-map-mcp/scripts/mcp/object-data/v8-hero-objects.json').objects,
  ...json(input.ability_definitions).objects
];
assert.equal(after.object_data.length, definitions.length);
for (const expected of definitions) {
  const actual = after.object_data.find(x => x.category === expected.category && x.rawcode === expected.rawcode);
  assert(actual, `Missing ${expected.rawcode}`);
  assert.equal(actual.base_rawcode, expected.base_rawcode);
  assert.equal(actual.display_name, expected.display_name);
  assert.deepEqual(actual.modifications, expected.modifications, `Roundtrip changed ${expected.rawcode}`);
}
for (const [category, member, levels] of [['unit', 'war3map.w3u', false], ['ability', 'war3map.w3a', true], ['buff', 'war3map.w3h', false]]) {
  const expectedRecords = definitions.filter(x => x.category === category);
  if (!expectedRecords.length) continue;
  const bytes = archiveMember(member), records = decodeObjects(bytes, levels);
  assert.equal(records.length, expectedRecords.length);
  assert.equal(createHash('sha256').update(bytes).digest('hex').toUpperCase(), after.archive_members.find(x => x.path === member).sha256);
  for (const expected of expectedRecords) {
    const record = records.find(x => x.rawcode === expected.rawcode);
    assert(record, expected.rawcode);
    assert.equal(record.base, expected.base_rawcode);
    assert.equal(record.mods.length, expected.modifications.length);
    for (const mod of expected.modifications) {
      const actual = record.mods.find(x => x.id === mod.id && x.level === mod.level && x.pointer === mod.pointer);
      assert(actual, `${expected.rawcode}.${mod.id} level ${mod.level}`);
      assert.equal(actual.type, { Int: 0, Real: 1, Unreal: 2, String: 3, Bool: 0 }[mod.type]);
      if (typeof mod.value === 'number') assert(Math.abs(actual.value - mod.value) < 0.00001);
      else assert.equal(actual.value, mod.value);
    }
  }
}
assert.equal(archiveMember('war3map.j').toString('utf8'), newScript);
for (const old of before.object_data) {
  const current = after.object_data.find(x => x.rawcode === old.rawcode);
  const filter = mods => mods.filter(x => old.rawcode !== 'H003' || !['uabi', 'uhab'].includes(x.id));
  assert.deepEqual(filter(current.modifications), filter(old.modifications), `Unrelated ${old.rawcode} field changed`);
}
const functions = source => new Map([...source.matchAll(/^function (\w+) takes[^]*?^endfunction/gm)].map(m => [m[1], m[0]]));
const oldFunctions = functions(oldScript), newFunctions = functions(newScript);
const permittedChanges = new Set(input.changed_functions);
for (const [name, body] of oldFunctions) {
  if (!permittedChanges.has(name)) assert.equal(newFunctions.get(name), body, `Unrelated JASS function changed: ${name}`);
}
assert(newScript.includes('call FogEnable(false)'));
assert(newScript.includes('call FogMaskEnable(false)'));
assert.equal(createHash('sha256').update(newScript).digest('hex').toUpperCase(), after.scripts.find(x => x.archive_path === 'war3map.j').sha256);
const report = { build_id: manifest.build_id, transaction_id: manifest.transaction_id, revision: manifest.revision,
  output_path: manifest.output_path, output_sha256: manifest.output_sha256, source_sha256: expectedSource,
  v20_sha256: expectedV20, all_checks_passed: true, runtime_status: 'unverified',
  object_rawcodes: after.object_data.map(x => x.rawcode), preserved_functions: oldFunctions.size - permittedChanges.size,
  allowed_changed_functions: [...permittedChanges], published_path: null };
if (publishFlag === '--publish') {
  const versions = readdirSync(testRoot).filter(x => /^v\d+$/.test(x)).map(x => Number(x.slice(1)));
  assert.equal(Math.max(...versions) + 1, 21, 'Next unused version must be v21');
  const folder = join(testRoot, 'v21');
  assert(!existsSync(folder), 'Never overwrite an existing version');
  mkdirSync(folder);
  const destination = join(folder, 'HeroTeamWars_v21.w3m');
  copyFileSync(output, destination, constants.COPYFILE_EXCL);
  assert.equal(sha(destination), manifest.output_sha256);
  assert.equal(sha(v20), expectedV20);
  assert.equal(sha(resolve(root, 'map/HeroTeamWars_M0_2Arena.w3m')), expectedSource);
  report.published_path = destination;
  writeFileSync(join(folder, 'publish-summary.json'), JSON.stringify(report, null, 2), { flag: 'wx' });
}
writeFileSync(resolve(root, input.verification_report), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
