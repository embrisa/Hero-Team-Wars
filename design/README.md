# Hero Team Wars — Design Docs

Status: Design + editor-led implementation contract v0.2  
Working title: **Hero Team Wars**  
Target platform: Warcraft III: Reforged custom map

## One-sentence pitch

Two heroes cooperate to survive escalating enemy waves while spending their personal gold to send dangerous creep compositions into rival teams' arenas.

## Recommended first target

- **12 human players**: six teams of two.
- **No required AI lobby slot**: the wave director can be implemented with triggers and neutral-hostile ownership.
- Each team has one arena, one shared life pool, two heroes, and two player-owned War Camps.
- The last team with lives remaining wins; when only one team remains, the game ends immediately.

Reforged maps support up to 24 internal player slots, while older Warcraft III references describe the original 12-player limit. Six teams of two fit exactly into the original 12 human-player format, so this design does not need to spend a player slot on an AI. A computer-controlled director can be added later if useful.

## The core match loop

1. **Prepare**: earn personal gold, upgrade your War Camp, buy creeps, buy hero items, and select talents. Purchased attack units are routed to one opposing team according to the round-robin schedule.
2. **Defend**: incoming creeps spawn in staggered squads inside each team's arena.
3. **Resolve**: survivors, hero deaths, gold, and experience are calculated.
4. Repeat until only one team remains.

## Current design decisions

| Area | First-pass decision |
|---|---|
| Team size | 2 heroes per team |
| Hero selection | No duplicate hero on the same team; different teams may choose the same hero |
| Starting lives | 15 shared team lives |
| Match format | 6-team free-for-all with indirect PvP |
| Hero death | One death costs 1 life; both heroes dying in one wave costs 3 lives total |
| Life-loss rule | Hero death is the only way to lose team lives; there is no gate or leak mechanic |
| Respawn | Dead heroes return after the wave, if the team is still alive |
| Elimination cleanup | Creeps attacking an eliminated team disappear immediately; they do not carry over or retarget |
| Economy | Personal gold; each player controls their own spending |
| Creep purchases | Each player's purchased creeps are routed to exactly one opposing team's wave; the destination rotates each round and never targets the buyer's own team |
| Creep spawning | Squads and staggered spawn timing rather than one giant instant flood |
| Wave modifiers | None; creeps should be interesting enough on their own |
| Progression | Hero levels plus a small talent tree; personal War Camp upgrades unlock stronger creep tiers |
| Victory | Last surviving team, with a short sudden-death rule if needed |

## Document index

- [Vision and pillars](01-vision-and-pillars.md)
- [Core loop and match rules](02-core-loop-and-rules.md)
- [Gameplay systems](03-gameplay-systems.md)
- [MVP build plan](04-mvp-build-plan.md)
- [Open decisions and playtest questions](05-open-decisions.md)
- [Editor-led implementation workflow](06-implementation-workflow.md)
- [Editor state ledger](07-editor-state.yaml)
- [Implementation chunks](08-implementation-chunks.md)
- [Change log](09-change-log.md)

## Important scope rule

The first playable version should prove only three things:

1. Two heroes can survive together in an arena.
2. A team can buy an enemy composition that its assigned opposing team must fight.
3. The wave/life loop produces understandable strategic decisions.

Hero variety, elaborate talent trees, custom models, bosses, map decoration, and a large item catalog should come after that proof.
