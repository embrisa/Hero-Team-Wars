function HTW_Content_SendUnits takes nothing returns nothing
    // The sender passes the selected type and destination to this narrow
    // content boundary; no map structure is mutated here.
endfunction

function HTW_Content_SendOne takes integer unitType, integer destinationTeam returns unit
    local real x
    local real y
    set x = GetRectCenterX(HTW_ArenaRectA)
    set y = GetRectCenterY(HTW_ArenaRectA)
    if ModuloInteger(destinationTeam - 1, 2) == 1 then
        set x = GetRectCenterX(HTW_ArenaRectB)
        set y = GetRectCenterY(HTW_ArenaRectB)
    endif
    return CreateUnit(Player(PLAYER_NEUTRAL_AGGRESSIVE), unitType, x, y, 270.)
endfunction
