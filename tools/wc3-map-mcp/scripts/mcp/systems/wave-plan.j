function HTW_PlanKey takes integer ownerId, integer kind returns integer
    return ownerId * 8 + kind
endfunction

function HTW_Players_IsActive takes integer playerId returns boolean
    if playerId < 1 or playerId > HTW_ActivePlayerCount then
        return false
    endif
    return GetPlayerSlotState(Player(playerId - 1)) == PLAYER_SLOT_STATE_PLAYING and GetPlayerController(Player(playerId - 1)) == MAP_CONTROL_USER
endfunction

function HTW_Sending_Budget takes nothing returns integer
    local integer budget
    set budget = HTW_SendBudgetStart + HTW_SendBudgetGrowth * (HTW_Wave - 1)
    if budget < HTW_SendBudgetStart then
        set budget = HTW_SendBudgetStart
    endif
    if budget > HTW_SendBudgetMaximum then
        set budget = HTW_SendBudgetMaximum
    endif
    return budget
endfunction

function HTW_Waves_PlanWorth takes integer teamIndex returns integer
    local integer kind
    local integer key
    local integer worth
    set worth = 0
    set kind = 1
    loop
        exitwhen kind > HTW_SendCatalog_Count()
        set key = HTW_PlanKey(teamIndex, kind)
        set worth = worth + (HTW_PlanBase[key] + HTW_PlanSends[key] + HTW_PlanFiller[key]) * HTW_SendCatalog_Threat(kind)
        set kind = kind + 1
    endloop
    return worth
endfunction

function HTW_Waves_RefreshPlan takes nothing returns nothing
    local integer teamIndex
    local integer playerId
    local integer kind
    local integer key
    local integer sender
    local integer sendCount
    if HTW_Phase != 1 or HTW_SendPlanLocked or HTW_MatchOver then
        return
    endif
    set teamIndex = 1
    loop
        exitwhen teamIndex > HTW_TeamCount
        set kind = 1
        set sendCount = 0
        loop
            exitwhen kind > HTW_SendCatalog_Count()
            set key = HTW_PlanKey(teamIndex, kind)
            set HTW_PlanBase[key] = 0
            set HTW_PlanSends[key] = 0
            set HTW_PlanFiller[key] = 0
            set kind = kind + 1
        endloop
        if HTW_TeamLiving[teamIndex] then
            set HTW_PlanBase[HTW_PlanKey(teamIndex, 1)] = HTW_BaseFootmen
            set sender = HTW_Routing_Incoming(teamIndex)
            if sender > 0 then
                set playerId = 1
                loop
                    exitwhen playerId > HTW_ActivePlayerCount
                    if HTW_Teams_FindByPlayer(playerId) == sender and HTW_PlayerQueueDestination[playerId] == teamIndex then
                        set kind = 1
                        loop
                            exitwhen kind > HTW_SendCatalog_Count()
                            set key = HTW_PlanKey(teamIndex, kind)
                            set HTW_PlanSends[key] = HTW_PlanSends[key] + HTW_PlayerQueueCount[HTW_PlanKey(playerId, kind)]
                            set sendCount = sendCount + HTW_PlayerQueueCount[HTW_PlanKey(playerId, kind)]
                            set kind = kind + 1
                        endloop
                    endif
                    set playerId = playerId + 1
                endloop
                // Preserve accepted buys on disconnect; filler replaces only
                // an empty absent-human send, never the base or purchased units.
                if sendCount == 0 and not HTW_Players_IsActive(HTW_TeamMemberA[sender]) and not HTW_Players_IsActive(HTW_TeamMemberB[sender]) then
                    set HTW_PlanFiller[HTW_PlanKey(teamIndex, 1)] = HTW_FillerFootmen
                    set HTW_PlanFiller[HTW_PlanKey(teamIndex, 2)] = HTW_FillerRiflemen
                endif
            endif
        endif
        set teamIndex = teamIndex + 1
    endloop
endfunction

function HTW_Waves_LockPlan takes nothing returns nothing
    local integer teamIndex
    local integer kind
    local integer key
    if HTW_SendPlanLocked or HTW_Phase != 1 or HTW_MatchOver then
        return
    endif
    call HTW_Waves_RefreshPlan()
    set teamIndex = 1
    loop
        exitwhen teamIndex > HTW_TeamCount
        set kind = 1
        loop
            exitwhen kind > HTW_SendCatalog_Count()
            set key = HTW_PlanKey(teamIndex, kind)
            set HTW_PlanRemaining[key] = HTW_PlanBase[key] + HTW_PlanSends[key] + HTW_PlanFiller[key]
            set kind = kind + 1
        endloop
        set teamIndex = teamIndex + 1
    endloop
    set HTW_SendPlanLocked = true
endfunction

function HTW_Waves_ClearArena takes integer teamIndex returns nothing
    local integer kind
    local integer key
    call HTW_Content_ClearArena(teamIndex)
    set kind = 1
    loop
        exitwhen kind > HTW_SendCatalog_Count()
        set key = HTW_PlanKey(teamIndex, kind)
        set HTW_PlanBase[key] = 0
        set HTW_PlanSends[key] = 0
        set HTW_PlanFiller[key] = 0
        set HTW_PlanRemaining[key] = 0
        set kind = kind + 1
    endloop
endfunction
