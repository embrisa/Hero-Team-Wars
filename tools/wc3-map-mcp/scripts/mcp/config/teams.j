function HTW_Teams_Initialize takes nothing returns nothing
    call HTW_Teams_ConfigureProfile()
    set HTW_TeamLives[1] = HTW_StartingLives
    set HTW_TeamLives[2] = HTW_StartingLives
    set HTW_TeamLives[3] = HTW_StartingLives
    set HTW_TeamLives[4] = HTW_StartingLives
    set HTW_TeamLives[5] = HTW_StartingLives
    set HTW_TeamLives[6] = HTW_StartingLives
    set HTW_TeamLives[7] = HTW_StartingLives
    set HTW_TeamLives[8] = HTW_StartingLives
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
