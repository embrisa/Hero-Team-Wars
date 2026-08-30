function HTW_Information_Display takes nothing returns nothing
    local integer teamIndex
    local string state
    set state = "running"
    if HTW_TerminalState == 1 then
        set state = "victory"
    elseif HTW_TerminalState == 2 then
        set state = "draw"
    endif
    call DisplayTextToForce(GetPlayersAll(), "[HTW] round=" + I2S(HTW_Round) + " wave=" + I2S(HTW_Wave) + " phase=" + I2S(HTW_Phase) + " state=" + state)
    set teamIndex = 1
    loop
        exitwhen teamIndex > HTW_TeamCount
        if HTW_TeamLiving[teamIndex] then
            call DisplayTextToForce(GetPlayersAll(), "[HTW] team=" + I2S(teamIndex) + " lives=" + I2S(HTW_TeamLives[teamIndex]) + " destination=" + I2S(HTW_TeamDestination[teamIndex]))
        endif
        set teamIndex = teamIndex + 1
    endloop
    call HTW_Debug_LogText("public phase/lives/route information displayed")
endfunction
