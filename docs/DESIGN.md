# Embervale — Design Bible

> **What this is.** [`LORE.md`](LORE.md) pins the *fantasy* (the world, the story, the
> feeling we promise the player). [`ARCHITECTURE.md`](ARCHITECTURE.md) pins *how the
> systems work*. **This document pins the design decisions both of those leave open** —
> the calls a content author, a balancer, or the Phase 29 "Combat Feel" work has to make
> and would otherwise make inconsistently or by accident. When LORE says "heavy weapon
> impact, no button mashing," this is where that becomes a rule you can build against.
>
> **Authority.** This is the document content and balance answer to. Where it states a
> *decision*, that decision holds until this file changes — not until someone tunes a
> `.tres` differently. Where it cites a number, that number is a **starting point**:
> concrete feel values are Phase 29's to set, balance values are Phase 56's. Design sets
> *intent and direction*; those phases set the digits.
>
> **Status.** All five pillars are pinned: Combat (§1) and the Core Loop (§2) from
> Phase 22A; Progression (§3), Difficulty (§4), Corruption fantasy (§5), and Economy (§6)
> from Phase 22B. Built as part of the Production Bible (`PRODUCTION_ROADMAP.md` Phase 22).

---

## 1. Combat Pillars

### 1.1 The fantasy, restated as design rules

LORE's combat brief is **"Skyrim breadth × Elden Ring weight; easy to learn, hard to
master; heavy weapon impact; precise timing; meaningful encounters; no button mashing."**
That is a feeling. The four pillars below are the *rules* that produce it. Every combat
decision — a new weapon's timing, an enemy's attack cadence, a tuning pass — must serve
at least one pillar and break none.

1. **Weight & impact** (§1.2) — every hit, given and taken, is a *committed, readable
   event*, never a tick of DPS.
2. **Precise timing** (§1.3) — the skill ceiling is *when*, not *how fast*. Wind-ups,
   commitment, dodge/parry windows.
3. **No button mashing** (§1.4) — the resource economy (§1.6) makes spam strictly worse
   than reading the fight.
4. **Breadth without a class lock** (§1.5) — melee, magic, and stealth are all complete,
   viable answers; the player authors the build.

> **The one-sentence test for any combat change:** *does it reward reading the fight over
> out-pressing it?* If yes, it fits. If it makes faster inputs the dominant strategy, it
> violates Pillar 3 and is wrong regardless of how good it feels in isolation.

### 1.2 Pillar — Weight & impact

**Decision: a hit is a transaction, not a tick.** Attacks have real wind-up, a short
active window, and a recovery you are committed to; landing one *moves the world* (poise
loss, stagger, knock of feedback), and taking one *costs you posture*, not just a health
sliver.

The framework already encodes the *state* for this — Phase 29 adds the *feel* on top:

- **Poise & stagger** are real and authored per-actor. `CombatComponent`
  (`src/Combat/CombatComponent.cs`) tracks `MaxPoise`/`PoiseRegen`; a hit subtracts
  `WeaponResource.PoiseDamage`; crossing zero fires `EntityStaggeredEvent` and locks the
  victim for `StaggerDuration`. **Design intent:** stagger is the *reward* for aggression
  read correctly — heavy weapons trade speed for poise damage, so a well-timed big hit
  opens a window a flurry of light hits cannot.
- **Weapon timing is the weight dial.** `WeaponResource` (`src/Combat/WeaponResource.cs`)
  exposes `WindupTime` / `ActiveTime` / `RecoveryTime`, `BaseDamage`, `PoiseDamage`,
  `StaminaCost`, and a `FinisherMultiplier` on the last combo hit. **Design intent:**
  heavier = longer wind-up, more poise damage, higher stamina cost, slower recovery. A
  dagger and a war-axe must *feel* like different verbs, authored entirely in these
  fields (CLAUDE.md §8 "a new weapon").
- **What Phase 29 owes this pillar:** hit-stop/freeze-frames on impact, directional hit
  reactions, camera shake on crit/stagger, weapon trails, impact VFX/SFX. The math says a
  hit landed; juice makes the *player* feel it. See §1.7.

### 1.3 Pillar — Precise timing

**Decision: the skill expression is timing, not input rate.** The depth of the fight is
in *when* you commit, dodge, and block — windows the player learns and masters.

- **Commitment is real.** The attacker FSM in `MeleeWeaponComponent`
  (`src/Combat/MeleeWeaponComponent.cs`) is `Idle → Windup → Active → Recovery`; an attack
  cannot be freely cancelled, and a new swing is blocked while staggered. **Design intent:**
  you *choose* to swing and you *live with* the recovery — this is the Elden-Ring half of
  the brief. Phase 29 adds **animation-cancel windows + input buffering** so commitment
  reads as deliberate, not as input lag.
- **Wind-up is a tell, not a tax.** Every heavy attack — player or enemy — telegraphs.
  **Design intent:** enemies must be *readable*; an unreadable hit is a bug, not
  difficulty. Boss work (Phases 28, 36) inherits this as a hard rule.
- **Defensive options are timed, not held.** Blocking already costs stamina per hit
  (`BlockMitigation`, `BlockStaminaCost`); holding block is a *stopgap*, not a strategy.
  **Design intent for Phase 29:** add **dodge i-frames** (a timed roll, stamina-costed)
  and a **parry/riposte** window (a tight, well-timed block that opens a punish) so the
  best defense is a read, not a wall. Lock-on / soft-target (built from the Phase 18
  `FocusedEntity`) keeps timing legible at range and in melee.

### 1.4 Pillar — No button mashing

**Decision: spam is mechanically dominated.** It is not enough that mashing is
*discouraged*; the systems must make reading-the-fight the *higher-EV* choice for a
competent player. This is the pillar most easily lost in tuning, so it is the most
explicit.

Mashing is forbidden to ever feel like the right answer. The enforcers:

- **Stamina gates offense.** Every swing costs `WeaponResource.StaminaCost`; stamina
  regenerates (Phase-13 stat regen) but not fast enough to sustain a mash. Empty stamina =
  no attack, no dodge, no block. **The anti-mash economy is stamina** (§1.6).
- **Poise gates *enemies'* offense too** — a staggered foe can't trade, so *your* correct
  reads create openings spam would miss.
- **Recovery punishes over-commitment.** Whiffing into recovery against a readable counter
  is how a masher dies; that death is *intended feedback*, not unfairness.
- **What Phase 29 owes this pillar:** the **stamina/poise pacing tune** is explicitly an
  anti-mash pass — costs and regen set so that "attack, attack, attack" empties the bar
  before it kills, while "read, punish, recover" sustains. Phase 56 balances the final
  numbers; Phase 29 proves the *shape*.

### 1.5 Pillar — Breadth without a class lock

**Decision: there are no classes; there are tools, all complete.** LORE: *"No traditional
class lock. Players create their own build."* That is a content *and* combat promise —
melee, magic, and stealth are each a full answer to an encounter, not a flavor on top of a
mandatory sword.

- **Three pillars of offense, one stat spine.** Melee (`MeleeWeaponComponent` + weapons),
  magic (`src/Magic` — projectile/area/self spells across the `DamageType` schools:
  Physical, Fire, Frost, Lightning, Arcane, Nature, Necrotic, True), and stealth
  (positioning, the Umbral fantasy, ambush damage) all route through the same stats,
  damage pipeline, and poise/stagger model. **Design intent:** an encounter author must
  assume the player might answer with *any* of the three and design openings for each.
- **The build is authored by the player, not chosen at creation.** Race (Phase 26) nudges
  a starting lean (Valari → magic, Grondar → strength, Umbral → stealth) but never *locks*
  one out. Perks and gear (Phases 6–8) do the shaping. **Full progression intent is
  Phase 22B** (§3) — this pillar only fixes the *combat* contract: every weapon family and
  every magic school must be a viable spine to build around, none a trap.

### 1.6 The stamina & poise economy (the model that enforces §1.2–1.5)

One resource model carries all four pillars, so it is stated once, here, as the contract:

| Resource | Owns | Gates | Regenerates | Pillar it serves |
| -------- | ---- | ----- | ----------- | ---------------- |
| **Stamina** | The player's *action economy* | Attacks, dodge (Phase 29), block, sprint | Passive, per-second (Phase 13); paused/slowed under load (Phase 29 tune) | **No mashing** (§1.4), Timing (§1.3) |
| **Poise** | An actor's *posture* | Staying upright vs. stagger-lock | Passive while not staggered (`PoiseRegen`) | **Weight** (§1.2) |
| **Mana** | The *magic* economy | Spellcasting | Passive, per-second | Breadth (§1.5) — magic has its *own* pool so casters aren't taxed on the melee economy |

**Decisions encoded here:**

- **Stamina is the anti-mash currency.** It governs *all* physical exertion — attack,
  dodge, block, sprint — so over-pressing in one verb starves the others. This is the
  single most important balance lever for Pillar 3; Phase 29 sets its shape, Phase 56 its
  values.
- **Mana is separate from stamina** so a magic build and a melee build don't compete for
  the same bar — breadth (§1.5) requires it.
- **Poise is an actor property, not a global**, authored per enemy/boss in
  `CombatComponent` — a chip-damage flurry can't stagger a heavy foe, but a committed
  heavy hit can. That asymmetry *is* the weight fantasy.

### 1.7 What exists vs. what Phase 29 owns

The combat **framework** (Phase 3) is built and live in the sandbox; it has the *math and
state*. The combat **feel** (Phase 29 — "Combat Feel & Game Juice") is the layer that
makes that math *land*. This doc is the contract between them.

| Concern | Built today (framework, Phase 3) | Phase 29 owns (feel) |
| ------- | -------------------------------- | -------------------- |
| Damage / crit | `CombatMath.RollAttack`, `DamagePacket`/`DamageResult` | — |
| Poise / stagger | `CombatComponent` state + `EntityStaggeredEvent` | Hit-stop, hit-react animation, stagger camera shake |
| Blocking | Stamina-gated `BlockMitigation` | Block spark/feedback; **parry/riposte** window |
| Attack commitment | `MeleeWeaponComponent` Windup→Active→Recovery + combo/finisher | **Animation-cancel windows + input buffering** |
| Weapon identity | `WeaponResource` timing/damage/poise/combo fields | Weapon trails, per-weapon impact VFX/SFX |
| Defense (mobility) | — (block only) | **Dodge + i-frames** (roll) |
| Targeting | `FocusedEntity` (Phase 18 soft focus) | **Lock-on** with target switching |
| Anti-mash | Stamina cost per action + poise | **Stamina/poise pacing tune** (the anti-mash pass) |
| Screen feedback | `DamageDealtEvent` (data) | Crit/stagger/block/parry screen + HUD feedback (`UiTheme`) |

> **Reading this table:** anything in the right column is *intentionally not built yet* —
> it is Phase 29's job, and Phase 29's "Done when" bars (`SESSION_PLAYBOOK.md` 29A–29I)
> are the concrete answer to the intent set here. See §2.4 for that checklist.

---

## 2. The Core Loop (moment-to-moment)

### 2.1 The loop

**Decision: the minute-to-minute is `explore → fight → loot → grow`, and corruption bends
the return.**

```
        ┌─────────────────────────────────────────────┐
        │                                             │
        ▼                                             │
   ┌─────────┐    ┌────────┐    ┌────────┐    ┌────────┐
   │ EXPLORE │ →  │  FIGHT │ →  │  LOOT  │ →  │  GROW  │
   └─────────┘    └────────┘    └────────┘    └────────┘
   a dying world  weighty,      affixes,      XP, perks,
   worth seeing   readable      rarity,       gear, and —
                  combat        gold          over the arc —
        ▲                                     CORRUPTION
        └──────────────  return changed  ◄────────────────┘
```

Every loop is a complete, self-contained satisfaction *and* feeds the next. The "return
changed" arrow is the Embervale-specific twist: over a play arc the player grows in power
*and* in corruption (the defining mechanic — Phase 23; fantasy pinned in 22B), so the
world they re-enter reacts to who they are becoming. The minute-loop is genre-standard on
purpose; the *macro* loop is where Embervale is itself.

### 2.2 Beat-by-beat (which system serves each verb)

Each verb is already served by a shipped system — the loop is grounded, not aspirational:

- **Explore** — a living world: the day/night `WorldClock`, weather, roaming
  `EncounterDirector` patrols, and procedural `WorldEventDirector` raids/caches/hunts
  (`src/World`) give the space *events to walk into*. **Design intent:** exploration is
  never dead air — there is always a reason the next ridge matters (a POI, a patrol, a
  weather shift), and a dying world is *worth looking at* (the art-direction promise,
  Phase 30/53).
- **Fight** — the combat pillars (§1). **Design intent:** an encounter is a *readable
  problem* answerable by any build (§1.5), with weight (§1.2) and timing (§1.3) that
  reward the read.
- **Loot** — the `LootGenerator` rolls tables → rarity → affixes (`src/Loot`,
  `src/Items`); pickups drop on death via `LootComponent`. **Design intent:** the reward
  is *legible and tempting* — a drop should pose a build question ("is this prefix worth
  the slot?"), which is what keeps the loop spinning.
- **Grow** — XP/levels (`ProgressionComponent`), perks (`PerkDatabase`), equipment
  (`EquipmentComponent` → stats), and over the arc divine relics + corruption.
  **Design intent:** growth is *player-authored* (§1.5) — every loop hands the player a
  small build decision, not just a bigger number. (Detailed progression *intent* — class
  freedom, perk philosophy — is Phase 22B; §3.)

### 2.3 Session shape (the arc the loop must sustain)

**Decision: the loop must carry a satisfying 30–60 minute session — the Gate G1 vertical
slice bar.** A single sitting should arc: *arrive somewhere new → a quest hook → a chain
of fights that escalate → a meaningful reward → a beat of growth (a perk, a relic, a
corruption nudge) → a reason to return.* The minute-loop (§2.1) is the engine; the session
shape is the chassis it has to move. Any region or questline author (Phases 27, 44, 50)
designs *to this arc*, and the slice (Phase 33) is its first full proof.

### 2.4 Input & feel intent — the Phase 29 contract

This is the concrete checklist the combat-feel work answers to. It restates the §1 pillars
as *what the player's hands and eyes must experience*, so Phase 29's sub-phases
(`SESSION_PLAYBOOK.md` 29A–29I) have an unambiguous target:

- **A landed heavy hit feels like a collision** — brief hit-stop, a directional reaction,
  a camera kick on crit/stagger. (29A, 29B; serves §1.2)
- **A swing is a decision you live with** — visible wind-up, a committed recovery, but
  cancel/buffer windows so it reads as *deliberate*, never as lag. (29G; serves §1.3)
- **Defense is a read, not a wall** — a timed dodge with i-frames and a parry that earns a
  riposte; holding block bleeds stamina and only delays. (29E, 29F; serves §1.3/§1.4)
- **Mashing empties the bar before it wins** — stamina/poise paced so pressing is strictly
  worse than reading. (29I; serves §1.4)
- **The fight stays legible** — lock-on/soft-target with switching keeps the read possible
  in chaos. (29H; serves §1.3)
- **Every combat state speaks** — crit, stagger, block, and parry each get distinct
  screen/HUD feedback through `UiTheme`. (29D; serves all)

> If a Phase 29 change satisfies its own "Done when" but fails the one-sentence test in
> §1.1 (*rewards reading over out-pressing?*), the test wins — re-open it.

---

## 3. Progression

> **Intent:** the player *authors* their character through play, never picks a class.
> Growth is a stream of small build *decisions*, not a rising number — and power has a
> second axis (corruption, §5) that the safe one (levels/perks) does not.

LORE: *"No traditional class lock. Players create their own build."* This is already true
in code, and the design holds it there:

- **No classes — perks + gear are the build.** There is no class system; the character is
  the sum of `PerkResource` passives learned (`PerksComponent`) and equipment bonuses
  (`EquipmentComponent` → stats). **Decision:** it stays that way. Race (Phase 26) *nudges*
  a starting lean (Valari → magic, Grondar → strength, Umbral → stealth) but never locks a
  path — exactly the breadth pillar (§1.5) expressed in progression.
- **Perks shape, they don't gate.** `PerkResource` is a *rankable single-stat passive*
  bought with skill points (`ProgressionResource.SkillPointsPerLevel`, banked by
  `ProgressionComponent`). **Decision:** perks make a playstyle *better*, never make
  another playstyle *impossible*; no perk is a prerequisite-wall that forecloses a build.
  A player who respecs or branches mid-game is never bricked.
- **Every level is a decision, not just a stat bump.** The XP curve
  (`BaseXpToLevel × level^XpCurveExponent`) and per-level flat gains give baseline growth;
  the *interesting* growth is the skill point the player spends. **Decision:** level-up
  hands the player a choice (a perk, a rank) — the §2.2 "Grow" beat must always pose a
  build question, or progression has gone flat.
- **Two power axes.** Levels/perks/gear are the *clean* axis. Divine relics and
  corruption (§5, Phase 23) are a *parallel, riskier* axis — power that costs something.
  **Decision:** the clean axis must be a complete path to victory on its own, so embracing
  corruption is always a *temptation* (§5), never a *requirement*.

> Concrete curve/skill-point/cap values are a Phase 56 balance call; this section fixes the
> *shape*, not the digits. Cross-links: `ARCHITECTURE.md` §2.6c; `src/Progression/*`;
> CLAUDE.md §8 "a new perk".

---

## 4. Difficulty philosophy

> **Intent:** *easy to learn, hard to master* (LORE) — and the two halves are served by
> *different* design levers, neither of which is a class lock or a content gate.

- **Mastery is the combat read, not bigger numbers.** The "hard to master" ceiling already
  exists in the §1 pillars: timing windows (§1.3), the stamina/poise economy (§1.6), the
  no-mash rule (§1.4). **Decision:** depth comes from *the player getting better at the
  fight*, not from the game inflating health bars. A skilled player beats a hard encounter
  by reading it; that is the skill expression we protect.
- **"Easy to learn" is legibility, not weakness.** **Decision:** every threat is
  *readable* — telegraphed wind-ups, clear feedback (§2.4), honest tells. A new player
  loses because they misread, understands why, and improves. An unreadable hit is a bug
  (§1.3), never "difficulty."
- **Difficulty is options, never locks.** LORE forbids class locks; this extends it.
  **Decision:** difficulty is *scalable, opt-in settings* — damage-taken/dealt dials,
  aim/lock-on assists, encounter aggression — surfaced in Settings (the toggles are
  Phase 54; tuning is Phase 56). No difficulty setting gates content, changes the story, or
  locks a build.
- **What scales vs. what never does.** *May scale:* enemy damage/health/aggression, assist
  toggles, the corruption pacing's pressure. *Never scales:* readability, the §1.1
  one-sentence test (reward reading over out-pressing), or access to any quest/region/
  ending. **Decision:** if a "harder" mode would make mashing or rote pattern-memorization
  the dominant strategy, it has violated §1 and is wrong.

> Cross-links: §1 (the mastery ceiling these options sit on top of); Phase 54 (the option
> system), Phase 56 (the numbers).

---

## 5. Corruption fantasy

> **Intent:** corruption is the defining mechanic (LORE) and the *dial behind both
> endings* — earned power you pay for, a temptation the player feels themselves losing to,
> not a punishment the game inflicts. This section is the **design contract Phase 23
> implements**; corruption does not exist in code yet.

The central question (LORE): *can the Seventh Flamebearer resist the fate that consumed the
other six?* The design must make that question *felt*, not narrated:

- **Power and corruption are the same transaction.** Every defeated Flamebearer grants new
  power *and* raises corruption (LORE). **Decision:** there is no corruption-free way to
  take that power — the player chooses how much to drink, and the cost is always real and
  visible. This is the §3 "second power axis" made concrete.
- **Temptation, not punishment.** **Decision:** the corrupt path must be genuinely
  *attractive* — stronger, darker abilities, options the pure path lacks — or the choice is
  fake. Both endings (Dawnfire / Lord of Embers) must be earnable and appealing; corruption
  is a seduction the player has to actively resist, not a debuff to avoid.
- **The world must react — this is the §2.1 "return changed" arrow.** **Decision:**
  corruption visibly bends the moment-to-moment loop: NPCs fear a corrupted player
  (a global "dread" standing), dialogue options shift, the player's *appearance* changes.
  The macro-loop's whole point is that the world you re-enter responds to who you are
  becoming.
- **The player should feel themselves *becoming* a fallen Flamebearer.** **Decision:** the
  fiction (LORE: *"increasingly resembles previous fallen heroes"*) is a design requirement
  — the tiers should evoke the six who failed, so reaching the highest tier feels like
  joining them.

**The seams Phase 23 must build (intent, not implementation):** a tiered 0–100 meter; an
*appearance* shift per tier; *dialogue* gates/branches on corruption; *NPC dread*
reactions via the faction/reputation system; *darker ability* variants unlocked by tier;
and an *ending-eligibility* read that Act IV (Phase 49) consumes for the Dawnfire vs Lord
of Embers choice. Cross-links: LORE "The Corruption System" + both endings; Phase 23
(build), Phase 49 (endings consume it), §2.1 (the loop it bends).

---

## 6. Economy intent

> **Intent:** a *dying* world means *scarcity* — money is tight, meaningful, and spent on
> things that matter. Gold is a sink-driven economy, not a number that only climbs.

Gold today is a **stackable inventory item** (`QuestLogComponent` grants a `GoldItemId`
through `InventoryComponent`; loot tables roll gold). There is no vendor, wallet, or sink
yet — those are **Phase 38**; balance is **Phase 56**. This section fixes what money is
*for* so Phase 38 builds the right machinery:

- **Scarcity is the setting expressed economically.** **Decision:** the player should
  rarely feel rich; gold is a constrained resource in a world that is running down, not a
  trivially-overflowing counter. Income sources are deliberate, not a faucet.
- **Sinks, not just sources.** **Decision:** gold drains into things the player *wants* —
  housing (Phase 37), training/perks-for-pay and repair (services, Phase 38),
  fast-travel/inn costs (Phase 25/38). A healthy economy is defined by its sinks; design
  every income beat alongside what it can be spent on.
- **Money buys convenience and gear — not the soul of the build.** **Decision:** gold can
  buy equipment, services, and property, but the *defining* power — divine relics, perks
  (bought with skill points, not gold), corrupted abilities — is earned through effort,
  exploration, and choice (§3, §5), never purchased. This keeps the build player-authored
  and the world's rewards meaningful.

> Cross-links: `src/Items/*`, `src/Loot/*` (gold as item today); Phase 37 (housing sink),
> Phase 38 (vendors/services/sinks), Phase 56 (the numbers).

---

> **House rules for editing this file** (carry them forward in 22B and beyond): state
> *decisions* with a one-line rationale, not restatements of LORE; cross-link real paths
> (`src/...`, `ARCHITECTURE.md`, `CLAUDE.md` §8) so claims are verifiable; mark every
> concrete number as a Phase-29/56 starting point, not a fixed value.
