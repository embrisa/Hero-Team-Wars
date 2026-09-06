function HTW_Sending_ResetQueues takes nothing returns nothing
    local integer ownerId
    local integer kind
    local integer key
    set HTW_SendPlanLocked = false
    set ownerId = 1
    loop
        exitwhen ownerId > 24
        set HTW_PlayerThreatUsed[ownerId] = 0
        set HTW_PlayerQueueDestination[ownerId] = 0
        set kind = 1
        loop
            exitwhen kind > HTW_SendCatalog_Count()
            set key = HTW_PlanKey(ownerId, kind)
            set HTW_PlayerQueueCount[key] = 0
            set HTW_PlanBase[key] = 0
            set HTW_PlanSends[key] = 0
            set HTW_PlanFiller[key] = 0
            set HTW_PlanRemaining[key] = 0
            set kind = kind + 1
        endloop
        set ownerId = ownerId + 1
    endloop
endfunction

function HTW_Sending_QueueCreep takes integer playerId, integer unitType, integer quantity, integer cost returns boolean
    return HTW_Economy_Purchase(playerId, unitType, quantity, cost)
endfunction

function HTW_Sending_OnPurchaseSpell takes nothing returns nothing
    local unit camp
    local integer playerId
    local integer kind
    set camp = GetTriggerUnit()
    set playerId = GetPlayerId(GetOwningPlayer(camp)) + 1
    if playerId < 1 or playerId > HTW_ActivePlayerCount or camp != HTW_WarCampByPlayer[playerId] then
        set camp = null
        return
    endif
    set kind = 1
    loop
        exitwhen kind > HTW_SendCatalog_Count()
        if GetSpellAbilityId() == HTW_SendCatalog_AbilityId(kind) then
            call HTW_Economy_TryPurchase(playerId, kind, 1)
            set kind = HTW_SendCatalog_Count()
        endif
        set kind = kind + 1
    endloop
    set camp = null
endfunction

function HTW_Sending_Initialize takes nothing returns nothing
    local trigger purchaseTrigger
    local integer playerId
    set purchaseTrigger = CreateTrigger()
    call TriggerAddAction(purchaseTrigger, function HTW_Sending_OnPurchaseSpell)
    set playerId = 1
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        call TriggerRegisterPlayerUnitEvent(purchaseTrigger, Player(playerId - 1), EVENT_PLAYER_UNIT_SPELL_EFFECT, null)
        if HTW_Players_IsActive(playerId) then
            set HTW_PlayerGold[playerId] = HTW_StartingGold
        endif
        call HTW_Economy_SyncGold(playerId)
        set playerId = playerId + 1
    endloop
    // Script owns personal income; disable native neutral creep bounty.
    call SetPlayerState(Player(PLAYER_NEUTRAL_AGGRESSIVE), PLAYER_STATE_GIVES_BOUNTY, 0)
    set purchaseTrigger = null
endfunction

function HTW_Sending_ProcessQueues takes nothing returns nothing
    local integer teamIndex
    local integer kind
    local integer key
    local boolean emitted
    if HTW_Phase != 2 or not HTW_WaveActive or not HTW_SendPlanLocked or HTW_MatchOver then
        return
    endif
    // One arrival per living arena per second, consuming only pending counts.
    set teamIndex = 1
    loop
        exitwhen teamIndex > HTW_TeamCount
        if HTW_TeamLiving[teamIndex] then
            set kind = 1
            loop
                exitwhen kind > HTW_SendCatalog_Count()
                set key = HTW_PlanKey(teamIndex, kind)
                if HTW_PlanRemaining[key] > 0 then
                    set emitted = HTW_Content_SendOne(HTW_SendCatalog_UnitType(kind), teamIndex)
                    if emitted then
                        set HTW_PlanRemaining[key] = HTW_PlanRemaining[key] - 1
                    endif
                    set kind = HTW_SendCatalog_Count()
                endif
                set kind = kind + 1
            endloop
        endif
        set teamIndex = teamIndex + 1
    endloop
endfunction

function HTW_Sending_Queue takes nothing returns nothing
    call HTW_Sending_ProcessQueues()
endfunction
