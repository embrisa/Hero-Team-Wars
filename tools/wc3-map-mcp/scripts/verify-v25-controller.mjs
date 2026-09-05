import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { spawnSync } from 'node:child_process';

// Read-only verification of exact MCP records and independently decoded bytes.
const root = process.cwd();
const json = path => JSON.parse(readFileSync(resolve(root, path), 'utf8'));
const sha = path => createHash('sha256').update(readFileSync(path)).digest('hex').toUpperCase();
const [directory] = process.argv.slice(2);
assert(directory, 'Supply the v25 MCP evidence directory.');
const result = name => json(join(directory, name)).map(r => {
  assert(!r.error && !r.result.isError);
  assert(r.result.structuredContent.ok);
  return r.result.structuredContent.data;
});
const [report, inspection, script] = result('verify-results.json');
assert.equal(report.verified, true);
const manifest = report.build;
const after = json(inspection.artifact.path);
const before = json('tools/wc3-map-mcp/artifacts/reports/inspect-b9f25884-7a62-49cc-bd00-8f1bb4a735f9.json');
const output = resolve(root, manifest.output_path);
function archiveMember(name) {
  const quote = value => "'" + value.replaceAll("'", "''") + "'";
  const script = `Add-Type -Path ${quote(join(root, 'tools/wc3-map-mcp/map-engine/publish/War3Net.IO.Mpq.dll'))}
$v25Archive = [War3Net.IO.Mpq.MpqArchive]::Open(${quote(output)}, $true)
try {
  $v25Stream = $v25Archive.OpenFile(${quote(name)})
  try {
    $v25Bytes = [System.IO.MemoryStream]::new()
    try { $v25Stream.CopyTo($v25Bytes); [Convert]::ToBase64String($v25Bytes.ToArray()) }
    finally { $v25Bytes.Dispose() }
  } finally { $v25Stream.Dispose() }
} finally { $v25Archive.Dispose() }`;
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
assert.equal(sha(output), manifest.output_sha256);
assert.equal(after.source.sha256, manifest.output_sha256);
assert.equal(sha(resolve(root, 'map/HeroTeamWars_M0_2Arena.w3m')), '027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834');
assert.equal(before.source.sha256, '50639249A0E676B7EAC620492E11BC6C73BEB5775A003FA13505B9C89FC7EF74');
assert.equal(sha(before.source.path), before.source.sha256);
assert.deepEqual(after.parse_warnings, []);
assert.equal(manifest.archive_comparison.membership_equal, true);
assert.deepEqual(manifest.archive_comparison.content_changes.map(x => x.path).sort(), ['(attributes)', 'war3map.j', 'war3map.w3a']);
for (const field of ['players', 'forces', 'regions', 'placed_objects', 'imports']) assert.deepEqual(after[field], before[field]);
const ids = ['A2Q1', 'A2W1', 'A2E1', 'A2R1', 'A2S1', 'A2T1'];
for (const object of before.object_data.filter(x => !ids.includes(x.rawcode))) {
  assert.deepEqual(after.object_data.find(x => x.id === object.id), object);
}
const bytes = archiveMember('war3map.w3a');
const records = decodeObjects(bytes, true);
assert.equal(records.length, 6);
// Compare every emitted field against the reopened canonical record, including
// native Bool-as-Int encoding and full consumption of the ability member.
for (const record of records) {
  const expected = after.object_data.find(x => x.rawcode === record.rawcode);
  assert.equal(record.mods.length, expected.modifications.length);
  for (const mod of expected.modifications) {
    const actual = record.mods.find(x => x.id === mod.id && x.level === mod.level && x.pointer === mod.pointer);
    assert(actual);
    assert.equal(actual.type, {Int: 0, Real: 1, Unreal: 2, String: 3, Bool: 0}[mod.type]);
    if (mod.type === 'Bool') assert.equal(actual.value, Number(mod.value));
    else if (typeof mod.value === 'number') assert(Math.abs(actual.value - mod.value) < 0.00001);
    else assert.equal(actual.value, mod.value);
  }
}
const orders = ['thunderbolt', 'blizzard', 'banish', 'flamestrike'];
for (let i = 0; i < records.length; i++) {
  const record = records.find(x => x.rawcode === ids[i]);
  const get = (id, level = 0) => {
    const matches = record.mods.filter(m => m.id === id && m.level === level);
    assert.equal(matches.length, 1, `${record.rawcode}.${id}.${level}`);
    return matches[0];
  };
  const levels = i === 3 || i === 5 ? 3 : 4;
  assert.equal(get('alev').value, levels);
  assert.equal(get('areq').value, '');
  if (i < 4) {
    assert.equal(get('ahky').value, 'QWER'[i]);
    assert.equal(get('arhk').value, 'QWER'[i]);
    assert.equal(get('abpx').value, i); assert.equal(get('abpy').value, 2);
    assert.equal(get('arpx').value, i); assert.equal(get('arpy').value, 0);
    assert.equal(get('arar').value, get('aart').value);
    assert.equal(get('arlv').value, i === 3 ? 6 : 1);
    assert.equal(get('alsk').value, 2);
    for (let rank = 1; rank <= levels; rank++) {
      assert.equal(get('Ncl6', rank).value, orders[i]);
      assert.equal(get('Ncl6', rank).pointer, 6);
      assert.equal(get('Ncl5', rank).value, 0);
      assert.equal(get('Ncl5', rank).pointer, 5);
      assert.equal(get('Ncl2', rank).value, i === 0 || i === 2 ? 1 : 2);
      assert.equal(get('Ncl1', rank).value, 0);
      assert(!/unit |point /.test(get('atar', rank).value));
      for (const field of ['amcs', 'acdn', 'aran']) {
        const old = before.object_data.find(x => x.rawcode === ids[i]).modifications.find(x => x.id === field && x.level === rank);
        assert.equal(get(field, rank).value, old.value);
      }
    }
  } else {
    assert.equal(get('aher').value, 0);
    for (let rank = 1; rank <= levels; rank++) {
      assert.equal(get('amcs', rank).value, 0); assert.equal(get('acdn', rank).value, 0);
      assert.equal(get('aran', rank).value, 1200);
      for (const duration of ['adur', 'ahdu']) assert.equal(get(duration, rank).value, i === 4 ? 3 : [1.25, 1.75, 2.25][rank - 1]);
      if (i === 4) {
        assert(Math.abs(get('Slo1', rank).value - [0.25, 0.3, 0.35, 0.4][rank - 1]) < 0.00001);
        assert.equal(get('Slo1', rank).pointer, 1);
        assert.equal(get('Slo2', rank).value, 0); assert.equal(get('Slo2', rank).pointer, 2);
      } else { assert.equal(get('Htb1', rank).value, 0); assert.equal(get('Htb1', rank).pointer, 1); }
    }
  }
}
const original = readFileSync(join(directory, 'source.j'), 'utf8');
const functions = source => new Map([...source.matchAll(/function (\w+) takes[\s\S]*?endfunction/g)].map(x => [x[1], x[0]]));
const oldFunctions = functions(original), newFunctions = functions(script.source);
assert.deepEqual([...newFunctions.keys()], [...oldFunctions.keys()]);
const changedFunctions = [...oldFunctions.keys()].filter(name => oldFunctions.get(name) !== newFunctions.get(name));
assert.deepEqual(changedFunctions, ['HTW_Abilities_CastNativeSlow', 'HTW_Abilities_CastNativeStun']);
for (const name of changedFunctions) {
  const body = newFunctions.get(name);
  assert(!body.includes('TriggerSleepAction'));
  assert(body.includes("call UnitApplyTimedLife(dummy, 'BTLF', 2.)"));
  assert(body.includes('GetUnitX(target), GetUnitY(target)'));
  assert(body.includes('if not IssueTargetOrder'));
}
const evidence = {build_id: manifest.build_id, output: manifest.output_path, sha256: manifest.output_sha256,
  ability_bytes: bytes.length, native_records: records.length, changed_functions: changedFunctions,
  static_checks: 'passed', runtime_verified: false};
writeFileSync(join(directory, 'verification.json'), JSON.stringify(evidence, null, 2) + '\n');
console.log(JSON.stringify(evidence, null, 2));
