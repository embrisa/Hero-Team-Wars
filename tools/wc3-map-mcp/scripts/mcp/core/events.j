function HTW_Events_Register takes nothing returns nothing
    // The composer owns native trigger registration.  These fire helpers are
    // the single source of custom-event transitions used by runtime systems.
endfunction

function HTW_Events_FireRoundStart takes nothing returns nothing
    set HTW_Event_round_start = 1.
    set HTW_Event_round_start = 0.
endfunction

function HTW_Events_FireWaveResolved takes nothing returns nothing
    set HTW_Event_wave_resolved = 1.
    set HTW_Event_wave_resolved = 0.
endfunction
