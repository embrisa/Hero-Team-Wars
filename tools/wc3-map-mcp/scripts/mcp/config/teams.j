function HTW_Teams_Initialize takes nothing returns nothing
    local integer teamIndex
    call HTW_Teams_ConfigureProfile()
    set teamIndex = 1
    loop
        exitwhen teamIndex > HTW_TeamCount
        set HTW_TeamLives[teamIndex] = HTW_StartingLives
        set teamIndex = teamIndex + 1
    endloop
endfunction

function HTW_Teams_FindByPlayer takes integer playerId returns integer
    local integer teamIndex
    set teamIndex = 1
    loop
        exitwhen teamIndex > HTW_TeamCount
        if HTW_TeamMemberA[teamIndex] == playerId or HTW_TeamMemberB[teamIndex] == playerId then
            return teamIndex
        endif
        set teamIndex = teamIndex + 1
    endloop
    return 0
endfunction
