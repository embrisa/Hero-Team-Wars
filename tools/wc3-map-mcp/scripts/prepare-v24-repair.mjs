import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';

// Prepare an MCP operation batch; this script never edits a map or transaction.
// Input is the full wc3_inspect_map artifact for the published v23.
const [inspection, output] = process.argv.slice(2);
assert(inspection && output, 'Usage: node prepare-v24-repair.mjs <v23-inspect.json> <operations.json>');
const source = JSON.parse(readFileSync(inspection, 'utf8'));
assert.equal(source.source.sha256, '2A47A6E88EB2AD57F3CE66870E4997D9073D1621EA113BE7CE8813A3D1899E12');
const catalog = JSON.parse(readFileSync(new URL('../map-engine/data/object-fields.json', import.meta.url), 'utf8'));
const ids = ['A2Q1', 'A2W1', 'A2E1', 'A2R1'];
const operations = ids.map(rawcode => {
  const object = source.object_data.find(o => o.category === 'ability' && o.rawcode === rawcode);
  assert.equal(object.base_rawcode, 'ANcl');
  const levels = object.modifications.find(m => m.id === 'alev').value;
  const modifications = [];
  for (const old of object.modifications) {
    const field = catalog.fields.find(f => f.category === 'ability' && f.id === old.id);
    assert(field, `Uncataloged primary ability field ${old.id}`);
    const siblings = object.modifications.filter(m => m.id === old.id);
    const nativeLevels = field.repeat === 0 ? [0] : siblings.length === 1
      ? Array.from({ length: levels }, (_, i) => i + 1) : [old.level + 1];
    for (const level of nativeLevels) {
      let value = old.value;
      if (old.id === 'Ncl2') value = ['A2Q1', 'A2E1'].includes(rawcode) ? 'unit' : 'point';
      if (old.id === 'Ncl3') value = ['visible'];
      modifications.push({ field: field.name, value, level, pointer: field.data_pointer });
    }
  }
  return { operation_id: randomUUID(), type: 'update_object_definition',
    target: { id: object.id }, expected: object, value: { modifications },
    rationale: 'Regenerate native Bool integer encoding; use catalog types, 1-based ability levels, Channel data pointers, target enums and visible option.' };
});
writeFileSync(output, JSON.stringify(operations, null, 2) + '\n');
