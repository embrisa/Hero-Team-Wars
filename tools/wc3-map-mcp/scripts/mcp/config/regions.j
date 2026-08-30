function HTW_Regions_Initialize takes nothing returns nothing
    // The composer emits this profile-specific registry from typed region
    // data. MVP and six-team profiles therefore share one runtime path.
    call HTW_Regions_InitializeProfile()
endfunction
