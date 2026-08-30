# 1. Vision and Design Pillars

## Vision

Hero Team Wars is a cooperative hero-survival game wrapped in a competitive drafting game. The immediate problem is personal—keep your hero alive—but the strategic problem is shared—decide how much of the team's resources should be spent making someone else's next wave worse.

The intended feeling is:

> “We barely survived that wave. Do we strengthen our defense, or risk everything on sending a counterattack before the other team scales?”

## Player fantasy

Each player should feel like a powerful Warcraft III hero. Each team should feel like a small war council: two people coordinating builds, send timing, attack composition, and emergency spending. The team is not merely waiting for a random wave; it is shaping the danger sent to its assigned opposing team.

## Design pillars

### 1. Heroes are the stars

The arena exists to create interesting hero decisions. Creeps should pressure movement, positioning, spell timing, and teamwork rather than simply becoming a wall of hit points.

### 2. Sending is a meaningful tradeoff

Buying an attacking creep is not free damage. It competes with War Camp upgrades, defensive items, and recovery after a bad wave. The best attack should sometimes be the purchase you cannot comfortably afford.

### 3. Two-player teams need coordination, not duplicate chores

Both heroes should have reasons to move, cast, rescue, and specialize. A player should not feel like a weaker second copy of the other hero. Coordination should come from fighting together and timing two independent attack queues, not from one player controlling the team's entire bank.

### 4. Information creates strategy

Players should know the round number, their own life count, the incoming wave's composition and total worth, the assigned sending team, and the major traits of enemy sends. The MVP should also show every team's two-hero composition and current hero levels, because those are important context for judging relative strength. The round-robin route should be visible before purchases lock so teams can plan around the next opponent. Any hidden information should be limited to non-essential timing details until playtests show that more concealment improves the game.

### 5. Escalation should be dramatic but recoverable

A team may lose ground without being immediately doomed. Catch-up tools, comeback gold, and limited life recovery can keep the fifth and sixth waves interesting. Avoid a snowball where the first successful send decides the whole match.

## Non-goals for the first version

- A full Warcraft III base-building or resource-gathering game.
- Dozens of heroes with bespoke imported models.
- A giant unit cap or hundreds of simultaneous creeps.
- Precise competitive balance across every possible team composition.
- A complex PvP combat system where heroes directly invade each other's arenas.

## Format and slot model

The intended format is six teams of two human players:

| Team | Human slots | Suggested role |
|---|---:|---|
| Team 1 | 1–2 | Heroes and team systems |
| Team 2 | 3–4 | Heroes and team systems |
| Team 3 | 5–6 | Heroes and team systems |
| Team 4 | 7–8 | Heroes and team systems |
| Team 5 | 9–10 | Heroes and team systems |
| Team 6 | 11–12 | Heroes and team systems |

Neutral Hostile, Neutral Passive, or an unused map player can own spawned units where convenient. The map does not need a joined AI player just to make the wave system function.

## Match length target

The first playable should aim for **25–40 minutes**:

- Preparation: 25–40 seconds.
- Combat: usually 60–120 seconds.
- Resolution and transition: 5–10 seconds.
- Expected first eliminations: around waves 6–10.

These are starting targets, not promises. The life count, wave duration, and economy should be adjusted through playtests.
