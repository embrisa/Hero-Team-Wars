import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import test from 'node:test';
import { verifyGeneratedDevStartup } from './v29-startup-check.mjs';

const variablesRoot = new URL('../scripts/mcp/variables/', import.meta.url);
const readJson = name => JSON.parse(readFileSync(new URL(name, variablesRoot), 'utf8'));
const manifestVariables = readdirSync(variablesRoot).filter(name => name.endsWith('.variable.json'))
  .flatMap(name => { const value = readJson(name); return Array.isArray(value) ? value : [value]; });

// Explicit generated-source environment entries that are profile/config
// declarations rather than variable-manifest entries. These are test input,
// not defaults supplied by the runtime interpreter.
const generatedEnvironment = [
  ...['ActivePlayerCount', 'TeamCount', 'ArenaCount', 'LivingTeamCount', 'RouteOffset', 'RouteDestinationTeam']
    .map(name => ({ name: `HTW_${name}`, type: 'integer', initial: 0 })),
  ...['TeamMemberA', 'TeamMemberB', 'TeamForce', 'TeamDestination', 'LivingTeamIds']
    .map(name => ({ name: `HTW_${name}`, type: 'integer', array: true })),
  { name: 'HTW_TeamStableId', type: 'string', array: true },
  { name: 'HTW_TeamArena', type: 'string', array: true },
  { name: 'HTW_TeamLiving', type: 'boolean', array: true },
  { name: 'HTW_RoutingLocked', type: 'boolean', initial: false },
  { name: 'HTW_ArenaRect', type: 'rect', array: true },
  { name: 'HTW_Event_round_start', type: 'real', initial: 0 },
  { name: 'HTW_Event_wave_resolved', type: 'real', initial: 0 },
  ...Array.from({ length: 10 }, (_, index) => ({ name: `HTW_Region_region_${index}`, type: 'region', initial: null })),
];

const explicitScalarInitials = Object.freeze({
  HTW_DevUIClick: null, HTW_DevUILoad: null, HTW_DevChatTrigger: null,
  HTW_HudLoadTrigger: null, HTW_HeroSelectionBuilding: null,
  HTW_HeroSelectionTrigger: null, HTW_HeroSelectionTimer: null,
  HTW_PreparationTimer: null, HTW_CombatTimer: null, HTW_SendTimer: null,
  HTW_CleanupGroup: null, HTW_ArenaRectA: null, HTW_ArenaRectB: null,
  HTW_StartingGold: 0, HTW_SendBudgetStart: 0, HTW_SendBudgetGrowth: 0,
  HTW_SendBudgetMaximum: 0, HTW_BaseFootmen: 0, HTW_FillerFootmen: 0,
  HTW_FillerRiflemen: 0,
});
const allGeneratedGlobals = [...manifestVariables, ...generatedEnvironment];
const uniqueGlobals = [...new Map(allGeneratedGlobals.map(global => [global.name, global])).values()];

function literal(type, value, name) {
  if (value === null) return 'null';
  if (type === 'boolean') return value ? 'true' : 'false';
  if (type === 'integer') return String(value);
  if (type === 'real') return `${value}.`;
  if (type === 'string') return JSON.stringify(value);
  assert.fail(`missing literal serializer for ${type} ${name}`);
}

function generatedSource() {
  const declarations = uniqueGlobals.map(global => {
    if (global.array) return `    ${global.type} array ${global.name}`;
    const value = Object.hasOwn(global, 'initial') && global.initial !== undefined
      ? global.initial : explicitScalarInitials[global.name];
    assert.notEqual(value, undefined, `test source needs an explicit scalar initializer for ${global.name}`);
    return `    ${global.type} ${global.name} = ${literal(global.type, value, global.name)}`;
  }).join('\n');
  return `globals\n${declarations}\nendglobals\n\nfunction HTW_MCP_InitializeVariables takes nothing returns nothing
    set HTW_DevChatTrigger = null
    set HTW_DevUILoad = null
    set HTW_HudLoadTrigger = null
    set HTW_Region_region_0 = CreateRegion()
    call RegionAddRect(HTW_Region_region_0, Rect(0., 0., 1., 1.))
endfunction\n`;
}

test('v29 startup checker rejects each v28 bare trigger scalar and accepts repaired literals', () => {
  const repaired = generatedSource();
  const targets = ['HTW_DevChatTrigger', 'HTW_DevUILoad', 'HTW_HudLoadTrigger'];
  for (const target of targets) {
    const oldV28Like = repaired.replace(new RegExp(`(\\btrigger\\s+${target})\\s*=\\s*null\\b`), '$1');
    assert.throws(() => verifyGeneratedDevStartup(oldV28Like), new RegExp(`generated scalar ${target} has no explicit initializer`));
  }
  const report = verifyGeneratedDevStartup(repaired);
  assert.equal(report.ok, true);
  assert.equal(report.mocked_native_execution, true);
  assert.equal(report.runtime_verified, false);
  assert.deepEqual(report.startup, {
    chat_registration_count: 4,
    dev_load_registration_count: 1,
    hud_load_registration_count: 1,
    solo_hud_root_visible: true,
    solo_dev_root_visible: true,
    chat_toggle_dispatches: 2,
    chat_gold_dispatches: 1,
    click_dispatches: 1,
    gold_native_mirrors: 2,
  });
});
