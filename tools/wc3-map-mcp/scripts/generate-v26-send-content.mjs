import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';

// Repository-root command: node tools/wc3-map-mcp/scripts/generate-v26-send-content.mjs [--check]
// Writes only the two generated files; never creates operations, transactions or maps.
// Gameplay modules own manifest wiring, personal-gold charging and preparation enforcement.
const args = process.argv.slice(2);
assert(args.length === 0 || (args.length === 1 && args[0] === '--check'), 'Usage: generate-v26-send-content.mjs [--check]');
const check = args.includes('--check');
const readJson = path => JSON.parse(readFileSync(new URL(path, import.meta.url), 'utf8'));
const catalog = readJson('./mcp/content/send-catalog.json');
const fields = readJson('../map-engine/data/object-fields.json').fields;
const { sends } = catalog;
assert.equal(catalog.schema_version, '1.0');
assert.equal(sends.length, 3, 'v26 requires exactly three standard sends');
const rawcode = value => assert(typeof value === 'string' && /^[A-Za-z0-9]{4}$/.test(value), `Invalid rawcode: ${value}`);
for (const [index, send] of sends.entries()) {
  assert.equal(send.kind, index + 1, 'Kinds must be contiguous and ordered from 1');
  rawcode(send.unit_type); rawcode(send.ability_id);
  for (const key of ['name', 'role', 'description', 'icon', 'base_order']) {
    assert(typeof send[key] === 'string' && /^[\x20-\x7e]+$/.test(send[key]), `Invalid ${key}`);
  }
  for (const key of ['cost', 'threat']) assert(Number.isInteger(send[key]) && send[key] > 0 && send[key] <= 2147483647, `Invalid ${key}`);
  assert(/^[A-Z]$/.test(send.hotkey), 'Expected a single uppercase hotkey');
  assert.equal(send.button_column, index);
  assert.equal(send.button_row, 0);
}
for (const key of ['unit_type', 'ability_id', 'base_order', 'hotkey']) assert.equal(new Set(sends.map(s => s[key])).size, sends.length, `Duplicate ${key}`);

// Resolve every named field, native scope, pointer and logical type from the supported catalog.
// These fields were searched then exactly looked up through the read-only MCP tools.
function modification(category, base, name, value) {
  const matches = fields.filter(f => f.category === category && f.name === name);
  assert.equal(matches.length, 1, `Unsupported or ambiguous field ${category}.${name}`);
  const field = matches[0];
  assert(!field.base_rawcodes.length || field.base_rawcodes.includes(base), `Invalid parent for ${name}`);
  if (field.value_kind === 'enum') assert(field.values.some(v => v.name === value), `Invalid enum ${name}`);
  else if (field.value_kind === 'flags') assert(Array.isArray(value) && value.every(v => field.values.some(f => f.name === v)), `Invalid flags ${name}`);
  else if (field.value_kind === 'rawcode_list') { assert(Array.isArray(value)); value.forEach(rawcode); }
  else if (field.type === 'String') assert.equal(typeof value, 'string', name);
  else if (field.type === 'Bool') assert.equal(typeof value, 'boolean', name);
  else { assert.equal(typeof value, 'number', name); assert(Number.isFinite(value)); if (field.type === 'Int') assert(Number.isInteger(value)); }
  if (field.scope === 'simple') return { field: field.name, value };
  assert.equal(field.scope, 'level');
  return { field: field.name, value, level: field.repeat === 0 ? 0 : 1, pointer: field.data_pointer };
}
const unit = (name, value) => modification('unit', 'hhou', name, value);
const ability = (name, value) => modification('ability', 'ANcl', name, value);
function definition(category, base, id, name, modifications) {
  return { category, object_kind: 'custom', base_rawcode: base, custom_rawcode: id, rawcode: id, display_name: name, modifications };
}
const campTooltip = 'Select War Camp. Prepare only: ' + sends.map(s => `${s.hotkey}: ${s.name} (${s.cost} personal gold, ${s.threat} threat)`).join('; ') + '. Sends spend your personal gold during preparation only.';
const objects = [definition('unit', 'hhou', 'n26C', 'War Camp', [
  unit('unitName', 'War Camp'), unit('normalAbilities', ['Avul', ...sends.map(s => s.ability_id)]),
  unit('heroAbilities', []), unit('castPoint', 0), unit('castBackswing', 0), unit('unitTooltip', campTooltip)
]), ...sends.map(s => definition('ability', 'ANcl', s.ability_id, `Send ${s.name}`, [
  ability('abilityName', `Send ${s.name}`), ability('heroAbility', false), ability('itemAbility', false),
  ability('maximumLevels', 1), ability('normalIcon', s.icon), ability('hotkey', s.hotkey),
  ability('buttonPositionX', s.button_column), ability('buttonPositionY', s.button_row), ability('abilityRequirements', ''),
  ability('manaCost', 0), ability('cooldown', 0), ability('channelFollowThroughTime', 0),
  ability('channelTargetType', 'instant'), ability('channelOptions', ['visible']),
  ability('channelDisableOtherAbilities', false), ability('channelBaseOrder', s.base_order), ability('allowedTargets', ''),
  ability('tooltip', `Send ${s.name} (|cffffcc00${s.hotkey}|r) - ${s.cost} personal gold`),
  ability('extendedTooltip', `${s.role} | ${s.threat} threat|n${s.description}|n|nCost: ${s.cost} personal gold.|nPrepare only: purchase this send during preparation.`)
]))];
// Caster/target art fields are absent from the current supported field catalog.
// Preserve inherited art rather than authoring unsupported raw modifications.
// The output is an array of create_object_definition VALUE records, without operation UUIDs.
const json = JSON.stringify(objects, null, 2) + '\n';
const stringLiteral = value => JSON.stringify(value);
const accessors = [
  ['UnitType', 'unit_type', 'integer', value => `'${value}'`],
  ['AbilityId', 'ability_id', 'integer', value => `'${value}'`],
  ['Name', 'name', 'string', stringLiteral], ['Role', 'role', 'string', stringLiteral],
  ['Description', 'description', 'string', stringLiteral],
  ['Cost', 'cost', 'integer', String], ['Threat', 'threat', 'integer', String]
];
const jass = [
  '// Generated from content/send-catalog.json by generate-v26-send-content.mjs. Do not edit.',
  '// Standard unit types retain stock combat mechanics; purchase enforcement belongs to the send system.',
  `function HTW_SendCatalog_Count takes nothing returns integer\n    return ${sends.length}\nendfunction`,
  ...accessors.map(([name, key, type, literal]) => [
    `function HTW_SendCatalog_${name} takes integer kind returns ${type}`,
    ...sends.flatMap((s, index) => [`    ${index === 0 ? 'if' : 'elseif'} kind == ${s.kind} then`, `        return ${literal(s[key])}`]),
    '    endif', `    return ${type === 'string' ? '""' : '0'}`, 'endfunction'
  ].join('\n'))
].join('\n\n') + '\n';
const outputs = [['./mcp/content/send-catalog.j', jass], ['./mcp/object-data/v26-send-objects.json', json]];
if (check) {
  const drift = outputs.filter(([path, expected]) => {
    try { return readFileSync(new URL(path, import.meta.url), 'utf8') !== expected; }
    catch (error) { if (error.code === 'ENOENT') return true; throw error; }
  }).map(([path]) => path);
  assert.equal(drift.length, 0, `Generated send content drift: ${drift.join(', ')}. Run the generator without --check.`);
} else {
  for (const [path, content] of outputs) writeFileSync(new URL(path, import.meta.url), content, 'utf8');
}
console.log(`v26 send content ${check ? 'checked' : 'generated'}: ${sends.length} sends, ${objects.length} typed object values.`);
