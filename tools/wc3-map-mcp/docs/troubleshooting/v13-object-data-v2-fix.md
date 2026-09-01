# V13 Object-Data Format & Altar Vision Resolution

## Context & Finding
Following user runtime testing of the bisection matrix (`v9`, `v10`, `v11`, `v12`):
- `v9` (w3u custom objects included, JASS hero selection disabled): **CRASHED**
- `v10` (w3u custom heroes included, direct spawn): **CRASHED**
- `v12` (w3u custom heroes with only `unam`, direct spawn): **CRASHED**
- `v11` (0 custom objects, NO `war3map.w3u` in archive): **LOADED CLEANLY**

This conclusively proved the crash root cause was the `war3map.w3u` binary object data format emitted by the map engine.

## Root Cause Analysis
- `MapComponentCodec.SerializeObjectMember` serialized custom objects with `ObjectDataFormatVersion.v3` when no source object file existed in the base map.
- In format version 3 (`v3`), `War3Net.Build.Core` writes an extra 4-byte `Unk` integer per custom object definition header (`OldId` [4B], `NewId` [4B], `Unk` [4B], `ModificationCount` [4B]).
- In standard Warcraft III map archives (including version 2.0.4 Reforged), unit object data uses `ObjectDataFormatVersion.v2` where each custom object header contains only `OldId` [4B], `NewId` [4B], and `ModificationCount` [4B].
- The extra 4 bytes in `v3` misaligned the modification stream parser inside Warcraft III, resulting in an immediate fatal memory access crash during map loading.

## Fixes Applied
1. **Object Serialization Format Version**: Changed `MapComponentCodec.SerializeObjectMember` default from `ObjectDataFormatVersion.v3` to `ObjectDataFormatVersion.v2`.
2. **Hero Altar Object Modifications**: Removed obsolete string-based `useu` modification on `n0AL` so unit stock is handled exclusively via runtime JASS natives.
3. **Fog of War Visibility**: Added `CreateFogModifierRadius` over the shared Hero Altar `(216., -336.)` during `HTW_HeroSelection_Begin` so players have clear line of sight to view and interact with the altar.

## Verification
- Built `v13` (`DA9E1C0153C2895FE73416A9E199E3DDEC78D246B31AD854F836D74966A5CEDA`).
- Verified raw binary header: format version 2, 0 orig units, 5 custom units (`H001`..`H004`, `n0AL`), modifications properly byte-aligned with 0 trailing alignment bytes.
- Executed full test suite: 94 tests passing.
- Published updated `Wc3MapEngine.Cli` binary.
- Copied `HeroTeamWars_v13.w3m` to `C:\Users\hp\Documents\Warcraft III\Maps\Test\v13\HeroTeamWars_v13.w3m`.
