// Regular Frame UI, allocated for every client at elapsed time only.
// Native CONTROL_CLICK events are synchronized. Only root/panel visibility is local.
function HTW_DevMenu_Key takes integer viewer, integer slot returns integer
    return viewer * 16 + slot
endfunction

function HTW_DevMenu_Command takes integer slot returns string
    if slot == 0 then
        return "menu"
    elseif slot == 1 then
        return "next"
    elseif slot == 2 then
        return "prep"
    elseif slot == 3 then
        return "combat"
    elseif slot == 4 then
        return "hold"
    elseif slot == 5 then
        return "gold 1000"
    elseif slot == 6 then
        return "xp 1000"
    elseif slot == 7 then
        return "levelup"
    elseif slot == 8 then
        return "heal"
    elseif slot == 9 then
        return "addlives"
    elseif slot == 10 then
        return "nextwave"
    elseif slot == 11 then
        return "reset"
    elseif slot == 12 then
        return "help"
    endif
    return ""
endfunction

function HTW_DevMenu_Label takes integer slot returns string
    if slot == 0 then
        return "DEV"
    elseif slot == 1 then
        return "Next phase"
    elseif slot == 2 then
        return "Restart prep"
    elseif slot == 3 then
        return "Combat now"
    elseif slot == 4 then
        if HTW_DevClockHeld then
            return "Resume timer"
        endif
        return "Hold timer"
    elseif slot == 5 then
        return "+1000 gold"
    elseif slot == 6 then
        return "+1000 XP"
    elseif slot == 7 then
        return "+1 level"
    elseif slot == 8 then
        return "Heal / cooldowns"
    elseif slot == 9 then
        return "+5 lives"
    elseif slot == 10 then
        return "Next wave"
    elseif slot == 11 then
        return "Reset practice"
    endif
    return "Commands / help"
endfunction

function HTW_DevMenu_Tooltip takes integer slot returns string
    local string detail
    if slot == 0 then
        return "|cffffcc66Solo developer controls|r|nOpen/close this panel, or type -dev.|nControls are available when you are the only active human player."
    elseif slot == 1 then
        set detail = "Advance immediately. Selection auto-picks a hero; preparation starts combat; combat resolves normally with its wave reward."
    elseif slot == 2 then
        set detail = "Restart preparation for this wave. Clears current creeps, sends and queues; grants no wave reward. Keeps resources and hero."
    elseif slot == 3 then
        set detail = "Start combat now from preparation, locking the full incoming plan. Finishes hero selection first if needed."
    elseif slot == 4 then
        set detail = "Hold/resume the preparation or combat countdown. Units and combat keep running. Phase jumps release the hold."
    elseif slot == 5 then
        set detail = "Add 1000 personal gold. Custom amount: -dev gold 5000. Positive additions up to 100000; total balance capped at 1000000."
    elseif slot == 6 then
        set detail = "Add 1000 XP to your chosen hero. Custom amount: -dev xp 2500. Positive additions up to 100000."
    elseif slot == 7 then
        set detail = "Raise your hero one level, up to 10. Set a level: -dev level 5. Lowering levels can remove learned ability ranks."
    elseif slot == 8 then
        set detail = "Restore your hero's HP/mana and reset ability cooldowns. Revive a dead hero if your team is still living."
    elseif slot == 9 then
        set detail = "Add five shared lives to your living team, up to 999. Set a value: -dev lives 1. Use Reset practice after elimination."
    elseif slot == 10 then
        set detail = "Jump to fresh preparation for the next wave, without a reward. Clears old creeps/queues. Choose a wave: -dev wave 10 (1-100)."
    elseif slot == 11 then
        set detail = "Restart practice at wave 1 with both teams' starting lives. Clears creeps, sends and terminal state. Keeps heroes, gold and XP."
    else
        set detail = "Print the complete command list and numeric limits in chat. All buttons have equivalent -dev commands."
    endif
    return "|cffffcc66" + HTW_DevMenu_Label(slot) + "|r|n" + detail + "|n|n-dev " + HTW_DevMenu_Command(slot)
endfunction

function HTW_DevMenu_Refresh takes nothing returns nothing
    local integer viewer = 1
    local boolean available
    local string status = "SOLO DEV | timer running"
    if not HTW_DevUIReady then
        return
    endif
    if HTW_DevClockHeld then
        set status = "SOLO DEV | TIMER HELD"
    endif
    loop
        exitwhen viewer > 4
        call BlzFrameSetText(HTW_DevUITitle[viewer], "|cffffcc66" + status + "|r")
        call BlzFrameSetText(HTW_DevUIButton[HTW_DevMenu_Key(viewer, 4)], HTW_DevMenu_Label(4))
        set available = HTW_Dev_CanUse(viewer)
        if GetLocalPlayer() == Player(viewer - 1) then
            call BlzFrameSetVisible(HTW_DevUIRoot[viewer], available)
            call BlzFrameSetVisible(HTW_DevUIPanel[viewer], available and HTW_DevMenuOpen[viewer])
        endif
        set viewer = viewer + 1
    endloop
endfunction

function HTW_DevMenu_OnClick takes nothing returns nothing
    local integer viewer = GetPlayerId(GetTriggerPlayer()) + 1
    local integer slot = 0
    local framehandle clicked = BlzGetTriggerFrame()
    if not HTW_DevUIReady or viewer < 1 or viewer > 4 then
        set clicked = null
        return
    endif
    loop
        exitwhen slot > 12
        if clicked == HTW_DevUIButton[HTW_DevMenu_Key(viewer, slot)] then
            // Clear keyboard focus; do not use frame text or local UI state as input.
            call BlzFrameSetEnable(clicked, false)
            call BlzFrameSetEnable(clicked, true)
            call HTW_Dev_Run(viewer, HTW_DevMenu_Command(slot))
            call HTW_DevMenu_Refresh()
            set clicked = null
            return
        endif
        set slot = slot + 1
    endloop
    set clicked = null
endfunction

function HTW_DevMenu_CreateButton takes integer viewer, integer slot, framehandle parent, real x, real y, real width returns nothing
    local integer key = HTW_DevMenu_Key(viewer, slot)
    local framehandle button = BlzCreateFrame("ScriptDialogButton", parent, 0, 400 + key)
    local framehandle tip = BlzCreateFrame("QuestButtonBaseTemplate", button, 1, 400 + key)
    local framehandle text = BlzCreateFrameByType("TEXT", "HTWDevTip", tip, "", 400 + key)
    call BlzFrameSetPoint(button, FRAMEPOINT_TOPLEFT, parent, FRAMEPOINT_TOPLEFT, x, -y)
    call BlzFrameSetSize(button, width, 0.024)
    call BlzFrameSetText(button, HTW_DevMenu_Label(slot))
    set HTW_DevUIButton[key] = button
    call BlzFrameSetAbsPoint(text, FRAMEPOINT_TOPRIGHT, 0.520, 0.294)
    call BlzFrameSetSize(text, 0.230, 0.)
    call BlzFrameSetScale(text, 0.85)
    call BlzFrameSetEnable(text, false)
    call BlzFrameSetTextAlignment(text, TEXT_JUSTIFY_TOP, TEXT_JUSTIFY_LEFT)
    call BlzFrameSetText(text, HTW_DevMenu_Tooltip(slot))
    call BlzFrameSetPoint(tip, FRAMEPOINT_TOPLEFT, text, FRAMEPOINT_TOPLEFT, -0.008, 0.008)
    call BlzFrameSetPoint(tip, FRAMEPOINT_BOTTOMRIGHT, text, FRAMEPOINT_BOTTOMRIGHT, 0.008, -0.008)
    call BlzFrameSetTooltip(button, tip)
    call BlzFrameSetVisible(tip, false)
    call BlzTriggerRegisterFrameEvent(HTW_DevUIClick, button, FRAMEEVENT_CONTROL_CLICK)
    set text = null
    set tip = null
    set button = null
endfunction

function HTW_DevMenu_Create takes nothing returns nothing
    local framehandle gameUI = BlzGetOriginFrame(ORIGIN_FRAME_GAME_UI, 0)
    local framehandle root
    local framehandle panel
    local integer viewer = 1
    local integer slot
    set HTW_DevUIClick = CreateTrigger()
    call TriggerAddAction(HTW_DevUIClick, function HTW_DevMenu_OnClick)
    loop
        exitwhen viewer > 4
        set root = BlzCreateFrameByType("FRAME", "HTWDevRoot", gameUI, "", 400 + viewer)
        set HTW_DevUIRoot[viewer] = root
        call BlzFrameSetAbsPoint(root, FRAMEPOINT_TOPLEFT, 0.281, 0.550)
        call BlzFrameSetSize(root, 0.250, 0.242)
        call BlzFrameSetVisible(root, false)
        set panel = BlzCreateFrame("QuestButtonBaseTemplate", root, 0, 400 + viewer)
        set HTW_DevUIPanel[viewer] = panel
        call BlzFrameSetPoint(panel, FRAMEPOINT_TOPLEFT, root, FRAMEPOINT_TOPLEFT, 0., -0.028)
        call BlzFrameSetSize(panel, 0.250, 0.214)
        call BlzFrameSetVisible(panel, false)
        set HTW_DevUITitle[viewer] = HTW_Information_Text(panel, 400 + viewer, 0.009, 0.009, 0.234, 0.018, 0.82)
        call HTW_DevMenu_CreateButton(viewer, 0, root, 0.207, 0., 0.043)
        set slot = 1
        loop
            exitwhen slot > 12
            call HTW_DevMenu_CreateButton(viewer, slot, panel, 0.009 + ModuloInteger(slot - 1, 2) * 0.120, 0.035 + ((slot - 1) / 2) * 0.027, 0.112)
            set slot = slot + 1
        endloop
        set viewer = viewer + 1
    endloop
    set panel = null
    set root = null
    set gameUI = null
    set HTW_DevUIReady = true
endfunction

function HTW_DevMenu_OnLoad takes nothing returns nothing
    // The trigger is saved game state; old framehandles must never be touched.
    set HTW_DevUIReady = false
    if HTW_DevUIClick != null then
        call DestroyTrigger(HTW_DevUIClick)
        set HTW_DevUIClick = null
    endif
endfunction

function HTW_DevMenu_Initialize takes nothing returns nothing
    if HTW_DevUILoad == null then
        set HTW_DevUILoad = CreateTrigger()
        call TriggerRegisterGameEvent(HTW_DevUILoad, EVENT_GAME_LOADED)
        call TriggerAddAction(HTW_DevUILoad, function HTW_DevMenu_OnLoad)
    endif
endfunction

function HTW_DevMenu_Display takes nothing returns nothing
    if not HTW_DevUIReady then
        call HTW_DevMenu_Create()
    endif
    call HTW_DevMenu_Refresh()
endfunction
