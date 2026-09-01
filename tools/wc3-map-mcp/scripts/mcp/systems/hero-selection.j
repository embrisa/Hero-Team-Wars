function HTW_HeroSelection_RefillStock takes nothing returns nothing
    if HTW_HeroSelectionBuilding == null then
        return
    endif
    call RemoveUnitFromStock(HTW_HeroSelectionBuilding, HTW_Content_HeroTypeForSlot(1))
    call RemoveUnitFromStock(HTW_HeroSelectionBuilding, HTW_Content_HeroTypeForSlot(2))
    call RemoveUnitFromStock(HTW_HeroSelectionBuilding, HTW_Content_HeroTypeForSlot(3))
    call RemoveUnitFromStock(HTW_HeroSelectionBuilding, HTW_Content_HeroTypeForSlot(4))
    call AddUnitToStock(HTW_HeroSelectionBuilding, HTW_Content_HeroTypeForSlot(1), 1, 1)
    call AddUnitToStock(HTW_HeroSelectionBuilding, HTW_Content_HeroTypeForSlot(2), 1, 1)
    call AddUnitToStock(HTW_HeroSelectionBuilding, HTW_Content_HeroTypeForSlot(3), 1, 1)
    call AddUnitToStock(HTW_HeroSelectionBuilding, HTW_Content_HeroTypeForSlot(4), 1, 1)
endfunction

function HTW_HeroSelection_PlayerHasTeammateHero takes integer playerId, integer heroType returns boolean
    local integer teamIndex
    local integer teammateId
    set teamIndex = HTW_Teams_FindByPlayer(playerId)
    if teamIndex <= 0 then
        return true
    endif
    set teammateId = HTW_TeamMemberA[teamIndex]
    if teammateId == playerId then
        set teammateId = HTW_TeamMemberB[teamIndex]
    endif
    return teammateId > 0 and HTW_HeroSelectedByPlayer[teammateId] and HTW_HeroTypeByPlayer[teammateId] == heroType
endfunction

function HTW_HeroSelection_DeployHero takes integer playerId returns nothing
    local integer teamIndex
    local real x
    local real y
    set teamIndex = HTW_Teams_FindByPlayer(playerId)
    if teamIndex <= 0 or HTW_HeroUnitByPlayer[playerId] == null then
        return
    endif
    set x = GetRectCenterX(HTW_ArenaRect[teamIndex])
    set y = GetRectCenterY(HTW_ArenaRect[teamIndex])
    call SetUnitPosition(HTW_HeroUnitByPlayer[playerId], x + I2R(playerId * 64), y + I2R(playerId * 48))
    set HTW_HeroAliveByPlayer[playerId] = true
    set HTW_HeroDeathAccountedByPlayer[playerId] = false
    set HTW_AliveHeroCount = HTW_AliveHeroCount + 1
endfunction

function HTW_HeroSelection_Complete takes nothing returns nothing
    local integer playerId
    if HTW_HeroSelectionComplete then
        return
    endif
    set HTW_HeroSelectionComplete = true
    if HTW_HeroSelectionTimer != null then
        call PauseTimer(HTW_HeroSelectionTimer)
        call DestroyTimer(HTW_HeroSelectionTimer)
        set HTW_HeroSelectionTimer = null
    endif
    if HTW_HeroSelectionBuilding != null then
        call RemoveUnit(HTW_HeroSelectionBuilding)
        set HTW_HeroSelectionBuilding = null
    endif
    set playerId = 1
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        call HTW_HeroSelection_DeployHero(playerId)
        set playerId = playerId + 1
    endloop
    set HTW_Phase = 1
    call HTW_Debug_LogText("hero selection complete; first preparation phase started")
    call HTW_Waves_Prepare()
endfunction

function HTW_HeroSelection_SelectForPlayer takes integer playerId, integer heroType returns boolean
    if playerId < 1 or playerId > HTW_ActivePlayerCount or not HTW_Content_IsHeroType(heroType) then
        return false
    endif
    if HTW_HeroSelectedByPlayer[playerId] or HTW_HeroSelection_PlayerHasTeammateHero(playerId, heroType) then
        return false
    endif
    set HTW_HeroSelectedByPlayer[playerId] = true
    set HTW_HeroTypeByPlayer[playerId] = heroType
    set HTW_HeroUnitByPlayer[playerId] = HTW_Content_CreateHero(playerId, heroType, 216., -336.)
    call DisplayTextToPlayer(Player(playerId - 1), 0., 0., "Selected " + HTW_Content_HeroName(heroType) + ".")
    return true
endfunction

function HTW_HeroSelection_AllPlayersReady takes nothing returns boolean
    local integer playerId
    set playerId = 1
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        if not HTW_HeroSelectedByPlayer[playerId] then
            return false
        endif
        set playerId = playerId + 1
    endloop
    return true
endfunction

function HTW_HeroSelection_OnSell takes nothing returns nothing
    local unit soldHero
    local integer playerId
    local integer heroType
    set soldHero = GetSoldUnit()
    set playerId = GetPlayerId(GetOwningPlayer(soldHero)) + 1
    set heroType = GetUnitTypeId(soldHero)
    if not HTW_HeroSelection_SelectForPlayer(playerId, heroType) then
        call RemoveUnit(soldHero)
        call HTW_HeroSelection_RefillStock()
        call DisplayTextToPlayer(Player(playerId - 1), 0., 0., "That hero is unavailable. Choose another hero.")
        set soldHero = null
        return
    endif
    call HTW_HeroSelection_RefillStock()
    call HTW_Debug_LogText("player " + I2S(playerId) + " selected " + HTW_Content_HeroName(heroType))
    if HTW_HeroSelection_AllPlayersReady() then
        call HTW_HeroSelection_Complete()
    endif
    set soldHero = null
endfunction

function HTW_HeroSelection_AutoPick takes integer playerId returns nothing
    local integer slot
    local integer heroType
    set slot = 1
    loop
        exitwhen slot > 4
        set heroType = HTW_Content_HeroTypeForSlot(slot)
        if HTW_HeroSelection_SelectForPlayer(playerId, heroType) then
            return
        endif
        set slot = slot + 1
    endloop
endfunction

function HTW_HeroSelection_OnTimeout takes nothing returns nothing
    local integer playerId
    set playerId = 1
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        if not HTW_HeroSelectedByPlayer[playerId] then
            call HTW_HeroSelection_AutoPick(playerId)
        endif
        set playerId = playerId + 1
    endloop
    if HTW_HeroSelection_AllPlayersReady() then
        call HTW_HeroSelection_Complete()
    endif
endfunction

function HTW_HeroSelection_Begin takes nothing returns nothing
    set HTW_HeroSelectionComplete = false
    set HTW_HeroSelectionBuilding = CreateUnit(Player(PLAYER_NEUTRAL_PASSIVE), 'n0AL', 216., -336., 270.)
    set HTW_HeroSelectionTrigger = CreateTrigger()
    call TriggerRegisterUnitEvent(HTW_HeroSelectionTrigger, HTW_HeroSelectionBuilding, EVENT_UNIT_SELL)
    call TriggerAddAction(HTW_HeroSelectionTrigger, function HTW_HeroSelection_OnSell)
    call HTW_HeroSelection_RefillStock()
    set HTW_HeroSelectionTimer = CreateTimer()
    call TimerStart(HTW_HeroSelectionTimer, I2R(HTW_HeroSelectionSeconds), false, function HTW_HeroSelection_OnTimeout)
    call DisplayTextToPlayer(GetLocalPlayer(), 0., 0., "Choose a hero at the shared HTW Hero Altar.")
    call HTW_Debug_LogText("shared custom Hero Altar created; hero selection is open")
endfunction
