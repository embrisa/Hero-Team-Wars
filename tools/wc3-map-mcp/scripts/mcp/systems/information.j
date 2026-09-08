// Presentation only. Allocate at the first runtime tick, never blocking map init.
// All frames and their data are synchronized; only root visibility is local.
// Each regular BUTTON owns exactly one regular tooltip. No frame click events.
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

function HTW_Information_Key takes integer viewer, integer slot returns integer
    return viewer * 32 + slot
endfunction

function HTW_Information_Text takes framehandle parent, integer context, real x, real y, real width, real height, real scale returns framehandle
    local framehandle label = BlzCreateFrameByType("TEXT", "HTWText", parent, "", context)
    call BlzFrameSetPoint(label, FRAMEPOINT_TOPLEFT, parent, FRAMEPOINT_TOPLEFT, x, -y)
    call BlzFrameSetSize(label, width, height)
    call BlzFrameSetTextAlignment(label, TEXT_JUSTIFY_TOP, TEXT_JUSTIFY_LEFT)
    call BlzFrameSetScale(label, scale)
    call BlzFrameSetEnable(label, false)
    return label
endfunction

function HTW_Information_CreateCell takes integer viewer, integer slot, real x, real y, real width, boolean below returns nothing
    local integer key = HTW_Information_Key(viewer, slot)
    local framehandle root = HTW_HudRoot[viewer]
    local framehandle hover = BlzCreateFrameByType("BUTTON", "HTWHover", root, "", key)
    local framehandle icon = BlzCreateFrameByType("BACKDROP", "HTWIcon", hover, "QuestButtonBaseTemplate", key)
    local framehandle tip = BlzCreateFrame("QuestButtonBaseTemplate", hover, 1, key)
    local framehandle label
    // Known stock BACKDROP template supplies required FDF fields, including borders.
    call BlzFrameSetPoint(hover, FRAMEPOINT_TOPLEFT, root, FRAMEPOINT_TOPLEFT, x, -y)
    call BlzFrameSetSize(hover, width, 0.029)
    if below then
        call BlzFrameSetSize(hover, width, 0.040)
        set HTW_HudValue[key] = HTW_Information_Text(hover, key, 0., 0.026, width, 0.012, 0.65)
    else
        set HTW_HudValue[key] = HTW_Information_Text(hover, key, 0.028, 0.006, width - 0.028, 0.016, 0.78)
    endif
    call BlzFrameSetPoint(icon, FRAMEPOINT_TOPLEFT, hover, FRAMEPOINT_TOPLEFT, 0., 0.)
    call BlzFrameSetSize(icon, 0.024, 0.024)
    call BlzFrameSetEnable(icon, false)
    set HTW_HudIcon[key] = icon
    set label = BlzCreateFrameByType("TEXT", "HTWTooltipText", tip, "", key)
    // Fixed tooltip position below the panel, within the regular 4:3 frame area.
    // Zero text height lets wrapping determine height; background follows both corners.
    call BlzFrameSetAbsPoint(label, FRAMEPOINT_TOPRIGHT, 0.779, 0.353)
    call BlzFrameSetSize(label, 0.235, 0.)
    call BlzFrameSetTextAlignment(label, TEXT_JUSTIFY_TOP, TEXT_JUSTIFY_LEFT)
    call BlzFrameSetScale(label, 0.90)
    call BlzFrameSetEnable(label, false)
    call BlzFrameSetPoint(tip, FRAMEPOINT_TOPLEFT, label, FRAMEPOINT_TOPLEFT, -0.008, 0.008)
    call BlzFrameSetPoint(tip, FRAMEPOINT_BOTTOMRIGHT, label, FRAMEPOINT_BOTTOMRIGHT, 0.008, -0.008)
    set HTW_HudTooltipText[key] = label
    call BlzFrameSetTooltip(hover, tip)
    call BlzFrameSetVisible(tip, false)
    // Disabled BUTTON remains hoverable for tooltips and passes clicks through.
    call BlzFrameSetEnable(hover, false)
    set label = null
    set tip = null
    set icon = null
    set hover = null
    set root = null
endfunction

function HTW_Information_OnLoad takes nothing returns nothing
    // Saved framehandles are invalid. The next elapsed display callback replaces
    // every cached HUD handle before use, including the synchronized origin frame.
    // Never hide/destroy stale frames or allocate UI from this load event itself.
    set HTW_InformationReady = false
endfunction

function HTW_Information_CreateHud takes nothing returns nothing
    local framehandle gameUI = BlzGetOriginFrame(ORIGIN_FRAME_GAME_UI, 0)
    local framehandle root
    local framehandle label
    local integer viewer = 1
    local integer slot
    if HTW_HudLoadTrigger == null then
        set HTW_HudLoadTrigger = CreateTrigger()
        call TriggerRegisterGameEvent(HTW_HudLoadTrigger, EVENT_GAME_LOADED)
        call TriggerAddAction(HTW_HudLoadTrigger, function HTW_Information_OnLoad)
    endif
    loop
        exitwhen viewer > 4
        set root = BlzCreateFrame("QuestButtonBaseTemplate", gameUI, 0, viewer)
        set HTW_HudRoot[viewer] = root
        call BlzFrameSetAbsPoint(root, FRAMEPOINT_TOPLEFT, 0.542, 0.552)
        call BlzFrameSetSize(root, 0.250, 0.185)
        call BlzFrameSetVisible(root, false)
        set HTW_HudTitle[viewer] = HTW_Information_Text(root, viewer, 0.009, 0.008, 0.235, 0.018, 0.86)
        set slot = 1
        loop
            exitwhen slot > 6
            call HTW_Information_CreateCell(viewer, slot, 0.009 + (slot - 1) * 0.040, 0.029, 0.036, true)
            set slot = slot + 1
        endloop
        call HTW_Information_CreateCell(viewer, 7, 0.009, 0.077, 0.076, false)
        call HTW_Information_CreateCell(viewer, 8, 0.089, 0.077, 0.076, false)
        call HTW_Information_CreateCell(viewer, 9, 0.169, 0.077, 0.076, false)
        set label = HTW_Information_Text(root, viewer, 0.009, 0.120, 0.041, 0.020, 0.72)
        call BlzFrameSetText(label, "|cffffd580SEND|r")
        set HTW_HudIncoming[viewer] = HTW_Information_Text(root, viewer, 0.009, 0.147, 0.043, 0.032, 0.72)
        set slot = 1
        loop
            exitwhen slot > 3
            call HTW_Information_CreateCell(viewer, 9 + slot, 0.055 + (slot - 1) * 0.063, 0.114, 0.060, false)
            call HTW_Information_CreateCell(viewer, 12 + slot, 0.055 + (slot - 1) * 0.063, 0.151, 0.060, false)
            set slot = slot + 1
        endloop
        set viewer = viewer + 1
    endloop
    set label = null
    set root = null
    set gameUI = null
    set HTW_InformationReady = true
endfunction

function HTW_Information_Cell takes integer viewer, integer slot, string icon, string value, string tooltip returns nothing
    local integer key = HTW_Information_Key(viewer, slot)
    call BlzFrameSetTexture(HTW_HudIcon[key], icon, 0, true)
    call BlzFrameSetText(HTW_HudValue[key], value)
    call BlzFrameSetText(HTW_HudTooltipText[key], tooltip)
endfunction

function HTW_Information_Team takes integer viewer, integer teamId, integer slot returns nothing
    local string status = "Active"
    local string route = "Routes unavailable"
    if not HTW_TeamLiving[teamId] then
        set status = "Eliminated"
    elseif not HTW_Players_IsActive(HTW_TeamMemberA[teamId]) and not HTW_Players_IsActive(HTW_TeamMemberB[teamId]) then
        set status = "Inactive team / practice arena"
    endif
    if HTW_Information_WaveVisible(teamId) then
        set route = "Outgoing: " + HTW_Information_RouteTeam(HTW_TeamDestination[teamId]) + "|nIncoming: " + HTW_Information_RouteTeam(HTW_Routing_Incoming(teamId))
    endif
    call HTW_Information_Cell(viewer, slot, BlzGetAbilityIcon('AHds'), "T" + I2S(teamId) + " " + I2S(HTW_TeamLives[teamId]), "|cffffcc66Team " + I2S(teamId) + " - shared lives|r|n" + I2S(HTW_TeamLives[teamId]) + " lives | " + status + "|n" + route + "|nHero deaths cost shared lives.")
endfunction

function HTW_Information_HeroCell takes integer viewer, integer playerId, integer teamId, integer slot returns nothing
    local string value = "P" + I2S(playerId) + " -"
    local string icon = BlzGetAbilityIcon('n26C')
    if HTW_HeroSelectedByPlayer[playerId] and HTW_Players_IsActive(playerId) then
        set icon = BlzGetAbilityIcon(HTW_HeroTypeByPlayer[playerId])
        if HTW_HeroUnitByPlayer[playerId] != null then
            set value = "P" + I2S(playerId) + " L" + I2S(GetHeroLevel(HTW_HeroUnitByPlayer[playerId]))
        endif
        if not HTW_TeamLiving[teamId] or not HTW_HeroAliveByPlayer[playerId] then
            set value = "P" + I2S(playerId) + " X"
        endif
    endif
    call HTW_Information_Cell(viewer, slot, icon, value, "|cffffcc66Team " + I2S(teamId) + " hero|r|n" + HTW_Information_Hero(playerId, teamId))
endfunction

function HTW_Information_PurchaseStatus takes integer playerId, integer teamId returns string
    if HTW_MatchOver or HTW_TerminalState != 0 then
        return "Match over - purchases closed"
    elseif not HTW_Players_IsActive(playerId) then
        return "Inactive player - purchases closed"
    elseif teamId < 1 or teamId > 2 then
        return "No active team"
    elseif not HTW_TeamLiving[teamId] then
        return "Eliminated - purchases closed"
    elseif not HTW_HeroSelectedByPlayer[playerId] then
        return "Select a hero to prepare"
    elseif HTW_Phase != 1 or HTW_SendPlanLocked then
        return "Purchases closed outside preparation"
    elseif HTW_TeamDestination[teamId] == 0 then
        return "No outgoing destination"
    endif
    return "Preparation open - select War Camp and use Q/W/E"
endfunction

function HTW_Information_Personal takes integer viewer returns nothing
    local integer teamId = HTW_Teams_FindByPlayer(viewer)
    local integer kind = 1
    local integer key
    local integer total
    local integer remaining = HTW_Sending_Budget() - HTW_PlayerThreatUsed[viewer]
    local string status = HTW_Information_PurchaseStatus(viewer, teamId)
    local string route = "None"
    local string incoming = "None"
    local string queueValue
    local string queueDetail
    local string waveDetail
    local string lock = "Updating until preparation ends"
    local boolean visible = HTW_Players_IsActive(viewer) and HTW_Information_WaveVisible(teamId)
    if remaining < 0 then
        set remaining = 0
    endif
    if visible then
        set route = HTW_Information_RouteTeam(HTW_TeamDestination[teamId])
        set incoming = HTW_Information_RouteTeam(HTW_Routing_Incoming(teamId))
    endif
    if HTW_Phase == 2 then
        set lock = "Locked full wave - includes later arrivals"
    endif
    call HTW_Information_Cell(viewer, 7, "UI\\Widgets\\ToolTips\\Human\\ToolTipGoldIcon.blp", I2S(HTW_PlayerGold[viewer]), "|cffffcc66Personal gold - P" + I2S(viewer) + "|r|nAvailable: " + I2S(HTW_PlayerGold[viewer]) + "|nYour purchases spend only your gold.|nWave reward: " + I2S(HTW_WaveReward + HTW_InterestGold) + "|n" + status)
    if visible then
        call HTW_Information_Cell(viewer, 8, BlzGetAbilityIcon('Adef'), I2S(remaining) + " left", "|cffffcc66Personal threat budget|r|n" + I2S(HTW_PlayerThreatUsed[viewer]) + " used | " + I2S(remaining) + " left | " + I2S(HTW_Sending_Budget()) + " total|n" + status)
        call HTW_Information_Cell(viewer, 9, BlzGetAbilityIcon('n26C'), "T" + I2S(HTW_TeamDestination[teamId]), "|cffffcc66Your routes|r|nSend to: " + route + "|nReceive from: " + incoming + "|n" + status)
        call BlzFrameSetText(HTW_HudIncoming[viewer], "|cff90e8bfIN|n" + I2S(HTW_Waves_PlanWorth(teamId)) + " th|r")
    else
        call HTW_Information_Cell(viewer, 8, BlzGetAbilityIcon('Adef'), "-", "|cffffcc66Personal threat budget|r|n" + status)
        call HTW_Information_Cell(viewer, 9, BlzGetAbilityIcon('n26C'), "-", "|cffffcc66Your routes|r|nRoutes unavailable|n" + status)
        call BlzFrameSetText(HTW_HudIncoming[viewer], "|cffaaaaaaIN|n-|r")
    endif
    loop
        exitwhen kind > 3
        set queueValue = "-"
        set queueDetail = status
        set waveDetail = "Wave preview unavailable|n" + status
        set total = 0
        if visible then
            set queueValue = I2S(HTW_PlayerQueueCount[HTW_PlanKey(viewer, kind)])
            set queueDetail = "P" + I2S(viewer) + " queued: " + queueValue + "|nSend to: " + route + "|n" + status
            set key = HTW_PlanKey(teamId, kind)
            set total = HTW_PlanBase[key] + HTW_PlanSends[key] + HTW_PlanFiller[key]
            set waveDetail = lock + "|nFrom: " + incoming + "|nBase: " + I2S(HTW_PlanBase[key]) + " | Enemy sends: " + I2S(HTW_PlanSends[key]) + " | Filler: " + I2S(HTW_PlanFiller[key]) + "|nTotal: " + I2S(total) + " | Threat: " + I2S(total * HTW_SendCatalog_Threat(kind)) + "|nWhole wave threat: " + I2S(HTW_Waves_PlanWorth(teamId))
        endif
        call HTW_Information_Cell(viewer, 9 + kind, BlzGetAbilityIcon(HTW_SendCatalog_UnitType(kind)), queueValue, "|cffffcc66Send " + HTW_SendCatalog_Name(kind) + "|r|n" + HTW_SendCatalog_Role(kind) + " | " + I2S(HTW_SendCatalog_Cost(kind)) + " gold | " + I2S(HTW_SendCatalog_Threat(kind)) + " threat|n" + HTW_SendCatalog_Description(kind) + "|n" + queueDetail)
        set queueValue = "-"
        if visible then
            set queueValue = I2S(total)
        endif
        call HTW_Information_Cell(viewer, 12 + kind, BlzGetAbilityIcon(HTW_SendCatalog_UnitType(kind)), queueValue, "|cff90e8bfIncoming " + HTW_SendCatalog_Name(kind) + "|r|n" + HTW_SendCatalog_Role(kind) + " | " + I2S(HTW_SendCatalog_Threat(kind)) + " threat each|n" + waveDetail)
        set kind = kind + 1
    endloop
endfunction

function HTW_Information_Display takes nothing returns nothing
    local integer viewer = 1
    local boolean firstDisplay = not HTW_InformationReady
    if firstDisplay then
        call HTW_Information_CreateHud()
    endif
    loop
        exitwhen viewer > 4
        call BlzFrameSetText(HTW_HudTitle[viewer], "|cffffcc66W" + I2S(HTW_Wave) + " | " + HTW_Information_Phase() + "|r")
        call HTW_Information_Team(viewer, 1, 1)
        call HTW_Information_HeroCell(viewer, HTW_TeamMemberA[1], 1, 2)
        call HTW_Information_HeroCell(viewer, HTW_TeamMemberB[1], 1, 3)
        call HTW_Information_Team(viewer, 2, 4)
        call HTW_Information_HeroCell(viewer, HTW_TeamMemberA[2], 2, 5)
        call HTW_Information_HeroCell(viewer, HTW_TeamMemberB[2], 2, 6)
        call HTW_Information_Personal(viewer)
        set viewer = viewer + 1
    endloop
    if firstDisplay then
        set viewer = 1
        loop
            exitwhen viewer > 4
            if GetLocalPlayer() == Player(viewer - 1) then
                call BlzFrameSetVisible(HTW_HudRoot[viewer], true)
            endif
            set viewer = viewer + 1
        endloop
    endif
endfunction
