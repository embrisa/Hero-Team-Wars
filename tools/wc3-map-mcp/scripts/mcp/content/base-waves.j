function HTW_Content_BaseWaves takes nothing returns nothing
    local integer arenaIndex
    local integer creepIndex
    local real x
    local real y
    local unit creep
    set arenaIndex = 1
    loop
        exitwhen arenaIndex > HTW_ArenaCount
        if HTW_ArenaCreepGroup[arenaIndex] == null then
            set HTW_ArenaCreepGroup[arenaIndex] = CreateGroup()
        endif
        set x = GetRectCenterX(HTW_ArenaRect[arenaIndex])
        set y = GetRectCenterY(HTW_ArenaRect[arenaIndex])
        set creepIndex = 1
        loop
            exitwhen creepIndex > 3
            set creep = CreateUnit(Player(PLAYER_NEUTRAL_AGGRESSIVE), 'hfoo', x + I2R(creepIndex * 48), y + I2R(creepIndex * 32), 270.)
            call GroupAddUnit(HTW_ArenaCreepGroup[arenaIndex], creep)
            set HTW_ArenaCreepCount[arenaIndex] = HTW_ArenaCreepCount[arenaIndex] + 1
            set creepIndex = creepIndex + 1
        endloop
        set arenaIndex = arenaIndex + 1
    endloop
    call HTW_Debug_LogText("base wave spawned")
endfunction

function HTW_Content_RemoveCreep takes nothing returns nothing
    if GetEnumUnit() != null then
        call RemoveUnit(GetEnumUnit())
    endif
endfunction

function HTW_Content_CleanupBaseWaves takes nothing returns nothing
    local integer arenaIndex
    set arenaIndex = 1
    loop
        exitwhen arenaIndex > HTW_ArenaCount
        if HTW_ArenaCreepGroup[arenaIndex] != null then
            set HTW_CleanupGroup = HTW_ArenaCreepGroup[arenaIndex]
            call ForGroup(HTW_CleanupGroup, function HTW_Content_RemoveCreep)
            call DestroyGroup(HTW_CleanupGroup)
            set HTW_ArenaCreepGroup[arenaIndex] = null
            set HTW_ArenaCreepCount[arenaIndex] = 0
        endif
        set arenaIndex = arenaIndex + 1
    endloop
endfunction
