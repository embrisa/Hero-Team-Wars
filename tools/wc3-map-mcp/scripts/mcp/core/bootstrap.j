function HTW_MCP_Bootstrap takes nothing returns nothing
    call HTW_Teams_Initialize()
    call HTW_Regions_Initialize()
    call HTW_Objects_Initialize()
    set HTW_Round = 0
    set HTW_Wave = 0
    set HTW_Phase = 0
endfunction
