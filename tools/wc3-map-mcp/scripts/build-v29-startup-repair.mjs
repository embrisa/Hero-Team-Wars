import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { constants, copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createHash, randomUUID } from 'node:crypto';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyGeneratedDevStartup } from '../tests/v29-startup-check.mjs';

// Supported MCP operations only. Stage for review, then build the exact staged
// revision. Publication/manual evidence is separate and never fabricated here.
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const [mode, evidenceDirectory] = process.argv.slice(2);
assert(['stage', 'build', 'rebuild'].includes(mode) && evidenceDirectory, 'Usage: build-v29-startup-repair.mjs stage|build|rebuild <project-relative-evidence-directory>');
const evidence = resolve(root, evidenceDirectory);
assert(evidence.startsWith(join(root, 'tools/wc3-map-mcp/artifacts') + '\\') || evidence.startsWith(join(root, 'tools/wc3-map-mcp/artifacts') + '/'), 'Evidence must remain under MCP artifacts');
const rel = path => relative(root, path).replaceAll('\\', '/');
const json = path => JSON.parse(readFileSync(path, 'utf8'));
const save = (name, value) => writeFileSync(join(evidence, name), JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
const sha = path => createHash('sha256').update(readFileSync(path)).digest('hex').toUpperCase();
const golden = resolve(root, 'map/HeroTeamWars_M0_2Arena.w3m');
const goldenHash = '027AA23AAB7D94EDD8CD09EFBE799DBCFCDC5B2775FF0B36A07CD6BB19CEC834';
const baselineHash = 'E845EBCF3113B77AFC266900D095FFA6E169B21BE90D0FAE4B0348428154C02C';
const baseline = resolve(root, 'builds/mcp/hero-team-wars/4ee28f78-6adc-467a-9f22-a054a792c6cb/HeroTeamWars_v28-dev-menu_4ee28f78-6adc-467a-9f22-a054a792c6cb.w3m');
assert.equal(sha(golden), goldenHash);
assert.equal(sha(baseline), baselineHash);
function declarations(source, requireInitializers = false) {
  const block = source.match(/^globals\r?\n([\s\S]*?)^endglobals/m);
  assert(block, 'Expected complete generated globals');
  return block[1].split(/\r?\n/).map(line => line.trim()).filter(line => line && !line.startsWith('//')).map(line => {
    const match = /^(\w+)( array)? (\w+)(?: = (.+))?$/.exec(line);
    assert(match, `Unsupported global declaration: ${line}`);
    const [, type, array, name, value] = match;
    if (requireInitializers) {
      const expected = array ? undefined : ({ integer: '0', real: '0.', boolean: 'false', string: '""' }[type] ?? 'null');
      assert.equal(value, expected, `Unsafe or missing initial value: ${name}`);
    }
    return `${type}${array ?? ''} ${name}`;
  }).sort();
}
const configFile = join(evidence, 'config.json');
if (mode === 'stage') {
  assert(!existsSync(evidence), 'Refusing to reuse evidence directory');
  mkdirSync(evidence, { recursive: true });
  const config = json(resolve(root, 'tools/wc3-map-mcp/config/wc3-map-mcp.local.json'));
  config.engine.executable = resolve(root, 'tools/wc3-map-mcp/map-engine/publish/Wc3MapEngine.Cli.exe');
  const project = config.projects['hero-team-wars'];
  project.root = root;
  copyFileSync(baseline, join(evidence, 'baseline-v28.w3m'), constants.COPYFILE_EXCL);
  project.source_maps = [rel(join(evidence, 'baseline-v28.w3m'))];
  project.baseline_sha256 = baselineHash;
  project.gameplay_manifest = 'tools/wc3-map-mcp/scripts/mcp/manifest.json';
  project.profile = 'mvp_2arena';
  save('config.json', config);
}
const config = json(configFile);
const projectId = 'hero-team-wars';
const sourceMap = config.projects[projectId].source_maps[0];
const child = spawn(process.execPath, [resolve(root, 'tools/wc3-map-mcp/mcp-server/dist/index.js')], {
  cwd: root, windowsHide: true, env: { ...process.env, WC3_MAP_MCP_CONFIG: configFile }, stdio: ['pipe', 'pipe', 'pipe'],
});
let buffer = '', nextId = 0, stderr = '';
const pending = new Map();
child.stderr.on('data', chunk => { stderr = (stderr + chunk).slice(-8000); });
const rejectAll = error => { for (const request of pending.values()) { clearTimeout(request.timeout); request.reject(error); } pending.clear(); };
child.on('error', rejectAll);
child.on('exit', code => rejectAll(new Error(`MCP exited ${code}: ${stderr}`)));
child.stdout.on('data', chunk => {
  buffer += chunk;
  let end;
  while ((end = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, end); buffer = buffer.slice(end + 1);
    if (!line.trim()) continue;
    try {
      const response = JSON.parse(line), request = pending.get(response.id);
      if (request) { clearTimeout(request.timeout); pending.delete(response.id); request.resolve(response); }
    } catch (error) { rejectAll(error); child.kill(); }
  }
});
function request(method, params) {
  return new Promise((resolveRequest, reject) => {
    const id = ++nextId;
    const timeout = setTimeout(() => { pending.delete(id); reject(new Error(`MCP request ${method} timed out`)); child.kill(); }, 150000);
    pending.set(id, { resolve: resolveRequest, reject, timeout });
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  });
}
let sequence = 0;
const attempt = `${mode}-${randomUUID()}`;
async function call(name, args) {
  const response = await request('tools/call', { name, arguments: args });
  save(`${attempt}-${String(++sequence).padStart(2, '0')}-${name}.json`, response);
  if (response.error || response.result?.isError) {
    const error = response.error ?? response.result.structuredContent.error;
    throw new Error(`${name}: ${error.code}: ${error.message}; full evidence saved to ${rel(evidence)}`);
  }
  assert.equal(response.result.structuredContent.ok, true);
  console.log(`${name}: passed`);
  return response.result.structuredContent.data;
}
try {
  await request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'v29-startup-repair', version: '1' } });
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  await call('wc3_project_status', { project_id: projectId });
  if (mode === 'stage') {
    const inspect = await call('wc3_inspect_map', { project_id: projectId, map: sourceMap });
    const before = json(resolve(root, inspect.artifact.path));
    assert.equal(before.source.sha256, baselineHash);
    const oldScript = await call('wc3_get_script_source', { project_id: projectId, map: sourceMap });
    const tx = await call('wc3_begin_transaction', { project_id: projectId, map: sourceMap, expected_source_hash: baselineHash, label: 'v29 initialized JASS startup' });
    const initial = json(resolve(root, tx.paths.canonical));
    const script = initial.scripts.find(s => s.archive_path === 'war3map.j');
    const validation = await call('jass_validate_source', { source: script.source });
    assert.equal(validation.valid, true);
    const functions = source => new Map([...source.matchAll(/^function (\w+) takes[\s\S]*?^endfunction/gm)].map(m => [m[1], m[0].replace(/\/\/[^\n]*/g, '').replace(/\s+/g, ' ').trim()]));
    const oldFunctions = functions(oldScript.source), newFunctions = functions(script.source);
    assert.deepEqual(declarations(script.source, true), declarations(oldScript.source), 'Changed global names/types/arrays');
    assert.deepEqual([...newFunctions.keys()].sort(), [...oldFunctions.keys()].sort(), 'Changed function inventory');
    const changed = [...oldFunctions].filter(([name, body]) => newFunctions.get(name) !== body).map(([name]) => name);
    const allowedChanges = new Set(['HTW_Dev_Run', 'HTW_Dev_Initialize']);
    for (const [name, body] of oldFunctions) {
      if (!allowedChanges.has(name)) assert.equal(newFunctions.get(name), body, `Unintended baseline change: ${name}`);
    }
    save('staged-startup-check.json', verifyGeneratedDevStartup(script.source));
    const operations = [];
    operations.push({ operation_id: randomUUID(), type: 'set_script_source', target: { archive_path: 'war3map.j' }, expected: script.source_sha256 ?? script.sha256, value: { language: 'jass', source: script.source, source_strategy: 'composed' }, rationale: 'Initialize generated scalar globals before first use and add dev startup/menu acknowledgements; preserve v28 gameplay and all object data.' });
    await call('wc3_apply_operations', { project_id: projectId, transaction_id: tx.transaction_id, expected_revision: 0, operations, dry_run: true });
    save('operations.json', operations);
    save('state.json', { transaction_id: tx.transaction_id, source_map: sourceMap, source_sha256: baselineHash, baseline_inspection: inspect.artifact.path, initial_source_sha256: script.sha256, changed_functions: changed });
    console.log(JSON.stringify({ transaction_id: tx.transaction_id, changed_functions: changed, evidence: rel(evidence) }, null, 2));
  } else {
    const state = json(join(evidence, 'state.json'));
    if (mode === 'build') await call('wc3_apply_operations', { project_id: projectId, transaction_id: state.transaction_id, expected_revision: 0, operations: json(join(evidence, 'operations.json')) });
    await call('wc3_transaction_diff', { project_id: projectId, transaction_id: state.transaction_id });
    const validation = await call('wc3_validate_transaction', { project_id: projectId, transaction_id: state.transaction_id, revision: 1 });
    assert.equal(validation.buildable, true);
    const result = await call('wc3_build_map', { project_id: projectId, transaction_id: state.transaction_id, revision: 1, expected_source_hash: baselineHash, profile: 'debug', label: 'v29-startup-repair' });
    const build = result.build;
    const report = await call('wc3_build_report', { project_id: projectId, build_id: build.build_id });
    assert.equal(report.verified, true);
    const inspect = await call('wc3_inspect_map', { project_id: projectId, map: build.output_path });
    const after = json(resolve(root, inspect.artifact.path)), before = json(resolve(root, state.baseline_inspection));
    assert.deepEqual(build.reinspection.semantic_differences, []);
    assert.deepEqual(build.archive_comparison.unexpected_content_changes, []);
    assert.deepEqual(build.archive_comparison.content_changes.map(c => c.path).sort(), ['(attributes)', 'war3map.j']);
    for (const key of ['players', 'forces', 'regions', 'placed_objects', 'imports']) assert.deepEqual(after[key], before[key], `Unintended ${key} change`);
    for (const object of before.object_data) assert.deepEqual(after.object_data.find(o => o.id === object.id), object, `Changed v28 object ${object.id}`);
    assert.equal(after.object_data.length, before.object_data.length);
    const reopened = await call('wc3_get_script_source', { project_id: projectId, map: build.output_path });
    assert(reopened?.source, 'Reinspection must expose exact generated source');
    declarations(reopened.source, true);
    save(`${attempt}-reopened-startup-check.json`, verifyGeneratedDevStartup(reopened.source));
    assert.equal(sha(resolve(root, build.output_path)), build.output_sha256);
    save('handoff.json', { ...build, baseline_inspection: state.baseline_inspection, output_inspection: inspect.artifact.path, runtime_verified: false });
    console.log(JSON.stringify({ build_id: build.build_id, output: build.output_path, sha256: build.output_sha256, runtime_verified: false }, null, 2));
  }
} finally {
  child.kill();
  assert.equal(sha(golden), goldenHash);
  assert.equal(sha(baseline), baselineHash);
  assert.equal(sha(resolve(root, sourceMap)), baselineHash);
}
