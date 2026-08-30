function HTW_Content_Heroes takes nothing returns nothing
    // The MVP intentionally uses one standard placeholder hero per active
    // player.  The hero-selection and custom-object layers remain separate.
endfunction

function HTW_Content_CreateHero takes integer playerId, real x, real y returns unit
    return CreateUnit(Player(playerId - 1), 'Hpal', x, y, 270.)
endfunction
