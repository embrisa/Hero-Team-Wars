function HTW_Economy_GetGold takes integer playerId returns integer
    if playerId < 1 or playerId > 24 then
        return 0
    endif
    return HTW_PlayerGold[playerId]
endfunction

function HTW_Economy_SyncGold takes integer playerId returns nothing
    call SetPlayerState(Player(playerId - 1), PLAYER_STATE_RESOURCE_GOLD, HTW_PlayerGold[playerId])
endfunction

function HTW_Economy_GrantPersonalGold takes nothing returns nothing
    local integer playerId
    local integer teamIndex
    set playerId = 1
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        set teamIndex = HTW_Teams_FindByPlayer(playerId)
        if teamIndex > 0 and HTW_TeamLiving[teamIndex] and HTW_Players_IsActive(playerId) then
            set HTW_PlayerGold[playerId] = HTW_PlayerGold[playerId] + HTW_WaveReward + HTW_InterestGold
            call HTW_Economy_SyncGold(playerId)
        endif
        set playerId = playerId + 1
    endloop
endfunction

function HTW_Economy_Reject takes integer playerId, string reason returns boolean
    if playerId >= 1 and playerId <= HTW_ActivePlayerCount then
        call DisplayTimedTextToPlayer(Player(playerId - 1), 0., 0., 3., "|cffffcc00War Camp:|r " + reason)
    endif
    return false
endfunction

function HTW_Economy_TryPurchase takes integer playerId, integer kind, integer quantity returns boolean
    local integer teamIndex
    local integer destinationTeam
    local integer totalCost
    local integer totalThreat
    local integer key
    if playerId < 1 or playerId > HTW_ActivePlayerCount or kind < 1 or kind > HTW_SendCatalog_Count() or quantity < 1 or quantity > HTW_SendBudgetMaximum then
        return HTW_Economy_Reject(playerId, "Invalid purchase.")
    endif
    if HTW_Phase != 1 or HTW_MatchOver or HTW_SendPlanLocked then
        return HTW_Economy_Reject(playerId, "Purchases are open only during preparation.")
    endif
    if not HTW_Players_IsActive(playerId) then
        return HTW_Economy_Reject(playerId, "Player is inactive.")
    endif
    if not HTW_HeroSelectedByPlayer[playerId] then
        return HTW_Economy_Reject(playerId, "Choose your hero first.")
    endif
    set teamIndex = HTW_Teams_FindByPlayer(playerId)
    if teamIndex == 0 or not HTW_TeamLiving[teamIndex] then
        return HTW_Economy_Reject(playerId, "Your team is eliminated.")
    endif
    set destinationTeam = HTW_TeamDestination[teamIndex]
    if destinationTeam == 0 or destinationTeam == teamIndex or not HTW_TeamLiving[destinationTeam] then
        return HTW_Economy_Reject(playerId, "No living destination is assigned.")
    endif
    set totalCost = quantity * HTW_SendCatalog_Cost(kind)
    set totalThreat = quantity * HTW_SendCatalog_Threat(kind)
    if HTW_PlayerGold[playerId] < totalCost then
        return HTW_Economy_Reject(playerId, "Insufficient personal gold: need " + I2S(totalCost) + ", have " + I2S(HTW_PlayerGold[playerId]) + ".")
    endif
    if HTW_PlayerThreatUsed[playerId] + totalThreat > HTW_Sending_Budget() then
        return HTW_Economy_Reject(playerId, "Insufficient threat budget: need " + I2S(totalThreat) + ", remaining " + I2S(HTW_Sending_Budget() - HTW_PlayerThreatUsed[playerId]) + ".")
    endif
    // All rejection paths precede this single synchronized commit point.
    set key = HTW_PlanKey(playerId, kind)
    set HTW_PlayerGold[playerId] = HTW_PlayerGold[playerId] - totalCost
    set HTW_PlayerThreatUsed[playerId] = HTW_PlayerThreatUsed[playerId] + totalThreat
    set HTW_PlayerQueueCount[key] = HTW_PlayerQueueCount[key] + quantity
    set HTW_PlayerQueueDestination[playerId] = destinationTeam
    call HTW_Economy_SyncGold(playerId)
    call HTW_Waves_RefreshPlan()
    call HTW_Information_Display()
    // The persistent board is the success receipt; no chat message per click.
    return true
endfunction

function HTW_Economy_Purchase takes integer playerId, integer unitType, integer quantity, integer cost returns boolean
    local integer kind
    set kind = 1
    loop
        exitwhen kind > HTW_SendCatalog_Count()
        if unitType == HTW_SendCatalog_UnitType(kind) and cost == HTW_SendCatalog_Cost(kind) then
            return HTW_Economy_TryPurchase(playerId, kind, quantity)
        endif
        set kind = kind + 1
    endloop
    return HTW_Economy_Reject(playerId, "Unknown creep or incorrect catalog price.")
endfunction
