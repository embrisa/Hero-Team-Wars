# 2. Core Loop and Match Rules

## Match structure

### Lobby and start

- The map supports six teams of two as the primary mode.
- Each player chooses one hero from the available hero list.
- Duplicate heroes are not allowed on the same team. Different teams may choose the same hero.
- Each player begins with personal gold and a basic War Camp.
- The team begins with a shared life pool.

### Phase A — Prepare

Recommended starting duration: **35 seconds**.

Both team members may:

- Buy creeps from your War Camp using personal gold.
- Upgrade your War Camp using personal gold.
- Buy or upgrade hero items.
- Spend talent points.
- Review the attack composition that will be sent to this round's assigned opposing team.
- Review the expected incoming wave, including its composition, total worth (summed threat value), and major roles.
- Review every team's two-hero composition and each hero's current level.

The purchase interface should always distinguish between:

- **Incoming wave**: what this team must fight.
- **Your attack queue**: what this player is sending elsewhere.
- **Team overview**: every team's hero composition and current hero levels.

Purchased attack creeps are removed from the player's attack queue when the timer ends. Each purchased unit creates one corresponding unit in the assigned target team's defense wave; it never appears in the same player's or team's defense wave. The team target is determined by the round-robin route for that round.

### Phase B — Defend

Recommended starting duration: **90 seconds**, ending early when the wave is defeated.

- A wave director creates the base wave plus enemy-sent additions.
- Units spawn in squads with a delay between squads and a delay between units in each squad.
- Heroes fight inside their own arena; there is no direct hero invasion in the MVP.
- Heroes may use shops during combat only if testing shows that this creates interesting decisions. The default recommendation is to lock purchases during combat and keep the phase focused on fighting.

### Phase C — Resolve

At the end of combat:

- Count hero deaths.
- Award personal gold and experience.
- Respawn surviving-team heroes.
- Show a short team summary: damage dealt, hero deaths, lives lost, creeps killed, and incoming threats.
- Open the next preparation phase.

If a wave reaches the time limit, end combat and clean up remaining creeps so one hidden caster or stuck unit cannot freeze the match. The team loses lives only for hero deaths recorded during the wave.

## Lives and elimination

Each team starts with **15 shared team lives**.

At wave resolution:

| Hero deaths in the wave | Life loss |
|---:|---:|
| 0 | 0 |
| 1 | 1 |
| 2 | 3 total |

This uses the user's intended rule: the second death in the same wave is substantially worse than the first.

Dead heroes do not respawn until the wave ends. If the team reaches zero lives, it is eliminated. Immediately remove every creep currently attacking that team's arena. Those creeps do not carry over, retarget, or get redistributed to another team. Routes are recalculated from the living teams during the next preparation phase; no route changes during combat. The eliminated team's players may spectate the remaining match or leave the game; they have no further gameplay role.

### Life-loss invariant

There is no gate, heart, leak penalty, or other non-hero life-loss mechanic. Creeps reaching the back of the arena may be tracked for wave feedback or cleaned up at the combat time limit, but they never remove team lives. The only event that reduces the shared life pool is a hero dying.

## Victory

The game ends immediately when only one team remains. That team wins.

If multiple teams are eliminated during the same resolution and more than one team remains, rank the eliminated teams by aggregate hero level after applying the same hero-death life-loss rules. This is mainly a tie-breaker for unusual cases.

If a match reaches a late-game stalemate, activate sudden death:

- Faster wave scaling.
- A small reduction in preparation time.

## Targeting model

### MVP decision: rotating round-robin routing

Each team sends its attack queue to exactly one opposing team per round. The destination rotates forward through the ordered list of living teams:

- Round 1: each team sends to the next team.
- Round 2: each team skips one team and sends to the following team.
- Round 3: each team skips two teams, and so on.
- After `N - 1` rounds in a match with `N` living teams, the route pattern repeats. A team never sends to itself.

For `N` living teams, calculate the route with:

```text
offset = 1 + ((round - 1) mod (N - 1))
target = sender + offset, wrapping around the ordered living-team list
```

For six teams, Team 1 sends to Team 2 in round 1, Team 3 in round 2, Team 4 in round 3, Team 5 in round 4, Team 6 in round 5, and Team 2 again in round 6. Because the route is a one-to-one rotation, every living team sends to one team and receives from one team each round.

The route is calculated at the start of preparation, shown to all teams, and locked for the wave. If a team is eliminated, recalculate the route from the remaining living teams during the next preparation phase; never retarget creeps during combat. If an assigned sender has no active human players, the wave director supplies a neutral filler queue so a sparse lobby does not create an unintentionally empty wave. This does not require a joined AI player slot.

## Threat budget

Do not balance the game around raw unit count. Give every purchased creep a threat value.

- Basic creep: 1 threat.
- Specialist: 2 threat.
- Elite: 4 threat.
- Boss or event unit: 8–10 threat.

Each player has a personal send budget per wave. A personal War Camp upgrade can increase the budget or unlock better ways to spend it. A threat cap prevents one preparation phase from creating an unmanageable flood.

## Example round

1. Player A begins the preparation phase with 42 personal gold.
2. Player A upgrades their War Camp, leaving 22 gold.
3. Player A buys two Shieldbearers, four Grunts, and one Hexer for 10 total threat.
4. Player A's attack queue is routed to the team's assigned target for the current round.
5. The assigned target receives its base wave plus the corresponding enemy send queue from that one predecessor team.
6. One hero dies, the other survives, and two creeps reach the back of the arena.
7. The team loses exactly 1 life for the hero death; the creeps do not cause additional life loss. Each player then receives personal income and experience for the next round.
