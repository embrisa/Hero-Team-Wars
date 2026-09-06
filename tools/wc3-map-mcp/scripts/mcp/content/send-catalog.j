// Generated from content/send-catalog.json by generate-v26-send-content.mjs. Do not edit.

// Standard unit types retain stock combat mechanics; purchase enforcement belongs to the send system.

function HTW_SendCatalog_Count takes nothing returns integer
    return 3
endfunction

function HTW_SendCatalog_UnitType takes integer kind returns integer
    if kind == 1 then
        return 'hfoo'
    elseif kind == 2 then
        return 'hrif'
    elseif kind == 3 then
        return 'hkni'
    endif
    return 0
endfunction

function HTW_SendCatalog_AbilityId takes integer kind returns integer
    if kind == 1 then
        return 'A6F1'
    elseif kind == 2 then
        return 'A6R1'
    elseif kind == 3 then
        return 'A6K1'
    endif
    return 0
endfunction

function HTW_SendCatalog_Name takes integer kind returns string
    if kind == 1 then
        return "Footman"
    elseif kind == 2 then
        return "Rifleman"
    elseif kind == 3 then
        return "Knight"
    endif
    return ""
endfunction

function HTW_SendCatalog_Role takes integer kind returns string
    if kind == 1 then
        return "Frontline"
    elseif kind == 2 then
        return "Ranged"
    elseif kind == 3 then
        return "Heavy"
    endif
    return ""
endfunction

function HTW_SendCatalog_Description takes integer kind returns string
    if kind == 1 then
        return "A melee body for the frontline."
    elseif kind == 2 then
        return "Ranged rifle fire."
    elseif kind == 3 then
        return "Durable mounted melee."
    endif
    return ""
endfunction

function HTW_SendCatalog_Cost takes integer kind returns integer
    if kind == 1 then
        return 10
    elseif kind == 2 then
        return 20
    elseif kind == 3 then
        return 40
    endif
    return 0
endfunction

function HTW_SendCatalog_Threat takes integer kind returns integer
    if kind == 1 then
        return 1
    elseif kind == 2 then
        return 2
    elseif kind == 3 then
        return 4
    endif
    return 0
endfunction
