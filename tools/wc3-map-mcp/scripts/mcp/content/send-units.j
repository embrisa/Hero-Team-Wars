function HTW_Content_SendUnits takes nothing returns nothing
    // The sender passes the selected type and destination to this narrow
    // content boundary; no map structure is mutated here.
endfunction

function HTW_Content_SendOne takes integer unitType, integer destinationTeam returns unit
    local real x
    local real y
    set x = GetRectCenterX(HTW_ArenaRect[destinationTeam])
    set y = GetRectCenterY(HTW_ArenaRect[destinationTeam])
    return CreateUnit(Player(PLAYER_NEUTRAL_AGGRESSIVE), unitType, x, y, 270.)
endfunction
