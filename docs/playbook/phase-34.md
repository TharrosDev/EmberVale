## Phase 34 — Enemy & Creature Roster `[F/C]` ✅ **complete**

> Turned the enemy roster from code into content. **26 creatures are spawnable by id and only
> three have a factory** — the rest are `.tres`. The systems reference is
> `ARCHITECTURE.md` §2.5; the authoring recipes are docs/RECIPES.md. This block is the log.

- [x] **34A — AI behaviour profiles: data-fy `EnemyAIComponent`** `[F]`
  - `AIProfileResource` + `AIProfileDatabase` (`data/ai_profiles/`, ids `ai.*`). Every knob the
    component exported moved onto the resource, and **the component stayed one class** — each
    behaviour is a branch gated on a profile number, so they compose (a shielded flanking ambusher
    is authorable) instead of forking the brain. Pure `GuardCycle` + `PackFlank` hold the testable
    arithmetic. The goblin kept `ai.brute` at the old defaults, so slice feel was untouched.
- [x] **34B — Humanoid archetypes (bandit, cultist, soldier, Iron Syndicate)** `[F/C]`
  - `EnemyArchetypeResource` + `EnemyArchetypeDatabase` (`data/enemies/`) driven by one shared
    factory, which registers a builder per archetype with `EnemyTemplateRegistry` — a new `.tres`
    is spawnable with no code change. Four archetypes, four encounters, two new factions
    (`faction.outlaws`, `faction.iron_syndicate`).
- [x] **34C — Beast archetypes** `[F/C]`
  - Grey wolf, dire wolf, frost stalker, thornback boar, ashfall elk on two new profiles
    (`ai.territorial`, `ai.prey`), plus `faction.beasts` — hostile by default with **zero kill
    penalty**, the standing a Sylthari communion perk can later flip. Beasts carry no coin.
  - `HumanoidEnemyFactory` → **`EnemyArchetypeFactory`**: it builds quadrupeds now, so the name had
    to stop lying. Melee reach became body-relative (`height / 1.8`), since a 0.9 m wolf was
    otherwise biting a metre past its own nose. Only behaviour delta: the 1.85 m soldier's box grew
    2.8%.
- [x] **34D — Undead archetypes (the Hollow Queen's legions)** `[F/C]`
  - Hollow husk, bone knight, barrow wight, grave shade and a necromancer on two fearless profiles
    (`ai.mindless`, `ai.deathless_guard` — every prior melee profile retreats on wounds, and the
    dead shouldn't), plus `faction.hollow` and the Necrotic school's first enemy content
    (`spell.wither` + `status.decay`).
  - **The first caster archetype authored as data** — all nine prior archetypes had empty
    `KnownSpellIds`, so the path 34B built had never run. `spell.knit_bone` bought ally-mending for
    free from 34A's caster-support branch: the necromancer repairs its own husks with no new code.
  - Bug fixed at the root: the spellbook renders *every* spell in `SpellDatabase`, so monster
    loadouts leaked in as purchasable. `SpellResource.PlayerLearnable` + one filter at the single
    seam every future faction caster routes through.
- [x] **34E — Construct + elemental archetypes** `[F/C]`
  - Three constructs (new `ai.sentry` — holds its post, never patrols, never calls for help) and one
    elemental per offensive school, each resistant to its own.
  - **Had to land a mechanic first:** `CombatMath.Mitigate` mitigated only Physical, so nothing could
    resist a magic school and an elemental had no way to be elemental. Six resistance stats through
    the *same* `ArmorMultiplier` curve — resistance, never immunity. It also closed a live bug where
    a school-typed melee weapon bypassed armour entirely.
  - `spell.arcane_lance` — Arcane's first offensive spell; it had only Self casts.
- [x] **34E.5 — Arcane on-hit dispel** `[F]`
  - An Arcane hit strips the target's longest-lasting buff, never a harmful one, one per hit.
    **Every magic school now has an on-hit identity** — the table 29.5B opened is closed. A Self cast
    can't trigger it (`OnSpellHit` is only reached from the projectile/area paths).
- [x] **34F — Corrupted / Ashen creatures** `[F/C]`
  - Built as a **variant layer**, not another roster row: `AshenAffliction` takes any spawned enemy
    and makes it Morthul's — tougher, charred, ember-lit, worth more — rolled per enemy off
    `EncounterResource.CorruptionChance`. Corruption is authored per *place*, since LORE puts it on
    the realm and never on the player. 35E's "Ash dragon (corrupted elite)" inherits this.
  - Two flagships a modifier can't produce: `enemy.ash_maw` and `enemy.cinder_thrall`, which wields
    the player's own corruption-gated lifesteal.
  - One line of LORE added under Morthul — the sentence the mechanic rests on.
- [x] **34F.5 — Encounter table balance pass** `[C]`
  - A playthrough reported seeing far fewer new enemies than the roster held. The roster was fine;
    the table wasn't. Two archetypes had **no encounter at all** (34E shipped them `spawn`-only);
    the goblin still carried its Phase-4 weights and took **44% of every daylight roll**; and dawn
    was a duplicate of day. After: dawn 10→14 types, day 10→12, goblin share 44%→~20%.
- [x] **34G — `BestiaryDatabase` + bestiary UI** `[F/C]`
  - `B` opens the Ash Hunters' field journal: 26 creatures, seven tabs, kill counts, Ashen counts,
    and lore staged Unseen → Sighted → Known. Built on `UiPanel` + the `MapService` persistence
    shape; `EntityDiedEvent` already carried `TemplateId`, so no new event was needed.
  - Entries key off the **template id**, not the archetype — the goblin, Iron King and Ashen Acolyte
    have no archetype at all. Counts party kills, not just the player's (a quest is a contract; a
    journal is a record). Also fixed three hard-coded English `DisplayName`s.

### What outlived the session

- **Durable rules moved into the permanent docs**, which are the ones to trust: docs/RECIPES.md has
  the recipes (new archetype, AI profile, bestiary entry, corrupted variant, new stat) and the
  traps — a caster needs spells *and* a standoff profile *and* a Mana pool or it silently never
  casts; never change `TemplateId`; always `Duplicate()` a material before tinting.
  `ARCHITECTURE.md` §2.5 and §2.2 describe the systems.
- **The validator got stricter twice, both times from a real bug.** `CorruptionChance` is range-
  checked (34F), and the bestiary is checked **in both directions** — every registered creature must
  have an entry (34G). That second one is the guard against the exact failure 34F.5 had to fix by
  hand: content that exists but nothing can reach.
- **Both guards were proven by making them fail**, not by trusting them.

### Still owed to Phase 34 (maintainer, at the keyboard)

Everything below needs the `F1` console or `F5`/`F9`, which no remote session can drive:

- **The bestiary's `ISaveable` round trip** — kill a few creatures, quick-save, quick-load, confirm
  the counts survive. This is a Done-when clause that has only been read, not run.
- **An Ashen spawn, seen.** `time 22`, wait out a wolf pack, confirm the nameplate reads *Ashen
  Wolf* — then kill a plain wolf and confirm it is **not** tinted (that is where a material-sharing
  bug would surface).
- **Resistance visibly landing:** `spawn 1 enemy.cinder_wisp`, hit it with `firebolt`, expect about
  half damage.
- **The necromancer mending its husks** — `spawn 2 enemy.hollow_husk`, then
  `spawn 1 enemy.hollow_necromancer`, hurt a husk. No automated coverage at all.
- Spot-check the read of `spawn 1 enemy.stone_sentinel` (150 poise: a flurry can't stagger it, one
  committed heavy hit can) and `spawn 3 enemy.wolf` (the pack fans out rather than queueing).

### Known limits, deliberately not fixed

- `EncounterResource` has **no region filter**, so a Frostfang creature can roll in the Ember Crown.
- **One encounter = one template id**, so mixed warbands (an alpha with its pack, a necromancer with
  husks) aren't authorable. The necromancer's mending is only observable when groups overlap.
- **Art:** every 34-series creature is a tinted capsule. A 2.4 m stone golem reads worst. Phase 53.

---
