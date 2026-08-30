function HTW_Regions_Initialize takes nothing returns nothing
    // These rects mirror the inspected MVP region registry.  The generated
    // region handles are created by the composer; these rects are retained for
    // deterministic unit placement and send destinations.
    set HTW_ArenaRectA = Rect(1152., -3072., 2496., -448.)
    set HTW_ArenaRectB = Rect(-2464., -64., -320., 2240.)
endfunction
