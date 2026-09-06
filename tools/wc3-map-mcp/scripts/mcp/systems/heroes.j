function HTW_Heroes_Initialize takes nothing returns nothing
    local integer playerId
    local integer teamIndex
    local real x
    local real y
    set playerId = 1
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        set teamIndex = HTW_Teams_FindByPlayer(playerId)
        set x = GetRectCenterX(HTW_ArenaRect[teamIndex])
        set y = GetRectCenterY(HTW_ArenaRect[teamIndex])
        // Hero units are selected at the shared altar before the first wave.
        // Each Camp uses zero-cost command abilities; script owns purchases.
        set HTW_HeroUnitByPlayer[playerId] = null
        set HTW_HeroAliveByPlayer[playerId] = false
        set HTW_HeroDeathAccountedByPlayer[playerId] = false
        if HTW_Players_IsActive(playerId) then
            set HTW_WarCampByPlayer[playerId] = CreateUnit(Player(playerId - 1), 'n26C', x + I2R(playerId * 96), y - I2R(playerId * 48), 270.)
        endif
        set playerId = playerId + 1
    endloop
    call HTW_Debug_LogText("personal War Camps initialized; awaiting shared hero altar selections")
endfunction

function HTW_Heroes_IsTracked takes unit hero returns boolean
    local integer playerId
    set playerId = 1
    loop
        exitwhen playerId > 24
        if HTW_HeroUnitByPlayer[playerId] == hero then
            return true
        endif
        set playerId = playerId + 1
    endloop
    return false
endfunction

function HTW_Heroes_PlayerId takes unit hero returns integer
    local integer playerId
    set playerId = 1
    loop
        exitwhen playerId > 24
        if HTW_HeroUnitByPlayer[playerId] == hero then
            return playerId
        endif
        set playerId = playerId + 1
    endloop
    return 0
endfunction

function HTW_Heroes_OnDeath takes nothing returns nothing
    local unit deadHero
    set deadHero = GetTriggerUnit()
    if HTW_Heroes_IsTracked(deadHero) then
        call HTW_Lives_AccountDeath()
        if HTW_MatchOver then
            call HTW_Waves_Resolve()
        endif
    endif
    set deadHero = null
endfunction

function HTW_Heroes_ReviveLiving takes nothing returns nothing
    local integer playerId
    local integer teamIndex
    local real x
    local real y
    set playerId = 1
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        set teamIndex = HTW_Teams_FindByPlayer(playerId)
        if teamIndex > 0 and HTW_TeamLiving[teamIndex] and HTW_HeroSelectedByPlayer[playerId] and not HTW_HeroAliveByPlayer[playerId] then
            set x = GetRectCenterX(HTW_ArenaRect[teamIndex])
            set y = GetRectCenterY(HTW_ArenaRect[teamIndex])
            call ReviveHero(HTW_HeroUnitByPlayer[playerId], x, y, true)
            set HTW_HeroAliveByPlayer[playerId] = true
            set HTW_HeroDeathAccountedByPlayer[playerId] = false
            set HTW_AliveHeroCount = HTW_AliveHeroCount + 1
        endif
        set playerId = playerId + 1
    endloop
endfunction
