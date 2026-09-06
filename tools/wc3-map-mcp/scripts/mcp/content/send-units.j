function HTW_Content_SendUnits takes nothing returns nothing
    // Roster and prices are generated from content/send-catalog.json.
endfunction

function HTW_Content_SendOne takes integer unitType, integer destinationTeam returns boolean
    local real x
    local real y
    local unit creep
    if destinationTeam < 1 or destinationTeam > HTW_TeamCount or not HTW_TeamLiving[destinationTeam] then
        return false
    endif
    set x = GetRectCenterX(HTW_ArenaRect[destinationTeam])
    set y = GetRectCenterY(HTW_ArenaRect[destinationTeam])
    set creep = CreateUnit(Player(PLAYER_NEUTRAL_AGGRESSIVE), unitType, x + 240., y + 240., 225.)
    if creep != null then
        if HTW_ArenaCreepGroup[destinationTeam] == null then
            set HTW_ArenaCreepGroup[destinationTeam] = CreateGroup()
        endif
        call GroupAddUnit(HTW_ArenaCreepGroup[destinationTeam], creep)
        set HTW_ArenaCreepCount[destinationTeam] = HTW_ArenaCreepCount[destinationTeam] + 1
        call IssuePointOrder(creep, "attack", x, y)
        set creep = null
        return true
    endif
    set creep = null
    return false
endfunction
