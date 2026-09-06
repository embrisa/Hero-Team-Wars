import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import test from 'node:test';
import { createJassRuntime, rawcode } from './jass-runtime-harness.mjs';

// Evidence: repository JASS bodies executed with mocked natives ONLY.
// This suite does not verify Warcraft runtime, object spell wiring, or multiplayer.
const root = new URL('../scripts/mcp/', import.meta.url);
const read = path => readFileSync(new URL(path, root), 'utf8').replace(/^\uFEFF/, '');
const definition = (name, type, initial, array = false) => ({ name, type, initial, array });
const native = (type, arity, fn) => ({ type, arity, fn });

test('harness: actual subset control flow, arrays, callbacks, rawcodes and strings', () => {
  const source = `
function Mark takes nothing returns nothing
    set Flag = true
endfunction
function Flow takes integer limit returns integer
    local integer i = 0
    local integer sum = 0
    loop
        exitwhen i >= limit
        if i == 0 then
            set Values[i] = 2
        elseif i == 1 then
            set Values[i] = 3
        else
            set Values[i] = 4
        endif
        set sum = sum + Values[i]
        set i = i + 1
    endloop
    if not Flag and (sum == 9 or limit < 0) then
        call Schedule(function Mark)
        call Message("literal // and or not " + "quoted: \\\"ok\\\"")
        set TypeId = 'hfoo'
        return sum + 7 / 2
    endif
    return -1
endfunction`;
  let callback;
  let message;
  const runtime = createJassRuntime({ sources: [{ path: 'control-flow-self-test.j', source }],
    globals: [definition('Flag', 'boolean'), definition('Values', 'integer', undefined, true), definition('TypeId', 'integer')],
    natives: { Schedule: native('nothing', 1, fn => { callback = fn; }), Message: native('nothing', 1, text => { message = text; }) } });
  assert.equal(runtime.call('Flow', 3), 12);
  assert.equal(runtime.state.Values[2], 4);
  assert.equal(runtime.state.Values[500], 0);
  assert.equal(runtime.state.TypeId, rawcode('hfoo'));
  assert.equal(message, 'literal // and or not quoted: "ok"');
  runtime.invoke(callback);
  assert.equal(runtime.call('Flow', 3), -1);
});

test('harness: unsupported/unbound syntax fails closed and execution is bounded', () => {
  const build = (body, extra = {}) => createJassRuntime({ sources: [{ path: 'fail-closed-self-test.j',
    source: `function Probe takes nothing returns nothing\n${body}\nendfunction` }], ...extra });
  for (const body of ['set missing = 1', 'call UnknownNative()', 'call eval("bad")',
    'debug call BJDebugMsg("bad")', 'set x = 1 % 2', 'exitwhen true',
    'if true then\nreturn', 'loop\nendif', 'return 1', 'call Probe().constructor()', 'call Probe() == Probe()']) {
    assert.throws(() => build(body), undefined, body);
  }
  assert.throws(() => build('loop\nendloop', { maxSteps: 20 }).call('Probe'), /step limit/);
  assert.throws(() => build('loop\nendloop', { maxSteps: Number.MAX_SAFE_INTEGER, timeout: 10 }).call('Probe'), /timed out/);
  assert.throws(() => build('call Probe()', { maxSteps: 20 }).call('Probe'), /step limit/);
  assert.throws(() => build('', { natives: { HTW_Fake: native('nothing', 0, () => {}) } }), /Cannot mock repository/);
  const globals = [definition('Value', 'integer')];
  for (const body of ['set Value = "wrong type"', 'set Value = true + 1', 'if 1 then\nreturn\nendif', 'call Probe(1)']) {
    assert.throws(() => build(body, { globals }), undefined, body);
  }
  assert.throws(() => build('set Value = 2147483647 + 1', { globals }).call('Probe'), /integer overflow/);
  assert.throws(() => build('set Value = 7 / 0', { globals }).call('Probe'), /division by zero/);
});

function fixture() {
  // Read fresh on every fixture. A missing production module is a test failure.
  const modules = ['core/state.j', 'core/debug.j', 'core/events.j', 'config/tuning.j',
    'config/teams.j', 'content/send-catalog.j', 'content/send-units.j', 'content/base-waves.j', 'content/heroes.j',
    'systems/economy.j', 'systems/sending.j', 'systems/waves.j', 'systems/phases.j',
    'systems/routing.j', 'systems/elimination.j', 'systems/lives.j', 'systems/heroes.j',
    'systems/information.j', 'systems/wave-plan.j', 'systems/hero-selection.j'];
  const sources = modules.map(path => ({ path, source: read(path),
    // The composer-generated profile setup is outside this source harness.
    // Use only the real lookup body from this module; fixture profile data below
    // supplies the same environment boundary as generated global declarations.
    ...(path === 'config/teams.j' ? { only: ['HTW_Teams_FindByPlayer'] } : {}),
    ...(path === 'systems/hero-selection.j' ? { only: ['HTW_HeroSelection_AllPlayersReady', 'HTW_HeroSelection_Complete'] } : {}) }));
  const variables = readdirSync(new URL('variables/', root)).filter(path => path.endsWith('.variable.json'))
    .flatMap(path => JSON.parse(read(`variables/${path}`)));
  const globals = [...variables];
  const add = (name, type, initial, array = false) => {
    if (!globals.some(value => value.name === name)) globals.push(definition(name, type, initial, array));
  };
  // Explicit composer/native environment. Never infer undeclared gameplay globals.
  for (const name of ['ActivePlayerCount', 'TeamCount', 'ArenaCount', 'LivingTeamCount', 'RouteOffset', 'RouteDestinationTeam']) add(`HTW_${name}`, 'integer');
  for (const name of ['TeamMemberA', 'TeamMemberB', 'TeamDestination', 'LivingTeamIds']) add(`HTW_${name}`, 'integer', undefined, true);
  add('HTW_TeamLiving', 'boolean', undefined, true);
  add('HTW_RoutingLocked', 'boolean');
  add('HTW_ArenaRect', 'rect', undefined, true);
  for (const name of ['round_start', 'wave_resolved']) add(`HTW_Event_${name}`, 'real');
  for (const [name, value] of Object.entries({ PLAYER_NEUTRAL_AGGRESSIVE: 12, MAP_CONTROL_USER: 1,
    MAP_CONTROL_COMPUTER: 2, PLAYER_SLOT_STATE_PLAYING: 1, PLAYER_STATE_RESOURCE_GOLD: 1,
    EVENT_PLAYER_UNIT_SPELL_EFFECT: 1, UNIT_STATE_LIFE: 0, UNIT_TYPE_DEAD: 1, PLAYER_STATE_GIVES_BOUNTY: 2 })) {
    add(name, 'integer', value);
    globals.find(global => global.name === name).constant = true;
  }
  const players = Array.from({ length: 25 }, (_, id) => ({ id, controller: 1, slot: 1, gold: 0 }));
  const units = [];
  const timers = [];
  const boards = [];
  const messages = [];
  let enumUnit = null;
  let triggerUnit = null;
  let abilityId = 0;
  let failedCreates = 0;
  let handleId = 0;
  const handle = (type, values = {}) => ({ handleId: ++handleId, handleType: type, ...values });
  const makeUnit = (owner, unitType, x, y, facing) => {
    if (failedCreates > 0) { failedCreates--; return null; }
    const unit = handle('unit', { owner, unitType, x, y, facing, life: 100, removed: false, orders: [] });
    units.push(unit);
    return unit;
  };
  const natives = {
    Player: native('player', 1, id => { assert.ok(players[id], `unknown native player ${id}`); return players[id]; }),
    GetPlayerId: native('integer', 1, player => player.id),
    GetPlayerController: native('integer', 1, player => player.controller),
    GetPlayerSlotState: native('integer', 1, player => player.slot),
    GetOwningPlayer: native('player', 1, unit => unit.owner),
    GetTriggerUnit: native('unit', 0, () => triggerUnit),
    GetSpellAbilityId: native('integer', 0, () => abilityId),
    SetPlayerState: native('nothing', 3, (player, state, value) => {
      if (state === 1) player.gold = value;
      else { assert.equal(state, 2); player.bounty = value; }
    }),
    GetPlayerState: native('integer', 2, (player, state) => { assert.equal(state, 1); return player.gold; }),
    I2R: native('real', 1, value => value), I2S: native('string', 1, String),
    R2I: native('integer', 1, Math.trunc), R2S: native('string', 1, String),
    SubString: native('string', 3, (value, start, end) => value.substring(start, end)),
    StringLength: native('integer', 1, value => value.length),
    ModuloInteger: native('integer', 2, (a, b) => ((a % b) + b) % b),
    GetRectCenterX: native('real', 1, rect => (rect.min_x + rect.max_x) / 2),
    GetRectCenterY: native('real', 1, rect => (rect.min_y + rect.max_y) / 2),
    CreateUnit: native('unit', 5, makeUnit),
    CreateGroup: native('group', 0, () => handle('group', { units: new Set(), destroyed: false })),
    GroupAddUnit: native('nothing', 2, (group, unit) => { assert.ok(unit); assert.equal(group.destroyed, false); group.units.add(unit); }),
    GroupRemoveUnit: native('nothing', 2, (group, unit) => group.units.delete(unit)),
    FirstOfGroup: native('unit', 1, group => [...group.units].find(unit => !unit.removed) ?? null),
    ForGroup: native('nothing', 2, (group, callback) => {
      const previous = enumUnit;
      try { for (const unit of [...group.units]) { enumUnit = unit; callback(); } }
      finally { enumUnit = previous; }
    }),
    GetEnumUnit: native('unit', 0, () => enumUnit),
    DestroyGroup: native('nothing', 1, group => { group.destroyed = true; group.units.clear(); }),
    GroupClear: native('nothing', 1, group => group.units.clear()),
    RemoveUnit: native('nothing', 1, unit => { assert.ok(unit); unit.removed = true; }),
    IssuePointOrder: native('boolean', 4, (unit, order, x, y) => { unit.orders.push({ order, x, y }); return true; }),
    GetUnitState: native('real', 2, (unit, state) => { assert.equal(state, 0); return unit.life; }),
    GetWidgetLife: native('real', 1, unit => unit.life),
    GetHeroLevel: native('integer', 1, unit => unit.level ?? 1),
    GetUnitTypeId: native('integer', 1, unit => unit?.removed ? 0 : unit?.unitType ?? 0),
    IsUnitType: native('boolean', 2, (unit, type) => { assert.equal(type, 1); return unit.life <= 0 || unit.removed; }),
    ReviveHero: native('boolean', 4, (unit, x, y, eyeCandy) => { assert.ok(unit); unit.life = 100; unit.x = x; unit.y = y; return true; }),
    CreateTimer: native('timer', 0, () => { const timer = handle('timer', { active: false, remaining: 0 }); timers.push(timer); return timer; }),
    TimerStart: native('nothing', 4, (timer, duration, periodic, callback) => {
      assert.equal(typeof callback, 'function'); assert.ok(duration > 0);
      Object.assign(timer, { active: true, duration, remaining: duration, periodic, callback });
    }),
    PauseTimer: native('nothing', 1, timer => { timer.active = false; }),
    TimerGetRemaining: native('real', 1, timer => timer.remaining),
    DestroyTimer: native('nothing', 1, timer => { timer.active = false; timer.destroyed = true; }),
    GetPlayersAll: native('force', 0, () => players),
    DisplayTextToForce: native('nothing', 2, (force, message) => messages.push({ audience: force, message })),
    DisplayTimedTextToPlayer: native('nothing', 5, (player, x, y, seconds, message) => messages.push({ audience: player, message })),
    DisplayTextToPlayer: native('nothing', 4, (player, x, y, message) => messages.push({ audience: player, message })),
    GetLocalPlayer: native('player', 0, () => players[0]),
    GetPlayerName: native('string', 1, player => `Player ${player.id + 1}`),
    CreateTrigger: native('trigger', 0, () => handle('trigger', { events: [], actions: [] })),
    TriggerRegisterPlayerUnitEvent: native('nothing', 4, (trigger, player, event, filter) => trigger.events.push({ player, event, filter })),
    TriggerAddAction: native('nothing', 2, (trigger, callback) => trigger.actions.push(callback)),
    DisableTrigger: native('nothing', 1, trigger => { trigger.disabled = true; }),
    DestroyTrigger: native('nothing', 1, trigger => { trigger.destroyed = true; }),
    CreateMultiboard: native('multiboard', 0, () => {
      const board = handle('multiboard', { rows: [], rowCount: 0, columnCount: 0, visible: false, items: [] });
      boards.push(board); return board;
    }),
    MultiboardSetTitleText: native('nothing', 2, (board, title) => { board.title = title; }),
    MultiboardSetColumnCount: native('nothing', 2, (board, count) => { board.columnCount = count; }),
    MultiboardSetRowCount: native('nothing', 2, (board, count) => { board.rowCount = count; }),
    MultiboardGetItem: native('multiboarditem', 3, (board, row, column) => {
      assert.ok(row >= 0 && row < board.rowCount); assert.ok(column >= 0 && column < board.columnCount);
      const item = handle('multiboarditem', { board, row, column, released: false });
      board.items.push(item); return item;
    }),
    MultiboardSetItemValue: native('nothing', 2, (item, value) => { assert.equal(item.released, false); item.board.rows[item.row] = value; }),
    MultiboardSetItemStyle: native('nothing', 3, (item, text, icon) => { item.style = { text, icon }; }),
    MultiboardSetItemWidth: native('nothing', 2, (item, width) => { item.width = width; }),
    MultiboardReleaseItem: native('nothing', 1, item => { assert.equal(item.released, false); item.released = true; }),
    MultiboardDisplay: native('nothing', 2, (board, visible) => { board.visible = visible; }),
  };
  const runtime = createJassRuntime({ sources, globals, natives });
  const s = runtime.state;
  const profile = JSON.parse(read('manifest.json')).profiles.mvp_2arena;
  s.HTW_ActivePlayerCount = profile.active_player_ids.length;
  s.HTW_TeamCount = profile.team_definitions.length;
  s.HTW_ArenaCount = profile.arena_ids.length;
  for (const [index, team] of profile.team_definitions.entries()) {
    s.HTW_TeamMemberA[index + 1] = team.member_player_ids[0];
    s.HTW_TeamMemberB[index + 1] = team.member_player_ids[1];
    s.HTW_TeamLiving[index + 1] = true;
    s.HTW_ArenaRect[index + 1] = JSON.parse(read('manifest.json')).regions.filter(region => /^Arena_[AB]$/.test(region.name))[index];
  }
  runtime.call('HTW_Tuning_Load');
  runtime.call('HTW_State_Reset');
  for (const playerId of profile.active_player_ids) {
    s.HTW_HeroSelectedByPlayer[playerId] = true;
    s.HTW_HeroAliveByPlayer[playerId] = true;
    s.HTW_HeroUnitByPlayer[playerId] = makeUnit(players[playerId - 1], rawcode('Hpal'), 0, 0, 0);
    s.HTW_WarCampByPlayer[playerId] = makeUnit(players[playerId - 1], rawcode('hhou'), 0, 0, 0);
    s.HTW_PlayerGold[playerId] = 200;
    players[playerId - 1].gold = 200;
  }
  s.HTW_HeroSelectionComplete = true;
  runtime.call('HTW_Waves_Prepare');
  s.HTW_AliveHeroCount = profile.active_player_ids.length;
  return { ...runtime, s, players, units, timers, messages, boards,
    creeps: () => units.filter(unit => unit.owner === players[12]),
    failCreates: count => { failedCreates = count; },
    spell(unit, ability) { triggerUnit = unit; abilityId = ability; runtime.call('HTW_Sending_OnPurchaseSpell'); },
    death(unit) { triggerUnit = unit; runtime.call('HTW_Lives_AccountDeath'); },
    fire(timer) {
      assert.ok(timer?.active, 'timer must be active');
      timer.remaining = 0;
      if (!timer.periodic) timer.active = false;
      runtime.invoke(timer.callback);
      if (timer.periodic && timer.active) timer.remaining = timer.duration;
    },
  };
}

const list = (array, count, start = 1) => Array.from({ length: count }, (_, i) => array[i + start]);
const key = (f, owner, kind) => f.call('HTW_PlanKey', owner, kind);
const counts = (f, name, owner) => [1, 2, 3].map(kind => f.s[name][key(f, owner, kind)]);
const plans = f => ['HTW_PlanBase', 'HTW_PlanSends', 'HTW_PlanFiller', 'HTW_PlanRemaining']
  .map(name => Array.from({ length: 6 }, (_, team) => counts(f, name, team + 1)));
const mirrorCalls = f => f.nativeCalls.filter(call => call.name === 'SetPlayerState');
function snapshot(f) {
  return { gold: list(f.s.HTW_PlayerGold, 24), nativeGold: f.players.map(player => player.gold),
    threat: list(f.s.HTW_PlayerThreatUsed, 24), destination: list(f.s.HTW_PlayerQueueDestination, 24),
    queues: Array.from({ length: 24 }, (_, p) => counts(f, 'HTW_PlayerQueueCount', p + 1)),
    plans: plans(f), locked: f.s.HTW_SendPlanLocked, mirrors: mirrorCalls(f).length };
}
function reject(f, args, name = 'HTW_Economy_TryPurchase') {
  const before = snapshot(f);
  const traceStart = f.functionCalls.length;
  assert.equal(f.call(name, ...args), false);
  assert.equal(f.functionCalls.slice(traceStart).filter(name => name === 'HTW_Economy_Reject').length, 1, 'rejection invokes source feedback helper once');
  assert.deepEqual(snapshot(f), before, `rejected ${name}(${args}) mutated purchase state`);
}
function buy(f, player, kind, quantity = 1) {
  const start = f.functionCalls.length;
  assert.equal(f.call('HTW_Economy_TryPurchase', player, kind, quantity), true);
  for (const name of ['HTW_Economy_SyncGold', 'HTW_Waves_RefreshPlan', 'HTW_Information_Display']) {
    assert.equal(f.functionCalls.slice(start).filter(called => called === name).length, 1, `${name} executes once per accepted purchase`);
  }
}

test('catalog has three distinct purchase types with stable cost/threat and readable details', () => {
  const f = fixture();
  assert.equal(f.call('HTW_SendCatalog_Count'), 3);
  assert.deepEqual([1, 2, 3].map(k => f.call('HTW_SendCatalog_UnitType', k)), ['hfoo', 'hrif', 'hkni'].map(rawcode));
  assert.deepEqual([1, 2, 3].map(k => f.call('HTW_SendCatalog_Cost', k)), [10, 20, 40]);
  assert.deepEqual([1, 2, 3].map(k => f.call('HTW_SendCatalog_Threat', k)), [1, 2, 4]);
  assert.equal(new Set([1, 2, 3].map(k => f.call('HTW_SendCatalog_AbilityId', k))).size, 3);
  for (const kind of [1, 2, 3]) for (const field of ['Name', 'Role', 'Description']) assert.ok(f.call(`HTW_SendCatalog_${field}`, kind).length > 0);
});

test('repeated mixed purchases charge once, isolate personal resources, and aggregate teammates', () => {
  const f = fixture();
  f.s.HTW_Wave = 3;
  const beforeMirrors = mirrorCalls(f).length;
  for (const kind of [1, 2, 3, 1]) buy(f, 1, kind);
  assert.equal(f.s.HTW_PlayerGold[1], 120);
  assert.equal(f.players[0].gold, 120);
  assert.equal(f.s.HTW_PlayerThreatUsed[1], 8);
  assert.deepEqual(counts(f, 'HTW_PlayerQueueCount', 1), [2, 1, 1]);
  assert.equal(f.s.HTW_PlayerQueueDestination[1], 2);
  assert.deepEqual(list(f.s.HTW_PlayerGold, 3, 2), [200, 200, 200]);
  assert.deepEqual(list(f.s.HTW_PlayerThreatUsed, 3, 2), [0, 0, 0]);
  assert.equal(mirrorCalls(f).length - beforeMirrors, 4);
  assert.ok(mirrorCalls(f).slice(beforeMirrors).every(call => call.args[0] === f.players[0]));
  buy(f, 2, 2, 2);
  assert.deepEqual(counts(f, 'HTW_PlanSends', 2), [2, 3, 1]);
  assert.deepEqual(counts(f, 'HTW_PlanBase', 2), [3, 0, 0]);
  assert.deepEqual(counts(f, 'HTW_PlanSends', 1), [0, 0, 0]);
  assert.equal(f.call('HTW_Waves_PlanWorth', 2), 15);
});

test('exact gold and exact personal threat budget accept; one-short and overflow reject atomically', () => {
  let f = fixture();
  f.s.HTW_PlayerGold[1] = 60;
  f.players[0].gold = 60;
  buy(f, 1, 2, 3);
  assert.equal(f.s.HTW_PlayerGold[1], 0);
  assert.equal(f.s.HTW_PlayerThreatUsed[1], 6);
  reject(f, [1, 1, 1]);
  f = fixture();
  f.s.HTW_PlayerGold[1] = 19;
  f.players[0].gold = 19;
  reject(f, [1, 2, 1]);
  f = fixture();
  buy(f, 1, 3);
  reject(f, [1, 2, 2]);
  buy(f, 1, 2);
  assert.equal(f.s.HTW_PlayerThreatUsed[1], 6);
  reject(f, [1, 1, 1]);
});

test('all invalid/closed/inactive/selection/elimination rejection paths preserve resources and plans', () => {
  const cases = [
    ['player zero', f => {}, [0, 1, 1]], ['negative player', f => {}, [-1, 1, 1]],
    ['outside active profile', f => {}, [5, 1, 1]], ['outside storage', f => {}, [25, 1, 1]],
    ['kind zero', f => {}, [1, 0, 1]], ['kind negative', f => {}, [1, -1, 1]], ['kind four', f => {}, [1, 4, 1]],
    ['quantity zero', f => {}, [1, 1, 0]], ['quantity negative', f => {}, [1, 1, -1]],
    ['excessive quantity', f => {}, [1, 1, 2147483647]],
    ['selection', f => { f.s.HTW_HeroSelectedByPlayer[1] = false; }],
    ['computer', f => { f.players[0].controller = 2; }],
    ['left', f => { f.players[0].slot = 0; }],
    ['match over', f => { f.s.HTW_MatchOver = true; }],
    ['locked', f => { f.call('HTW_Waves_LockPlan'); }],
    ['unassigned player', f => { f.s.HTW_TeamMemberA[1] = 0; }],
    ['sender eliminated', f => { f.s.HTW_TeamLiving[1] = false; }],
    ['target eliminated', f => { f.s.HTW_TeamLiving[2] = false; }],
    ['no route', f => { f.s.HTW_TeamDestination[1] = 0; }],
    ['self route', f => { f.s.HTW_TeamDestination[1] = 1; }],
    ['out of profile route', f => { f.s.HTW_TeamDestination[1] = 3; }],
    ...[0, 2, 3, 4, 5].map(phase => [`phase ${phase}`, f => { f.s.HTW_Phase = phase; }]),
  ];
  for (const [name, arrange, args = [1, 1, 1]] of cases) {
    const f = fixture(); arrange(f);
    try { reject(f, args); } catch (error) { throw new Error(name, { cause: error }); }
  }
});

test('compatibility purchase rejects forged prices/types and delegates accepted catalog prices', () => {
  const f = fixture();
  for (const price of [-1, 0, 1, 9, 11]) reject(f, [1, rawcode('hfoo'), 1, price], 'HTW_Economy_Purchase');
  reject(f, [1, rawcode('Hpal'), 1, 10], 'HTW_Economy_Purchase');
  assert.equal(f.call('HTW_Economy_Purchase', 1, rawcode('hrif'), 2, 20), true);
  assert.equal(f.s.HTW_PlayerGold[1], 160);
  assert.deepEqual(counts(f, 'HTW_PlayerQueueCount', 1), [0, 2, 0]);
});

test('spell owner and exact camp handle are authoritative; no forged/cross-camp spend', () => {
  const f = fixture();
  const ability = f.call('HTW_SendCatalog_AbilityId', 2);
  let before = snapshot(f);
  f.spell({ ...f.s.HTW_WarCampByPlayer[1] }, ability);
  assert.deepEqual(snapshot(f), before, 'same owner/type but different camp handle rejected');
  const camp = f.s.HTW_WarCampByPlayer[1];
  camp.owner = f.players[1];
  f.spell(camp, ability);
  assert.deepEqual(snapshot(f), before, 'changed owner cannot spend teammate gold');
  camp.owner = f.players[0];
  f.spell(camp, rawcode('A000'));
  assert.deepEqual(snapshot(f), before);
  const mirrors = mirrorCalls(f).length;
  f.spell(f.s.HTW_WarCampByPlayer[2], ability);
  assert.equal(f.s.HTW_PlayerGold[1], 200);
  assert.equal(f.s.HTW_PlayerGold[2], 180);
  assert.equal(mirrorCalls(f).length - mirrors, 1);
  assert.deepEqual(counts(f, 'HTW_PlayerQueueCount', 2), [0, 1, 0]);
});

test('preparation base-only, empty-human filler, and disconnected accepted sends are distinct plans', () => {
  const f = fixture();
  assert.equal(f.creeps().length, 0, 'preparation must not eagerly spawn the base wave');
  for (const team of [1, 2]) {
    assert.deepEqual(counts(f, 'HTW_PlanBase', team), [3, 0, 0]);
    assert.deepEqual(counts(f, 'HTW_PlanSends', team), [0, 0, 0]);
    assert.deepEqual(counts(f, 'HTW_PlanFiller', team), [0, 0, 0]);
    assert.equal(f.call('HTW_Waves_PlanWorth', team), 3);
  }
  f.players[2].slot = 0; f.players[3].controller = 2;
  f.call('HTW_Waves_RefreshPlan');
  assert.deepEqual(counts(f, 'HTW_PlanFiller', 1), [2, 1, 0]);
  assert.deepEqual(counts(f, 'HTW_PlanFiller', 2), [0, 0, 0]);
  assert.equal(f.call('HTW_Waves_PlanWorth', 1), 7);
  f.players[2].slot = 1;
  buy(f, 3, 3);
  f.players[2].slot = 0;
  f.call('HTW_Waves_RefreshPlan');
  assert.deepEqual(counts(f, 'HTW_PlanSends', 1), [0, 0, 1]);
  assert.deepEqual(counts(f, 'HTW_PlanFiller', 1), [0, 0, 0]);
  assert.deepEqual(counts(f, 'HTW_PlayerQueueCount', 3), [0, 0, 1]);
  assert.equal(f.call('HTW_Waves_PlanWorth', 1), 7);
});

test('refresh and deployment gates freeze a base-only plan across late disconnect and closed phases', () => {
  for (const phase of [0, 2, 3, 4, 5]) {
    const f = fixture();
    f.players.forEach(player => { player.slot = 0; });
    f.s.HTW_Phase = phase;
    const before = plans(f);
    f.call('HTW_Waves_RefreshPlan');
    f.call('HTW_Waves_LockPlan');
    f.call('HTW_Sending_ProcessQueues');
    assert.deepEqual(plans(f), before);
    assert.equal(f.creeps().length, 0);
    assert.equal(f.s.HTW_SendPlanLocked, false);
  }
  const f = fixture();
  f.call('HTW_Waves_LockPlan');
  const before = plans(f);
  f.players.forEach(player => { player.slot = 0; });
  f.call('HTW_Waves_RefreshPlan');
  f.fire(f.s.HTW_PreparationTimer);
  assert.deepEqual(plans(f), before, 'no new filler after lock even for an empty send');
  f.s.HTW_WaveActive = false;
  f.call('HTW_Sending_ProcessQueues');
  assert.deepEqual(plans(f), before);
  f.s.HTW_WaveActive = true;
  f.s.HTW_MatchOver = true;
  f.call('HTW_Sending_ProcessQueues');
  assert.deepEqual(plans(f), before);
  assert.equal(f.creeps().length, 0);
});

function emittedByArena(f) {
  return [1, 2].map(team => {
    const group = f.s.HTW_ArenaCreepGroup[team];
    return group ? [...group.units].map(unit => unit.unitType) : [];
  });
}
function drain(f) {
  for (let tick = 0; tick < 40; tick++) {
    const before = emittedByArena(f).map(units => units.length);
    f.call('HTW_Sending_ProcessQueues');
    const after = emittedByArena(f).map(units => units.length);
    for (let team = 0; team < 2; team++) assert.ok(after[team] - before[team] <= 1, 'at most one creep per arena per tick');
    if ([1, 2].every(team => counts(f, 'HTW_PlanRemaining', team).every(value => value === 0))) return;
  }
  assert.fail('pending plan did not drain within 40 source-executed ticks');
}

test('empty-human filler and disconnected accepted sends each deploy exactly the displayed full composition', () => {
  for (const acceptedBeforeDisconnect of [false, true]) {
    const f = fixture();
    if (acceptedBeforeDisconnect) buy(f, 3, 3);
    f.players[2].slot = 0; f.players[3].slot = 0;
    f.call('HTW_Waves_RefreshPlan');
    const displayed = plans(f).slice(0, 3);
    f.fire(f.s.HTW_PreparationTimer);
    drain(f);
    assert.deepEqual(emittedByArena(f), [acceptedBeforeDisconnect
      ? [...Array(3).fill(rawcode('hfoo')), rawcode('hkni')]
      : [...Array(5).fill(rawcode('hfoo')), rawcode('hrif')], Array(3).fill(rawcode('hfoo'))]);
    assert.deepEqual(plans(f).slice(0, 3), displayed);
    assert.deepEqual(counts(f, 'HTW_PlayerQueueCount', 3), acceptedBeforeDisconnect ? [0, 0, 1] : [0, 0, 0]);
  }
});

test('locked plan matches every staggered emission; late disconnects cannot inject filler; failed creation retries', () => {
  const f = fixture();
  buy(f, 1, 3); buy(f, 2, 2, 2); buy(f, 3, 1, 2);
  f.call('HTW_Waves_LockPlan');
  const full = plans(f).slice(0, 3);
  const accepted = snapshot(f).queues;
  assert.equal(f.s.HTW_SendPlanLocked, true);
  assert.deepEqual(counts(f, 'HTW_PlanRemaining', 1), [5, 0, 0]);
  assert.deepEqual(counts(f, 'HTW_PlanRemaining', 2), [3, 2, 1]);
  const pending = plans(f)[3];
  f.players.forEach(player => { player.slot = 0; });
  f.call('HTW_Waves_RefreshPlan');
  f.call('HTW_Waves_LockPlan');
  assert.deepEqual(plans(f), [...full, pending], 'lock is idempotent and refresh is frozen');
  f.fire(f.s.HTW_PreparationTimer);
  assert.equal(f.s.HTW_Phase, 2);
  f.failCreates(2);
  f.call('HTW_Sending_ProcessQueues');
  assert.equal(f.creeps().length, 0);
  assert.deepEqual(plans(f)[3], pending, 'failed creates retain all pending quantities');
  f.call('HTW_Sending_ProcessQueues');
  assert.equal(f.creeps().length, 2);
  assert.deepEqual(counts(f, 'HTW_PlanRemaining', 1), [4, 0, 0]);
  assert.deepEqual(counts(f, 'HTW_PlanRemaining', 2), [2, 2, 1]);
  f.call('HTW_Waves_LockPlan');
  drain(f);
  assert.deepEqual(emittedByArena(f), [Array(5).fill(rawcode('hfoo')),
    [...Array(3).fill(rawcode('hfoo')), ...Array(2).fill(rawcode('hrif')), rawcode('hkni')]]);
  assert.deepEqual(plans(f).slice(0, 3), full);
  assert.deepEqual(snapshot(f).queues, accepted, 'accepted count is retained through combat');
  for (const team of [1, 2]) {
    const rect = f.s.HTW_ArenaRect[team];
    const group = f.s.HTW_ArenaCreepGroup[team];
    assert.equal(f.s.HTW_ArenaCreepCount[team], group.units.size);
    for (const unit of group.units) assert.deepEqual(unit.orders, [{ order: 'attack',
      x: (rect.min_x + rect.max_x) / 2, y: (rect.min_y + rect.max_y) / 2 }]);
  }
  const total = f.creeps().length;
  f.call('HTW_Sending_ProcessQueues');
  assert.equal(f.creeps().length, total, 'no over-emission after pending reaches zero');
});

test('reset clears all 24 player and six team slots, and next preparation refreshes clean plans', () => {
  const f = fixture();
  for (let player = 1; player <= 24; player++) {
    f.s.HTW_PlayerQueueDestination[player] = 2;
    f.s.HTW_PlayerThreatUsed[player] = 9;
    for (let kind = 1; kind <= 3; kind++) f.s.HTW_PlayerQueueCount[key(f, player, kind)] = 7;
  }
  for (let team = 1; team <= 6; team++) for (let kind = 1; kind <= 3; kind++) {
    for (const name of ['HTW_PlanBase', 'HTW_PlanSends', 'HTW_PlanFiller', 'HTW_PlanRemaining']) f.s[name][key(f, team, kind)] = 8;
  }
  f.s.HTW_SendPlanLocked = true;
  f.call('HTW_Sending_ResetQueues');
  assert.equal(f.s.HTW_SendPlanLocked, false);
  assert.ok(snapshot(f).queues.flat().every(value => value === 0));
  assert.ok(snapshot(f).threat.every(value => value === 0));
  assert.ok(snapshot(f).destination.every(value => value === 0));
  assert.ok(plans(f).flat(2).every(value => value === 0));
  f.s.HTW_PlayerQueueCount[key(f, 24, 3)] = 4;
  f.s.HTW_PlanRemaining[key(f, 6, 3)] = 4;
  f.s.HTW_WaveActive = false;
  f.call('HTW_Waves_Prepare');
  assert.ok(snapshot(f).queues.flat().every(value => value === 0));
  assert.ok(plans(f)[3].flat().every(value => value === 0));
  assert.deepEqual(counts(f, 'HTW_PlanBase', 1), [3, 0, 0]);
  assert.deepEqual(counts(f, 'HTW_PlanBase', 2), [3, 0, 0]);
});

test('eliminated arena clears only its own live creeps, frozen plan and pending deployments', () => {
  const f = fixture();
  buy(f, 1, 3); buy(f, 3, 2);
  f.fire(f.s.HTW_PreparationTimer);
  f.call('HTW_Sending_ProcessQueues');
  const other = plans(f).map(array => array[1]);
  const arenaOneUnits = [...f.s.HTW_ArenaCreepGroup[1].units];
  const arenaTwoUnits = [...f.s.HTW_ArenaCreepGroup[2].units];
  f.s.HTW_TeamLiving[1] = false;
  f.call('HTW_Waves_ClearArena', 1);
  assert.ok(arenaOneUnits.every(unit => unit.removed));
  assert.ok(arenaTwoUnits.every(unit => !unit.removed));
  assert.equal(f.s.HTW_ArenaCreepCount[1], 0);
  for (const array of plans(f)) assert.deepEqual(array[0], [0, 0, 0]);
  assert.deepEqual(plans(f).map(array => array[1]), other);
  assert.equal(f.call('HTW_Waves_PlanWorth', 1), 0);
  f.call('HTW_Waves_ClearArena', 1);
  assert.deepEqual(plans(f).map(array => array[1]), other, 'cleanup is idempotent and arena-local');
  drain(f);
  assert.equal(f.s.HTW_ArenaCreepCount[1], 0, 'eliminated arena receives no later arrivals');
  assert.deepEqual(emittedByArena(f)[1], [...Array(3).fill(rawcode('hfoo')), rawcode('hkni')]);
});

test('real life-loss handler performs arena cleanup once and terminal resolution prevents a new wave', () => {
  const f = fixture();
  buy(f, 1, 3); buy(f, 3, 2);
  f.fire(f.s.HTW_PreparationTimer);
  f.call('HTW_Sending_ProcessQueues');
  f.s.HTW_TeamLives[1] = 1;
  const other = plans(f).map(array => array[1]);
  const firstArena = [...f.s.HTW_ArenaCreepGroup[1].units];
  const otherArena = [...f.s.HTW_ArenaCreepGroup[2].units];
  f.death(f.s.HTW_HeroUnitByPlayer[1]);
  assert.equal(f.s.HTW_TeamLives[1], 0);
  assert.equal(f.s.HTW_TeamLiving[1], false);
  assert.equal(f.s.HTW_MatchOver, true);
  assert.ok(firstArena.every(unit => unit.removed));
  assert.ok(otherArena.every(unit => !unit.removed));
  assert.deepEqual(plans(f).map(array => array[1]), other);
  for (const array of plans(f)) assert.deepEqual(array[0], [0, 0, 0]);
  f.death(f.s.HTW_HeroUnitByPlayer[1]);
  assert.equal(f.s.HTW_TeamLives[1], 0, 'repeated death does not charge another life');
  f.call('HTW_Waves_Resolve');
  assert.ok(f.creeps().every(unit => unit.removed));
  assert.ok(plans(f)[3].flat().every(value => value === 0));
  assert.ok(snapshot(f).queues.flat().every(value => value === 0));
  const wave = f.s.HTW_Wave;
  f.call('HTW_Waves_Prepare');
  assert.equal(f.s.HTW_Wave, wave);
  assert.equal(f.s.HTW_WaveActive, false);
});

test('information board source renders personal receipts and full locked plans, then hides stale rows', () => {
  const f = fixture();
  buy(f, 1, 2, 2);
  buy(f, 3, 3);
  const board = f.s.HTW_InformationBoard[1];
  assert.equal(f.boards.length, 4);
  assert.equal(board.visible, true);
  assert.equal(f.s.HTW_InformationBoard[2].visible, false);
  assert.equal(board.rowCount, 27);
  assert.match(board.rows[12], /Gold: 160/);
  assert.match(board.rows[13], /4 used.*2 left/);
  assert.match(board.rows[14], /Team 2/);
  assert.match(board.rows.slice(15, 18).join('\n'), /Rifleman.*2/);
  assert.match(board.rows[26], /Total incoming threat: 7/);
  f.fire(f.s.HTW_PreparationTimer);
  assert.match(board.rows[19], /Locked full wave/);
  const fullRows = board.rows.slice(20, 27);
  drain(f);
  f.call('HTW_Information_Display');
  assert.deepEqual(board.rows.slice(20, 27), fullRows, 'full plan remains displayed after pending drains');
  assert.equal(f.boards.length, 4, 'board refresh reuses handles');
  assert.ok(f.boards.flatMap(board => board.items).every(item => item.released));
  f.s.HTW_TeamLiving[1] = false;
  f.call('HTW_Waves_ClearArena', 1);
  f.call('HTW_Information_Display');
  assert.match(board.rows[13], /Eliminated/);
  assert.ok(board.rows.slice(14, 27).every(value => value === ''));
  f.s.HTW_MatchOver = true;
  f.call('HTW_Information_Display');
  assert.match(board.rows[13], /Match over/);
});

test('five complete timer-driven waves retain full plans, resolve once, grant/mirror once, and reset without carryover', () => {
  const f = fixture();
  for (let wave = 1; wave <= 5; wave++) {
    assert.equal(f.s.HTW_Wave, wave);
    assert.equal(f.s.HTW_Phase, 1);
    assert.equal(f.s.HTW_SendPlanLocked, false);
    assert.equal(f.call('HTW_Sending_Budget'), 6 + 2 * (wave - 1));
    assert.equal(f.s.HTW_PreparationTimer.duration, 35);
    assert.equal(f.s.HTW_PreparationTimer.periodic, false);
    assert.ok(snapshot(f).queues.flat().every(value => value === 0));
    assert.ok(snapshot(f).threat.every(value => value === 0));
    buy(f, 1, 1, 2);
    const totalBefore = f.creeps().length;
    f.call('HTW_Sending_ProcessQueues');
    assert.equal(f.creeps().length, totalBefore, 'no preparation spawns');
    f.fire(f.s.HTW_PreparationTimer);
    assert.equal(f.s.HTW_SendPlanLocked, true);
    assert.equal(f.s.HTW_Phase, 2);
    assert.equal(f.s.HTW_CombatTimer.duration, 90);
    assert.equal(f.s.HTW_CombatTimer.periodic, false);
    reject(f, [1, 1, 1]);
    if (wave % 2 === 1) drain(f);
    else f.call('HTW_Sending_ProcessQueues'); // Resolve with later spawns still pending.
    assert.deepEqual(counts(f, 'HTW_PlayerQueueCount', 1), [2, 0, 0]);
    const gold = list(f.s.HTW_PlayerGold, 4);
    const mirrors = mirrorCalls(f).length;
    f.fire(f.s.HTW_CombatTimer);
    assert.equal(f.s.HTW_LastResolvedWave, wave);
    assert.equal(f.s.HTW_WaveActive, false);
    assert.ok(f.creeps().every(unit => unit.removed));
    assert.ok(plans(f)[3].flat().every(value => value === 0));
    assert.ok(snapshot(f).queues.flat().every(value => value === 0));
    assert.deepEqual(list(f.s.HTW_PlayerGold, 4), gold.map(value => value + 60));
    assert.equal(mirrorCalls(f).length - mirrors, 4);
    assert.deepEqual(f.players.slice(0, 4).map(player => player.gold), list(f.s.HTW_PlayerGold, 4));
    const after = snapshot(f);
    f.call('HTW_Waves_Resolve');
    assert.deepEqual(snapshot(f), after);
    assert.equal(f.s.HTW_CombatTimer.active, false);
    assert.equal(f.s.HTW_PreparationTimer.active, false);
    if (wave < 5) f.call('HTW_Waves_Prepare');
  }
  for (const wave of [8, 9, 100]) {
    f.s.HTW_Wave = wave;
    assert.equal(f.call('HTW_Sending_Budget'), 20);
  }
});
