function HTW_Lives_AccountDeath takes nothing returns nothing
    local unit deadHero
    local integer playerId
    local integer teamIndex
    set deadHero = GetTriggerUnit()
    set playerId = HTW_Heroes_PlayerId(deadHero)
    if playerId == 0 or HTW_HeroDeathAccountedByPlayer[playerId] then
        set deadHero = null
        return
    endif
    set HTW_HeroDeathAccountedByPlayer[playerId] = true
    set HTW_HeroAliveByPlayer[playerId] = false
    set HTW_AliveHeroCount = HTW_AliveHeroCount - 1
    set teamIndex = HTW_Teams_FindByPlayer(playerId)
    if teamIndex > 0 and HTW_TeamLiving[teamIndex] then
        set HTW_TeamDeathsThisWave[teamIndex] = HTW_TeamDeathsThisWave[teamIndex] + 1
        if HTW_TeamDeathsThisWave[teamIndex] == 1 then
            set HTW_TeamLives[teamIndex] = HTW_TeamLives[teamIndex] - HTW_SingleDeathCost
        else
            // The second hero death costs two additional lives, making a
            // two-hero wipe exactly three lives for this wave.
            set HTW_TeamLives[teamIndex] = HTW_TeamLives[teamIndex] - HTW_AdditionalWipeCost
        endif
        if HTW_TeamLives[teamIndex] <= 0 then
            set HTW_TeamLiving[teamIndex] = false
            call HTW_Elimination_Recalculate()
        endif
    endif
    call HTW_Debug_LogText("hero death accounted once")
    set deadHero = null
endfunction
