function HTW_Content_Heroes takes nothing returns nothing
    // V8 keeps the hero roster deliberately small: four custom unit rawcodes
    // inherit their visuals and baseline abilities from standard heroes.  The
    // object definitions live in object-data/v8-hero-objects.json.
endfunction

function HTW_Content_HeroTypeForSlot takes integer slot returns integer
    if slot == 1 then
        return 'H001'
    elseif slot == 2 then
        return 'H002'
    elseif slot == 3 then
        return 'H003'
    endif
    return 'H004'
endfunction

function HTW_Content_IsHeroType takes integer heroType returns boolean
    return heroType == 'H001' or heroType == 'H002' or heroType == 'H003' or heroType == 'H004'
endfunction

function HTW_Content_HeroName takes integer heroType returns string
    if heroType == 'H001' then
        return "Guardian"
    elseif heroType == 'H002' then
        return "Striker"
    elseif heroType == 'H003' then
        return "Controller"
    endif
    return "Support"
endfunction

function HTW_Content_CreateHero takes integer playerId, integer heroType, real x, real y returns unit
    return CreateUnit(Player(playerId - 1), heroType, x, y, 270.)
endfunction
