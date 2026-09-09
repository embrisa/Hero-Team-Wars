import assert from 'node:assert/strict';
import { fixture } from './v28-runtime-fixture.mjs';
import { rawcode } from './jass-runtime-harness.mjs';

// This checker deliberately consumes the generated war3map.j text. It does
// not reconstruct declarations from variable JSON or supply defaults for a
// missing declaration; a missing scalar initializer is the regression.
const id = '[A-Za-z_][A-Za-z0-9_]*';
const types = new Set(['nothing', 'integer', 'real', 'boolean', 'string', 'code',
  'unit', 'player', 'timer', 'trigger', 'group', 'force', 'rect', 'location',
  'region', 'effect', 'texttag', 'multiboard', 'multiboarditem', 'timerdialog',
  'boolexpr', 'framehandle']);
const handleTypes = new Set([...types].filter(type => !['nothing', 'integer', 'real', 'boolean', 'string'].includes(type)));
const requiredScalars = ['HTW_DevChatTrigger', 'HTW_DevUILoad', 'HTW_HudLoadTrigger'];

function lineNumber(source, offset) {
  return source.slice(0, offset).split(/\r?\n/).length;
}

function parseLiteral(type, literal, line) {
  const value = literal.trim();
  if (value === 'null') {
    assert.ok(handleTypes.has(type), `line ${line}: null is not a ${type} initializer`);
    return null;
  }
  if (type === 'boolean') {
    assert.ok(value === 'true' || value === 'false', `line ${line}: invalid boolean initializer ${value}`);
    return value === 'true';
  }
  if (type === 'integer') {
    const raw = /^'([\x20-\x26\x28-\x7e]{4})'$/.exec(value);
    const number = raw ? rawcode(raw[1]) : Number(value);
    assert.ok(raw || /^-?(?:0|[1-9]\d*)$/.test(value), `line ${line}: invalid integer initializer ${value}`);
    assert.ok(Number.isSafeInteger(number) && number >= -2147483648 && number <= 2147483647,
      `line ${line}: integer initializer out of range ${value}`);
    return number;
  }
  if (type === 'real') {
    assert.ok(/^-?(?:\d+\.\d*|\.\d+|\d+)$/.test(value), `line ${line}: invalid real initializer ${value}`);
    const number = Number(value);
    assert.ok(Number.isFinite(number), `line ${line}: non-finite real initializer ${value}`);
    return number;
  }
  if (type === 'string') {
    assert.ok(value.startsWith('"') && value.endsWith('"'), `line ${line}: invalid string initializer ${value}`);
    const parsed = JSON.parse(value);
    assert.equal(typeof parsed, 'string', `line ${line}: invalid string initializer ${value}`);
    return parsed;
  }
  assert.fail(`line ${line}: unsupported scalar initializer ${value} for ${type}`);
}

function parseGlobalDeclarations(source) {
  assert.equal(typeof source, 'string', 'Generated source must be a string');
  const block = /(?:^|\r?\n)globals\r?\n([\s\S]*?)(?:^|\r?\n)endglobals(?:\r?\n|$)/m.exec(source);
  assert(block, 'Generated source is missing a complete globals block');
  const globals = [];
  const seen = new Set();
  for (const [offset, rawLine] of block[1].split(/\r?\n/).entries()) {
    const line = rawLine.replace(/\s*\/\/.*$/, '').trim();
    if (!line) continue;
    const match = new RegExp(`^(?:(constant)\\s+)?(${id})(?:\\s+(array))?\\s+(${id})(?:\\s*=\\s*(.+))?$`).exec(line);
    assert(match, `line ${lineNumber(source, block.index + block[0].indexOf(rawLine))}: unsupported global declaration ${line}`);
    const [, constant, type, array, name, literal] = match;
    assert.ok(types.has(type) && type !== 'nothing', `line ${lineNumber(source, block.index + block[0].indexOf(rawLine))}: unsupported global type ${type}`);
    assert.ok(!seen.has(name), `duplicate generated global ${name}`);
    seen.add(name);
    const lineNo = lineNumber(source, block.index + block[0].indexOf(rawLine));
    if (array) {
      assert.equal(literal, undefined, `line ${lineNo}: JASS arrays must remain bare: ${name}`);
      globals.push({ name, type, array: true, ...(constant ? { constant: true } : {}) });
    } else {
      assert.notEqual(literal, undefined, `line ${lineNo}: generated scalar ${name} has no explicit initializer`);
      globals.push({ name, type, initial: parseLiteral(type, literal, lineNo), ...(constant ? { constant: true } : {}) });
    }
  }
  return globals;
}

function extractFunction(source, name) {
  const lines = source.replace(/^\uFEFF/, '').split(/\r?\n/);
  const header = new RegExp(`^function ${name} takes nothing returns nothing\\s*$`);
  const start = lines.findIndex(line => header.test(line.trim()));
  assert.ok(start >= 0, `Generated source is missing ${name}`);
  const end = lines.findIndex((line, index) => index > start && line.trim() === 'endfunction');
  assert.ok(end > start, `Generated source has an unterminated ${name}`);
  return lines.slice(start, end + 1).join('\n');
}

const count = (items, name) => items.filter(item => item === name).length;

/**
 * Validate and mock-execute the generated v29 startup path.
 *
 * The result is static/generated-source evidence plus an explicit mocked
 * native execution report. It is not Warcraft III runtime or multiplayer
 * evidence. `initializationSource` and `fixtureSource` are optional seams for
 * a build driver that already extracted the generated initializer or supplies
 * an alternate checked-in production fixture; the generated globals always
 * come from `source` itself.
 */
export function verifyGeneratedDevStartup(source, {
  initializationSource,
  initializationFunction = 'HTW_MCP_InitializeVariables',
  fixtureSource,
  fixtureOptions = {},
} = {}) {
  const globals = parseGlobalDeclarations(source);
  const byName = new Map(globals.map(global => [global.name, global]));
  for (const name of requiredScalars) {
    const global = byName.get(name);
    assert(global, `Generated source is missing required scalar ${name}`);
    assert.equal(global.array, undefined, `${name} must be scalar`);
    assert.equal(global.type, 'trigger', `${name} must be a trigger scalar`);
    assert.equal(global.initial, null, `${name} must have an explicit null initializer`);
  }
  const initializer = initializationSource ?? extractFunction(source, initializationFunction);
  const f = fixture({
    ...fixtureOptions,
    localPlayerId: 1,
    activeHumanIds: [1],
    prepare: true,
    manifestEvents: true,
    sourceOverrides: fixtureSource ?? fixtureOptions.sourceOverrides,
    globalDefinitions: globals,
    initializationSource: initializer,
    initializationFunction,
  });
  assert.equal(f.functionCalls[0], initializationFunction, 'Generated variable initializer was not executed first');

  f.call('HTW_Dev_Initialize');
  f.call('HTW_DevMenu_Initialize');
  f.call('HTW_Information_Display');
  f.call('HTW_DevMenu_Display');

  const chatTrigger = f.s.HTW_DevChatTrigger;
  const devLoadTrigger = f.s.HTW_DevUILoad;
  const hudLoadTrigger = f.s.HTW_HudLoadTrigger;
  assert.equal(chatTrigger.actions.length, 1, 'DEV chat trigger must have one action');
  assert.deepEqual(chatTrigger.events.map(event => [event.player.id, event.text, event.exact]),
    [0, 1, 2, 3].map(id => [id, '-dev', false]));
  assert.equal(devLoadTrigger.events.length, 1, 'DEV load trigger must be registered once');
  assert.equal(hudLoadTrigger.events.length, 1, 'HUD load trigger must be registered once');
  assert.equal(f.s.HTW_HudRoot[1].visible, true, 'solo HUD root must be visible for the local player');
  assert.equal(f.s.HTW_DevUIRoot[1].visible, true, 'solo DEV root must be visible for the local player');
  for (const viewer of [2, 3, 4]) {
    assert.equal(f.s.HTW_HudRoot[viewer].visible, false, `HUD root ${viewer} must stay local-only`);
    assert.equal(f.s.HTW_DevUIRoot[viewer].visible, false, `DEV root ${viewer} must stay local-only`);
  }

  const runBeforeMenu = count(f.functionCalls, 'HTW_Dev_Run');
  assert.equal(f.chat(1, '-dev'), 1, 'chat -dev must dispatch exactly once');
  assert.equal(f.s.HTW_DevMenuOpen[1], true, 'chat -dev must open the solo menu');
  assert.equal(count(f.functionCalls, 'HTW_Dev_Run') - runBeforeMenu, 1);
  assert.equal(f.chat(1, '-dev'), 1, 'second chat -dev must dispatch exactly once');
  assert.equal(f.s.HTW_DevMenuOpen[1], false, 'second chat -dev must close the solo menu');
  assert.equal(count(f.functionCalls, 'HTW_Dev_Run') - runBeforeMenu, 2);

  const goldBefore = f.s.HTW_PlayerGold[1];
  const mirrorBefore = f.nativeCalls.filter(call => call.name === 'SetPlayerState').length;
  const runBeforeGold = count(f.functionCalls, 'HTW_Dev_Run');
  assert.equal(f.chat(1, '-dev gold'), 1, 'chat gold must dispatch exactly once');
  assert.equal(f.s.HTW_PlayerGold[1], goldBefore + 1000, 'chat gold must apply once');
  assert.equal(f.players[0].gold, goldBefore + 1000, 'chat gold must mirror to the native player once');
  assert.equal(count(f.functionCalls, 'HTW_Dev_Run') - runBeforeGold, 1);
  assert.equal(f.nativeCalls.filter(call => call.name === 'SetPlayerState').length - mirrorBefore, 1);

  const button = f.s.HTW_DevUIButton[1 * 16 + 5];
  assert.ok(button, 'solo DEV gold button must be allocated');
  const clickGold = f.s.HTW_PlayerGold[1];
  const clickMirrors = f.nativeCalls.filter(call => call.name === 'SetPlayerState').length;
  const clickRuns = count(f.functionCalls, 'HTW_Dev_Run');
  assert.equal(f.click(1, button), 1, 'DEV button click must dispatch once');
  assert.equal(f.s.HTW_PlayerGold[1], clickGold + 1000, 'button gold must apply once');
  assert.equal(f.players[0].gold, clickGold + 1000, 'button gold must mirror once');
  assert.equal(count(f.functionCalls, 'HTW_Dev_Run') - clickRuns, 1);
  assert.equal(f.nativeCalls.filter(call => call.name === 'SetPlayerState').length - clickMirrors, 1);

  return {
    ok: true,
    mocked_native_execution: true,
    runtime_verified: false,
    globals: {
      total: globals.length,
      scalar: globals.filter(global => !global.array).length,
      arrays: globals.filter(global => global.array).length,
      explicit_scalar_initializers: globals.filter(global => !global.array && Object.hasOwn(global, 'initial')).length,
      required_null_triggers: requiredScalars,
    },
    initializer: { function: initializationFunction, executed: true },
    startup: {
      chat_registration_count: chatTrigger.events.length,
      dev_load_registration_count: devLoadTrigger.events.length,
      hud_load_registration_count: hudLoadTrigger.events.length,
      solo_hud_root_visible: true,
      solo_dev_root_visible: true,
      chat_toggle_dispatches: 2,
      chat_gold_dispatches: 1,
      click_dispatches: 1,
      gold_native_mirrors: 2,
    },
    limitations: ['Mocked JASS/native execution only; Warcraft III rendering, JASS thread scheduling, and multiplayer synchronization remain unverified.'],
  };
}

export { extractFunction, parseGlobalDeclarations };
