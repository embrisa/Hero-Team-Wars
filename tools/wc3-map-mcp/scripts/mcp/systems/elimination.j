function HTW_Elimination_Recalculate takes nothing returns nothing
    local integer teamIndex
    set HTW_LivingTeamCount = 0
    set teamIndex = 1
    loop
        exitwhen teamIndex > HTW_TeamCount
        if HTW_TeamLiving[teamIndex] then
            set HTW_LivingTeamCount = HTW_LivingTeamCount + 1
            set HTW_LivingTeamIds[HTW_LivingTeamCount] = teamIndex
        endif
        set teamIndex = teamIndex + 1
    endloop
    if HTW_LivingTeamCount == 0 then
        set HTW_TerminalState = 2
        set HTW_MatchOver = true
        set HTW_Phase = 5
        call HTW_Debug_LogText("draw: no living teams")
    elseif HTW_LivingTeamCount == 1 then
        set HTW_TerminalState = 1
        set HTW_MatchOver = true
        set HTW_Phase = 4
        call HTW_Debug_LogText("victory: one living team")
    endif
endfunction
