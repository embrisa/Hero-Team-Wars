import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { createJassRuntime, rawcode } from './jass-runtime-harness.mjs';
import { fixture, frameConstants, list, key, counts, plans, mirrorCalls, cell, cellData, hudData, incomingData, rootOf, snapshot, reject, buy } from './v28-runtime-fixture.mjs';

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
    globals: [definition('Flag', 'boolean', false), definition('Values', 'integer', undefined, true), definition('TypeId', 'integer', 0)],
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
  const globals = [definition('Value', 'integer', 0)];
  for (const body of ['set Value = "wrong type"', 'set Value = true + 1', 'if 1 then\nreturn\nendif', 'call Probe(1)']) {
    assert.throws(() => build(body, { globals }), undefined, body);
  }
  assert.throws(() => build('set Value = 2147483647 + 1', { globals }).call('Probe'), /integer overflow/);
  assert.throws(() => build('set Value = 7 / 0', { globals }).call('Probe'), /division by zero/);
});

test('harness: framehandle locals, parameters, returns and arrays retain strict handle typing', () => {
  const source = `function Store takes framehandle frame returns framehandle
    local framehandle previous = Frames[1]
    set Frames[1] = frame
    return previous
endfunction`;
  const globals = [definition('Frames', 'framehandle', undefined, true), definition('Unit', 'unit', null)];
  const build = source => createJassRuntime({ sources: [{ path: 'frame-self-test.j', source }], globals });
  const f = build(source);
  const frame = { handleType: 'framehandle' };
  assert.equal(f.state.Frames[1], null);
  assert.equal(f.call('Store', frame), null);
  assert.equal(f.state.Frames[1], frame);
  assert.equal(f.call('Store', null), frame);
  assert.equal(f.state.Frames[1], null);
  for (const value of ['1', 'true', 'Unit']) {
    assert.throws(() => build(source.replace('set Frames[1] = frame', `set Frames[1] = ${value}`)), /Unsupported type conversion/);
  }
  assert.throws(() => build(source + '\nfunction Wrong takes nothing returns nothing\ncall Store(Unit)\nendfunction'), /Unsupported type conversion/);
  assert.throws(() => build(source.replace('return previous', 'return UnknownFrameNative()')), /Unbound function\/native/);
});

test('harness: scalar globals and locals are fail-closed while explicit null, assignments, arrays and short circuit work', () => {
  const source = `
function BooleanGuard takes nothing returns nothing
    if Guard then
        set Count = 1
    endif
endfunction
function NullComparison takes nothing returns boolean
    return Trigger == null
endfunction
function NativeArgument takes nothing returns nothing
    call Consume(Trigger)
endfunction
function LocalRead takes nothing returns boolean
    local trigger value
    return value == null
endfunction
function LocalNull takes nothing returns boolean
    local trigger value
    set value = null
    return value == null
endfunction
function ShortCircuit takes nothing returns boolean
    if Guard and MissingGuard then
        return true
    endif
    return false
endfunction
function Initialize takes nothing returns nothing
    set Guard = false
    set Trigger = null
    set Count = 7
    set Values[4] = 9
endfunction`;
  const globals = [
    { name: 'Guard', type: 'boolean' }, { name: 'MissingGuard', type: 'boolean' },
    { name: 'Trigger', type: 'trigger' }, { name: 'Count', type: 'integer' },
    { name: 'Values', type: 'integer', array: true },
  ];
  const runtime = createJassRuntime({ sources: [{ path: 'strict-initialization-self-test.j', source }], globals,
    natives: { Consume: { type: 'nothing', arity: 1, params: [{ type: 'trigger' }], fn: () => {} } } });
  assert.throws(() => runtime.call('BooleanGuard'), /uninitialized global 'Guard'/);
  assert.throws(() => runtime.call('NullComparison'), /uninitialized global 'Trigger'/);
  assert.throws(() => runtime.call('NativeArgument'), /uninitialized global 'Trigger'/);
  assert.throws(() => runtime.call('LocalRead'), /uninitialized local in LocalRead 'value'/);
  assert.equal(runtime.state.Values[3], 0, 'array slots retain native defaults');

  runtime.call('Initialize');
  assert.equal(runtime.state.Guard, false);
  assert.equal(runtime.state.Trigger, null, 'explicit null is initialized, not absent');
  assert.equal(runtime.state.Count, 7);
  assert.equal(runtime.state.Values[4], 9);
  assert.equal(runtime.call('NullComparison'), true);
  assert.equal(runtime.call('LocalNull'), true, 'local assignment of null remains readable');
  assert.equal(runtime.call('ShortCircuit'), false, 'short circuit skips an uninitialized RHS');
  runtime.call('NativeArgument');
});

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

test('HUD: source wiring defers allocation to the first runtime tick during selection', () => {
  const manifest = JSON.parse(read('manifest.json'));
  const functions = new Map(manifest.modules.flatMap(module =>
    [...read(module.path).matchAll(/^function (HTW_\w+) takes[^\n]*\n([\s\S]*?)^endfunction/gm)]
      .map(([, name, body]) => [name, body])));
  const init = JSON.parse(read('triggers/map-init.trigger.json'));
  assert.deepEqual(init.events, [{ type: 'map_initialization' }]);
  for (const name of ['HTW_Dev_Initialize', 'HTW_DevMenu_Initialize']) {
    assert.equal(init.actions.filter(action => action.type === 'call_function' && action.function === name).length, 1);
  }
  const pending = init.actions.filter(action => action.type === 'call_function').map(action => action.function);
  const visited = new Set();
  while (pending.length) {
    const name = pending.pop();
    if (visited.has(name)) continue;
    visited.add(name);
    assert.ok(functions.has(name), `initialization source missing ${name}`);
    assert.doesNotMatch(name, /^HTW_Information_/);
    const body = functions.get(name).replace(/\/\/[^\n]*/g, '');
    assert.doesNotMatch(body, /\bBlzCreateFrame(?:ByType)?\s*\(/);
    // Follow synchronous calls; scheduled callbacks are not initialization work.
    pending.push(...[...body.matchAll(/\b(HTW_\w+)\s*\(/g)].map(match => match[1]));
  }
  const tick = JSON.parse(read('triggers/runtime-tick.trigger.json'));
  assert.equal(tick.enabled, true);
  assert.equal(tick.initially_on, true);
  assert.deepEqual(tick.events, [{ type: 'periodic_timer', period: 1, repeat: true }]);
  assert.deepEqual(tick.conditions, [{ type: 'always' }]);
  assert.deepEqual(tick.actions.slice(-2).map(action => action.function), ['HTW_Information_Display', 'HTW_DevMenu_Display']);
  assert.ok(tick.actions.findIndex(action => action.function === 'HTW_Dev_Tick') >= 0);
  assert.ok(tick.actions.findIndex(action => action.function === 'HTW_Dev_Tick') <
    tick.actions.findIndex(action => action.function === 'HTW_Phases_Tick'));
  const f = fixture({ prepare: false });
  f.call('HTW_Dev_Initialize');
  f.call('HTW_DevMenu_Initialize');
  assert.equal(f.nativeCalls.filter(call => call.name === 'TriggerRegisterPlayerChatEvent').length, 4);
  assert.equal(f.nativeCalls.filter(call => call.name === 'TriggerRegisterGameEvent').length, 1);
  assert.equal(f.s.HTW_Phase, 0);
  assert.equal(f.s.HTW_InformationReady, false);
  assert.equal(f.frames.length, 0);
  for (const action of tick.actions) {
    assert.equal(action.type, 'call_function'); f.call(action.function);
  }
  assert.equal(f.s.HTW_Phase, 0, 'unselected heroes keep the selection phase open');
  assert.equal(f.s.HTW_InformationReady, true);
  assert.equal(f.functionCalls.filter(name => name === 'HTW_Information_CreateHud').length, 1);
  for (const viewer of [1, 2, 3, 4]) {
    assert.match(f.s.HTW_HudTitle[viewer].text, /Hero selection/);
    for (const slot of [2, 3, 5, 6]) assert.match(cell(f, viewer, slot).tooltip.text, /Unselected/);
  }
});

test('HUD: compact roots and cells fit the regular 4:3 frame area', () => {
  const f = fixture();
  f.call('HTW_Information_Display');
  for (const viewer of [1, 2, 3, 4]) {
    const root = f.s.HTW_HudRoot[viewer];
    assert.equal(root.parent, f.gameUI);
    assert.equal(root.points.length, 1);
    const anchor = root.points[0];
    assert.equal(anchor.point, frameConstants.FRAMEPOINT_TOPLEFT);
    assert.equal(anchor.relative, undefined);
    assert.ok(root.width > 0 && root.width <= 0.26);
    assert.ok(root.height > 0 && root.height <= 0.20);
    assert.ok(anchor.x >= 0 && anchor.x + root.width <= 0.8);
    assert.ok(anchor.y <= 0.6 && anchor.y - root.height >= 0);
    const bounds = [];
    for (let slot = 1; slot <= 15; slot++) {
      const { hover, icon, value, tooltip } = cell(f, viewer, slot);
      assert.equal(hover.parent, root);
      assert.equal(hover.context, viewer * 32 + slot);
      assert.equal(value.parent, hover);
      assert.equal(hover.points.length, 1);
      const point = hover.points[0];
      assert.equal(point.relative, root);
      assert.equal(point.point, frameConstants.FRAMEPOINT_TOPLEFT);
      assert.equal(point.relativePoint, frameConstants.FRAMEPOINT_TOPLEFT);
      const box = { left: point.x, right: point.x + hover.width, top: -point.y, bottom: -point.y + hover.height };
      assert.ok(box.left >= 0 && box.right <= root.width + 1e-9);
      assert.ok(box.top >= 0 && box.bottom <= root.height + 1e-9);
      bounds.push(box);
      for (const child of [icon, value]) {
        const p = child.points[0];
        assert.equal(p.relative, hover);
        assert.ok(p.x >= 0 && p.x + child.width <= hover.width + 1e-9);
        assert.ok(-p.y >= 0 && -p.y + child.height <= hover.height + 1e-9);
      }
      assert.equal(icon.width, 0.024);
      assert.equal(icon.height, 0.024);
      const tipAnchor = tooltip.points[0];
      assert.equal(tipAnchor.point, frameConstants.FRAMEPOINT_TOPRIGHT);
      assert.equal(tipAnchor.relative, undefined);
      assert.ok(tipAnchor.x + 0.008 <= 0.8 && tipAnchor.x - tooltip.width - 0.008 >= 0);
      assert.ok(tipAnchor.y > 0 && tipAnchor.y + 0.008 < anchor.y - root.height);
      // Auto-height wrapping is recorded, not emulated or certified as unclipped.
      assert.equal(tooltip.height, 0);
    }
    for (let a = 0; a < bounds.length; a++) for (let b = a + 1; b < bounds.length; b++) {
      const x = bounds[a], y = bounds[b];
      assert.ok(x.right <= y.left || y.right <= x.left || x.bottom <= y.top || y.bottom <= x.top,
        `HUD cells ${a + 1} and ${b + 1} overlap`);
    }
  }
});

test('HUD: four clients allocate identical roots/data even for inactive viewers; only their own root is visible', () => {
  const clients = [1, 2, 3, 4].map(localPlayerId => {
    const f = fixture({ localPlayerId });
    f.players[1].slot = 0;
    f.players[2].controller = 2;
    f.players[3].slot = 0;
    f.call('HTW_Waves_RefreshPlan');
    const before = snapshot(f);
    f.call('HTW_Information_Display');
    assert.deepEqual(snapshot(f), before, 'presentation must not alter purchase/plan state');
    assert.equal(f.frames.length, 4 * 79);
    for (const viewer of [1, 2, 3, 4]) {
      const root = f.s.HTW_HudRoot[viewer];
      assert.equal(root.visible, viewer === localPlayerId, `client ${localPlayerId}, viewer ${viewer}`);
      const owned = f.frames.filter(frame => rootOf(f, frame) === root);
      assert.equal(owned.length, 79, 'inactive roots get the same complete allocation');
      assert.deepEqual(['BACKDROP', 'BUTTON', 'TEXT'].map(type => owned.filter(frame => frame.frameType === type).length), [31, 15, 33]);
      const positions = new Map(owned.map((frame, index) => [frame, index]));
      const shape = owned.map(frame => ({ type: frame.frameType, name: frame.name, inherits: frame.inherits,
        context: frame.context < 32 ? frame.context - viewer : frame.context - viewer * 32,
        parent: positions.get(frame.parent) ?? -1 }));
      if (viewer === 1) f.rootShape = shape;
      else assert.deepEqual(shape, f.rootShape);
    }
    const rootShows = f.nativeCalls.filter(call => call.name === 'BlzFrameSetVisible' && call.args[1]);
    assert.deepEqual(rootShows.map(call => call.args[0]), [f.s.HTW_HudRoot[localPlayerId]]);
    const writes = f.nativeCalls.filter(call => ['BlzFrameSetText', 'BlzFrameSetTexture'].includes(call.name))
      .map(call => ({ name: call.name, frame: call.args[0].handleId, values: call.args.slice(1) }));
    return { allocations: f.allocations, bindings: f.tooltipBindings, writes, data: hudData(f), state: snapshot(f) };
  });
  for (const client of clients.slice(1)) assert.deepEqual(client, clients[0], 'local player must not affect allocations, bindings or synchronized data');
});

test('HUD: every disabled hover/icon/label has one tooltip binding, reused across refreshes and phases', () => {
  const f = fixture();
  f.call('HTW_Information_Display');
  assert.equal(f.tooltipBindings.length, 60);
  assert.equal(new Set(f.tooltipBindings.map(binding => binding.frame)).size, 60);
  assert.equal(new Set(f.tooltipBindings.map(binding => binding.tooltip)).size, 60);
  for (const viewer of [1, 2, 3, 4]) for (let slot = 1; slot <= 15; slot++) {
    const c = cell(f, viewer, slot);
    assert.equal(c.hover.frameType, 'BUTTON');
    assert.equal(c.hover.tooltip, c.tooltip.parent);
    assert.equal(c.hover.tooltip.parent, c.hover);
    assert.equal(c.hover.tooltip.visible, false, 'regular tooltip begins hidden');
    assert.deepEqual(f.tooltipBindings.filter(binding => binding.frame === c.hover.handleId),
      [{ frame: c.hover.handleId, tooltip: c.tooltip.parent.handleId }]);
    assert.equal(c.hover.enabled, false);
    assert.equal(c.icon.enabled, false);
  }
  assert.ok(f.frames.filter(frame => frame.frameType === 'TEXT').every(frame => frame.enabled === false));
  const allocations = [...f.allocations], bindings = [...f.tooltipBindings];
  const refreshStart = f.nativeCalls.length;
  for (let tick = 0; tick < 10; tick++) f.call('HTW_Information_Display');
  buy(f, 1, 2, 2);
  f.fire(f.s.HTW_PreparationTimer);
  drain(f);
  f.call('HTW_Information_Display');
  f.fire(f.s.HTW_CombatTimer);
  f.call('HTW_Waves_Prepare');
  f.call('HTW_Information_Display');
  assert.deepEqual(f.allocations, allocations);
  assert.deepEqual(f.tooltipBindings, bindings);
  assert.equal(f.nativeCalls.slice(refreshStart).filter(call => /^(BlzCreateFrame|BlzCreateFrameByType|BlzFrameSetTooltip|BlzFrameSetVisible)$/.test(call.name)).length, 0);
  assert.ok(f.frames.filter(frame => ['TEXT', 'BUTTON'].includes(frame.frameType) || frame.name === 'HTWIcon').every(frame => !frame.enabled));
});

test('HUD: load callback defers rebuilding and the next display replaces all stale handles exactly once', () => {
  const clients = [1, 2, 3, 4].map(localPlayerId => {
    const f = fixture({ localPlayerId });
    buy(f, 1, 2, 2); buy(f, 3, 3);
    f.fire(f.s.HTW_PreparationTimer);
    const data = hudData(f), before = snapshot(f), roots = list(f.s.HTW_HudRoot, 4);
    const oldFrames = [...f.frames], oldOrigin = f.gameUI, loadTrigger = f.s.HTW_HudLoadTrigger;
    const nativeStart = f.nativeCalls.length;
    f.simulateGameLoad();
    assert.equal(f.s.HTW_InformationReady, false);
    assert.equal(f.nativeCalls.length, nativeStart, 'load event must not touch or allocate frames');
    assert.deepEqual(list(f.s.HTW_HudRoot, 4), roots, 'replacement is deferred to elapsed display');
    assert.equal(f.allocations.length, 316);
    assert.equal(f.tooltipBindings.length, 60);
    f.call('HTW_Information_Display');
    assert.equal(f.s.HTW_InformationReady, true);
    assert.notEqual(f.gameUI, oldOrigin);
    assert.ok(oldFrames.every(frame => frame.invalid));
    assert.equal(f.allocations.length, 632);
    assert.equal(f.tooltipBindings.length, 120);
    assert.equal(f.s.HTW_HudLoadTrigger, loadTrigger);
    assert.equal(f.nativeCalls.filter(call => call.name === 'TriggerRegisterGameEvent').length, 1);
    for (const viewer of [1, 2, 3, 4]) {
      const root = f.s.HTW_HudRoot[viewer];
      assert.notEqual(root, roots[viewer - 1]);
      assert.equal(root.parent, f.gameUI);
      assert.equal(root.visible, viewer === localPlayerId);
      assert.ok(!oldFrames.includes(f.s.HTW_HudTitle[viewer]));
      assert.ok(!oldFrames.includes(f.s.HTW_HudIncoming[viewer]));
      for (let slot = 1; slot <= 15; slot++) {
        const c = cell(f, viewer, slot);
        for (const frame of [c.icon, c.value, c.tooltip, c.hover, c.hover.tooltip]) assert.ok(!oldFrames.includes(frame));
      }
    }
    assert.deepEqual(hudData(f), data);
    assert.deepEqual(snapshot(f), before);
    f.call('HTW_Information_Display');
    assert.equal(f.allocations.length, 632);
    assert.equal(f.tooltipBindings.length, 120);
    return { allocations: f.allocations, bindings: f.tooltipBindings, data: hudData(f) };
  });
  for (const client of clients.slice(1)) assert.deepEqual(client, clients[0]);
});

test('HUD: all six public life/hero cells expose the same team identities and status to every viewer', () => {
  const f = fixture();
  f.s.HTW_TeamLives[1] = 12;
  f.s.HTW_TeamLives[2] = 9;
  for (const playerId of [1, 2, 3, 4]) f.s.HTW_HeroUnitByPlayer[playerId].level = playerId + 2;
  f.call('HTW_Information_Display');
  const expected = [
    ['T1 12', 'AHds', /Team 1 - shared lives.*12 lives \| Active/],
    ['P1 L3', 'H001', /Team 1 hero.*P1: Guardian Lv 3/],
    ['P2 L4', 'H002', /Team 1 hero.*P2: Striker Lv 4/],
    ['T2 9', 'AHds', /Team 2 - shared lives.*9 lives \| Active/],
    ['P3 L5', 'H003', /Team 2 hero.*P3: Controller Lv 5/],
    ['P4 L6', 'H004', /Team 2 hero.*P4: Support Lv 6/],
  ];
  for (const viewer of [1, 2, 3, 4]) for (const [index, [value, icon, tooltip]] of expected.entries()) {
    const c = cellData(f, viewer, index + 1);
    assert.equal(c.value, value); assert.equal(c.icon, `mock-icon:${icon}`); assert.match(c.tooltip, tooltip);
  }
  f.s.HTW_HeroAliveByPlayer[1] = false;
  f.s.HTW_HeroSelectedByPlayer[2] = false;
  f.players[2].slot = 0;
  f.s.HTW_TeamLiving[2] = false;
  f.call('HTW_Information_Display');
  for (const viewer of [1, 2, 3, 4]) {
    assert.equal(cell(f, viewer, 2).value.text, 'P1 X'); assert.match(cell(f, viewer, 2).tooltip.text, /Down/);
    assert.equal(cell(f, viewer, 3).value.text, 'P2 -'); assert.match(cell(f, viewer, 3).tooltip.text, /Unselected/);
    assert.equal(cell(f, viewer, 5).value.text, 'P3 -'); assert.match(cell(f, viewer, 5).tooltip.text, /Inactive/);
    assert.equal(cell(f, viewer, 6).value.text, 'P4 X'); assert.match(cell(f, viewer, 6).tooltip.text, /Eliminated/);
    assert.match(cell(f, viewer, 4).tooltip.text, /Eliminated/);
  }
  f.s.HTW_HeroTypeByPlayer[1] = rawcode('BAD!');
  assert.throws(() => f.call('HTW_Information_Display'), /unmocked icon rawcode/, 'unknown icon natives are never permissively mocked');
});

test('HUD: personal gold/budget/sends and receiving-team previews isolate all four owners across clients', () => {
  const clients = [1, 2, 3, 4].map(localPlayerId => {
    const f = fixture({ localPlayerId });
    buy(f, 1, 1, 1); buy(f, 2, 2, 2); buy(f, 3, 3, 1); buy(f, 4, 1, 3);
    const queues = [[1, 0, 0], [0, 2, 0], [0, 0, 1], [3, 0, 0]];
    const gold = [190, 160, 160, 170], remaining = [5, 2, 2, 3];
    for (const viewer of [1, 2, 3, 4]) {
      assert.equal(cell(f, viewer, 7).value.text, String(gold[viewer - 1]));
      assert.match(cell(f, viewer, 7).tooltip.text, new RegExp(`Personal gold - P${viewer}`));
      assert.equal(cell(f, viewer, 8).value.text, `${remaining[viewer - 1]} left`);
      assert.match(cell(f, viewer, 8).tooltip.text, new RegExp(`${6 - remaining[viewer - 1]} used.*${remaining[viewer - 1]} left.*6 total`));
      const sourceTeam = viewer <= 2 ? 2 : 1;
      assert.equal(cell(f, viewer, 9).value.text, `T${sourceTeam}`);
      assert.match(cell(f, viewer, 9).tooltip.text, new RegExp(`Send to: Team ${sourceTeam}.*Receive from: Team ${sourceTeam}`));
      const incoming = viewer <= 2 ? [6, 0, 1] : [4, 2, 0];
      const sends = viewer <= 2 ? [3, 0, 1] : [1, 2, 0];
      for (const kind of [1, 2, 3]) {
        const send = cellData(f, viewer, 9 + kind), wave = cellData(f, viewer, 12 + kind);
        assert.equal(send.value, String(queues[viewer - 1][kind - 1]));
        assert.match(send.tooltip, new RegExp(`P${viewer} queued: ${send.value}\\|nSend to: Team ${sourceTeam}`));
        assert.equal(wave.value, String(incoming[kind - 1]));
        assert.match(wave.tooltip, new RegExp(`From: Team ${sourceTeam}.*Base: ${kind === 1 ? 3 : 0} \\| Enemy sends: ${sends[kind - 1]} \\| Filler: 0`));
        assert.equal(send.icon, wave.icon);
      }
      assert.equal(f.s.HTW_HudIncoming[viewer].text, `|cff90e8bfIN|n${viewer <= 2 ? 10 : 8} th|r`);
    }
    const before = snapshot(f), data = hudData(f);
    f.call('HTW_Information_Display');
    assert.deepEqual(snapshot(f), before);
    assert.deepEqual(hudData(f), data);
    return { allocations: f.allocations, bindings: f.tooltipBindings, data, state: before };
  });
  for (const client of clients.slice(1)) assert.deepEqual(client, clients[0]);
});

test('HUD: full incoming tooltip composition freezes at lock and survives staggered spawns and disconnects', () => {
  const f = fixture();
  buy(f, 1, 2, 2);
  buy(f, 3, 3);
  const preview = incomingData(f, 1);
  assert.deepEqual(preview.cells.map(c => c.value), ['3', '0', '1']);
  assert.match(preview.summary, /IN\|n7 th/);
  assert.match(preview.cells[2].tooltip, /Base: 0 \| Enemy sends: 1 \| Filler: 0.*Total: 1 \| Threat: 4.*Whole wave threat: 7/);
  f.call('HTW_Waves_LockPlan');
  f.players[2].slot = 0; f.players[3].slot = 0;
  f.call('HTW_Waves_RefreshPlan');
  f.call('HTW_Information_Display');
  assert.deepEqual(incomingData(f, 1), preview, 'disconnect cannot replace the accepted locked plan with filler');
  f.fire(f.s.HTW_PreparationTimer);
  const locked = incomingData(f, 1);
  assert.deepEqual(locked.cells.map(c => c.value), preview.cells.map(c => c.value));
  for (const c of locked.cells) assert.match(c.tooltip, /Locked full wave - includes later arrivals/);
  f.call('HTW_Sending_ProcessQueues');
  f.call('HTW_Information_Display');
  assert.deepEqual(incomingData(f, 1), locked, 'first emission must not decrement displayed totals');
  drain(f);
  f.call('HTW_Information_Display');
  assert.deepEqual(counts(f, 'HTW_PlanRemaining', 1), [0, 0, 0]);
  assert.deepEqual(incomingData(f, 1), locked, 'full tooltip remains after all pending arrivals drain');
  assert.equal(cell(f, 1, 11).value.text, '2', 'sender receipt remains through combat');
});

function assertPreviewUnavailable(f, viewer, status) {
  for (let slot = 8; slot <= 15; slot++) {
    const c = cellData(f, viewer, slot);
    assert.equal(c.value, '-', `viewer ${viewer} stale slot ${slot}`);
    assert.match(c.tooltip, status);
    assert.doesNotMatch(c.tooltip, /P\d queued:|Base: \d|Enemy sends: \d|Filler: \d|Total: \d|Whole wave threat:|From: Team|Send to: Team|Receive from: Team/);
    if (slot >= 13) assert.match(c.tooltip, /Wave preview unavailable/);
  }
  assert.equal(f.s.HTW_HudIncoming[viewer].text, '|cffaaaaaaIN|n-|r');
}

test('HUD: elimination, resolution, inactivity and terminal states remove stale previews and receipts', () => {
  const cases = [
    ['elimination', f => { f.s.HTW_TeamLiving[1] = false; f.call('HTW_Waves_ClearArena', 1); }, [1, 2], /Eliminated/],
    ['resolution', f => f.fire(f.s.HTW_CombatTimer), [1, 2, 3, 4], /Purchases closed outside preparation/],
    ['match over', f => { f.s.HTW_MatchOver = true; }, [1, 2, 3, 4], /Match over/],
    ['victory', f => { f.s.HTW_TerminalState = 1; f.s.HTW_Phase = 4; }, [1, 2, 3, 4], /Match over/],
    ['draw', f => { f.s.HTW_TerminalState = 2; f.s.HTW_Phase = 5; }, [1, 2, 3, 4], /Match over/],
    ['inactive viewer', f => { f.players[0].slot = 0; }, [1], /Inactive player/],
  ];
  for (const [name, arrange, viewers, status] of cases) {
    const f = fixture();
    buy(f, 1, 2, 2); buy(f, 3, 3);
    f.fire(f.s.HTW_PreparationTimer);
    const allocations = [...f.allocations], bindings = [...f.tooltipBindings];
    assert.equal(cell(f, 1, 15).value.text, '1', `${name} starts with a nonempty preview`);
    arrange(f);
    f.call('HTW_Information_Display');
    for (const viewer of viewers) assertPreviewUnavailable(f, viewer, status);
    assert.deepEqual(f.allocations, allocations);
    assert.deepEqual(f.tooltipBindings, bindings);
    if (name === 'elimination') assert.deepEqual([13, 14, 15].map(slot => cell(f, 3, slot).value.text), ['3', '2', '0'], 'surviving arena keeps its own locked composition');
    if (name === 'inactive viewer') assert.deepEqual(incomingData(f, 2).cells.map(c => c.value), ['3', '0', '1'], 'active teammate retains the same receiving plan');
    if (name === 'resolution') {
      assert.equal(f.s.HTW_Phase, 3);
      f.call('HTW_Waves_Prepare');
      f.call('HTW_Information_Display');
      for (const viewer of [1, 2, 3, 4]) {
        assert.deepEqual([10, 11, 12].map(slot => cell(f, viewer, slot).value.text), ['0', '0', '0']);
        assert.deepEqual(incomingData(f, viewer).cells.map(c => c.value), ['3', '0', '0']);
        assert.doesNotMatch(incomingData(f, viewer).cells[0].tooltip, /Locked full wave/);
      }
    }
  }
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
