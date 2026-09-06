function HTW_Content_BaseWaves takes nothing returns nothing
    // Preparation plans the base composition; the combat scheduler emits it.
    call HTW_Waves_RefreshPlan()
endfunction

function HTW_Content_RemoveCreep takes nothing returns nothing
    if GetEnumUnit() != null then
        call RemoveUnit(GetEnumUnit())
    endif
endfunction

function HTW_Content_ClearArena takes integer arenaIndex returns nothing
    if HTW_ArenaCreepGroup[arenaIndex] != null then
        call ForGroup(HTW_ArenaCreepGroup[arenaIndex], function HTW_Content_RemoveCreep)
        call DestroyGroup(HTW_ArenaCreepGroup[arenaIndex])
        set HTW_ArenaCreepGroup[arenaIndex] = null
    endif
    set HTW_ArenaCreepCount[arenaIndex] = 0
endfunction

function HTW_Content_CleanupBaseWaves takes nothing returns nothing
    local integer arenaIndex
    set arenaIndex = 1
    loop
        exitwhen arenaIndex > HTW_ArenaCount
        call HTW_Content_ClearArena(arenaIndex)
        set arenaIndex = arenaIndex + 1
    endloop
endfunction
