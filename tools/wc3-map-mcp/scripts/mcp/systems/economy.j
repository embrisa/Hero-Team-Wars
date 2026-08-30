function HTW_Economy_GrantPersonalGold takes nothing returns nothing
    local integer playerId
    local integer teamIndex
    set playerId = 1
    loop
        exitwhen playerId > 12
        set teamIndex = HTW_Teams_FindByPlayer(playerId)
        if teamIndex > 0 and HTW_TeamLiving[teamIndex] then
            set HTW_PlayerGold[playerId] = HTW_PlayerGold[playerId] + HTW_WaveReward + HTW_InterestGold
        endif
        set playerId = playerId + 1
    endloop
endfunction

function HTW_Economy_Purchase takes integer playerId, integer unitType, integer quantity, integer cost returns boolean
    local integer teamIndex
    local integer destinationTeam
    if playerId < 1 or playerId > 12 or quantity <= 0 or cost < 0 or unitType == 0 then
        return false
    endif
    if HTW_Phase != 1 or HTW_MatchOver then
        return false
    endif
    set teamIndex = HTW_Teams_FindByPlayer(playerId)
    if teamIndex == 0 or not HTW_TeamLiving[teamIndex] then
        return false
    endif
    set destinationTeam = HTW_TeamDestination[teamIndex]
    if destinationTeam == 0 or destinationTeam == teamIndex then
        return false
    endif
    if HTW_PlayerGold[playerId] < quantity * cost then
        return false
    endif
    if HTW_PlayerQueueRemaining[playerId] > 0 then
        return false
    endif
    set HTW_PlayerGold[playerId] = HTW_PlayerGold[playerId] - quantity * cost
    set HTW_PlayerQueueUnitType[playerId] = unitType
    set HTW_PlayerQueueRemaining[playerId] = quantity
    set HTW_PlayerQueueDestination[playerId] = destinationTeam
    call HTW_Debug_LogText("personal purchase queued")
    return true
endfunction

function HTW_Economy_GetGold takes integer playerId returns integer
    if playerId < 1 or playerId > 24 then
        return 0
    endif
    return HTW_PlayerGold[playerId]
endfunction
