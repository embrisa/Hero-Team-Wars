# 3. Gameplay Systems

## 3.1 Heroes

Each team controls two heroes. Heroes use standard Warcraft III movement and combat as much as possible so the game feels native to the platform.

Hero selection is unique within each team: the two teammates must choose different heroes. This restriction does not apply across teams, so different teams may use the same hero.

### Recommended hero roles for the first roster

Start with four or five clearly different roles rather than a large roster:

| Role | Job in the arena |
|---|---|
| Frontliner | Holds the line, interrupts dangerous units, protects the other hero |
| Striker | Deletes priority targets and scales through single-target damage |
| Controller | Slows, roots, silences, or groups enemies |
| Support | Heals, shields, dispels, or boosts the partner |
| Specialist | Uses summons, traps, area denial, or a unique mechanic |

The first prototype only needs two heroes. A third and fourth hero are useful for testing whether the team format creates composition choices.

### Hero progression

Recommended baseline:

- Hero levels are personal, but team-earned experience is split fairly between both heroes.
- Heroes gain a talent point at selected level milestones, such as levels 2, 4, 6, 8, and 10.
- A hero has three talent paths: offense, defense, and utility/identity.
- Each talent should modify an existing ability or combat pattern before introducing a completely new spell.

Example talent choices for a controller:

- **Deep Chill**: slow effects last longer.
- **Fracture**: the first target hit by a root takes bonus damage.
- **Rescue Current**: casting a control spell briefly pulls the allied hero away from danger.

Keep the first talent trees small. Four meaningful choices are more valuable than twenty filler nodes.

## 3.2 Personal economy

Each player has an independent gold pool. Gold, creep purchases, War Camp upgrades, hero items, and consumables belong to that player. The team still shares lives and fights the same incoming wave, but neither teammate can spend the other's resources.

This is the safer default for random matchmaking. It preserves cooperation through positioning and wave survival while preventing a stranger from emptying the team's bank.

### Personal gold sources

- Guaranteed personal income at the beginning of each preparation phase.
- A personal survival bonus for being alive when the wave ends.
- Small personal bounties or assist rewards from elite and special creeps.
- A small comeback bonus for players on teams with fewer lives than the leader.

Guaranteed income should be the main source of gold. Do not let last-hitting become the only way for one player to keep up.

### MVP income rules

- Sending attack creeps does not grant immediate or periodic gold income. Sending is a personal gold sink that creates pressure on the team's assigned round-robin target.
- The guaranteed base wave reward is paid at wave resolution even if the player's hero died.
- A dead hero loses only that player's survival bonus for the wave; there is no additional gold tax per death and no deduction from the player's existing bank.
- Elite and special-creep bounties remain small supplemental rewards rather than a replacement for the guaranteed wave income.

### Personal gold sinks

- Your War Camp upgrades.
- Your attack creeps.
- Your hero items and consumables.
- No gate, leak, or gate-repair system in the MVP; no non-hero event can reduce team lives.

Each player should rarely be able to buy everything. The important decision is whether their next gold goes into surviving now or creating pressure later.

## 3.3 Personal War Camps and creep shops

Each arena has two War Camps, one owned by each player. They are visually part of the same team base, but each has its own upgrade tier, shop inventory, gold costs, and attack queue. This keeps the system readable and avoids shared-bank conflicts.

### Suggested tiers

| Tier | Unlocks |
|---|---|
| 1 — Muster | Basic frontline and ranged creeps |
| 2 — Tactics | Specialists and simple support units |
| 3 — Warhost | Elites, stronger auras, and counterplay units |
| 4 — Doom | Bosses or high-risk event units; reserve for later testing |

Each War Camp should also show:

- Cost in personal gold.
- Threat value.
- Target role.
- One-line ability summary.
- Counterplay hint, such as “vulnerable to dispel” or “slow resistant.”

### Initial creep roster

Use a small roster with strong identities:

| Creep | Role | Interesting trait |
|---|---|---|
| Grunt | Frontline | Reliable melee body |
| Archer | Ranged | Prioritizes wounded heroes |
| Shieldbearer | Tank | Brief frontal damage reduction |
| Hexer | Caster | Periodic silence or curse; fragile |
| Banner Guard | Support | Grants nearby creeps attack speed or armor |
| Burrower | Ambusher | Appears closer to the backline after a delay |
| Splitter | Disruptor | Creates two weak units on death |
| Siege Beast | Breakthrough threat | Pressures the backline and ignores some hero aggro |

Each creep should have one memorable rule. Avoid making every unit a bundle of passives.

## 3.4 Wave director

Every defense wave is composed of three sources:

1. **Base wave**: controlled by the round number and difficulty curve.
2. **Enemy sends**: compositions purchased by rival players.
3. **Wave modifier**: none; creeps should provide enough interest through their identities and compositions.

For the MVP, use only the base wave and enemy sends. Do not add global wave modifiers unless a later design decision explicitly reverses this choice.

### Elimination cleanup

If a team is eliminated, immediately remove all creeps currently attacking that team's arena. These creeps never carry over, retarget, or get redistributed to another team. Attack queues already deployed to other living teams continue normally. The round-robin route is recalculated from the remaining living teams during the next preparation phase.

### Spawn pacing

Use squads instead of a single mass:

- Squad 1: frontline units.
- Squad 2: ranged or support units.
- Squad 3: specialist or elite units.

Start a squad every 6–10 seconds and stagger units inside it by about 0.75–1.25 seconds. The exact timing should be exposed as tuning values. The goal is to create windows where heroes can reposition and respond, not to test Warcraft III's maximum unit count.

### Wave readability

Before combat begins, show a compact preview:

- Expected base-wave roles.
- Player-sent creep icons.
- Incoming creep composition and roles.
- **Total wave worth**, calculated as the summed threat value of the base wave and all enemy sends.
- First special unit's estimated arrival.

The same preparation view should include a team overview showing all teams' two selected heroes and their current levels. This information is public in the MVP; do not hide team composition or level progression behind scouting, fog-of-war, or delayed reveals. Exact spawn timing and ability-proc timing may remain hidden if exposing them would make the wave too predictable.

During combat, use overhead icons or brief announcements for rare abilities. Players should know why a wave suddenly became dangerous.

## 3.5 Arena layout

Each team receives a compact, mirrored arena:

- Spawn entrance.
- Main fighting space.
- Backline or retreat area.
- War Camp and hero shop.
- Clear rear boundary for wave cleanup; it is not a life-loss objective.
- A clear camera boundary.

Keep all six arenas visually similar in the first prototype. Symmetry makes balance problems easier to diagnose. Add visual identity later through terrain, team color, and decorative props.

## 3.6 Interface needs

The map needs a small, reliable custom interface or clear floating text for:

- Current phase and timer.
- Team lives and hero-death summary.
- Personal gold, War Camp tier, and upgrade button.
- Personal attack queue and remaining threat budget.
- Incoming wave preview.
- Hero death state and respawn timing.
- Round-robin route showing the team's assigned target and incoming sender.
- Public team overview showing every team's hero composition and current hero levels.

If a custom UI is not ready, use buildings, multiboards, unit tooltips, and a few clean messages to prove the loop first.
