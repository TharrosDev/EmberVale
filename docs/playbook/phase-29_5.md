## Phase 29.5 — Spellcraft & the Fading Weave `[F]`

> Magic made deep + original. Phase 12 built the *system*; this gives it identity and
> depth so magic is a real build spine for the slice (DESIGN §1.5). All new *mechanics*
> land here, before the G2 freeze; breadth/content is woven through 26/34/35/42/47–48/51.
> Theme: magic is the fading **Weave** of a dying world — recover lost spellcraft, and
> corruption is the darker shortcut (extends 23H). Read `src/Magic/` first.

- [x] **29.5A — Cast archetypes: Charged + Channeled** `[F]` ✅
  - **Goal:** casts have feel beyond fire-and-forget.
  - **Tasks:** add a `CastMode` (Instant · Charged · Channeled) to `SpellResource`
    (append-only enum), layered on the existing Projectile/Area/Self *shape*; give
    `SpellcastingComponent` charge build-up (power/radius scale with hold) and channel
    (sustained tick at a mana-per-second cost, interruptible). Drive from the player
    controller. Persists nothing new (transient cast state).
  - **Done when:** one charged and one channeled spell cast and feel distinct from instant;
    mana drains correctly; round-trips (known spells already save).
  - **Done:** `CastMode` enum on `SpellResource` + `SpellcastingComponent` cast state machine
    (`BeginCast`/`UpdateCast`/`EndCast`/`CancelCast`): charged scales damage by hold time via pure
    `SpellCharge.PowerMultiplier`; channeled ticks every `ChannelTickInterval` at `ChannelManaPerSecond`,
    interrupted on key-up/out-of-mana/death. `PlayerController` drives press/hold/release off the Cast key.
    Slice spells **Flame Lance** (charged Fire) + **Storm Conduit** (channeled Lightning). Damage-only power
    scaling for now (projectile impact-radius scaling deferred).
- [x] **29.5B — School identities + status effects** `[F/C]` ✅
  - **Goal:** each `DamageType` school plays differently, not just tint+resist.
  - **Tasks:** author the signature mechanic + status effects per school — Fire ignite/DoT
    stacks, Frost chill→freeze, Lightning chain-to-nearby, Arcane ward/dispel, Nature
    heal-over-time/totem, Necrotic lifesteal/decay (corruption-gated per 23H). Mostly new
    `StatusEffectResource` `.tres` + small resolver hooks (docs/RECIPES.md).
  - **Done when:** every school has a distinct on-hit behavior provable in the sandbox.
  - **Done:** one shared on-hit seam (`SchoolIdentity.OnSpellHit`, invoked by `SpellResolver`
    after damage, before the spell's own status). **Fire** = stacking ignite (`StatusEffectResource.MaxStacks`,
    DoT × stacks; Burning stacks to 5). **Frost** = chill→freeze (`Frozen.tres` hard-root, applied when
    hitting an already-chilled target). **Lightning** = single chain to the nearest other hostile for ½
    damage (`StormConduit`). **Nature** = heal-over-time (`HealPerTick` on the status resource +
    `Regrowth.tres`; `LesserHeal` now leaves a regrowth). **Necrotic** = caster lifesteals 35% of the hit
    (corruption-gated by the spell, e.g. `EmberSiphon`). **Arcane** = the ward (`ArcaneShield`) stays its
    identity; on-hit dispel deferred until an offensive Arcane spell exists (29.5G). Pure bits unit-tested
    (`SchoolIdentityTests`).
- [x] **29.5C — Spell scaling + school mastery track** `[F]` ✅
  - **Goal:** "hard to master" magic ceiling that isn't just bigger numbers.
  - **Tasks:** extend `CombatMath.RollSpell` scaling off SpellPower/Intelligence; add a
    per-school **mastery** that ranks by casting that school and empowers/unlocks its
    spells (reuse perk/progression patterns; `ISaveable`). New `MasteryComponent` or fold
    into progression.
  - **Done when:** casting a school raises its mastery, which measurably empowers it;
    mastery round-trips through save/load.
  - **Done:** `RollSpell` now also scales off Intelligence (alongside gear's SpellPower). New
    `SchoolMasteryComponent` (`ISaveable`) banks a point per cast of a school (off `SpellCastEvent`),
    converts points→rank via pure `SchoolMasteryMath` (10 casts/rank, cap 5, +8%/rank), and
    `SpellcastingComponent` folds the school's mastery multiplier into every cast's damage **and** heal.
    Points persist; rank is derived. `mastery` dev command inspects it. Curve unit-tested
    (`SchoolMasteryMathTests`). Mastery-gated *unlocks* deferred (no spell needs one yet).
- [x] **29.5D — Reactive spell combos** `[F]` ✅
  - **Goal:** cross-school reads, the magic analogue of the combat read.
  - **Tasks:** a small `SpellCombo` resolver that inspects the target's
    `StatusEffectsComponent` on hit and fires a bonus effect (Chill + Lightning = shatter,
    etc.), data-described where possible.
  - **Done when:** at least two combos trigger and are documented; no hard-coded one-offs.
  - **Done:** `SpellCombo` reads the target's pre-hit afflictions on the same on-hit seam (before the
    spell's own status applies) and resolves the first matching rule from a declarative `ComboRule[]` table:
    **Shatter** (Lightning into Chill) and **Thermal Shock** (Fire into Chill) — each a burst plus consuming
    the chill. `StatusEffectsComponent.Consume` strips the spent status. Pure matcher unit-tested
    (`SpellComboTests`); table promotes to `.tres` only if the catalogue grows (Phase 51).
- [x] **29.5E — The fading Weave (region potency + spell recovery)** `[F]` ✅
  - **Goal:** the dying-world magic identity, mechanical.
  - **Tasks:** a light, dev-tunable per-region **magic-potency** dial (ties to Phase 25
    streaming) feeding cast cost/power; spells are *recovered* (tome/teacher), not vendored
    — a `Learn`/recovery seam reusing the 23H learn path; corrupted casting eases as potency
    falls. A `weave` dev-console command to inspect/tune.
  - **Done when:** potency visibly shifts cast power in two regions; a recovered spell is
    learnable via the recovery path; saved.
  - **Done:** `RegionResource.WeavePotency` (0..1, dev-tunable) feeds a global `Weave` static (mirrors
    `SafeZones`), set on world build + every region transition. Pure `WeaveMath` bends a cast by potency:
    as the Weave fades, **ordinary** magic weakens + costs more (×0.5 pow / ×1.5 cost at zero), **corrupted**
    magic (gated above Untainted) strengthens + cheapens (×1.4 / ×0.6) — the 23H temptation made mechanical.
    `SpellcastingComponent` folds it into both damage and mana cost. Two regions contrast (Ember Crown 1.0,
    Frostfang Reach 0.5). Recovery seam: `SpellTomeComponent` (an interactable) teaches a spell through the
    same corruption-gated `Learn` — an Ashen Tome near spawn holds the corrupted Ember Siphon. `weave`
    dev command inspects/tunes. Potency is region data, so it restores on load with the region (no new save
    state); learned spells already persist. Math unit-tested (`WeaveMathTests`).
- [x] **29.5F — Enemy & NPC caster AI** `[F]` ✅
  - **Goal:** the world casts back (the sandbox has zero enemy magic today).
  - **Tasks:** a casting behavior in `EnemyAIComponent` (cast at range, kite to keep
    distance, heal/buff allies) reusing `SpellcastingComponent` on enemies; one caster
    archetype factory (a Valari mage / cultist) with a `.tres` spell loadout.
  - **Done when:** an enemy caster engages with spells, kites, and is beatable; reuses the
    player casting path, no parallel system.
  - **Done:** the `EnemyAIComponent` Combat state gains a **caster branch** (taken when the actor has a
    `SpellcastingComponent` — the very component the player uses, no parallel system): hold the cast band
    via pure `CasterDecision` (approach when far, **kite** when crowded, hold otherwise), face the target so
    the cast aims true, and pick one cast per tick by priority — **heal a wounded ally** (`FindWoundedAlly`
    over the enemy group, on the caster's team, incl. itself), else the hardest-hitting ready **offensive**
    spell, else **ward itself**. New `SpellcastingComponent` levers reused by the AI: `TryCastById`,
    `TryCastSupportOn(ally)`. First archetype: the **Ashen Acolyte** (`AshenAcolyteFactory` +
    `CultistAttributes.tres`), a squishy Fallen fire-caster (Firebolt/Fireball/ArcaneShield/LesserHeal) that
    aims from a chest `CastOrigin` marker; registered in `EnemyTemplateRegistry`, spawnable via
    `spawn <n> enemy.ashen_acolyte`. Wounded casters also cast while retreating. Positioning unit-tested
    (`CasterDecisionTests`). The school-themed caster *roster* is Phase 34 (data, no new code).
- [x] **29.5G — Magic UI + one signature spell per school (slice content)** `[F/C]` ✅
  - **Goal:** the slice shows magic as a real, legible spine.
  - **Tasks:** a spellbook/school view with charge/channel/mastery feedback through
    `UiTheme` (functional; beautified in 30.5); author one signature spell per school for
    the slice (full catalogue is Phase 51).
  - **Done when:** the player can browse schools, see mastery/charge, and cast a signature
    spell from each school; content validates.
  - **Done:** every school now has a **signature spell with its own delivery mechanic**: Fire =
    **Flame Lance** (29.5A charged) · Lightning = **Ball Lightning** (a slow orb that *homes* on the
    nearest hostile — new `SpellResource.HomingRange` + pure `SpellHoming.Steer`, applied per-frame in
    `SpellProjectile`) · Frost = **Blizzard** (a lingering *zone* — `ZoneDuration`/`ZoneTickInterval`
    spawn a `SpellZone` that re-`Detonate`s the spell on a cadence, chilling everything inside) ·
    Arcane = **Blink** (`BlinkDistance` teleports the caster along their aim, ray-stopped by world
    geometry) · Nature = **Lifebloom Totem** (`SummonDuration` spawns a `SpellTotem` that heals its
    owner per tick) · Necrotic = **Ember Siphon** (23H corrupted lifesteal). The **spellbook** (the
    character screen's Spells tab) is now a school view: spells grouped under a per-school header
    showing **mastery rank/cap + power bonus** with a progress bar toward the next rank (29.5C data),
    cast-mode tags (`[charged]`/`[channeled]`), and the existing Buy/Upgrade rows — funded by a new
    **SpellPoints** pool on `ProgressionComponent` (1/level, saved as `spell_sp`) so spells no longer
    compete with perks for skill points. **Charge/channel feedback**: `GameHud` gained a school-tinted
    cast meter under the vitals (fills with `SpellcastingComponent.ChargeProgress`, pinned full while
    channeling) and the prepared-spell footer states `charging…`/`channeling`. All strings through
    `Loc` (catalogue 260). Build + **313 tests** (5 new `SpellHomingTests`) + `--validate` (12 spells,
    exit 0) green; in-engine boot clean. **Phase 29.5 (Spellcraft & the Fading Weave) is complete.**

---
