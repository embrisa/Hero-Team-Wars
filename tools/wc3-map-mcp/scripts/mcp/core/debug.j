function HTW_Debug_Log takes nothing returns nothing
    call HTW_Debug_LogText("heartbeat")
endfunction

function HTW_Debug_LogText takes string message returns nothing
    call DisplayTextToForce(GetPlayersAll(), "[HTW] chunk=HTW-05 round=" + I2S(HTW_Round) + " wave=" + I2S(HTW_Wave) + " phase=" + I2S(HTW_Phase) + " " + message)
endfunction
