import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';

// Produces reviewed typed MCP operations, never writes a map or transaction.
const [inspection, output] = process.argv.slice(2);
assert(inspection && output, 'Usage: node prepare-v25-controller.mjs <v24-inspect.json> <operations.json>');
const source = JSON.parse(readFileSync(inspection, 'utf8'));
assert.equal(source.source.sha256, '50639249A0E676B7EAC620492E11BC6C73BEB5775A003FA13505B9C89FC7EF74');
const catalog = JSON.parse(readFileSync(new URL('../map-engine/data/object-fields.json', import.meta.url), 'utf8'));
const ids = ['A2Q1', 'A2W1', 'A2E1', 'A2R1', 'A2S1', 'A2T1'];
// Order strings verified in pinned Units/HumanAbilityFunc.txt. These are
// separate Channel dispatch orders, not additional native effects on H003.
const orders = ['thunderbolt', 'blizzard', 'banish', 'flamestrike'];
const descriptions = [
  'Deals 80/120/160/200 magic damage to a living enemy unit.',
  'Deals 60/100/140/180 magic damage in a 300 radius and slows enemy movement by 25/30/35/40% for 3 seconds.',
  'Restores 100/150/200/250 mana and 50/100/150/200 life to a living same-team hero, including yourself.',
  'Deals 225/350/475 magic damage in a 325 radius and stuns enemies for 1.25/1.75/2.25 seconds.'
];
const rankDescriptions = [
  n => `Deals ${[80,120,160,200][n]} magic damage to a living enemy unit.`,
  n => `Deals ${[60,100,140,180][n]} magic damage in a 300 radius and slows enemy movement by ${[25,30,35,40][n]}% for 3 seconds.`,
  n => `Restores ${[100,150,200,250][n]} mana and ${[50,100,150,200][n]} life to a living same-team hero, including yourself.`,
  n => `Deals ${[225,350,475][n]} magic damage in a 325 radius and stuns enemies for ${[1.25,1.75,2.25][n]} seconds.`
];
const operations = ids.map((rawcode, index) => {
  const object = source.object_data.find(o => o.category === 'ability' && o.rawcode === rawcode);
  assert(object, `Missing ${rawcode}`);
  const levels = object.modifications.find(m => m.id === 'alev').value;
  let mods = structuredClone(object.modifications);
  // v24 fixed primary scopes, but left both helpers in old zero-based form.
  if (index >= 4) mods = mods.flatMap(old => {
    const field = catalog.fields.find(f => f.category === 'ability' && f.id === old.id);
    if (!field) return [old]; // preserve unknown records unchanged
    const siblings = object.modifications.filter(m => m.id === old.id);
    const nativeLevels = field.repeat === 0 ? [0] : siblings.length === 1
      ? Array.from({ length: levels }, (_, n) => n + 1) : [old.level + 1];
    return nativeLevels.map(level => ({field: field.name, value: old.value, level, pointer: field.data_pointer}));
  });
  function set(id, value, level = 0) {
    const field = catalog.fields.find(f => f.category === 'ability' && f.id === id);
    assert(field, `Missing catalog field ${id}`);
    mods = mods.filter(m => !((m.id === id || m.field === field.name) && m.level === level));
    mods.push({field: field.name, value, level, pointer: field.data_pointer});
  }
  set('areq', '');
  if (index < 4) {
    assert.equal(object.base_rawcode, 'ANcl');
    const key = 'QWER'[index];
    const name = object.modifications.find(m => m.id === 'anam').value;
    const icon = object.modifications.find(m => m.id === 'aart').value;
    set('ahky', key); set('arhk', key);
    set('abpx', index); set('abpy', 2);
    set('arpx', index); set('arpy', 0);
    set('arar', icon);
    set('aret', `Learn ${name} (|cffffcc00${key}|r)`);
    set('arut', descriptions[index] + (index === 3 ? '|n|nRequired hero levels: 6, 8, 10.' : '|n|nRequired hero levels: 1, 3, 5, 7.'));
    set('arlv', index === 3 ? 6 : 1); set('alsk', 2);
    for (let level = 1; level <= levels; level++) {
      set('Ncl6', orders[index], level);
      set('Ncl5', false, level);
      set('Ncl3', index === 1 || index === 3 ? ['visible', 'targetingImage'] : ['visible'], level);
      set('atar', index === 2 ? 'air,ground,friend,hero,alive' : 'air,ground,enemy,alive', level);
      set('atp1', `${name} (|cffffcc00${key}|r) - Level ${level}`, level);
      set('aub1', rankDescriptions[index](level - 1), level);
    }
  } else {
    set('aher', false); set('aite', false);
    for (let level = 1; level <= levels; level++) {
      set('amcs', 0, level); set('acdn', 0, level); set('aran', 1200, level);
      set('atar', 'air,ground,enemy,alive', level);
    }
  }
  return {operation_id: randomUUID(), type: 'update_object_definition',
    target: {id: object.id}, expected: object, value: {modifications: mods},
    rationale: index < 4 ? 'Distinct Channel order, QWER cast/learn buttons, target filters and explicit rank metadata.'
      : 'Correct every native helper rank and remove inherited hero, mana, cooldown and technology obstacles.'};
});
writeFileSync(output, JSON.stringify(operations, null, 2) + '\n');
