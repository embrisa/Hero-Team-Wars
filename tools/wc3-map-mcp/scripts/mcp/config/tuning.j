function HTW_Tuning_Load takes nothing returns nothing
    // Centralized MVP tuning.  These values are intentionally source-owned
    // so a transaction changes one place and the generated map script stays
    // reproducible.
    set HTW_StartingLives = 15
    set HTW_SingleDeathCost = 1
    set HTW_AdditionalWipeCost = 2
    set HTW_PreparationSeconds = 35
    set HTW_CombatSeconds = 90
    set HTW_WaveReward = 50
    set HTW_InterestGold = 10
endfunction
