// Solo practice authority. Both chat and synchronized frame clicks use Run.
// Never call Run from a GetLocalPlayer branch or add a second sync-data hop.
function HTW_Dev_CanUse takes integer playerId returns boolean
    local integer slot = 1
    local integer humans = 0
    if not HTW_DevEnabled or HTW_ActivePlayerCount != 4 or HTW_TeamCount != 2 then
        return false
    endif
    if playerId < 1 or playerId > 4 or not HTW_Players_IsActive(playerId) then
        return false
    endif
    loop
        exitwhen slot > 4
        if HTW_Players_IsActive(slot) then
            set humans = humans + 1
        endif
        set slot = slot + 1
    endloop
    return humans == 1
endfunction

function HTW_Dev_Feedback takes integer playerId, string message returns nothing
    if playerId >= 1 and playerId <= 4 then
        call DisplayTimedTextToPlayer(Player(playerId - 1), 0., 0., 8., "|cff80ddffDev:|r " + message)
    endif
endfunction

function HTW_Dev_Reject takes integer playerId, string message returns boolean
    call HTW_Dev_Feedback(playerId, message)
    return false
endfunction

function HTW_Dev_Success takes integer playerId, string message, boolean changed returns boolean
    if changed then
        set HTW_DevUsed = true
    endif
    if message != "" then
        call HTW_Dev_Feedback(playerId, message)
    endif
    call HTW_Information_Display()
    return true
endfunction

function HTW_Dev_Trim takes string value returns string
    local integer first = 0
    local integer last = StringLength(value)
    loop
        exitwhen first >= last or SubString(value, first, first + 1) != " "
        set first = first + 1
    endloop
    loop
        exitwhen last <= first or SubString(value, last - 1, last) != " "
        set last = last - 1
    endloop
    return SubString(value, first, last)
endfunction

// Positive ASCII digits only. Check the bound BEFORE multiplying or adding.
// S2I sees one verified digit, never an untrusted number with a valid prefix.
function HTW_Dev_ParsePositive takes string value, integer maximum returns integer
    local integer index = 0
    local integer length = StringLength(value)
    local integer result = 0
    local integer digit
    local string character
    if length == 0 then
        return -1
    endif
    loop
        exitwhen index >= length
        set character = SubString(value, index, index + 1)
        set digit = S2I(character)
        if SubString("0123456789", digit, digit + 1) != character then
            return -1
        endif
        if result > maximum / 10 or (result == maximum / 10 and digit > maximum - (maximum / 10) * 10) then
            return -1
        endif
        set result = result * 10 + digit
        set index = index + 1
    endloop
    if result < 1 then
        return -1
    endif
    return result
endfunction

function HTW_Dev_LiveTeam takes integer playerId returns boolean
    local integer teamId = HTW_Teams_FindByPlayer(playerId)
    if HTW_MatchOver or HTW_TerminalState != 0 or HTW_Phase >= 4 then
        return false
    endif
    if teamId < 1 or teamId > 2 then
        return false
    endif
    return HTW_TeamLiving[teamId] and HTW_TeamLives[teamId] > 0
endfunction

function HTW_Dev_HasHero takes integer playerId returns boolean
    local unit hero = HTW_HeroUnitByPlayer[playerId]
    local boolean valid = HTW_HeroSelectedByPlayer[playerId] and hero != null
    if valid then
        set valid = GetUnitTypeId(hero) != 0 and IsUnitType(hero, UNIT_TYPE_HERO)
    endif
    set hero = null
    return valid
endfunction

function HTW_Dev_ClearClock takes nothing returns nothing
    set HTW_DevClockHeld = false
    set HTW_DevClockPhase = 0
    set HTW_DevClockRemaining = 0.
    set HTW_DevClockPlayer = 0
endfunction

function HTW_Dev_ResumeClock takes nothing returns nothing
    local real remaining = HTW_DevClockRemaining
    local integer phase = HTW_DevClockPhase
    local boolean restart = HTW_DevClockHeld and phase == HTW_Phase and not HTW_MatchOver and HTW_TerminalState == 0
    call HTW_Dev_ClearClock()
    if not restart then
        return
    endif
    if remaining <= 0. then
        set remaining = 0.01
    endif
    // ResumeTimer can fire twice. Restart the same timer as a one-shot instead.
    if phase == 1 and HTW_PreparationTimer != null then
        call TimerStart(HTW_PreparationTimer, remaining, false, function HTW_Phases_BeginCombat)
    elseif phase == 2 and HTW_CombatTimer != null then
        call TimerStart(HTW_CombatTimer, remaining, false, function HTW_Phases_BeginResolution)
    endif
endfunction

function HTW_Dev_HoldClock takes integer playerId returns boolean
    local timer clock = null
    if HTW_Phase == 1 then
        set clock = HTW_PreparationTimer
    elseif HTW_Phase == 2 then
        set clock = HTW_CombatTimer
    endif
    if clock == null then
        return HTW_Dev_Reject(playerId, "A phase timer is available only during preparation or combat.")
    endif
    set HTW_DevClockRemaining = TimerGetRemaining(clock)
    call PauseTimer(clock)
    set HTW_DevClockHeld = true
    set HTW_DevClockPhase = HTW_Phase
    set HTW_DevClockPlayer = playerId
    set clock = null
    return true
endfunction

function HTW_Dev_Tick takes nothing returns nothing
    local integer playerId = 1
    if HTW_DevClockHeld then
        if HTW_MatchOver or HTW_TerminalState != 0 or HTW_DevClockPhase != HTW_Phase or (HTW_Phase != 1 and HTW_Phase != 2) then
            call HTW_Dev_ClearClock()
        elseif not HTW_Dev_CanUse(HTW_DevClockPlayer) then
            // Losing solo eligibility must never strand a paused normal match.
            call HTW_Dev_ResumeClock()
        endif
    endif
    loop
        exitwhen playerId > 4
        if HTW_DevMenuOpen[playerId] and not HTW_Dev_CanUse(playerId) then
            set HTW_DevMenuOpen[playerId] = false
        endif
        set playerId = playerId + 1
    endloop
endfunction

function HTW_Dev_SyncHeroState takes nothing returns nothing
    local integer playerId = 1
    local boolean alive
    set HTW_AliveHeroCount = 0
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        set alive = false
        if HTW_Dev_HasHero(playerId) then
            set alive = not IsUnitType(HTW_HeroUnitByPlayer[playerId], UNIT_TYPE_DEAD)
        endif
        set HTW_HeroAliveByPlayer[playerId] = alive
        if alive then
            set HTW_HeroDeathAccountedByPlayer[playerId] = false
            set HTW_AliveHeroCount = HTW_AliveHeroCount + 1
        endif
        set playerId = playerId + 1
    endloop
endfunction

function HTW_Dev_HealHero takes integer playerId returns boolean
    local unit hero = HTW_HeroUnitByPlayer[playerId]
    local integer teamId = HTW_Teams_FindByPlayer(playerId)
    if not HTW_Dev_HasHero(playerId) or teamId < 1 or teamId > 2 then
        set hero = null
        return false
    endif
    if IsUnitType(hero, UNIT_TYPE_DEAD) then
        if not ReviveHero(hero, GetRectCenterX(HTW_ArenaRect[teamId]), GetRectCenterY(HTW_ArenaRect[teamId]), true) then
            set hero = null
            return false
        endif
    endif
    call SetUnitState(hero, UNIT_STATE_LIFE, GetUnitState(hero, UNIT_STATE_MAX_LIFE))
    call SetUnitState(hero, UNIT_STATE_MANA, GetUnitState(hero, UNIT_STATE_MAX_MANA))
    call UnitResetCooldown(hero)
    call HTW_Dev_SyncHeroState()
    set hero = null
    return true
endfunction

function HTW_Dev_StopWave takes nothing returns nothing
    local integer playerId = 1
    call HTW_Dev_ClearClock()
    if HTW_PreparationTimer != null then
        call PauseTimer(HTW_PreparationTimer)
    endif
    if HTW_CombatTimer != null then
        call PauseTimer(HTW_CombatTimer)
    endif
    if HTW_SendTimer != null then
        call PauseTimer(HTW_SendTimer)
    endif
    // Abandon directly; Waves_Resolve would grant a reward and emit an event.
    set HTW_WaveActive = false
    set HTW_ResolutionApplied = true
    call HTW_Content_CleanupBaseWaves()
    call HTW_Sending_ResetQueues()
    set HTW_SendCursor = 1
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        set HTW_PlayerQueueUnitType[playerId] = 0
        set HTW_PlayerQueueRemaining[playerId] = 0
        set playerId = playerId + 1
    endloop
    set HTW_RoutingLocked = false
endfunction

function HTW_Dev_FreshPreparation takes integer wave returns boolean
    call HTW_Dev_StopWave()
    set HTW_Round = wave
    // Waves_Prepare owns the single wave increment and fresh plan/timer setup.
    set HTW_Wave = wave - 1
    if not HTW_HeroSelectionComplete then
        set HTW_Phase = 0
        call HTW_HeroSelection_OnTimeout()
    else
        set HTW_Phase = 1
        call HTW_Waves_Prepare()
    endif
    return HTW_Phase == 1 and HTW_WaveActive and HTW_Wave == wave
endfunction

function HTW_Dev_Reset takes integer caller returns boolean
    local integer teamId = 1
    local integer playerId = 1
    local integer failedHeals = 0
    local boolean prepared
    call HTW_Dev_StopWave()
    set HTW_MatchOver = false
    set HTW_TerminalState = 0
    set HTW_TransitionGuard = false
    set HTW_LastResolvedWave = 0
    loop
        exitwhen teamId > HTW_TeamCount
        set HTW_TeamLives[teamId] = 15
        set HTW_TeamLiving[teamId] = true
        set HTW_TeamDeathsThisWave[teamId] = 0
        set HTW_TeamDestination[teamId] = 0
        set teamId = teamId + 1
    endloop
    // Rebuild living-team membership before the fresh routing calculation.
    call HTW_Elimination_Recalculate()
    set prepared = HTW_Dev_FreshPreparation(1)
    loop
        exitwhen playerId > HTW_ActivePlayerCount
        if HTW_Players_IsActive(playerId) and HTW_HeroSelectedByPlayer[playerId] then
            if not HTW_Dev_HealHero(playerId) then
                set failedHeals = failedHeals + 1
            endif
        endif
        set playerId = playerId + 1
    endloop
    call HTW_Dev_SyncHeroState()
    if failedHeals > 0 then
        call HTW_Dev_Feedback(caller, "Practice reset, but " + I2S(failedHeals) + " hero revival(s) failed. Check the hero and try heal again.")
    endif
    return prepared
endfunction

function HTW_Dev_Help takes integer playerId returns nothing
    call HTW_Dev_Feedback(playerId, "Use -dev <command>. menu: toggle buttons; help: this list; next: advance normally; prep: restart this wave; combat: finish selection and start combat from preparation.")
    call HTW_Dev_Feedback(playerId, "hold: toggle preparation/combat timer only (units keep running); resume: release hold. gold [N] / xp [N]: add 1..100000, default 1000; gold balance cap 1000000.")
    call HTW_Dev_Feedback(playerId, "level N: exact level 1..10 (lowering may unlearn/reduce skills); levelup: +1, max 10; heal: revive, full HP/mana and cooldown reset. lives N: team lives 1..999; addlives: +5, cap 999.")
    call HTW_Dev_Feedback(playerId, "wave N: fresh preparation for wave 1..100; nextwave: +1, max 100. prep/wave/nextwave give no rewards. reset: wave 1, lives 15, restore teams/heroes; keep chosen heroes, XP and gold.")
endfunction

function HTW_Dev_Run takes integer playerId, string command returns boolean
    local string verb
    local string argument
    local integer split = 0
    local integer length
    local integer amount = 0
    local integer maximum = 0
    local integer teamId
    local integer current
    local unit hero = null
    if not HTW_Dev_CanUse(playerId) then
        return HTW_Dev_Reject(playerId, "Controls require one active human in the four-player, two-team MVP.")
    endif
    set command = HTW_Dev_Trim(command)
    set length = StringLength(command)
    loop
        exitwhen split >= length or SubString(command, split, split + 1) == " "
        set split = split + 1
    endloop
    set verb = StringCase(SubString(command, 0, split), false)
    set argument = ""
    if split < length then
        set argument = HTW_Dev_Trim(SubString(command, split + 1, length))
    endif
    // Parse the entire optional/required argument before any gameplay writes.
    if verb == "gold" or verb == "xp" then
        set maximum = 100000
        if argument == "" then
            set argument = "1000"
        endif
    elseif verb == "level" then
        set maximum = 10
    elseif verb == "lives" then
        set maximum = 999
    elseif verb == "wave" then
        set maximum = 100
    elseif argument != "" then
        return HTW_Dev_Reject(playerId, "That command takes no value. Use -dev help for exact commands.")
    endif
    if maximum > 0 then
        set amount = HTW_Dev_ParsePositive(argument, maximum)
        if amount < 1 then
            return HTW_Dev_Reject(playerId, "Use " + verb + " with one whole number from 1 to " + I2S(maximum) + "; no signs, suffixes or extra values.")
        endif
    endif
    if verb == "menu" then
        set HTW_DevMenuOpen[playerId] = not HTW_DevMenuOpen[playerId]
        if HTW_DevMenuOpen[playerId] then
            return HTW_Dev_Success(playerId, "Menu opened. Use -dev help for text commands.", false)
        endif
        return HTW_Dev_Success(playerId, "Menu closed. Text commands remain available.", false)
    elseif verb == "help" then
        call HTW_Dev_Help(playerId)
        return HTW_Dev_Success(playerId, "", false)
    elseif verb == "gold" then
        set current = HTW_PlayerGold[playerId]
        if current >= 1000000 then
            return HTW_Dev_Reject(playerId, "Personal gold is already at the dev cap of 1000000.")
        endif
        if amount > 1000000 - current then
            set amount = 1000000 - current
        endif
        set HTW_PlayerGold[playerId] = current + amount
        call HTW_Economy_SyncGold(playerId)
        return HTW_Dev_Success(playerId, "Added " + I2S(amount) + " gold; balance " + I2S(HTW_PlayerGold[playerId]) + ".", true)
    elseif verb == "xp" or verb == "level" or verb == "levelup" then
        if not HTW_Dev_HasHero(playerId) then
            return HTW_Dev_Reject(playerId, "Choose your hero first; this command targets your tracked hero.")
        endif
        set hero = HTW_HeroUnitByPlayer[playerId]
        if verb == "xp" then
            call AddHeroXP(hero, amount, true)
        else
            set current = GetHeroLevel(hero)
            if verb == "levelup" then
                if current >= 10 then
                    set hero = null
                    return HTW_Dev_Reject(playerId, "Your hero is already at the dev level cap of 10.")
                endif
                set amount = current + 1
            endif
            if amount > current then
                call SetHeroLevel(hero, amount, true)
            elseif amount < current then
                if not UnitStripHeroLevel(hero, current - amount) then
                    set hero = null
                    return HTW_Dev_Reject(playerId, "The game could not lower this hero's level.")
                endif
                call HTW_Dev_Feedback(playerId, "Lowering levels can unlearn or reduce hero skills.")
            endif
        endif
        set current = GetHeroLevel(hero)
        set hero = null
        return HTW_Dev_Success(playerId, "Hero updated; level " + I2S(current) + ".", true)
    elseif verb == "reset" then
        if HTW_TransitionGuard then
            return HTW_Dev_Reject(playerId, "A phase transition is in progress; try again.")
        endif
        if not HTW_Dev_Reset(playerId) then
            return HTW_Dev_Reject(playerId, "Reset could not finish hero selection; choose a hero and retry.")
        endif
        return HTW_Dev_Success(playerId, "Practice restarted at wave 1 with 15 lives. Chosen heroes, XP and gold kept.", true)
    endif
    if verb != "next" and verb != "prep" and verb != "combat" and verb != "hold" and verb != "resume" and verb != "heal" and verb != "lives" and verb != "addlives" and verb != "wave" and verb != "nextwave" then
        return HTW_Dev_Reject(playerId, "Unknown command. Use -dev help.")
    endif
    if not HTW_Dev_LiveTeam(playerId) then
        return HTW_Dev_Reject(playerId, "Your team or match has ended. Use reset to restart practice.")
    endif
    if HTW_TransitionGuard then
        return HTW_Dev_Reject(playerId, "A phase transition is in progress; try again.")
    endif
    if verb == "lives" or verb == "addlives" then
        set teamId = HTW_Teams_FindByPlayer(playerId)
        if verb == "addlives" then
            set amount = HTW_TeamLives[teamId] + 5
            if amount > 999 then
                set amount = 999
            endif
        endif
        set HTW_TeamLives[teamId] = amount
        return HTW_Dev_Success(playerId, "Team lives set to " + I2S(amount) + ".", true)
    elseif verb == "heal" then
        if not HTW_Dev_HealHero(playerId) then
            return HTW_Dev_Reject(playerId, "Choose a hero first, or retry if the game could not revive your hero.")
        endif
        return HTW_Dev_Success(playerId, "Hero restored: full HP/mana and cooldowns ready.", true)
    elseif verb == "hold" or verb == "resume" then
        if HTW_Phase != 1 and HTW_Phase != 2 then
            return HTW_Dev_Reject(playerId, "Hold and resume apply only to preparation or combat timers.")
        endif
        if HTW_DevClockHeld and HTW_DevClockPhase == HTW_Phase then
            call HTW_Dev_ResumeClock()
            return HTW_Dev_Success(playerId, "Phase timer resumed.", true)
        elseif verb == "resume" then
            return HTW_Dev_Reject(playerId, "The phase timer is not held.")
        endif
        if not HTW_Dev_HoldClock(playerId) then
            return false
        endif
        return HTW_Dev_Success(playerId, "Phase timer HELD. Units and gameplay keep running.", true)
    elseif verb == "prep" or verb == "wave" or verb == "nextwave" then
        if verb == "prep" then
            set amount = HTW_Wave
            if amount < 1 then
                set amount = 1
            endif
        elseif verb == "nextwave" then
            if HTW_Wave >= 100 then
                return HTW_Dev_Reject(playerId, "The dev wave cap is 100; use wave 1 to jump back.")
            endif
            set amount = HTW_Wave + 1
        endif
        if not HTW_Dev_FreshPreparation(amount) then
            return HTW_Dev_Reject(playerId, "Preparation could not finish hero selection; choose a hero and retry.")
        endif
        return HTW_Dev_Success(playerId, "Fresh preparation for wave " + I2S(HTW_Wave) + "; no wave reward granted.", true)
    elseif verb == "combat" then
        if HTW_Phase != 0 and HTW_Phase != 1 then
            return HTW_Dev_Reject(playerId, "Combat can start only from selection or preparation.")
        endif
        call HTW_Dev_ClearClock()
        if HTW_Phase == 0 then
            call HTW_HeroSelection_OnTimeout()
        endif
        if HTW_Phase != 1 then
            return HTW_Dev_Reject(playerId, "Finish choosing your hero before starting combat.")
        endif
        call HTW_Phases_BeginCombat()
    elseif verb == "next" then
        if HTW_Phase < 0 or HTW_Phase > 3 then
            return HTW_Dev_Reject(playerId, "There is no active phase to advance.")
        endif
        call HTW_Dev_ClearClock()
        if HTW_Phase == 0 then
            call HTW_HeroSelection_OnTimeout()
        elseif HTW_Phase == 1 then
            call HTW_Phases_BeginCombat()
        elseif HTW_Phase == 2 then
            // The generated wave_resolved event already calls Waves_Prepare.
            call HTW_Phases_BeginResolution()
        elseif HTW_Phase == 3 then
            if HTW_WaveActive and not HTW_ResolutionApplied then
                call HTW_Waves_Resolve()
            elseif not HTW_WaveActive then
                call HTW_Waves_Prepare()
            else
                return HTW_Dev_Reject(playerId, "Wave resolution is in progress; try again.")
            endif
        endif
    endif
    return HTW_Dev_Success(playerId, "Wave " + I2S(HTW_Wave) + " | " + HTW_Information_Phase() + ".", true)
endfunction

function HTW_Dev_OnChat takes nothing returns nothing
    local string message = GetEventPlayerChatString()
    local integer length = StringLength(message)
    local integer playerId = GetPlayerId(GetTriggerPlayer()) + 1
    // Registration with exactMatchOnly=false also matches substrings. Require
    // the exact prefix and its word boundary before dispatching any command.
    if message == "-dev" then
        call HTW_Dev_Run(playerId, "menu")
    elseif length >= 5 and SubString(message, 0, 5) == "-dev " then
        call HTW_Dev_Run(playerId, SubString(message, 5, length))
    endif
endfunction

function HTW_Dev_Initialize takes nothing returns nothing
    local integer playerId = 1
    if HTW_DevChatTrigger != null then
        return
    endif
    set HTW_DevChatTrigger = CreateTrigger()
    loop
        exitwhen playerId > 4
        call TriggerRegisterPlayerChatEvent(HTW_DevChatTrigger, Player(playerId - 1), "-dev", false)
        set playerId = playerId + 1
    endloop
    call TriggerAddAction(HTW_DevChatTrigger, function HTW_Dev_OnChat)
    set playerId = 1
    loop
        exitwhen playerId > 4
        if HTW_Dev_CanUse(playerId) then
            call HTW_Dev_Feedback(playerId, "v29 controls ready. Click DEV or type -dev help.")
        endif
        set playerId = playerId + 1
    endloop
endfunction
