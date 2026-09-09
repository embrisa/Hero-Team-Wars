import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { createJassRuntime, rawcode } from './jass-runtime-harness.mjs';
import { fixture, list, counts, snapshot, incomingData, rootOf } from './v28-runtime-fixture.mjs';

// Source execution only: explicit native event boundaries, never a Warcraft VM
// or proof of rendering, native XP thresholds, live clicks or multiplayer sync.
const read = path => readFileSync(new URL(`../scripts/mcp/${path}`, import.meta.url), 'utf8');
const commands = ['menu', 'next', 'prep', 'combat', 'hold', 'gold 1000', 'xp 1000',
  'levelup', 'heal', 'addlives', 'nextwave', 'reset', 'help'];
const tickActions = () => JSON.parse(read('triggers/runtime-tick.trigger.json')).actions;
const tick = f => { for (const action of tickActions()) { assert.equal(action.type, 'call_function'); f.call(action.function); } };
const button = (f, viewer, slot) => f.s.HTW_DevUIButton[viewer * 16 + slot];
const run = (f, command, player = 1) => f.call('HTW_Dev_Run', player, command);
const calls = (f, name) => f.functionCalls.filter(called => called === name).length;
const natives = (f, name) => f.nativeCalls.filter(called => called.name === name);
function devFixture(options = {}) {
  const f = fixture({ activeHumanIds: [1], manifestEvents: true, ...options });
  f.call('HTW_Dev_Initialize');
  f.call('HTW_DevMenu_Initialize');
  return f;
}
function state(f) {
  return { economy: snapshot(f), phase: f.s.HTW_Phase, wave: f.s.HTW_Wave, round: f.s.HTW_Round,
    active: f.s.HTW_WaveActive, resolved: f.s.HTW_ResolutionApplied, lastResolved: f.s.HTW_LastResolvedWave,
    terminal: f.s.HTW_TerminalState, over: f.s.HTW_MatchOver, guard: f.s.HTW_TransitionGuard,
    lives: list(f.s.HTW_TeamLives, 6), living: list(f.s.HTW_TeamLiving, 6),
    deaths: list(f.s.HTW_TeamDeathsThisWave, 6), creepCounts: list(f.s.HTW_ArenaCreepCount, 6),
    selected: list(f.s.HTW_HeroSelectedByPlayer, 24), alive: list(f.s.HTW_HeroAliveByPlayer, 24),
    accounted: list(f.s.HTW_HeroDeathAccountedByPlayer, 24), aliveCount: f.s.HTW_AliveHeroCount,
    held: f.s.HTW_DevClockHeld, heldPhase: f.s.HTW_DevClockPhase,
    heldRemaining: f.s.HTW_DevClockRemaining, heldPlayer: f.s.HTW_DevClockPlayer,
    menu: list(f.s.HTW_DevMenuOpen, 4), used: f.s.HTW_DevUsed,
    units: f.units.map(unit => ({ type: unit.unitType, owner: unit.owner.id, x: unit.x, y: unit.y,
      removed: unit.removed, life: unit.life, mana: unit.mana, level: unit.level, xp: unit.xp,
      cooldowns: unit.cooldownResets, orders: [...unit.orders] })),
    timers: f.timers.map(timer => ({ active: timer.active, remaining: timer.remaining, duration: timer.duration,
      periodic: timer.periodic, destroyed: timer.destroyed })) };
}
function reject(f, command, player = 1) {
  const before = state(f), events = f.eventCalls.length;
  assert.equal(run(f, command, player), false, `${player}: ${JSON.stringify(command)}`);
  assert.deepEqual(state(f), before, `rejected command mutated state: ${command}`);
  assert.equal(f.eventCalls.length, events, 'rejection must not emit gameplay events');
}
function assertCleanPreparation(f, wave) {
  assert.equal(f.s.HTW_Phase, 1); assert.equal(f.s.HTW_Wave, wave);
  assert.equal(f.s.HTW_WaveActive, true); assert.equal(f.s.HTW_ResolutionApplied, false);
  assert.equal(f.s.HTW_SendPlanLocked, false); assert.equal(f.s.HTW_DevClockHeld, false);
  assert.ok(f.creeps().every(unit => unit.removed));
  assert.ok(snapshot(f).queues.flat().every(value => value === 0));
  assert.ok(snapshot(f).threat.every(value => value === 0));
  assert.ok(snapshot(f).destination.every(value => value === 0));
  for (let team = 1; team <= 6; team++) {
    assert.deepEqual(counts(f, 'HTW_PlanRemaining', team), [0, 0, 0]);
    assert.deepEqual(counts(f, 'HTW_PlanSends', team), [0, 0, 0]);
    assert.equal(f.s.HTW_ArenaCreepCount[team], 0);
  }
  assert.equal(f.s.HTW_PreparationTimer.active, true);
  assert.equal(f.s.HTW_PreparationTimer.remaining, 35);
  assert.equal(f.s.HTW_PreparationTimer.periodic, false);
  if (f.s.HTW_CombatTimer) assert.equal(f.s.HTW_CombatTimer.active, false);
  assert.deepEqual(counts(f, 'HTW_PlanBase', 1), [3, 0, 0]);
}

test('v28 harness: variable events execute source actions synchronously and retain the execution bound', () => {
  const source = `function OnEvent takes nothing returns nothing
    set Count = Count + 1
endfunction
function Fire takes nothing returns nothing
    set Event = 1.
    set Observed = Count
    set Event = 1.
    set Event = 0.
endfunction`;
  const config = { sources: [{ path: 'variable-event-boundary.j', source }], globals: [
    { name: 'Event', type: 'real', initial: 0. }, { name: 'Count', type: 'integer', initial: 0 }, { name: 'Observed', type: 'integer', initial: 0 }],
  variableEvents: [{ name: 'Event', equals: 1, actions: ['OnEvent'] }] };
  const f = createJassRuntime(config);
  f.call('Fire'); assert.equal(f.state.Observed, 1); assert.equal(f.state.Count, 1);
  f.call('Fire'); assert.equal(f.state.Observed, 2); assert.equal(f.eventCalls.length, 2);
  assert.throws(() => createJassRuntime({ ...config, variableEvents: [{ name: 'Missing', equals: 1, actions: [] }] }), /Invalid variable event/);
  assert.throws(() => createJassRuntime({ ...config, variableEvents: [{ name: 'Event', equals: 1, actions: ['Unknown'] }] }), /Invalid variable event action/);
  const recursive = source.replace('set Count = Count + 1', 'set Event = 0.\n    call Fire()');
  assert.throws(() => createJassRuntime({ ...config, sources: [{ path: 'recursive-event.j', source: recursive }], maxSteps: 20 }).call('Fire'), /step limit/);
});

test('v28 parser: numeric bounds and malformed arguments reject atomically before any action', () => {
  const f = devFixture();
  for (const [verb, max] of [['gold', 100000], ['xp', 100000], ['level', 10], ['lives', 999], ['wave', 100]]) {
    for (const value of ['0', '-1', '+1', '1.0', '1e2', '1x', 'x1', '1 2', '1\t2', '1\n2',
      '０１', '١', '2147483648', '9'.repeat(128), String(max + 1)]) reject(f, `${verb} ${value}`);
  }
  for (const command of ['', ' ', 'unknown', 'level', 'lives', 'wave', '-dev gold 1', 'gold\t1000',
    ...commands.filter(command => !command.includes(' ')).map(command => `${command} 1`)]) reject(f, command);
  for (const [value, maximum, expected] of [['00001', 10, 1], ['10', 10, 10], ['999', 999, 999],
    ['100', 100, 100], ['100000', 100000, 100000], ['000', 10, -1]]) {
    assert.equal(f.call('HTW_Dev_ParsePositive', value, maximum), expected);
  }
});

test('v28 eligibility: disabled, multiplayer, foreign sender and non-MVP calls cannot change state', () => {
  const attempts = [...commands, 'gold', 'xp', 'level 2', 'lives 1', 'wave 100', 'resume'];
  for (const arrange of [f => { f.s.HTW_DevEnabled = false; }, f => { f.players[1].slot = 1; },
    f => { f.s.HTW_ActivePlayerCount = 12; }, f => { f.s.HTW_TeamCount = 6; },
    f => { f.players[0].controller = 2; }, f => { f.players[0].slot = 0; }]) {
    const f = devFixture(); arrange(f);
    assert.equal(f.call('HTW_Dev_CanUse', 1), false);
    for (const command of attempts) reject(f, command);
  }
  const f = devFixture();
  assert.equal(f.call('HTW_Dev_CanUse', 1), true);
  for (const player of [-1, 0, 2, 3, 4, 5, 24]) for (const command of attempts) reject(f, command, player);
  for (const solo of [1, 2, 3, 4]) {
    const g = devFixture({ activeHumanIds: [solo] });
    assert.deepEqual([1, 2, 3, 4].map(p => g.call('HTW_Dev_CanUse', p)), [1, 2, 3, 4].map(p => p === solo));
    const gold = list(g.s.HTW_PlayerGold, 4);
    assert.equal(run(g, 'gold 1', solo), true);
    assert.deepEqual(list(g.s.HTW_PlayerGold, 4), gold.map((value, i) => value + (i + 1 === solo ? 1 : 0)));
  }
});

test('v28 chat: exact prefix and registered sender context dispatch once; invalid chat is inert', () => {
  const f = devFixture();
  assert.equal(f.s.HTW_DevChatTrigger.actions.length, 1);
  assert.deepEqual(f.s.HTW_DevChatTrigger.events.map(event => [event.player.id, event.text, event.exact]),
    [0, 1, 2, 3].map(id => [id, '-dev', false]));
  f.call('HTW_Dev_Initialize'); assert.equal(natives(f, 'TriggerRegisterPlayerChatEvent').length, 4);
  for (const text of ['say -dev gold 1', ' -dev gold 1', '-developer gold 1', '-devx', '-dev\tgold 1',
    '--dev gold 1', '-DEV gold 1', '-dev gold 1 2', '-dev level', '-dev ', 'ordinary chat']) {
    const before = state(f); f.chat(1, text); assert.deepEqual(state(f), before, text);
  }
  const before = state(f); f.chat(2, '-dev reset'); assert.deepEqual(state(f), before);
  const start = calls(f, 'HTW_Dev_Run'), mirrors = natives(f, 'SetPlayerState').length;
  assert.equal(f.chat(1, '-dev gold'), 1);
  assert.equal(calls(f, 'HTW_Dev_Run') - start, 1);
  assert.equal(f.s.HTW_PlayerGold[1], 1200); assert.equal(f.players[0].gold, 1200);
  assert.equal(natives(f, 'SetPlayerState').length - mirrors, 1);
  f.chat(1, '-dev'); assert.equal(f.s.HTW_DevMenuOpen[1], true);
  f.chat(1, '-dev'); assert.equal(f.s.HTW_DevMenuOpen[1], false);
});

test('v28 resources: defaults, inclusive caps and exact levels use the correct native once', () => {
  const f = devFixture(); const hero = f.s.HTW_HeroUnitByPlayer[1];
  for (const [command, delta] of [['gold', 1000], ['gold 1', 1], ['gold 100000', 100000]]) {
    const gold = f.s.HTW_PlayerGold[1], mirrors = natives(f, 'SetPlayerState').length;
    assert.equal(run(f, command), true); assert.equal(f.s.HTW_PlayerGold[1], gold + delta);
    assert.equal(f.players[0].gold, gold + delta); assert.equal(natives(f, 'SetPlayerState').length - mirrors, 1);
  }
  f.s.HTW_PlayerGold[1] = 999999; f.players[0].gold = 999999;
  assert.equal(run(f, 'gold 100000'), true); assert.equal(f.s.HTW_PlayerGold[1], 1000000);
  assert.equal(f.players[0].gold, 1000000); reject(f, 'gold');
  for (const [command, delta] of [['xp', 1000], ['xp 1', 1], ['xp 100000', 100000]]) {
    const before = hero.xp, nativeCount = natives(f, 'AddHeroXP').length;
    assert.equal(run(f, command), true); assert.equal(hero.xp, before + delta);
    assert.equal(natives(f, 'AddHeroXP').length - nativeCount, 1);
    assert.equal(natives(f, 'AddHeroXP').at(-1).args[0], hero);
  }
  assert.equal(run(f, 'level 10'), true); assert.equal(hero.level, 10);
  assert.equal(natives(f, 'SetHeroLevel').length, 1); assert.equal(natives(f, 'UnitStripHeroLevel').length, 0);
  reject(f, 'levelup');
  assert.equal(run(f, 'level 1'), true); assert.equal(hero.level, 1);
  assert.deepEqual(natives(f, 'UnitStripHeroLevel').at(-1).args, [hero, 9]);
  assert.equal(run(f, 'level 1'), true); assert.equal(natives(f, 'UnitStripHeroLevel').length, 1);
  assert.equal(run(f, 'levelup'), true); assert.equal(hero.level, 2);
  assert.equal(natives(f, 'SetHeroLevel').length, 2);
  assert.equal(f.s.HTW_DevUsed, true);
});

test('v28 hero gates: missing, removed and nonhero tracked handles reject all hero commands', () => {
  for (const arrange of [f => { f.s.HTW_HeroSelectedByPlayer[1] = false; },
    f => { f.s.HTW_HeroUnitByPlayer[1] = null; }, f => { f.s.HTW_HeroUnitByPlayer[1].removed = true; },
    f => { f.s.HTW_HeroUnitByPlayer[1].unitType = rawcode('hfoo'); }]) {
    const f = devFixture(); arrange(f);
    for (const command of ['xp', 'level 10', 'levelup', 'heal']) reject(f, command);
  }
});

test('v28 heal: HP, mana, cooldowns and successful revival reconcile tracking without duplicate life charges', () => {
  const f = devFixture(); const hero = f.s.HTW_HeroUnitByPlayer[1];
  hero.life = 12; hero.mana = 3; hero.maxLife = 987; hero.maxMana = 654;
  assert.equal(run(f, 'heal'), true);
  assert.equal(hero.life, 987); assert.equal(hero.mana, 654); assert.equal(hero.cooldownResets, 1);
  assert.equal(natives(f, 'ReviveHero').length, 0); assert.equal(f.s.HTW_AliveHeroCount, 1);
  hero.life = 0; f.death(hero);
  const lives = list(f.s.HTW_TeamLives, 2), deaths = list(f.s.HTW_TeamDeathsThisWave, 2);
  assert.equal(f.s.HTW_AliveHeroCount, 0); assert.equal(f.s.HTW_HeroDeathAccountedByPlayer[1], true);
  assert.equal(run(f, 'heal'), true);
  assert.equal(natives(f, 'ReviveHero').length, 1); assert.equal(hero.life, 987); assert.equal(hero.mana, 654);
  assert.equal(f.s.HTW_AliveHeroCount, 1); assert.equal(f.s.HTW_HeroAliveByPlayer[1], true);
  assert.equal(f.s.HTW_HeroDeathAccountedByPlayer[1], false);
  assert.deepEqual(list(f.s.HTW_TeamLives, 2), lives); assert.deepEqual(list(f.s.HTW_TeamDeathsThisWave, 2), deaths);
  assert.equal(run(f, 'heal'), true); assert.equal(f.s.HTW_AliveHeroCount, 1); assert.equal(natives(f, 'ReviveHero').length, 1);
});

test('v28 heal: failed native revival leaves counters, flags, HP/mana and cooldowns unchanged', () => {
  const f = devFixture(); const hero = f.s.HTW_HeroUnitByPlayer[1];
  hero.life = 0; hero.mana = 0; f.death(hero); f.failRevives(1);
  reject(f, 'heal'); assert.equal(natives(f, 'ReviveHero').length, 1);
  assert.equal(natives(f, 'UnitResetCooldown').length, 0); assert.equal(natives(f, 'SetUnitState').length, 0);
  assert.equal(run(f, 'heal'), true); assert.equal(f.s.HTW_AliveHeroCount, 1);
});

test('v28 lives: exact bounds and capped increments affect only the callers team', () => {
  for (const player of [1, 2, 3, 4]) {
    const f = devFixture({ activeHumanIds: [player] }), team = player <= 2 ? 1 : 2, other = 3 - team;
    assert.equal(run(f, 'lives 1', player), true); assert.equal(f.s.HTW_TeamLives[team], 1);
    assert.equal(run(f, 'addlives', player), true); assert.equal(f.s.HTW_TeamLives[team], 6);
    assert.equal(run(f, 'lives 998', player), true); assert.equal(run(f, 'addlives', player), true);
    assert.equal(f.s.HTW_TeamLives[team], 999); assert.equal(run(f, 'lives 999', player), true);
    assert.equal(f.s.HTW_TeamLives[other], 15); assert.equal(f.s.HTW_TeamLiving[team], true);
  }
});

test('v28 next: actual wave_resolved manifest prepares exactly once and stale completion cannot pay twice', () => {
  const f = devFixture();
  for (let wave = 1; wave <= 3; wave++) {
    assert.equal(f.s.HTW_Wave, wave); assert.equal(run(f, 'next'), true); assert.equal(f.s.HTW_Phase, 2);
    f.call('HTW_Sending_ProcessQueues');
    const callback = f.s.HTW_CombatTimer.callback, gold = f.s.HTW_PlayerGold[1];
    const resolved = calls(f, 'HTW_Events_FireWaveResolved'), prepared = calls(f, 'HTW_Waves_Prepare');
    const rewards = calls(f, 'HTW_Economy_GrantPersonalGold');
    assert.equal(run(f, 'next'), true);
    assertCleanPreparation(f, wave + 1);
    assert.equal(f.s.HTW_LastResolvedWave, wave);
    assert.equal(f.s.HTW_PlayerGold[1], gold + 60); assert.equal(f.players[0].gold, gold + 60);
    assert.equal(calls(f, 'HTW_Economy_GrantPersonalGold') - rewards, 1);
    assert.equal(calls(f, 'HTW_Events_FireWaveResolved') - resolved, 1);
    assert.equal(calls(f, 'HTW_Waves_Prepare') - prepared, 1);
    const after = state(f); f.invoke(callback); assert.deepEqual(state(f), after);
  }
  assert.equal(f.eventCalls.filter(event => event.name === 'HTW_Event_wave_resolved').length, 3);
});

test('v28 hold: countdown stays held even at zero while preparation purchases and combat sends keep working', () => {
  const f = devFixture();
  f.s.HTW_PreparationTimer.remaining = 7.25;
  assert.equal(run(f, 'hold'), true); assert.equal(f.s.HTW_DevClockRemaining, 7.25);
  assert.equal(f.s.HTW_PreparationTimer.active, false);
  f.s.HTW_PreparationTimer.remaining = 0;
  for (let i = 0; i < 3; i++) tick(f);
  f.invoke(f.s.HTW_PreparationTimer.callback);
  assert.equal(f.s.HTW_Phase, 1); assert.equal(f.s.HTW_DevClockHeld, true);
  assert.equal(f.call('HTW_Economy_TryPurchase', 1, 1, 1), true);
  assert.equal(run(f, 'resume'), true);
  assert.equal(f.s.HTW_PreparationTimer.remaining, 7.25); assert.equal(f.s.HTW_PreparationTimer.periodic, false);
  assert.equal(f.s.HTW_DevClockHeld, false); assert.equal(f.s.HTW_DevClockRemaining, 0);
  reject(f, 'resume'); f.fire(f.s.HTW_PreparationTimer); assert.equal(f.s.HTW_Phase, 2);
  f.s.HTW_CombatTimer.remaining = 19.5; assert.equal(run(f, 'hold'), true);
  f.s.HTW_CombatTimer.remaining = 0;
  const creeps = f.creeps().length; tick(f); assert.ok(f.creeps().length > creeps);
  f.invoke(f.s.HTW_CombatTimer.callback); assert.equal(f.s.HTW_Phase, 2);
  assert.equal(run(f, 'hold'), true, 'hold toggles an existing hold off');
  assert.equal(f.s.HTW_CombatTimer.remaining, 19.5); assert.equal(f.s.HTW_CombatTimer.periodic, false);
  const gold = f.s.HTW_PlayerGold[1]; f.fire(f.s.HTW_CombatTimer);
  assert.equal(f.s.HTW_Wave, 2); assert.equal(f.s.HTW_PlayerGold[1], gold + 60);
  assert.equal(natives(f, 'ResumeTimer').length, 0);
});

test('v28 hold: zero remaining resumes as a positive one-shot and eligibility loss cannot strand the clock', () => {
  for (const phase of [1, 2]) for (const loss of ['disabled', 'join', 'leave', 'profile']) {
    const f = devFixture(); if (phase === 2) assert.equal(run(f, 'combat'), true);
    const timer = phase === 1 ? f.s.HTW_PreparationTimer : f.s.HTW_CombatTimer;
    timer.remaining = loss === 'disabled' ? 0 : 2.5;
    assert.equal(run(f, 'menu'), true); assert.equal(run(f, 'hold'), true);
    if (loss === 'disabled') f.s.HTW_DevEnabled = false;
    if (loss === 'join') f.players[1].slot = 1;
    if (loss === 'leave') f.players[0].slot = 0;
    if (loss === 'profile') f.s.HTW_TeamCount = 3;
    const starts = natives(f, 'TimerStart').length; f.call('HTW_Dev_Tick');
    assert.equal(f.s.HTW_DevClockHeld, false); assert.equal(f.s.HTW_DevMenuOpen[1], false);
    assert.equal(timer.active, true); assert.equal(timer.periodic, false);
    assert.equal(timer.remaining, loss === 'disabled' ? 0.01 : 2.5);
    assert.equal(natives(f, 'TimerStart').length - starts, 1);
    f.call('HTW_Dev_Tick'); assert.equal(natives(f, 'TimerStart').length - starts, 1);
    f.call('HTW_Phases_Tick'); assert.equal(f.s.HTW_Phase, phase);
  }
});

test('v28 manual transitions clear held state, preserve guards and abandon without wave rewards', () => {
  for (const phase of [1, 2]) for (const command of ['next', 'prep', 'wave 5', 'nextwave', 'reset', ...(phase === 1 ? ['combat'] : [])]) {
    const f = devFixture(); if (phase === 2) assert.equal(run(f, 'combat'), true);
    assert.equal(run(f, 'hold'), true);
    const rewardCount = calls(f, 'HTW_Economy_GrantPersonalGold');
    assert.equal(run(f, command), true);
    assert.equal(f.s.HTW_DevClockHeld, false); assert.equal(f.s.HTW_DevClockPhase, 0);
    assert.equal(f.s.HTW_DevClockRemaining, 0); assert.equal(f.s.HTW_DevClockPlayer, 0);
    assert.equal(calls(f, 'HTW_Economy_GrantPersonalGold') - rewardCount, phase === 2 && command === 'next' ? 1 : 0);
  }
  const f = devFixture(); assert.equal(run(f, 'combat'), true); assert.equal(run(f, 'hold'), true);
  reject(f, 'combat'); f.s.HTW_TransitionGuard = true;
  for (const command of ['next', 'prep', 'wave 5', 'reset', 'resume']) reject(f, command);
});

test('v28 prep/wave/reset: repeated abandonment clears creeps, all queues and frozen plans without rewards', () => {
  const f = devFixture();
  assert.equal(run(f, 'xp 13'), true); assert.equal(run(f, 'level 4'), true);
  const hero = f.s.HTW_HeroUnitByPlayer[1];
  for (const [command, expectedWave] of [['prep', 1], ['prep', 1], ['wave 100', 100], ['wave 1', 1],
    ['nextwave', 2], ['reset', 1], ['reset', 1]]) {
    assert.equal(f.call('HTW_Economy_TryPurchase', 1, 1, 1), true);
    assert.equal(run(f, 'combat'), true); f.call('HTW_Sending_ProcessQueues');
    assert.ok(f.creeps().some(unit => !unit.removed)); assert.equal(f.s.HTW_SendPlanLocked, true);
    f.s.HTW_PlayerQueueCount[f.call('HTW_PlanKey', 24, 3)] = 9;
    f.s.HTW_PlanRemaining[f.call('HTW_PlanKey', 6, 3)] = 9;
    const gold = list(f.s.HTW_PlayerGold, 24), rewards = calls(f, 'HTW_Economy_GrantPersonalGold');
    const resolved = f.eventCalls.filter(event => event.name === 'HTW_Event_wave_resolved').length;
    assert.equal(run(f, command), true); assertCleanPreparation(f, expectedWave);
    assert.deepEqual(list(f.s.HTW_PlayerGold, 24), gold); assert.equal(hero.xp, 13); assert.equal(hero.level, 4);
    assert.equal(f.s.HTW_HeroUnitByPlayer[1], hero);
    assert.equal(calls(f, 'HTW_Economy_GrantPersonalGold'), rewards);
    assert.equal(f.eventCalls.filter(event => event.name === 'HTW_Event_wave_resolved').length, resolved);
  }
  assert.equal(run(f, 'wave 100'), true); reject(f, 'nextwave');
});

test('v28 terminal reset restores both teams and practice wave one while retaining hero, XP and gold', () => {
  for (const terminal of [1, 2]) {
    const f = devFixture(); const hero = f.s.HTW_HeroUnitByPlayer[1];
    assert.equal(run(f, 'xp 99'), true); assert.equal(run(f, 'level 7'), true);
    assert.equal(run(f, 'combat'), true); f.call('HTW_Sending_ProcessQueues');
    hero.life = 0; f.s.HTW_TeamLives[1] = 1; f.death(hero);
    f.s.HTW_TerminalState = terminal; f.s.HTW_Phase = terminal + 3; f.s.HTW_MatchOver = true;
    const gold = list(f.s.HTW_PlayerGold, 24), rewards = calls(f, 'HTW_Economy_GrantPersonalGold');
    for (const command of ['next', 'prep', 'combat', 'heal', 'lives 15', 'wave 1', 'hold']) reject(f, command);
    assert.equal(run(f, 'reset'), true); assertCleanPreparation(f, 1);
    assert.equal(f.s.HTW_TerminalState, 0); assert.equal(f.s.HTW_MatchOver, false);
    assert.deepEqual(list(f.s.HTW_TeamLives, 2), [15, 15]); assert.deepEqual(list(f.s.HTW_TeamLiving, 2), [true, true]);
    assert.deepEqual(list(f.s.HTW_TeamDeathsThisWave, 2), [0, 0]); assert.equal(f.s.HTW_LastResolvedWave, 0);
    assert.equal(f.s.HTW_AliveHeroCount, 1); assert.equal(f.s.HTW_HeroDeathAccountedByPlayer[1], false);
    assert.equal(hero.xp, 99); assert.equal(hero.level, 7); assert.deepEqual(list(f.s.HTW_PlayerGold, 24), gold);
    assert.equal(calls(f, 'HTW_Economy_GrantPersonalGold'), rewards);
  }
});

test('v28 reset reports failed revival accurately and keeps dead tracking until a successful retry', () => {
  const f = devFixture(); const hero = f.s.HTW_HeroUnitByPlayer[1];
  hero.life = 0; f.death(hero); f.failRevives(1);
  assert.equal(run(f, 'reset'), true); assertCleanPreparation(f, 1);
  assert.equal(hero.life, 0); assert.equal(f.s.HTW_AliveHeroCount, 0);
  assert.equal(f.s.HTW_HeroAliveByPlayer[1], false); assert.equal(f.s.HTW_HeroDeathAccountedByPlayer[1], true);
  assert.ok(f.messages.some(({ message }) => /1 hero revival\(s\) failed/.test(message)));
  assert.equal(run(f, 'heal'), true); assert.equal(f.s.HTW_AliveHeroCount, 1);
});

test('v28 selection: next/prep/combat/wave/reset execute real auto-pick and start the intended wave', () => {
  for (const [command, phase, wave] of [['next', 1, 1], ['prep', 1, 1], ['combat', 2, 1], ['wave 10', 1, 10], ['reset', 1, 1]]) {
    const f = devFixture({ prepare: false });
    assert.equal(f.frames.length, 0); assert.equal(f.s.HTW_Phase, 0);
    assert.equal(run(f, command), true); assert.equal(f.s.HTW_HeroSelectionComplete, true);
    assert.equal(f.s.HTW_HeroSelectedByPlayer[1], true); assert.equal(f.s.HTW_AliveHeroCount, 1);
    assert.equal(f.s.HTW_Phase, phase); assert.equal(f.s.HTW_Wave, wave);
    assert.equal(calls(f, 'HTW_Economy_GrantPersonalGold'), 0);
  }
});

test('v28 frames: identical synchronized allocations, registrations and tooltip binding for all clients', () => {
  const clients = [1, 2, 3, 4].map(localPlayerId => {
    const f = devFixture({ localPlayerId }); const before = state(f);
    f.call('HTW_DevMenu_Display'); assert.deepEqual(state(f), before);
    const devFrames = f.frames.filter(frame => rootOf(f, frame).name === 'HTWDevRoot');
    assert.equal(devFrames.length, 4 * 42);
    assert.equal(f.s.HTW_DevUIClick.events.length, 52); assert.equal(f.s.HTW_DevUIClick.actions.length, 1);
    for (let viewer = 1; viewer <= 4; viewer++) {
      assert.equal(f.s.HTW_DevUIRoot[viewer].visible, viewer === 1 && localPlayerId === 1);
      assert.equal(f.s.HTW_DevUIPanel[viewer].visible, false);
      assert.equal(f.s.HTW_DevMenuOpen[viewer], false);
      for (let slot = 0; slot <= 12; slot++) {
        const frame = button(f, viewer, slot);
        assert.equal(frame.frameType, 'BUTTON'); assert.equal(frame.enabled, true);
        assert.equal(rootOf(f, frame), f.s.HTW_DevUIRoot[viewer]);
        assert.equal(f.s.HTW_DevUIClick.events.filter(event => event.frame === frame).length, 1);
        assert.equal(f.tooltipBindings.filter(binding => binding.frame === frame.handleId).length, 1);
        assert.equal(frame.tooltip.visible, false);
        assert.equal(f.call('HTW_DevMenu_Command', slot), commands[slot]);
      }
    }
    const allocationCount = f.allocations.length, bindings = f.tooltipBindings.length, events = natives(f, 'BlzTriggerRegisterFrameEvent').length;
    for (let i = 0; i < 3; i++) tick(f);
    assert.equal(f.allocations.length, allocationCount); assert.equal(f.tooltipBindings.length, bindings);
    assert.equal(natives(f, 'BlzTriggerRegisterFrameEvent').length, events);
    return { allocations: f.allocations, bindings: f.tooltipBindings,
      events: f.s.HTW_DevUIClick.events.map(event => ({ frame: event.frame.handleId, event: event.event })) };
  });
  for (const client of clients.slice(1)) assert.deepEqual(client, clients[0]);
});

test('v28 buttons/chat parity: all thirteen slots dispatch once from native sender/frame without text parsing', () => {
  for (const [slot, command] of commands.entries()) {
    const chat = devFixture(), click = devFixture();
    chat.call('HTW_DevMenu_Display'); click.call('HTW_DevMenu_Display');
    button(click, 1, slot).text = '-dev gold 99999';
    const chatCalls = calls(chat, 'HTW_Dev_Run'), clickCalls = calls(click, 'HTW_Dev_Run');
    assert.equal(chat.chat(1, `-dev ${command}`), 1); assert.equal(click.click(1, button(click, 1, slot)), 1);
    assert.equal(calls(chat, 'HTW_Dev_Run') - chatCalls, 1, command);
    assert.equal(calls(click, 'HTW_Dev_Run') - clickCalls, 1, command);
    assert.deepEqual(state(click), state(chat), command);
    assert.equal(button(click, 1, slot).enabled, true);
    if (slot === 5) assert.equal(click.s.HTW_PlayerGold[1], 1200, 'gold action applied once');
    if (slot === 6) assert.equal(click.s.HTW_HeroUnitByPlayer[1].xp, 1000, 'XP action applied once');
  }
});

test('v28 button guards: foreign frames, foreign senders, disabled/multiplayer and UI-not-ready are inert', () => {
  const f = devFixture(); f.call('HTW_DevMenu_Display');
  for (const [sender, owner] of [[1, 2], [2, 1], [2, 2], [5, 1]]) for (let slot = 0; slot <= 12; slot++) {
    const before = state(f), start = calls(f, 'HTW_Dev_Run');
    assert.equal(f.click(sender, button(f, owner, slot)), 1); assert.deepEqual(state(f), before);
    assert.equal(calls(f, 'HTW_Dev_Run') - start, sender === owner ? 1 : 0);
  }
  for (const arrange of [f => { f.s.HTW_DevEnabled = false; }, f => { f.players[1].slot = 1; },
    f => { f.s.HTW_DevUIReady = false; }]) {
    const g = devFixture(); g.call('HTW_DevMenu_Display'); arrange(g);
    for (let slot = 0; slot <= 12; slot++) { const before = state(g); g.click(1, button(g, 1, slot)); assert.deepEqual(state(g), before); }
  }
  assert.equal(f.click(1, f.s.HTW_HudRoot[1]), 0, 'unregistered frame has no callback');
});

test('v28 collapse/load: no stale calls, one deferred rebuild/rebind, and refresh never reopens a collapsed panel', () => {
  for (const openAtLoad of [false, true]) {
    const f = devFixture(); f.call('HTW_DevMenu_Display');
    f.click(1, button(f, 1, 0)); assert.equal(f.s.HTW_DevMenuOpen[1], true);
    assert.equal(f.s.HTW_DevUIPanel[1].visible, true);
    f.click(1, button(f, 1, 0)); assert.equal(f.s.HTW_DevMenuOpen[1], false);
    for (let i = 0; i < 3; i++) tick(f);
    assert.equal(f.s.HTW_DevUIPanel[1].visible, false); assert.equal(f.s.HTW_DevMenuOpen[1], false);
    if (openAtLoad) f.click(1, button(f, 1, 0));
    const oldFrames = [...f.frames], oldClick = f.s.HTW_DevUIClick, oldLoad = f.s.HTW_DevUILoad;
    const allocations = f.allocations.length, bindings = f.tooltipBindings.length, nativeStart = f.nativeCalls.length;
    const before = state(f); f.simulateGameLoad();
    assert.equal(f.s.HTW_DevUIReady, false); assert.equal(f.s.HTW_DevUIClick, null); assert.equal(oldClick.destroyed, true);
    assert.deepEqual(f.nativeCalls.slice(nativeStart).map(call => call.name), ['DestroyTrigger']);
    assert.equal(f.allocations.length, allocations); assert.equal(f.tooltipBindings.length, bindings);
    assert.deepEqual(state(f), before);
    tick(f);
    assert.equal(f.s.HTW_DevUIReady, true); assert.notEqual(f.s.HTW_DevUIClick, oldClick);
    assert.equal(f.s.HTW_DevUILoad, oldLoad); assert.equal(f.s.HTW_DevUIClick.events.length, 52);
    assert.equal(f.s.HTW_DevMenuOpen[1], openAtLoad); assert.equal(f.s.HTW_DevUIPanel[1].visible, openAtLoad);
    assert.equal(f.allocations.length, allocations * 2); assert.equal(f.tooltipBindings.length, bindings * 2);
    assert.ok(oldFrames.every(frame => frame.invalid));
    assert.ok(f.s.HTW_DevUIClick.events.every(event => !oldFrames.includes(event.frame)));
    tick(f); assert.equal(f.allocations.length, allocations * 2); assert.equal(f.tooltipBindings.length, bindings * 2);
    assert.equal(natives(f, 'TriggerRegisterGameEvent').length, 2, 'one HUD and one dev load listener');
    const gold = f.s.HTW_PlayerGold[1], start = calls(f, 'HTW_Dev_Run');
    f.click(1, button(f, 1, 5)); assert.equal(f.s.HTW_PlayerGold[1], gold + 1000);
    assert.equal(calls(f, 'HTW_Dev_Run') - start, 1, 'only replacement trigger dispatches');
  }
});

test('v28 resources/HUD: gold mirror and locked incoming preview survive command and frame refresh', () => {
  const f = devFixture();
  assert.equal(f.call('HTW_Economy_TryPurchase', 1, 2, 2), true);
  assert.equal(run(f, 'combat'), true);
  const incoming = incomingData(f, 3), plan = snapshot(f).plans;
  assert.equal(run(f, 'gold'), true); assert.equal(f.s.HTW_PlayerGold[1], 1160); assert.equal(f.players[0].gold, 1160);
  f.call('HTW_DevMenu_Display'); assert.equal(run(f, 'hold'), true); tick(f);
  assert.deepEqual(incomingData(f, 3), incoming);
  assert.deepEqual(snapshot(f).plans.slice(0, 3), plan.slice(0, 3));
  const before = state(f); assert.equal(f.call('HTW_Economy_TryPurchase', 1, 1, 1), false);
  assert.deepEqual(state(f), before);
});
