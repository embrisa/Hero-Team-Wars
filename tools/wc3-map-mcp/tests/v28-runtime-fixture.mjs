import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { createJassRuntime, rawcode } from './jass-runtime-harness.mjs';

const root = new URL('../scripts/mcp/', import.meta.url);
const read = path => readFileSync(new URL(path, root), 'utf8').replace(/^\uFEFF/, '');
const definition = (name, type, initial, array = false) => ({ name, type, initial, array });
const native = (type, arity, fn) => ({ type, arity, fn });

// The composer emits an explicit literal for every scalar global. Keep the
// source-fixture boundary equally explicit; the strict harness must never
// turn an omitted fixture value into a native default.
const composerScalarDefaults = Object.freeze({
  HTW_DevUIClick: null, HTW_DevUILoad: null, HTW_DevChatTrigger: null,
  HTW_HudLoadTrigger: null, HTW_HeroSelectionBuilding: null,
  HTW_HeroSelectionTrigger: null, HTW_HeroSelectionTimer: null,
  HTW_PreparationTimer: null, HTW_CombatTimer: null, HTW_SendTimer: null,
  HTW_CleanupGroup: null, HTW_ArenaRectA: null, HTW_ArenaRectB: null,
  HTW_StartingGold: 0, HTW_SendBudgetStart: 0, HTW_SendBudgetGrowth: 0,
  HTW_SendBudgetMaximum: 0, HTW_BaseFootmen: 0, HTW_FillerFootmen: 0,
  HTW_FillerRiflemen: 0,
});
const explicitComposerGlobals = variables => variables.map(variable => {
  if (variable.array || (Object.hasOwn(variable, 'initial') && variable.initial !== undefined)) {
    return { ...variable };
  }
  assert.ok(Object.hasOwn(composerScalarDefaults, variable.name),
    `missing explicit fixture default for scalar ${variable.name}`);
  return { ...variable, initial: composerScalarDefaults[variable.name] };
});

// Symbolic stand-ins for native enum constants, not a Warcraft enum implementation.
const frameConstants = { ORIGIN_FRAME_GAME_UI: 0, FRAMEPOINT_TOPLEFT: 1,
  FRAMEPOINT_TOPRIGHT: 2, FRAMEPOINT_BOTTOMRIGHT: 3, TEXT_JUSTIFY_TOP: 4, TEXT_JUSTIFY_LEFT: 5,
  FRAMEEVENT_CONTROL_CLICK: 6 };
const iconPaths = new Map(['AHds', 'Adef', 'n26C', 'hfoo', 'hrif', 'hkni', 'H001', 'H002', 'H003', 'H004']
  .map(code => [rawcode(code), `mock-icon:${code}`]));

function fixture({ localPlayerId = 1, prepare = true, manifestEvents = false, activeHumanIds = [1, 2, 3, 4],
  globalDefinitions, initializationSource, initializationFunction = 'HTW_MCP_InitializeVariables', sourceOverrides = {} } = {}) {
  assert.ok(Number.isInteger(localPlayerId) && localPlayerId >= 1 && localPlayerId <= 4);
  // Read fresh on every fixture. A missing production module is a test failure.
  const modules = ['core/state.j', 'core/debug.j', 'core/events.j', 'config/tuning.j',
    'config/teams.j', 'content/send-catalog.j', 'content/send-units.j', 'content/base-waves.j', 'content/heroes.j',
    'systems/economy.j', 'systems/sending.j', 'systems/waves.j', 'systems/phases.j',
    'systems/routing.j', 'systems/elimination.j', 'systems/lives.j', 'systems/heroes.j',
    'systems/information.j', 'systems/wave-plan.j', 'systems/hero-selection.j',
    'systems/dev-tools.j', 'systems/dev-menu.j'];
  const sources = modules.map(path => ({ path, source: sourceOverrides[path] ?? read(path),
    // The composer-generated profile setup is outside this source harness.
    // Use only the real lookup body from this module; fixture profile data below
    // supplies the same environment boundary as generated global declarations.
    ...(path === 'config/teams.j' ? { only: ['HTW_Teams_FindByPlayer'] } : {}),
      ...(path === 'systems/hero-selection.j' ? { only: ['HTW_HeroSelection_AllPlayersReady', 'HTW_HeroSelection_Complete',
      'HTW_HeroSelection_OnTimeout', 'HTW_HeroSelection_AutoPick', 'HTW_HeroSelection_PlayerHasTeammateHero',
      'HTW_HeroSelection_SelectForPlayer', 'HTW_HeroSelection_SelectUnitForPlayer', 'HTW_HeroSelection_DeployHero'] } : {}) }));
  if (initializationSource) sources.unshift({ path: 'generated/war3map.j', source: initializationSource,
    only: [initializationFunction] });
  const variables = readdirSync(new URL('variables/', root)).filter(path => path.endsWith('.variable.json'))
    .flatMap(path => JSON.parse(read(`variables/${path}`)));
  const globals = globalDefinitions ? globalDefinitions.map(global => ({ ...global })) : explicitComposerGlobals(variables);
  const nativeConstantNames = new Set(['PLAYER_NEUTRAL_AGGRESSIVE', 'MAP_CONTROL_USER', 'MAP_CONTROL_COMPUTER',
    'PLAYER_SLOT_STATE_PLAYING', 'PLAYER_STATE_RESOURCE_GOLD', 'EVENT_PLAYER_UNIT_SPELL_EFFECT',
    'EVENT_GAME_LOADED', 'UNIT_STATE_LIFE', 'UNIT_TYPE_DEAD', 'PLAYER_STATE_GIVES_BOUNTY',
    'UNIT_STATE_MANA', 'UNIT_STATE_MAX_LIFE', 'UNIT_STATE_MAX_MANA', 'UNIT_TYPE_HERO', ...Object.keys(frameConstants)]);
  const add = (name, type, initial, array = false) => {
    if (globalDefinitions && !globals.some(value => value.name === name)) {
      assert.ok(nativeConstantNames.has(name), `generated source missing required global ${name}`);
    }
    if (!globals.some(value => value.name === name)) globals.push(definition(name, type, initial, array));
  };
  // Explicit composer/native environment. Never infer undeclared gameplay globals.
  for (const name of ['ActivePlayerCount', 'TeamCount', 'ArenaCount', 'LivingTeamCount', 'RouteOffset', 'RouteDestinationTeam']) add(`HTW_${name}`, 'integer', 0);
  for (const name of ['TeamMemberA', 'TeamMemberB', 'TeamDestination', 'LivingTeamIds']) add(`HTW_${name}`, 'integer', undefined, true);
  add('HTW_TeamLiving', 'boolean', undefined, true);
  add('HTW_RoutingLocked', 'boolean', false);
  add('HTW_ArenaRect', 'rect', undefined, true);
  for (const name of ['round_start', 'wave_resolved']) add(`HTW_Event_${name}`, 'real', 0.);
  for (const [name, value] of Object.entries({ PLAYER_NEUTRAL_AGGRESSIVE: 12, MAP_CONTROL_USER: 1,
    MAP_CONTROL_COMPUTER: 2, PLAYER_SLOT_STATE_PLAYING: 1, PLAYER_STATE_RESOURCE_GOLD: 1,
    EVENT_PLAYER_UNIT_SPELL_EFFECT: 1, EVENT_GAME_LOADED: 3,
    UNIT_STATE_LIFE: 0, UNIT_TYPE_DEAD: 1, PLAYER_STATE_GIVES_BOUNTY: 2,
    UNIT_STATE_MANA: 2, UNIT_STATE_MAX_LIFE: 3, UNIT_STATE_MAX_MANA: 4, UNIT_TYPE_HERO: 5,
    ...frameConstants })) {
    add(name, 'integer', value);
    globals.find(global => global.name === name).constant = true;
  }
  const players = Array.from({ length: 25 }, (_, id) => ({ id, controller: 1, slot: 1, gold: 0 }));
  for (let id = 1; id <= 4; id++) if (!activeHumanIds.includes(id)) players[id - 1].slot = 0;
  const units = [];
  const timers = [];
  const triggers = [];
  const frames = [];
  const allocations = [];
  const tooltipBindings = [];
  const messages = [];
  let enumUnit = null;
  let triggerUnit = null;
  let abilityId = 0;
  let failedCreates = 0;
  let failedRevives = 0;
  let eventContext = null;
  let handleId = 0;
  const handle = (type, values = {}) => ({ handleId: ++handleId, handleType: type, ...values });
  let gameUI = handle('framehandle', { name: 'gameUI' });
  const requireFrame = frame => {
    assert.ok(frame === gameUI || frames.includes(frame), 'native requires an allocated frame from this client');
    assert.ok(!frame.invalid, 'native must not use a stale saved framehandle');
    return frame;
  };
  const createFrame = (frameType, name, parent, inherits, priority, context, creationNative) => {
    requireFrame(parent);
    const frame = handle('framehandle', { frameType, name, parent, inherits, priority, context,
      points: [], visible: true, enabled: true, text: '', texture: null });
    frames.push(frame);
    allocations.push({ creationNative, handleId: frame.handleId, frameType, name,
      parent: parent.handleId, inherits, priority, context });
    return frame;
  };
  const frameNative = (type, params, fn) => ({ ...native(type, params.length, fn),
    params: params.map(type => ({ type })) });
  const makeUnit = (owner, unitType, x, y, facing) => {
    if (failedCreates > 0) { failedCreates--; return null; }
    const unit = handle('unit', { owner, unitType, x, y, facing, life: 100, maxLife: 100,
      mana: 50, maxMana: 50, level: 1, xp: 0, cooldownResets: 0, removed: false, orders: [] });
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
    GetTriggerPlayer: native('player', 0, () => { assert.ok(eventContext?.player, 'requires a player event'); return eventContext.player; }),
    GetEventPlayerChatString: native('string', 0, () => { assert.equal(eventContext?.kind, 'chat'); return eventContext.text; }),
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
    StringCase: native('string', 2, (value, upper) => { assert.equal(typeof upper, 'boolean'); return upper ? value.toUpperCase() : value.toLowerCase(); }),
    S2I: native('integer', 1, value => {
      assert.equal(value.length, 1, 'this explicit parser boundary supports single-character S2I only');
      return /^[0-9]$/.test(value) ? Number(value) : 0;
    }),
    ModuloInteger: native('integer', 2, (a, b) => ((a % b) + b) % b),
    Rect: native('rect', 4, (minX, minY, maxX, maxY) => ({ min_x: minX, min_y: minY, max_x: maxX, max_y: maxY })),
    CreateRegion: native('region', 0, () => handle('region', { rects: [] })),
    RegionAddRect: native('nothing', 2, (region, rect) => { assert.equal(region.handleType, 'region'); region.rects.push(rect); }),
    GetRectCenterX: native('real', 1, rect => (rect.min_x + rect.max_x) / 2),
    GetRectCenterY: native('real', 1, rect => (rect.min_y + rect.max_y) / 2),
    CreateUnit: native('unit', 5, makeUnit),
    SetUnitPosition: native('nothing', 3, (unit, x, y) => { assert.ok(unit && !unit.removed); unit.x = x; unit.y = y; }),
    SetUnitOwner: native('nothing', 3, (unit, player, changeColor) => { assert.ok(unit && !unit.removed); assert.ok(players.includes(player)); assert.equal(typeof changeColor, 'boolean'); unit.owner = player; }),
    PanCameraToTimed: native('nothing', 3, (x, y, duration) => { assert.ok([x, y, duration].every(Number.isFinite)); }),
    SelectUnit: native('nothing', 2, (unit, selected) => { assert.ok(unit && !unit.removed); assert.equal(typeof selected, 'boolean'); }),
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
    GetUnitState: native('real', 2, (unit, state) => {
      const field = { 0: 'life', 2: 'mana', 3: 'maxLife', 4: 'maxMana' }[state];
      assert.ok(field, `unmocked unit state ${state}`); assert.ok(unit && !unit.removed); return unit[field];
    }),
    SetUnitState: native('nothing', 3, (unit, state, value) => {
      const field = { 0: 'life', 2: 'mana' }[state];
      assert.ok(field, `unmocked writable unit state ${state}`); assert.ok(unit && !unit.removed); unit[field] = value;
    }),
    GetWidgetLife: native('real', 1, unit => unit.life),
    GetHeroLevel: native('integer', 1, unit => unit.level ?? 1),
    AddHeroXP: native('nothing', 3, (unit, amount, eyeCandy) => {
      assert.ok(unit && !unit.removed); assert.ok(Number.isInteger(amount) && amount > 0); assert.equal(typeof eyeCandy, 'boolean');
      unit.xp += amount; // XP-to-level thresholds are a Warcraft runtime concern.
    }),
    SetHeroLevel: native('nothing', 3, (unit, level, eyeCandy) => {
      assert.ok(unit && !unit.removed); assert.ok(Number.isInteger(level) && level > unit.level); assert.equal(typeof eyeCandy, 'boolean'); unit.level = level;
    }),
    UnitStripHeroLevel: native('boolean', 2, (unit, levels) => {
      assert.ok(unit && !unit.removed); assert.ok(Number.isInteger(levels) && levels > 0 && levels < unit.level); unit.level -= levels; return true;
    }),
    UnitResetCooldown: native('nothing', 1, unit => { assert.ok(unit && !unit.removed); unit.cooldownResets++; }),
    GetUnitTypeId: native('integer', 1, unit => unit?.removed ? 0 : unit?.unitType ?? 0),
    IsUnitType: native('boolean', 2, (unit, type) => {
      assert.ok(unit); assert.ok([1, 5].includes(type));
      return type === 1 ? unit.life <= 0 || unit.removed : ['H001', 'H002', 'H003', 'H004'].map(rawcode).includes(unit.unitType);
    }),
    ReviveHero: native('boolean', 4, (unit, x, y, eyeCandy) => {
      assert.ok(unit && !unit.removed); assert.ok(unit.life <= 0, 'ReviveHero requires a dead unit');
      assert.equal(typeof eyeCandy, 'boolean');
      if (failedRevives > 0) { failedRevives--; return false; }
      unit.life = unit.maxLife; unit.x = x; unit.y = y; return true;
    }),
    CreateTimer: native('timer', 0, () => { const timer = handle('timer', { active: false, remaining: 0 }); timers.push(timer); return timer; }),
    TimerStart: native('nothing', 4, (timer, duration, periodic, callback) => {
      assert.equal(typeof callback, 'function'); assert.ok(Number.isFinite(duration) && duration >= 0);
      Object.assign(timer, { active: true, duration, remaining: duration, periodic, callback });
    }),
    PauseTimer: native('nothing', 1, timer => { timer.active = false; }),
    TimerGetRemaining: native('real', 1, timer => timer.remaining),
    DestroyTimer: native('nothing', 1, timer => { timer.active = false; timer.destroyed = true; }),
    GetPlayersAll: native('force', 0, () => players),
    DisplayTextToForce: native('nothing', 2, (force, message) => messages.push({ audience: force, message })),
    DisplayTimedTextToPlayer: native('nothing', 5, (player, x, y, seconds, message) => messages.push({ audience: player, message })),
    DisplayTextToPlayer: native('nothing', 4, (player, x, y, message) => messages.push({ audience: player, message })),
    GetLocalPlayer: native('player', 0, () => players[localPlayerId - 1]),
    GetPlayerName: native('string', 1, player => `Player ${player.id + 1}`),
    CreateTrigger: native('trigger', 0, () => { const trigger = handle('trigger', { events: [], actions: [] }); triggers.push(trigger); return trigger; }),
    TriggerRegisterPlayerChatEvent: native('nothing', 4, (trigger, player, text, exact) => {
      assert.ok(triggers.includes(trigger) && !trigger.destroyed); assert.ok(players.includes(player));
      assert.equal(typeof text, 'string'); assert.equal(typeof exact, 'boolean'); trigger.events.push({ kind: 'chat', player, text, exact });
    }),
    TriggerRegisterPlayerUnitEvent: native('nothing', 4, (trigger, player, event, filter) => trigger.events.push({ player, event, filter })),
    TriggerRegisterGameEvent: native('nothing', 2, (trigger, event) => {
      assert.equal(trigger.handleType, 'trigger'); assert.equal(event, 3); trigger.events.push({ event });
    }),
    TriggerAddAction: native('nothing', 2, (trigger, callback) => trigger.actions.push(callback)),
    DisableTrigger: native('nothing', 1, trigger => { trigger.disabled = true; }),
    DestroyTrigger: native('nothing', 1, trigger => { trigger.destroyed = true; }),
    BlzTriggerRegisterFrameEvent: frameNative('nothing', ['trigger', 'framehandle', 'integer'], (trigger, frame, event) => {
      assert.ok(triggers.includes(trigger) && !trigger.destroyed); requireFrame(frame);
      assert.equal(event, frameConstants.FRAMEEVENT_CONTROL_CLICK); trigger.events.push({ kind: 'frame', frame, event });
    }),
    BlzGetTriggerFrame: frameNative('framehandle', [], () => {
      assert.equal(eventContext?.kind, 'frame'); return requireFrame(eventContext.frame);
    }),
    BlzGetOriginFrame: frameNative('framehandle', ['integer', 'integer'], (origin, index) => {
      assert.equal(origin, frameConstants.ORIGIN_FRAME_GAME_UI); assert.equal(index, 0); return gameUI;
    }),
    BlzCreateFrame: frameNative('framehandle', ['string', 'framehandle', 'integer', 'integer'], (name, parent, priority, context) => {
      assert.ok(['QuestButtonBaseTemplate', 'ScriptDialogButton'].includes(name), `unmocked frame template ${name}`);
      return createFrame(name === 'ScriptDialogButton' ? 'BUTTON' : 'BACKDROP', name, parent, name, priority, context, 'BlzCreateFrame');
    }),
    BlzCreateFrameByType: frameNative('framehandle', ['string', 'string', 'framehandle', 'string', 'integer'], (type, name, parent, inherits, context) => {
      assert.ok(['TEXT', 'BUTTON', 'BACKDROP', 'FRAME'].includes(type), `unsupported frame type ${type}`);
      assert.ok(['', 'QuestButtonBaseTemplate'].includes(inherits), `unsupported frame template ${inherits}`);
      return createFrame(type, name, parent, inherits, 0, context, 'BlzCreateFrameByType');
    }),
    BlzFrameSetPoint: frameNative('nothing', ['framehandle', 'integer', 'framehandle', 'integer', 'real', 'real'], (frame, point, relative, relativePoint, x, y) => {
      requireFrame(frame); requireFrame(relative);
      assert.ok([1, 2, 3].includes(point)); assert.ok([1, 2, 3].includes(relativePoint));
      frame.points.push({ point, relative, relativePoint, x, y });
    }),
    BlzFrameSetAbsPoint: frameNative('nothing', ['framehandle', 'integer', 'real', 'real'], (frame, point, x, y) => {
      requireFrame(frame); assert.ok([1, 2, 3].includes(point)); frame.points.push({ point, x, y });
    }),
    BlzFrameSetSize: frameNative('nothing', ['framehandle', 'real', 'real'], (frame, width, height) => {
      assert.ok(width >= 0 && height >= 0); Object.assign(requireFrame(frame), { width, height });
    }),
    BlzFrameSetTextAlignment: frameNative('nothing', ['framehandle', 'integer', 'integer'], (frame, vertical, horizontal) => {
      assert.equal(vertical, frameConstants.TEXT_JUSTIFY_TOP); assert.equal(horizontal, frameConstants.TEXT_JUSTIFY_LEFT);
      requireFrame(frame).alignment = { vertical, horizontal };
    }),
    BlzFrameSetScale: frameNative('nothing', ['framehandle', 'real'], (frame, scale) => {
      assert.ok(scale > 0); requireFrame(frame).scale = scale;
    }),
    BlzFrameSetEnable: frameNative('nothing', ['framehandle', 'boolean'], (frame, enabled) => { requireFrame(frame).enabled = enabled; }),
    BlzFrameSetVisible: frameNative('nothing', ['framehandle', 'boolean'], (frame, visible) => { requireFrame(frame).visible = visible; }),
    BlzFrameSetText: frameNative('nothing', ['framehandle', 'string'], (frame, text) => {
      assert.ok(requireFrame(frame).frameType === 'TEXT' || frame.name === 'ScriptDialogButton'); frame.text = text;
    }),
    BlzFrameSetTexture: frameNative('nothing', ['framehandle', 'string', 'integer', 'boolean'], (frame, texture, flag, blend) => {
      assert.equal(requireFrame(frame).frameType, 'BACKDROP'); assert.equal(flag, 0); assert.equal(blend, true);
      frame.texture = texture;
    }),
    BlzFrameSetTooltip: frameNative('nothing', ['framehandle', 'framehandle'], (frame, tooltip) => {
      requireFrame(frame); requireFrame(tooltip);
      frame.tooltip = tooltip;
      tooltipBindings.push({ frame: frame.handleId, tooltip: tooltip.handleId });
    }),
    BlzGetAbilityIcon: frameNative('string', ['integer'], code => {
      assert.ok(iconPaths.has(code), `unmocked icon rawcode ${code}`); return iconPaths.get(code);
    }),
  };
  const variableEvents = manifestEvents ? ['round-start', 'wave-resolved'].map(path => {
    const trigger = JSON.parse(read(`triggers/${path}.trigger.json`));
    assert.equal(trigger.enabled, true); assert.equal(trigger.initially_on, true);
    assert.deepEqual(trigger.conditions, [{ type: 'always' }]);
    assert.equal(trigger.events.length, 1); assert.equal(trigger.events[0].type, 'custom_event');
    return { name: `HTW_Event_${trigger.events[0].name}`, equals: 1, actions: trigger.actions.map(action => {
      assert.equal(action.type, 'call_function'); return action.function;
    }) };
  }) : [];
  const runtime = createJassRuntime({ sources, globals, natives, variableEvents });
  if (initializationSource) runtime.call(initializationFunction);
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
    if (prepare && activeHumanIds.includes(playerId)) {
      const heroType = runtime.call('HTW_Content_HeroTypeForSlot', playerId);
      s.HTW_HeroSelectedByPlayer[playerId] = true;
      s.HTW_HeroAliveByPlayer[playerId] = true;
      s.HTW_HeroTypeByPlayer[playerId] = heroType;
      s.HTW_HeroUnitByPlayer[playerId] = makeUnit(players[playerId - 1], heroType, 0, 0, 0);
    }
    s.HTW_WarCampByPlayer[playerId] = makeUnit(players[playerId - 1], rawcode('hhou'), 0, 0, 0);
    s.HTW_PlayerGold[playerId] = 200;
    players[playerId - 1].gold = 200;
  }
  if (prepare) {
    s.HTW_HeroSelectionComplete = true;
    runtime.call('HTW_Waves_Prepare');
    s.HTW_AliveHeroCount = activeHumanIds.length;
  }
  function dispatch(context, matches) {
    assert.equal(eventContext, null, 'source fixture expects a top-level native event');
    eventContext = context;
    let dispatched = 0;
    try {
      for (const trigger of [...triggers]) if (!trigger.destroyed && !trigger.disabled && trigger.events.some(matches)) {
        for (const action of trigger.actions) { runtime.invoke(action); dispatched++; }
      }
    } finally { eventContext = null; }
    return dispatched;
  }
  return { ...runtime, s, players, units, timers, triggers, messages, frames, allocations, tooltipBindings,
    get gameUI() { return gameUI; },
    simulateGameLoad() {
      // Explicit environment boundary only; this does not emulate save files.
      const trigger = s.HTW_HudLoadTrigger;
      assert.deepEqual(trigger.events, [{ event: 3 }]);
      assert.equal(trigger.actions.length, 1);
      for (const frame of [...frames, gameUI]) frame.invalid = true;
      gameUI = handle('framehandle', { name: 'gameUI' });
      dispatch({ kind: 'load' }, event => event.event === 3 && !event.kind);
    },
    creeps: () => units.filter(unit => unit.owner === players[12]),
    failCreates: count => { failedCreates = count; },
    failRevives: count => { failedRevives = count; },
    chat(playerId, text) {
      const player = players[playerId - 1]; assert.ok(player);
      return dispatch({ kind: 'chat', player, text }, event => event.kind === 'chat' && event.player === player &&
        (event.exact ? event.text === text : text.includes(event.text)));
    },
    click(playerId, frame) {
      const player = players[playerId - 1]; assert.ok(player); requireFrame(frame);
      return dispatch({ kind: 'frame', player, frame }, event => event.kind === 'frame' && event.frame === frame);
    },
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
// Use the contract key independently of HTW_Information_Key so a stride/slot bug
// cannot make both the implementation and its assertions agree accidentally.
const cell = (f, viewer, slot) => {
  const key = viewer * 32 + slot;
  const icon = f.s.HTW_HudIcon[key];
  const value = f.s.HTW_HudValue[key];
  const tooltip = f.s.HTW_HudTooltipText[key];
  assert.ok(icon && value && tooltip, `missing HUD cell ${viewer}:${slot}`);
  return { icon, value, tooltip, hover: icon.parent };
};
const cellData = (f, viewer, slot) => {
  const c = cell(f, viewer, slot);
  return { icon: c.icon.texture, value: c.value.text, tooltip: c.tooltip.text };
};
const hudData = f => [1, 2, 3, 4].map(viewer => ({ title: f.s.HTW_HudTitle[viewer].text,
  incoming: f.s.HTW_HudIncoming[viewer].text,
  cells: Array.from({ length: 15 }, (_, slot) => cellData(f, viewer, slot + 1)) }));
const incomingData = (f, viewer) => ({ summary: f.s.HTW_HudIncoming[viewer].text,
  cells: [13, 14, 15].map(slot => cellData(f, viewer, slot)) });
const rootOf = (f, frame) => {
  while (frame.parent !== f.gameUI) { assert.ok(frame.parent); frame = frame.parent; }
  return frame;
};
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


export { fixture, frameConstants, list, key, counts, plans, mirrorCalls, cell, cellData, hudData, incomingData, rootOf, snapshot, reject, buy };
