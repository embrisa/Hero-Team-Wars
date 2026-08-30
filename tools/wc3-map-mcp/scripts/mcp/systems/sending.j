function HTW_Sending_Queue takes nothing returns nothing
    call HTW_Sending_ProcessQueues()
endfunction

function HTW_Sending_QueueCreep takes integer playerId, integer unitType, integer quantity, integer cost returns boolean
    return HTW_Economy_Purchase(playerId, unitType, quantity, cost)
endfunction

function HTW_Sending_ProcessQueues takes nothing returns nothing
    local integer attempts
    local integer playerId
    local integer destinationTeam
    local unit sent
    if HTW_Phase != 2 or not HTW_WaveActive or HTW_MatchOver then
        return
    endif
    set attempts = 0
    set playerId = HTW_SendCursor
    loop
        exitwhen attempts >= 12
        if playerId > 12 then
            set playerId = 1
        endif
        if HTW_PlayerQueueRemaining[playerId] > 0 then
            set destinationTeam = HTW_PlayerQueueDestination[playerId]
            if destinationTeam != 0 and destinationTeam != HTW_Teams_FindByPlayer(playerId) then
                set sent = HTW_Content_SendOne(HTW_PlayerQueueUnitType[playerId], destinationTeam)
                if sent != null then
                    set HTW_PlayerQueueRemaining[playerId] = HTW_PlayerQueueRemaining[playerId] - 1
                    call HTW_Debug_LogText("staggered send emitted")
                endif
            else
                set HTW_PlayerQueueRemaining[playerId] = 0
            endif
            set HTW_SendCursor = playerId + 1
            set sent = null
            return
        endif
        set playerId = playerId + 1
        set attempts = attempts + 1
    endloop
endfunction
