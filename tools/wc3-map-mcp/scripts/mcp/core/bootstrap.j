function HTW_MCP_Bootstrap takes nothing returns nothing
    if HTW_Bootstrapped then
        return
    endif
    call HTW_Tuning_Load()
    call HTW_Teams_Initialize()
    call HTW_Regions_Initialize()
    call HTW_Objects_Initialize()
    call HTW_State_Reset()
    set HTW_Bootstrapped = true
    call HTW_Debug_LogText("bootstrap complete")
endfunction
