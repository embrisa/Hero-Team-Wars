function HTW_Phases_Advance takes nothing returns nothing
    if HTW_MatchOver or HTW_TransitionGuard then
        return
    endif
    if HTW_Phase == 1 then
        call HTW_Phases_BeginCombat()
    elseif HTW_Phase == 2 then
        call HTW_Phases_BeginResolution()
    endif
endfunction

function HTW_Phases_BeginCombat takes nothing returns nothing
    if HTW_MatchOver or HTW_Phase != 1 or HTW_TransitionGuard then
        return
    endif
    set HTW_TransitionGuard = true
    set HTW_Phase = 2
    if HTW_CombatTimer == null then
        set HTW_CombatTimer = CreateTimer()
    endif
    call TimerStart(HTW_CombatTimer, I2R(HTW_CombatSeconds), false, function HTW_Phases_BeginResolution)
    call HTW_Debug_LogText("phase preparation -> combat")
    set HTW_TransitionGuard = false
endfunction

function HTW_Phases_BeginResolution takes nothing returns nothing
    // A terminal result may be reached during combat. The active wave still
    // needs one guarded cleanup pass before the match stops accepting work.
    if HTW_Phase != 2 or HTW_TransitionGuard then
        return
    endif
    set HTW_TransitionGuard = true
    set HTW_Phase = 3
    set HTW_TransitionGuard = false
    call HTW_Waves_Resolve()
endfunction

function HTW_Phases_Tick takes nothing returns nothing
    if HTW_MatchOver and not (HTW_WaveActive and HTW_Phase == 2) then
        return
    endif
    if HTW_Phase == 1 and HTW_PreparationTimer != null and TimerGetRemaining(HTW_PreparationTimer) <= 0. then
        call HTW_Phases_BeginCombat()
    elseif HTW_Phase == 2 and HTW_CombatTimer != null and TimerGetRemaining(HTW_CombatTimer) <= 0. then
        call HTW_Phases_BeginResolution()
    endif
endfunction
