function HTW_OnMapInit takes nothing returns nothing
    call HTW_Debug_LogText("scenario=fresh_initialization expected=phase_preparation")
endfunction

function HTW_OnRoundStart takes nothing returns nothing
    call HTW_Debug_LogText("scenario=preparation_combat_resolution round_start")
endfunction

function HTW_OnWaveResolved takes nothing returns nothing
    call HTW_Debug_LogText("scenario=wave_resolution cleanup=complete")
endfunction

function HTW_Test_RunRuntimeSmoke takes nothing returns nothing
    call HTW_Test_AssertBoolean(HTW_Round >= 1, "fresh_initialization")
    call HTW_Test_AssertBoolean(HTW_Wave >= 1, "preparation_combat_resolution")
    call HTW_Test_AssertBoolean(HTW_RoutingLocked, "route_locked_during_wave")
endfunction
