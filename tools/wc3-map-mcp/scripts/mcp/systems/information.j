// Presentation only. First Display must run from a runtime tick, not map init.
// All allocation and updates are synchronized; only visibility is local.
function HTW_Information_Row takes multiboard board, integer row, string value returns nothing
    local multiboarditem item = MultiboardGetItem(board, row, 0)
    call MultiboardSetItemValue(item, value)
    call MultiboardReleaseItem(item)
    set item = null
endfunction

function HTW_Information_Name takes string value returns string
    if SubString(value, 0, 4) == "HTW " then
        return SubString(value, 4, StringLength(value))
    endif
    return value
endfunction

function HTW_Information_Seconds takes timer clock returns string
    local real remaining = 0.
    local integer seconds = 0
    if clock != null then
        set remaining = TimerGetRemaining(clock)
    endif
    if remaining > 0. then
        set seconds = R2I(remaining)
        if remaining > seconds then
            set seconds = seconds + 1
        endif
    endif
    return I2S(seconds) + "s"
endfunction

function HTW_Information_Phase takes nothing returns string
    if HTW_TerminalState == 2 or HTW_Phase == 5 then
        return "Draw - match over"
    elseif HTW_TerminalState == 1 or HTW_Phase == 4 then
        return "Victory - match over"
    elseif HTW_MatchOver then
        return "Match over"
    elseif HTW_Phase == 0 then
        return "Hero selection | " + HTW_Information_Seconds(HTW_HeroSelectionTimer)
    elseif HTW_Phase == 1 then
        return "Preparation | " + HTW_Information_Seconds(HTW_PreparationTimer)
    elseif HTW_Phase == 2 then
        return "Combat | " + HTW_Information_Seconds(HTW_CombatTimer)
    endif
    return "Resolution"
endfunction

function HTW_Information_WaveVisible takes integer teamId returns boolean
    if HTW_MatchOver or HTW_TerminalState != 0 or teamId < 1 or teamId > 2 then
        return false
    endif
    if not HTW_TeamLiving[teamId] then
        return false
    endif
    return HTW_Phase == 1 or (HTW_Phase == 2 and HTW_SendPlanLocked)
endfunction

function HTW_Information_Hero takes integer playerId, integer teamId returns string
    local string label = "P" + I2S(playerId) + ": "
    if playerId < 1 or playerId > 4 then
        return "Inactive slot"
    endif
    if not HTW_Players_IsActive(playerId) then
        return label + "|cffaaaaaaInactive|r"
    endif
    if HTW_HeroSelectedByPlayer[playerId] then
        set label = label + HTW_Information_Name(HTW_Content_HeroName(HTW_HeroTypeByPlayer[playerId]))
        if HTW_HeroUnitByPlayer[playerId] != null then
            set label = label + " Lv " + I2S(GetHeroLevel(HTW_HeroUnitByPlayer[playerId]))
        else
            set label = label + " Lv ?"
        endif
    else
        set label = label + "Unselected"
    endif
    if not HTW_TeamLiving[teamId] then
        return label + " |cffff7777Eliminated|r"
    elseif HTW_HeroSelectedByPlayer[playerId] and not HTW_HeroAliveByPlayer[playerId] then
        return label + " |cffaaaaaaDown|r"
    endif
    return label
endfunction

function HTW_Information_RouteTeam takes integer teamId returns string
    if teamId >= 1 and teamId <= 2 then
        if HTW_TeamLiving[teamId] then
            return "Team " + I2S(teamId)
        endif
    endif
    return "None"
endfunction

function HTW_Information_PublicTeam takes multiboard board, integer teamId, integer row returns nothing
    local string label = "|cff80cfffTeam " + I2S(teamId) + "|r | Lives " + I2S(HTW_TeamLives[teamId])
    local string route = "Routes: unavailable"
    if not HTW_TeamLiving[teamId] then
        set label = label + " | Eliminated"
    elseif not HTW_Players_IsActive(HTW_TeamMemberA[teamId]) and not HTW_Players_IsActive(HTW_TeamMemberB[teamId]) then
        set label = label + " | Inactive"
    endif
    if HTW_Information_WaveVisible(teamId) then
        set route = "Out: " + HTW_Information_RouteTeam(HTW_TeamDestination[teamId]) + " | In: " + HTW_Information_RouteTeam(HTW_Routing_Incoming(teamId))
    endif
    call HTW_Information_Row(board, row, label)
    call HTW_Information_Row(board, row + 1, HTW_Information_Hero(HTW_TeamMemberA[teamId], teamId))
    call HTW_Information_Row(board, row + 2, HTW_Information_Hero(HTW_TeamMemberB[teamId], teamId))
    call HTW_Information_Row(board, row + 3, route)
endfunction

function HTW_Information_Personal takes multiboard board, integer playerId returns nothing
    local integer teamId = HTW_Teams_FindByPlayer(playerId)
    local integer kind = 1
    local integer key
    local integer row = 13
    local integer remaining
    // Clear conditional rows so closed panels never retain an old wave plan.
    loop
        exitwhen row > 26
        call HTW_Information_Row(board, row, "")
        set row = row + 1
    endloop
    call HTW_Information_Row(board, 11, "|cffffd580PERSONAL | P" + I2S(playerId) + "|r")
    call HTW_Information_Row(board, 12, "Gold: " + I2S(HTW_PlayerGold[playerId]))
    if not HTW_Players_IsActive(playerId) then
        call HTW_Information_Row(board, 13, "Inactive player - no purchases")
        return
    endif
    if not HTW_Information_WaveVisible(teamId) then
        if HTW_MatchOver or HTW_TerminalState != 0 or HTW_Phase >= 4 then
            call HTW_Information_Row(board, 13, "Match over - no wave preview")
        elseif teamId < 1 or teamId > 2 then
            call HTW_Information_Row(board, 13, "No active team")
        elseif not HTW_TeamLiving[teamId] then
            call HTW_Information_Row(board, 13, "Eliminated - no wave preview")
        elseif HTW_Phase == 0 then
            call HTW_Information_Row(board, 13, "Select a hero to prepare")
        else
            call HTW_Information_Row(board, 13, "Wave preview unavailable")
        endif
        return
    endif
    set remaining = HTW_Sending_Budget() - HTW_PlayerThreatUsed[playerId]
    if remaining < 0 then
        set remaining = 0
    endif
    call HTW_Information_Row(board, 13, "Threat budget: " + I2S(HTW_PlayerThreatUsed[playerId]) + " used | " + I2S(remaining) + " left")
    call HTW_Information_Row(board, 14, "Send to: " + HTW_Information_RouteTeam(HTW_TeamDestination[teamId]) + " | Camp: Q/W/E")
    loop
        exitwhen kind > HTW_SendCatalog_Count() or kind > 3
        call HTW_Information_Row(board, 14 + kind, HTW_Information_Name(HTW_SendCatalog_Name(kind)) + ": " + I2S(HTW_PlayerQueueCount[HTW_PlanKey(playerId, kind)]) + " queued")
        set kind = kind + 1
    endloop
    call HTW_Information_Row(board, 18, "|cff90e8bfINCOMING | YOUR TEAM|r")
    if HTW_Phase == 1 then
        call HTW_Information_Row(board, 19, "Updating until prep ends")
    else
        call HTW_Information_Row(board, 19, "Locked full wave")
    endif
    set kind = 1
    set row = 20
    loop
        exitwhen kind > HTW_SendCatalog_Count() or kind > 3
        set key = HTW_PlanKey(teamId, kind)
        call HTW_Information_Row(board, row, HTW_Information_Name(HTW_SendCatalog_Name(kind)) + " | " + HTW_SendCatalog_Role(kind))
        call HTW_Information_Row(board, row + 1, "Base " + I2S(HTW_PlanBase[key]) + " | Sends " + I2S(HTW_PlanSends[key]) + " | Filler " + I2S(HTW_PlanFiller[key]))
        set row = row + 2
        set kind = kind + 1
    endloop
    call HTW_Information_Row(board, 26, "Total incoming threat: " + I2S(HTW_Waves_PlanWorth(teamId)))
endfunction

function HTW_Information_CreateBoards takes nothing returns nothing
    local integer playerId = 1
    local integer row
    local multiboard board
    local multiboarditem item
    loop
        exitwhen playerId > 4
        if HTW_Teams_FindByPlayer(playerId) != 0 then
            set board = CreateMultiboard()
            set HTW_InformationBoard[playerId] = board
            call MultiboardSetTitleText(board, "|cffffcc66Hero Team Wars|r")
            call MultiboardSetColumnCount(board, 1)
            // Canonical jassdoc: increase row count only one row at a time.
            set row = 0
            loop
                exitwhen row > 26
                call MultiboardSetRowCount(board, row + 1)
                set row = row + 1
            endloop
            set row = 0
            loop
                exitwhen row > 26
                set item = MultiboardGetItem(board, row, 0)
                call MultiboardSetItemStyle(item, true, false)
                call MultiboardSetItemWidth(item, 0.36)
                call MultiboardReleaseItem(item)
                set item = null
                set row = row + 1
            endloop
        endif
        set playerId = playerId + 1
    endloop
    set board = null
    set HTW_InformationReady = true
endfunction

function HTW_Information_Display takes nothing returns nothing
    local integer playerId = 1
    local boolean firstDisplay = not HTW_InformationReady
    local multiboard board
    if firstDisplay then
        call HTW_Information_CreateBoards()
    endif
    loop
        exitwhen playerId > 4
        set board = HTW_InformationBoard[playerId]
        if board != null then
            call HTW_Information_Row(board, 0, "|cff80cfffPUBLIC OVERVIEW|r")
            call HTW_Information_Row(board, 1, HTW_Information_Phase())
            call HTW_Information_Row(board, 2, "Wave " + I2S(HTW_Wave))
            call HTW_Information_PublicTeam(board, 1, 3)
            call HTW_Information_PublicTeam(board, 2, 7)
            call HTW_Information_Personal(board, playerId)
        endif
        set playerId = playerId + 1
    endloop
    // Populate every board before local visibility. Do not hide other boards:
    // native false can close the visible board. Preserve manual minimization.
    if firstDisplay then
        set playerId = 1
        loop
            exitwhen playerId > 4
            if HTW_InformationBoard[playerId] != null then
                if GetLocalPlayer() == Player(playerId - 1) then
                    call MultiboardDisplay(HTW_InformationBoard[playerId], true)
                endif
            endif
            set playerId = playerId + 1
        endloop
    endif
    set board = null
endfunction
