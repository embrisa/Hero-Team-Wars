function HTW_Waves_Prepare takes nothing returns nothing
    set HTW_Wave = HTW_Wave + 1
    set HTW_RoutingLocked = false
    call HTW_Routing_Compute()
endfunction
