function HTW_Routing_Incoming takes integer targetTeam returns integer
    local integer teamIndex
    if HTW_MatchOver or targetTeam < 1 or targetTeam > HTW_TeamCount or not HTW_TeamLiving[targetTeam] then
        return 0
    endif
    set teamIndex = 1
    loop
        exitwhen teamIndex > HTW_TeamCount
        if HTW_TeamLiving[teamIndex] and HTW_TeamDestination[teamIndex] == targetTeam then
            return teamIndex
        endif
        set teamIndex = teamIndex + 1
    endloop
    return 0
endfunction

function HTW_Routing_Compute takes nothing returns nothing
    local integer teamIndex
    local integer livingIndex
    if HTW_RoutingLocked then
        return
    endif
    set HTW_RouteOffset = 0
    set HTW_RouteDestinationTeam = 0
    if HTW_LivingTeamCount >= 2 then
        set HTW_RouteOffset = 1 + ModuloInteger(HTW_Round - 1, HTW_LivingTeamCount - 1)
    endif
    set teamIndex = 1
    loop
        exitwhen teamIndex > HTW_TeamCount
        set HTW_TeamDestination[teamIndex] = 0
        if HTW_TeamLiving[teamIndex] and HTW_LivingTeamCount >= 2 then
            set livingIndex = 1
            loop
                exitwhen livingIndex > HTW_LivingTeamCount
                if HTW_LivingTeamIds[livingIndex] == teamIndex then
                    set HTW_TeamDestination[teamIndex] = HTW_LivingTeamIds[ModuloInteger(livingIndex - 1 + HTW_RouteOffset, HTW_LivingTeamCount) + 1]
                    set livingIndex = HTW_LivingTeamCount
                endif
                set livingIndex = livingIndex + 1
            endloop
        endif
        set teamIndex = teamIndex + 1
    endloop
    set HTW_RouteDestinationTeam = HTW_TeamDestination[1]
    set HTW_RoutingLocked = true
endfunction
