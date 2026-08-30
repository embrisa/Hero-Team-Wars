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
endfunction
