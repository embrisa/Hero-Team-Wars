// HTW Controller spell kit.  A2Q1/A2W1/A2E1/A2R1 are visible ANcl-style
// dispatch abilities attached to H003 by typed object data.  Their scripted
// effects are deliberately gated to the purchased H003 unit.  The hidden
// n2D1 caster carries A2S1 (native Aslo slow) and A2T1 (native AHtb stun), so
// Warcraft owns the timed buff and stun expiration for every affected unit.

function HTW_Abilities_IsLiving takes unit target returns boolean
    return target != null and GetWidgetLife(target) > 0.405 and not IsUnitType(target, UNIT_TYPE_DEAD)
endfunction

function HTW_Abilities_IsHostileLiving takes unit caster, unit target returns boolean
    return HTW_Abilities_IsLiving(target) and not IsUnitAlly(target, GetOwningPlayer(caster))
endfunction

function HTW_Abilities_IsSameTeamHero takes unit caster, unit target returns boolean
    local integer casterPlayerId
    local integer targetPlayerId
    local integer casterTeam
    local integer targetTeam
    if target == null or not IsUnitType(target, UNIT_TYPE_HERO) then
        return false
    endif
    if not IsUnitAlly(target, GetOwningPlayer(caster)) then
        return false
    endif
    set casterPlayerId = HTW_Heroes_PlayerId(caster)
    set targetPlayerId = HTW_Heroes_PlayerId(target)
    if casterPlayerId <= 0 or targetPlayerId <= 0 then
        return false
    endif
    set casterTeam = HTW_Teams_FindByPlayer(casterPlayerId)
    set targetTeam = HTW_Teams_FindByPlayer(targetPlayerId)
    return casterTeam > 0 and casterTeam == targetTeam
endfunction

function HTW_Abilities_ApplyArcaneLance takes unit caster, unit target, integer level returns nothing
    local real damage
    if not HTW_Abilities_IsHostileLiving(caster, target) then
        return
    endif
    set damage = 80.
    if level == 2 then
        set damage = 120.
    elseif level == 3 then
        set damage = 160.
    elseif level >= 4 then
        set damage = 200.
    endif
    call UnitDamageTarget(caster, target, damage, false, true, ATTACK_TYPE_MAGIC, DAMAGE_TYPE_MAGIC, WEAPON_TYPE_WHOKNOWS)
endfunction

function HTW_Abilities_CastNativeSlow takes unit caster, unit target, integer level returns nothing
    local unit dummy
    if not HTW_Abilities_IsHostileLiving(caster, target) then
        return
    endif
    // A2S1 is the custom hidden copy of built-in Aslo.  n2D1 has the ability
    // in its unit ability list.  Add no visible ability to H003 and let the
    // native Aslo buff expire itself after the configured duration.
    set dummy = CreateUnit(GetOwningPlayer(caster), 'n2D1', GetUnitX(caster), GetUnitY(caster), 0.)
    call SetUnitAbilityLevel(dummy, 'A2S1', level)
    call IssueTargetOrder(dummy, "slow", target)
    // Yield once so the native cast is committed before its temporary caster
    // is removed.  No timer, trigger, or effect handle is retained by JASS.
    call TriggerSleepAction(0.)
    call RemoveUnit(dummy)
    set dummy = null
endfunction

function HTW_Abilities_CastNativeStun takes unit caster, unit target, integer level returns nothing
    local unit dummy
    if not HTW_Abilities_IsHostileLiving(caster, target) then
        return
    endif
    // A2T1 is the custom hidden copy of built-in AHtb.  The native stun owns
    // its pause/expiration state; only this short-lived caster is cleaned up.
    set dummy = CreateUnit(GetOwningPlayer(caster), 'n2D1', GetUnitX(caster), GetUnitY(caster), 0.)
    call SetUnitAbilityLevel(dummy, 'A2T1', level)
    call IssueTargetOrder(dummy, "thunderbolt", target)
    call TriggerSleepAction(0.)
    call RemoveUnit(dummy)
    set dummy = null
endfunction

function HTW_Abilities_ApplyPointDamageAndSlow takes unit caster, real x, real y, integer level returns nothing
    local group affected
    local unit target
    local real damage
    set damage = 60.
    if level == 2 then
        set damage = 100.
    elseif level == 3 then
        set damage = 140.
    elseif level >= 4 then
        set damage = 180.
    endif
    set affected = CreateGroup()
    call GroupEnumUnitsInRange(affected, x, y, 300., null)
    loop
        set target = FirstOfGroup(affected)
        exitwhen target == null
        call GroupRemoveUnit(affected, target)
        if HTW_Abilities_IsHostileLiving(caster, target) then
            call UnitDamageTarget(caster, target, damage, false, false, ATTACK_TYPE_MAGIC, DAMAGE_TYPE_MAGIC, WEAPON_TYPE_WHOKNOWS)
            call HTW_Abilities_CastNativeSlow(caster, target, level)
        endif
        set target = null
    endloop
    call DestroyGroup(affected)
    set affected = null
endfunction

function HTW_Abilities_ApplyPointDamageAndStun takes unit caster, real x, real y, integer level returns nothing
    local group affected
    local unit target
    local real damage
    set damage = 225.
    if level == 2 then
        set damage = 350.
    elseif level >= 3 then
        set damage = 475.
    endif
    set affected = CreateGroup()
    call GroupEnumUnitsInRange(affected, x, y, 325., null)
    loop
        set target = FirstOfGroup(affected)
        exitwhen target == null
        call GroupRemoveUnit(affected, target)
        if HTW_Abilities_IsHostileLiving(caster, target) then
            call UnitDamageTarget(caster, target, damage, false, false, ATTACK_TYPE_MAGIC, DAMAGE_TYPE_MAGIC, WEAPON_TYPE_WHOKNOWS)
            call HTW_Abilities_CastNativeStun(caster, target, level)
        endif
        set target = null
    endloop
    call DestroyGroup(affected)
    set affected = null
endfunction

function HTW_Abilities_ApplyManaRelay takes unit caster, unit target, integer level returns nothing
    local real mana
    local real heal
    local real value
    if not HTW_Abilities_IsLiving(target) or not HTW_Abilities_IsSameTeamHero(caster, target) then
        return
    endif
    set mana = 100.
    set heal = 50.
    if level == 2 then
        set mana = 150.
        set heal = 100.
    elseif level == 3 then
        set mana = 200.
        set heal = 150.
    elseif level >= 4 then
        set mana = 250.
        set heal = 200.
    endif
    set value = GetUnitState(target, UNIT_STATE_MANA) + mana
    if value > GetUnitState(target, UNIT_STATE_MAX_MANA) then
        set value = GetUnitState(target, UNIT_STATE_MAX_MANA)
    endif
    call SetUnitState(target, UNIT_STATE_MANA, value)
    set value = GetWidgetLife(target) + heal
    if value > GetUnitState(target, UNIT_STATE_MAX_LIFE) then
        set value = GetUnitState(target, UNIT_STATE_MAX_LIFE)
    endif
    call SetWidgetLife(target, value)
endfunction

function HTW_Abilities_OnSpellEffect takes nothing returns nothing
    local unit caster
    local unit target
    local integer abilityId
    local integer level
    local real x
    local real y
    set caster = GetTriggerUnit()
    if caster == null or GetUnitTypeId(caster) != 'H003' then
        set caster = null
        return
    endif
    set abilityId = GetSpellAbilityId()
    set level = GetUnitAbilityLevel(caster, abilityId)
    if level < 1 then
        set level = 1
    endif
    if abilityId == 'A2Q1' then
        set target = GetSpellTargetUnit()
        call HTW_Abilities_ApplyArcaneLance(caster, target, level)
    elseif abilityId == 'A2W1' then
        set x = GetSpellTargetX()
        set y = GetSpellTargetY()
        call HTW_Abilities_ApplyPointDamageAndSlow(caster, x, y, level)
    elseif abilityId == 'A2E1' then
        set target = GetSpellTargetUnit()
        call HTW_Abilities_ApplyManaRelay(caster, target, level)
    elseif abilityId == 'A2R1' then
        set x = GetSpellTargetX()
        set y = GetSpellTargetY()
        call HTW_Abilities_ApplyPointDamageAndStun(caster, x, y, level)
    endif
    set target = null
    set caster = null
endfunction

function HTW_Content_Abilities takes nothing returns nothing
    local trigger spellTrigger
    set spellTrigger = CreateTrigger()
    call TriggerRegisterAnyUnitEventBJ(spellTrigger, EVENT_PLAYER_UNIT_SPELL_EFFECT)
    call TriggerAddAction(spellTrigger, function HTW_Abilities_OnSpellEffect)
    // spellTrigger intentionally remains registered for the match; all
    // per-cast groups and dummy units are released exactly once in their paths.
    set spellTrigger = null
endfunction
