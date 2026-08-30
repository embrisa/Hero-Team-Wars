function HTW_Waves_Prepare takes nothing returns nothing
    if HTW_MatchOver then
        return
    endif
    if HTW_WaveActive then
        return
    endif
    if HTW_Round < 1 then
        set HTW_Round = 1
    endif
    call HTW_Elimination_Recalculate()
    if HTW_MatchOver then
        return
    endif
    set HTW_Phase = 1
    set HTW_Wave = HTW_Wave + 1
    set HTW_RoutingLocked = false
    call HTW_Routing_Compute()
    set HTW_WaveActive = true
    set HTW_ResolutionApplied = false
    set HTW_TeamDeathsThisWave[1] = 0
    set HTW_TeamDeathsThisWave[2] = 0
    set HTW_TeamDeathsThisWave[3] = 0
    set HTW_TeamDeathsThisWave[4] = 0
    set HTW_TeamDeathsThisWave[5] = 0
    set HTW_TeamDeathsThisWave[6] = 0
    call HTW_Content_BaseWaves()
    if HTW_PreparationTimer == null then
        set HTW_PreparationTimer = CreateTimer()
    endif
    call TimerStart(HTW_PreparationTimer, I2R(HTW_PreparationSeconds), false, function HTW_Phases_BeginCombat)
    call HTW_Events_FireRoundStart()
    call HTW_Debug_LogText("new wave prepared")
endfunction

function HTW_Waves_Resolve takes nothing returns nothing
    if not HTW_WaveActive or HTW_ResolutionApplied then
        return
    endif
    set HTW_ResolutionApplied = true
    call HTW_Content_CleanupBaseWaves()
    call HTW_Heroes_ReviveLiving()
    call HTW_Economy_GrantPersonalGold()
    set HTW_WaveActive = false
    set HTW_LastResolvedWave = HTW_Wave
    if HTW_PreparationTimer != null then
        call PauseTimer(HTW_PreparationTimer)
    endif
    if HTW_CombatTimer != null then
        call PauseTimer(HTW_CombatTimer)
    endif
    call HTW_Elimination_Recalculate()
    if not HTW_MatchOver then
        set HTW_Round = HTW_Round + 1
        call HTW_Events_FireWaveResolved()
    endif
    call HTW_Debug_LogText("wave resolved exactly once")
endfunction
