function HTW_State_Reset takes nothing returns nothing
    local integer index
    set HTW_Round = 1
    set HTW_Wave = 0
    set HTW_Phase = 0
    set HTW_RoutingLocked = false
    set HTW_TransitionGuard = false
    set HTW_WaveActive = false
    set HTW_ResolutionApplied = false
    set HTW_MatchOver = false
    set HTW_TerminalState = 0
    set HTW_LastResolvedWave = 0
    set HTW_SendCursor = 1
    set HTW_AliveHeroCount = 0
    set HTW_PreparationTimer = null
    set HTW_CombatTimer = null
    set HTW_SendTimer = null
    set HTW_CleanupGroup = null
    set HTW_HeroSelectionBuilding = null
    set HTW_HeroSelectionTrigger = null
    set HTW_HeroSelectionTimer = null
    set HTW_HeroSelectionComplete = false
    set index = 1
    loop
        exitwhen index > 24
        set HTW_HeroUnitByPlayer[index] = null
        set HTW_HeroAliveByPlayer[index] = false
        set HTW_HeroDeathAccountedByPlayer[index] = false
        set HTW_HeroSelectedByPlayer[index] = false
        set HTW_HeroTypeByPlayer[index] = 0
        set HTW_WarCampByPlayer[index] = null
        set HTW_PlayerGold[index] = 0
        set HTW_PlayerQueueUnitType[index] = 0
        set HTW_PlayerQueueRemaining[index] = 0
        set HTW_PlayerQueueDestination[index] = 0
        set index = index + 1
    endloop
    set index = 1
    loop
        exitwhen index > HTW_TeamCount
        set HTW_TeamLives[index] = HTW_StartingLives
        set HTW_TeamDeathsThisWave[index] = 0
        set HTW_ArenaCreepGroup[index] = null
        set HTW_ArenaCreepCount[index] = 0
        set index = index + 1
    endloop
endfunction
