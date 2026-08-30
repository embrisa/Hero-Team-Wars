# 5. Open Decisions and Playtest Questions

These are intentionally unresolved. The purpose of the first prototype is to answer them with play, not debate them forever. Decisions that are already locked for the MVP are documented separately below.

| Question | Current recommendation | What to test |
|---|---|---|
| How are attack units distributed? | Resolved MVP: rotating round-robin routing; each team sends to exactly one opposing team per round, and the destination advances each round | Does rotating one-to-one pressure create better counterplay without making waves unmanageable? |
| Should War Camps be shared? | No; one personal Camp per player | Does personal ownership prevent random-teammate griefing? |
| Should non-hero events cost lives? | No; hero death is the only way to lose lives | Does hero-death pressure alone create enough urgency? |
| How many lives should teams start with? | 15 shared team lives | Do first eliminations happen around waves 6–10? |
| Should purchases be allowed during combat? | No in the MVP | Does locking purchases make preparation more meaningful? |
| How much information is hidden? | Keep strategic information visible in the MVP: show the incoming wave's total worth (the summed threat value), its composition/roles, and every team's two-hero composition with current hero levels. Hide only details that are not needed for planning, such as exact spawn timing or ability-proc timing, until playtests show they should be exposed. | Can players understand the matchup and make preparation decisions without the preview removing all tension? |
| Are hero duplicates allowed? | No on the same team; different teams may choose the same hero | Do restrictions create better composition choices? |
| How is experience split? | Fairly between both heroes | Does personal progression still feel rewarding? |
| What happens when a player disconnects? | The remaining teammate continues solo; the disconnected hero disappears | Does solo continuation remain fair and understandable? |
| Are wave modifiers needed? | No; creeps should be interesting enough on their own | Do creep identities and compositions provide enough variety without extra global rules? |

## Resolved MVP rules

### Eliminated teams

When a team is eliminated, all creeps currently attacking that team's arena disappear immediately. Attack creeps do not carry over, retarget, or get redistributed to another team. Attack queues already deployed to other living teams continue normally. The round-robin route is recalculated from the remaining living teams during the next preparation phase. Eliminated players may spectate the remaining match or leave the game; they have no further gameplay role.

### Rotating round-robin sends

Each living team sends to exactly one other living team during a round and receives one incoming send queue. In round 1, a team sends to the next team in the ordered living-team list. Each subsequent round advances the destination by one additional position, wrapping around and skipping the sender. The route repeats after `N - 1` rounds for `N` living teams.

The route is visible and locked at the start of preparation. If the assigned sender has no active human players, the wave director supplies a neutral filler queue. When only one team remains, that team wins immediately and no further send route is created.

### Gold is not shared

Each player has an independent gold pool. Gold earned by one player cannot be spent by their teammate. Creep purchases, War Camp upgrades, hero items, and consumables are paid from the purchasing player's own gold.

## Biggest design risk

The game could become a passive economy contest where teams buy the mathematically strongest unit and wait. The antidote is not simply more units. It is counterplay:

- Clear creep identities.
- Hero abilities that answer specific threats.
- A visible but limited send budget.
- Real choices between defense, upgrades, and attacks.
- Waves that create positioning and timing problems.

## Suggested first playtest questions

Ask players only these after the first two-arena test:

1. Did you understand what you were supposed to buy and why?
2. Did the attack queue feel like a real choice or just spare-money spending?
3. When the wave became dangerous, could you identify the reason?
4. Did both heroes have something important to do?
5. Did you want to play another wave?

## Next concrete build task

Create a two-arena prototype with two personal War Camps per team and three enemy creep types. Do not add talents, bosses, direct hero PvP, or imported assets until this prototype produces a satisfying prepare–send–defend–resolve loop.
