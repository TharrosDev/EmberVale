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
  fields (docs/RECIPES.md "a new weapon").
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
- **Mitigation is one curve, and it never reaches immunity** (Phase 34E). Physical answers to
  `Armor`; each magic school answers to its own resistance stat; both run through
  `CombatMath.ArmorMultiplier`'s `100 / (100 + x)`, which stays in `(0, 1]`. **Design intent:**
  an enemy may be *resistant* enough to make a school the wrong first choice, never immune enough
  to make it a dead one — a specced player must always have a way in. A real vulnerability
  (damage *amplified* by a negative resist) is deliberately absent; add it only if an encounter
  needs it, and only alongside a resisted-school answer.
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

**Phase 29I tuned values (the *shape*; Phase 56 sets the final numbers).** The anti-mash
lever is a **stamina regen delay**: every spend pauses stamina regen, so mashing keeps
the bar starved while spaced reads let it refill (`StatsComponent.StaminaRegenDelay`,
applied via the pure `StaminaPacing`). Current player shape:

| Knob | Value | Where |
| ---- | ----- | ----- |
| Stamina pool | 120 | `PlayerAttributes.tres` |
| Stamina regen | 15 / s | `StatsComponent.StaminaRegen` |
| **Regen delay after a spend** | **0.9 s** | `StatsComponent.StaminaRegenDelay` |
| Light attack cost | 12 | `IronSword.tres` `StaminaCost` |
| Dodge-roll cost | 22 | `DodgeComponent.StaminaCost` |
| Block cost (per hit) | 10 | `CombatComponent.BlockStaminaCost` |

The result: a sustained mash empties the 120 bar in ~10 swings (~5.5 s) because regen
never gets its 0.9 s of quiet, then locks out attack/dodge/block until the player backs
off — while "swing, read, recover" spends inside the regen and sustains indefinitely.

### 1.7 The framework and the feel — both now built

The combat **framework** (Phase 3) supplied the *math and state*. The combat **feel**
(Phase 29 — "Combat Feel & Game Juice") is the layer that makes that math *land*, and it
**shipped in full (29A–29I)**. This doc is the contract between them; both columns below
are now live, and the right-hand column names the file that answers each intent.

| Concern | Framework (Phase 3) | Feel (Phase 29 — shipped) |
| ------- | ------------------- | ------------------------- |
| Damage / crit | `CombatMath.RollAttack`, `DamagePacket`/`DamageResult` | — |
| Poise / stagger | `CombatComponent` state + `EntityStaggeredEvent` | `HitStopDirector`, hit-react animation, stagger camera shake (29A/29B) |
| Blocking | Stamina-gated `BlockMitigation` | Block feedback; **parry/riposte** — `src/Combat/Parry.cs` (29F) |
| Attack commitment | `MeleeWeaponComponent` Windup→Active→Recovery + combo/finisher | Animation-cancel windows + input buffering (29G) |
| Weapon identity | `WeaponResource` timing/damage/poise/combo fields | `WeaponTrailComponent`, per-weapon impact VFX/SFX (29C) |
| Defense (mobility) | — (block only) | **Dodge + i-frames** — `src/Combat/DodgeComponent.cs` (29E) |
| Targeting | `FocusedEntity` (Phase 18 soft focus) | **Lock-on** with switching — `src/Combat/LockOnComponent.cs` (29H) |
| Anti-mash | Stamina cost per action + poise | Stamina regen delay via `StaminaPacing` (29I — see §1.6) |
| Screen feedback | `DamageDealtEvent` (data) | `CombatFeedbackOverlay` / `CombatFeedbackFx` (29D) |

> **Reading this table:** it is a *map of what exists*, not a to-do list. Phase 29's
> "Done when" bars (`docs/playbook/` 29A–29I) record how each intent was met. What
> remains open is **tuning**, which is Phase 56's — the shapes are set. See §2.4.

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

### 2.4 Input & feel intent — the Phase 29 contract, now met

This is the checklist the combat-feel work answered to. It restates the §1 pillars as *what
the player's hands and eyes must experience*. **All nine sub-phases shipped**, so read this
as the standing acceptance test any future combat change must still pass — not as
outstanding work:

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
> docs/RECIPES.md "a new perk".

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
> implemented**; it is built and live (`src/Corruption/*` — `CorruptionComponent`, tiers,
> the appearance controller, dialogue gates, the ending read). The decisions below still
> govern; they now describe shipped behaviour rather than intended behaviour.

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

**The seams Phase 23 built:** a tiered 0–100 meter; an *appearance* shift per tier; *dialogue*
gates/branches on corruption (`CorruptionAtLeast` / `CorruptionBelow`); *NPC dread*
reactions via the faction/reputation system; *darker ability* variants gated by tier
(`SpellResource.MinCorruptionTier`); and an *ending-eligibility* read that Act IV (Phase 49)
consumes for the Dawnfire vs Lord of Embers choice. Cross-links: LORE "The Corruption System" + both endings; Phase 23
(build), Phase 49 (endings consume it), §2.1 (the loop it bends).

---

## 6. Economy intent

> **Intent:** a *dying* world means *scarcity* — money is tight, meaningful, and spent on
> things that matter. Gold is a sink-driven economy, not a number that only climbs.

Gold is a **stackable inventory item** (`QuestLogComponent` grants a `GoldItemId`
through `InventoryComponent`; loot tables roll gold), and deliberately still has **no player wallet** —
a wallet would be a second place for money to live and a second thing to persist. Balance is
**Phase 56**; the machinery is built.

**The sinks, as they actually exist** (last extended by 38P2; 38L added merchants, not new kinds of sink):

| Sink | Where | Since |
| ---- | ----- | ----- |
| Property deeds | `PropertyResource.PriceGold` → `PropertyDeedComponent` | 37A |
| Goods | `ShopResource`'s spread over `ItemInstance.Value`, via `ShopPricing`. **Sixteen** merchants behind it since 38L — three in the town square, twelve in the Embermarket, one traveller | 38A |
| Fast travel | `TravelFee` / `TravelCosts`, charged in `GameBootstrap.OnFastTravelRequested` | 38C |
| A night's rest | `ServiceKind.Inn` — moves the clock, refills every resource. Charged every night | 38D |
| A bank account | `ServiceKind.Bank` — a one-off fee, then a persistent vault forever | 38D |
| Training | `ServiceKind.Trainer` — a lesson: recipes taught, XP granted, charged once | 38D |
| A mount | `ServiceKind.Stable` — the purchase and its record; Phase 39A owns the mount | 38D |
| A stake in a merchant | `ShopResource.InvestmentTiers` → `ShopStockService.Invest`. Permanent, never repaid | 38I |
| The Crossway toll | `RegionResource.TollGold` → `GameBootstrap.PayToll`, on every **portal** crossing. Not on fast travel, which already pays the row above | 38M |
| A road permit | `ServiceKind.Passage` — 250 gold once, then the Crossway is free. Ten crossings to break even | 38M |
| A bribe at the gate | `ServiceKind.Passage` — 10 gold **and 8 standing**, for one crossing. The standing is charged again at every counter in town through the 38C ramp | 38M |
| An impound fine | `ServiceKind.Redeem` — **12 gold a unit** against whatever the wardens took, through `ContrabandLaw.Fine`. The only price in the game not authored as the price it charges, because the bill depends on how much was seized | 38O |
| A broker's commission | `ShopResource.ConsignCommission` — **18%** of every consignment, taken out of a payout that is already the best in the realm (~0.70 of value against the most generous counter's 0.62). The player is paid more than anywhere else and still hands a slice back, which is why it is a sink rather than a discount | 38P |
| A master's commission | `ServiceKind.Commission` — **60 gold of labour** per piece at Bryn's order bench, plus every ingredient the player did not bring, priced through his own shop's markup. The one sink in the table that is *cheaper* than the alternative it competes with: commissioning undercuts buying the finished piece off his shelf in proportion to what the player already carries | 38Q |
| A sword for hire | `ServiceKind.Mercenary` — **500 gold once**, and `CompanionRoster` is the only record of it. The dearest thing the Ember Crown sells, deliberately above the mount at 400: a mount is a convenience, a second fighter changes what the player can walk into | 38R |
| Repair | — | **pending 40A** |

⚠️ **A commission is the first price in the economy the `ShopPricing` clamps do not protect, and the
labour fee is load-bearing** (38Q). Every earlier price was a spread over one item's value, so
`sell <= value <= buy` held by construction and 38P inherited it for free by calling `SellPrice`. A
commission spans *two different items* — ingredients in, a finished piece out — and crafting is meant
to add value, so nothing in the arithmetic stops buy-materials → commission → sell from paying for
itself. `--validate` runs `CommissionRules.Exploitable` over every recipe the counter can reach, at
the cheapest standing on the ramp, and refuses the data outright. **A future recipe, a keener buyer or
a new specialty can each open the loop**, which is why the fee is authored well clear of the printed
floor rather than on it.

**Supply contracts are the first deliberate gold SOURCE, and they are not in the table above on
purpose** (38Q2). Everything listed there takes money out; the Crossway caravan board puts it in, by
paying above what any merchant pays for goods brought to the yard. That does not contradict the
scarcity intent, because of what bounds it: **a posting can be filled once per rotation**, so the
board is a few hundred gold every four days for a specific errand, not a tap. `--validate` refuses a
posting that pays *less* than the best buyer — a contract worth less than selling is a longer walk
for less money, which the player would only discover by doing it — and deliberately imposes no
ceiling, because the ceiling is the rotation.

⚠️ **The board must never reach the quest log**, and that is the brief rather than a detail.
`QuestLogPanel` carries no Contracts heading on the rule that the journal shows the states the data
actually has; a haulage job in the journal beside the story is the failure 38K's notice board was
already written to avoid.

**Four services were struck rather than built, and each was struck for a different reason** (38R). The
38R brief listed seven; three of them are recorded here so nobody re-litigates them. **A barber/cosmetic
service has nothing to change** — no player appearance system exists anywhere in the game, and 40B's
rule is that a cut system leaves no stub. **A healer is strictly worse than the bed** — `ServiceKind.Inn`
already refills every resource stat and moves the clock for 10 gold, and the one version that *would* be
perceptible (selling corruption away) is forbidden by §5 above rather than merely redundant. **A
warehouse is the second vault 38D already declined**, in `EmberCrownBank.tres`'s own header: a bank you
cannot reach from the next town is a chest, and nothing about a contract or the toll reads a vault, so
"storage staged beside the caravan board" is a chest with a story. (The fourth, passage, was not a
decision at all — 38M had already shipped it.)

**A companion can be bought, and the price is what makes it a service** (38R). `ServiceKind.Mercenary`
puts a sword on the roster for 500 gold, and `--validate` refuses a free one — which is 38Q's ruling
rather than 38O's, because this hands over *goods* (a person who fights for you) rather than advice. The
sharper reason is that a free companion **already exists**: `DialogueEffect.RecruitCompanion` is how Kael
joins after his oath. A mercenary at zero gold is not a generous mercenary, it is a story recruit with
the story deleted. ⚠️ The hire is recorded by `CompanionRoster` and by **nothing else** — a story flag
beside it would survive a dismissal and retire her permanently, which is why `--validate` refuses one.

**The appraiser is free, and that is a decision too** (38P2). A valuation is an obvious per-use sink and it was deliberately not taken: `ServiceRules` refuses any service the player cannot afford *before* the verb runs, so a fee fails closed on the player with an empty purse and a full pack — exactly the person who walked over to ask what is worth carrying. An appraisal only the rich can buy is not a sink, it is a lock on the one screen that explains the economy. Same reasoning as 38O's free warden search and 38P's free consignment counter; `--validate` enforces all three.

**Repair is not built, and that is a decision rather than an omission.** No durability or condition
concept exists anywhere in the game: `StatType` has no such member and nothing in `src/` mentions wear.
Phase 40A decides whether durability is adopted *or explicitly cut*, and 40B's rule is that a cut system
leaves no stub — so 38D shipped no `ServiceKind.Repair` at all. If 40A adopts durability, repair is a new
kind and a branch, nothing more.

**Contraband is a market, not a sink** (38O), and it is the first thing in the economy that is priced
by *who will take it* rather than by what it is worth. `TradeTags.Contraband` is the one tag that fails
**closed**: a good wearing it is refused by every merchant except one who names `contraband` in her own
accepted list, which is the two fences at Hollowreach and nobody else. **Decision:** the cost of selling
it is standing, not gold, and it is **two-sided** — each sale pleases one faction and offends another
(`+5`/`-2` at the Wet Hull, `+3`/`-4` at the Longshore Locker, per sale rather than per unit). The
villagers' half is charged again at every honest counter in the realm through the 38C ramp, exactly as
38M's bribe is; the outlaws' half is what eventually stops bandits attacking on sight. Confiscation at
the Crossway is the counterweight, and it is **recoverable by design** — a fine the player can price,
never a deletion they cannot.

**A merchant fills up** (38H). Each shop tracks how much of a thing it has bought since its last restock,
and pays less for the next one — full price for the first several, then a slope down to a floor, cleared
by the same clock that refills the shelves and the purse. **Decision:** the anti-grind mechanism caps
nothing. There is no limit and no refusal; the price falls and the player decides whether it is still
worth the walk, and the answer — another buyer, another town, come back tomorrow — is the exploration the
economy is supposed to be buying. ⚠️ It only ever *reduces* the sell side, so it cannot touch the
`sell <= value <= buy` invariant, and each unit floors at 1 gold so a one-coin item never saturates its
way into being unsellable.

Two things shape income rather than drain it, and belong in the same picture: the **buy/sell spread**
(selling something back costs roughly two thirds of its price, so looting-to-sell is a slow income and
not a loop) and the **vendor purse** (`ShopResource.PurseGold`, 38C — a merchant runs out of coin and
refills on the restock clock, so a field of corpses cannot be fenced in one visit).

**A merchant is a trade, not a vending machine** (38F). Every shop authors what it will buy
(`AcceptedTags`) and what it is expert in (`Specialties`), matched against `ItemResource.TradeTags`. A
smith refuses herbs and pays over the odds for metal; a general store takes everything and pays plainly
for all of it, which is authored by saying nothing at all. **Decision:** *where* the player sells is a
decision worth making, and specialisation is what makes a world full of merchants necessary rather than
redundant — it is also the substrate regional demand, contraband and collectors are all built on.
⚠️ Both empties fail *open* (an untagged item sells anywhere, a merchant with no accept list buys
anything), and a settlement must always contain one merchant who takes everything, or loot becomes
unsellable by authoring accident.

**A merchant is a business the player can own a piece of** (38I). Stock rows can be gated by standing,
by a story flag, or by how far the player has bought into the merchant, and gold buys a permanent stake:
a rung of an authored ladder that raises her purse at every future restock and opens the shelves she
keeps back. **Decision:** this is the arc's flagship *late-game* sink, and it is the first one that is
not a purchase — every other entry in the table above hands the player an object or a night's sleep,
which stops mattering once the gear stops improving. A stake hands back capacity and access, and it is
never repaid in coin. ⚠️ **It moves no price**, deliberately: standing already owns the price ramp, and a
second buy-side multiplier would duplicate it from an authority that could drift. ⚠️ A locked row is
**shown, greyed, with its gate named** in the order flag → standing → gold, so a player is never sent to
earn coin for something a story beat is holding shut — and so that a gate teaches the player it exists
rather than hiding the fact that the shelf goes deeper.

**Commerce keeps hours, and one merchant keeps a road** (38J). Shops open and close on the world clock,
and a travelling trader is in town one day in four. **Decision:** a closing time is a *wait*, never a
gate — the inn already advances the clock, so being told to come back at dawn costs the player a night's
sleep and buys the world a rhythm. A merchant who might not be in town is the one exception, because no
amount of waiting is guaranteed to produce him: ⚠️ **a consumable may never be sold only by a travelling
shop**, and `--validate` enforces it. Services keep no hours at all, since an inn that closed at night
would be the only way to pass the night, closed at night. The traveller pays a specialist's premium for
what the town has no use for — pelts, trophies, gemstones — which makes catching him a reason to check
what day it is, and is the first time the calendar has mattered to anything.

**Standing moves prices, in both directions** (38C): `ShopPricing.PriceMultiplierFor` runs from a 15%
surcharge at the hostile end of the ramp to 15% off at Allied, and a faction the player is *hostile* to
will not trade at all. Only the buy side moves — see `CLAUDE.md` §8 for why the sell side deliberately
does not.

This section fixes what money is *for*:

- **Scarcity is the setting expressed economically.** **Decision:** the player should
  rarely feel rich; gold is a constrained resource in a world that is running down, not a
  trivially-overflowing counter. Income sources are deliberate, not a faucet.
- **Sinks, not just sources.** **Decision:** gold drains into things the player *wants* —
  housing (Phase 37), training and services (Phase 38D), fast travel and a bed for the night
  (Phase 38C/D). A healthy economy is defined by its sinks; design every income beat alongside what it
  can be spent on. (This bullet used to say "perks-for-pay", which contradicted the one below it;
  resolved in 38D — see there.)
- **Money buys convenience and gear — not the soul of the build.** **Decision:** gold can
  buy equipment, services, and property, but the *defining* power — divine relics, perks
  (bought with skill points, not gold), corrupted abilities — is earned through effort,
  exploration, and choice (§3, §5), never purchased. This keeps the build player-authored
  and the world's rewards meaningful.
  ✅ **Resolved in 38D, and this rule won.** "Training/perks-for-pay" in the bullet above could not both
  be true with "perks are bought with skill points, **not gold**", so the trainer sells **access, never a
  rank**: recipes it teaches (`CraftingComponent.Learn`) and XP for a lesson
  (`ProgressionComponent.AddXp`). Skill points therefore arrive only by *levelling*, and 38D deliberately
  added no way to grant one directly — `ProgressionComponent` gained no new API.
  ⚠️ **The bound matters as much as the route.** An XP lesson that could be re-bought would be gold
  buying levels without limit, which is this rule broken by arithmetic rather than by wording. So a
  trainer granting XP must record the lesson in a story flag, and `--validate` rejects one that does not.
  A future trainer selling a *perk line unlock* (a flag gating `PerksComponent.CanLearn`) stays inside
  the rule; one calling `GrantFree` for coin does not.

**The economy became reachable in 38E, which is a design fact and not a wiring detail.** Until then the
only way to trade was `shop <id>` in the dev console: one authored shop, no vendor placed in the world, and
a sink table describing money the player had no way to spend by playing. 38E put three merchants behind it
— Aldreth, Bryn and Mirela — reached by **talking to them**, through a `DialogueEffect.OpenShop` choice
rather than by replacing their conversations. That decision is load-bearing for everything §6 wants next:
a merchant who is a *person* can carry hours, standing, a haggle, a contract and a rumour, and a
`VendorComponent` on a crate cannot. It also gave two merchants different prices for the same goods for the
first time (Bryn pays more for metal than Aldreth does), which is the seed of the specialist premium.

> Cross-links: `src/Items/*`, `src/Loot/*` (gold as item today); Phase 37 (housing sink),
> Phase 38 (vendors/services/sinks), Phase 56 (the numbers).

---

> **House rules for editing this file** (carry them forward in 22B and beyond): state
> *decisions* with a one-line rationale, not restatements of LORE; cross-link real paths
> (`src/...`, `ARCHITECTURE.md`, `CLAUDE.md` §8) so claims are verifiable; mark every
> concrete number as a Phase-29/56 starting point, not a fixed value.
>
> **And one more, added by the Phase 35 audit:** when a phase this document describes in the
> future tense *completes*, its section must be revisited in the same session. This file
> claims authority over content and balance, which makes a stale status claim actively
> dangerous rather than untidy — §5 told readers corruption did not exist in code for the
> twelve phases after Phase 23 shipped it, and §1.7 listed all of Phase 29's shipped feel
> work as "not built yet". A reader trusting either would have rebuilt working systems.
