## Phase 28 — First Boss: a Fallen Flamebearer (Iron King slice) `[F/C]`

> One full multi-phase boss to build and prove boss tooling ahead of Phase 36, and
> to wire the defeat → reward → corruption-gain loop.

- [x] **28A — Iron King actor + arena** `[F/C]` ✅
  - **Goal:** the boss exists in a space.
  - **Tasks:** build the Iron King as a `CharacterEntity` via a boss factory
    (mirror `EnemyFactory`): stats `AttributeSet`, `CombatComponent` (Team), a
    weapon, hurt/hitboxes, AI behaviour. Build an arena sub-cell with an entry
    trigger. Register in `ServiceLocator` if the boss bar needs it.
  - **Done when:** you can enter the arena and fight a functional (single-phase)
    Iron King.
  - **Done:** `BossFactory` (`src/Enemies/BossFactory.cs`) mirrors `EnemyFactory` to build a
    `BossEntity` "Iron King" — bigger capsule + dark-iron/ember material, `IronKingAttributes.tres`
    (650 HP, Armor 15, slow heavy hits), `CombatComponent` Team 1 with `MaxPoise 150` (shrugs off chip
    stagger), `IronKingMaul.tres` weapon, and the **reused** `EnemyAIComponent` tuned for a boss
    (`RetreatHealthFraction=0`, `VisionRange 40`, `AttackRange 3.5`). Hostile via a new
    `faction.fallen` (`data/factions/Fallen.tres`, default-hostile). `BossEntity : EnemyEntity`
    marker (`src/Enemies/BossEntity.cs`) is its own `ServiceLocator` type (the 28C bar / 28D corruption
    hook). *(Superseded: the bar and the corruption loop moved onto `BossEncounterStartedEvent` /
    `BossPhaseChangedEvent`, and the Phase 36 audit removed the registration once nothing read it.
    The type itself is still load-bearing — see `BossEntity.cs`.)* Registered as `enemy.iron_king` in `EnemyTemplateRegistry` (seeded 1→2). The **arena**
    (`scenes/regions/ember_crown/arena.tscn`) is a streamed sub-cell (nav + floor + a U of walls open
    toward town) added to `EmberCrown.tres` `Cells` at `(55,0,-10)`; its **entry trigger** is an
    E-interact **challenge brazier** (`BossSummonComponent` — mirrors `RegionTransitionComponent`) that
    spawns the Iron King once, registers him, and re-arms on his death (the seed for the Phase 36
    `BossController` — intro lock/phases graft here). Build clean + 251 tests + `--validate` 0
    (`faction.fallen`/`IronKingAttributes`/`IronKingMaul`/arena cell resolve, registry reports
    `enemy.iron_king`); arena instances + bakes navmesh clean; boots clean (`errors: []`).
    **Deferred to 28B–D:** multi-phase + telegraphs, healthbar + intro/defeat, loot + corruption-gain +
    defeat persistence (so he re-summons on cell reload and drops nothing yet). Walking east to the
    arena and fighting him is the maintainer's at-keyboard check (MCP can't drive movement/`E`/combat).

- [x] **28B — Multi-phase behaviour + telegraphed attacks** `[F]` ✅
  - **Goal:** phases and readable wind-ups.
  - **Tasks:** add HP-threshold phase transitions (e.g. 66%/33%) that change the
    ability set, and telegraphed wind-up timing on heavy attacks (the "no
    button-mashing" feel). Keep it data-light but real; this becomes the seed for
    `BossController` in Phase 36 — note the generalizable bits.
  - **Done when:** the fight has ≥2 distinct phases with telegraphed attacks.
  - **Done:** a new `BossController` (`src/Enemies/BossController.cs`, added in `BossFactory`) rides on
    top of the shared `EnemyAIComponent`/`MeleeWeaponComponent` — no AI rewrite. **3 phases:** it
    watches `DamageDealtEvent` for hits on the boss and, crossing 66% / 33% HP, stacks attack-speed +
    move-speed `StatModifier`s (`boss.phase2/3`) so the later thirds are visibly more relentless;
    publishes `BossPhaseChangedEvent(boss, phase, total)` (the 28C bar / Phase-36 generalisation hook).
    **Telegraphs:** every swing (`AttackPerformedEvent` from the boss) flares the body's emissive glow
    during the maul's 0.55 s wind-up and fades it over the swing — readable heavy hits, brighter/redder
    each phase. **Generalizable bits for Phase 36** (noted in the class doc): the HP-threshold→profile
    table + publish-on-transition event, and the telegraph as a presentation hook any wind-up can drive.
    Build clean + 251 tests + `--validate` 0; boots clean (`errors: []`). Seeing the phase flares +
    speed-up mid-fight is the maintainer's at-keyboard check.

- [x] **28C — Boss healthbar + intro/defeat sequencing** `[F]` ✅
  - **Goal:** the boss UI/flow beats.
  - **Tasks:** add a boss healthbar to `GameHud` (through `UiTheme`), a short intro
    lock and a defeat sequence (slow-mo/fade hook for Phase 43 cinematics later).
    All strings via `Loc`.
  - **Done when:** the bar tracks the boss; intro and defeat beats play cleanly.
  - **Done:** `BossSummonComponent` now publishes a new `BossEncounterStartedEvent(boss, "boss.name")`.
    Two consumers, cleanly split: **(UI)** `GameHud.BuildBossBar()` — a top-centre panel (name + wide
    `UiTheme.Bar` + "Phase n/3" + a transient message line) that shows on start, polls
    `stats.GetNormalized(Health)` each frame, updates the phase label off `BossPhaseChangedEvent` (28B),
    and on the boss's `EntityDiedEvent` hides the bar + plays a defeat message + a manual `ColorRect`
    fade pulse (wall-clock timed, so `TimeScale` can't slow it). **(Flow)** `BossEncounterDirector`
    (`ProcessMode.Always`, created in `GameBootstrap`) — intro lock via `UiState.Open/Close` (~2.5 s)
    and a slow-mo defeat (`Engine.TimeScale = 0.35` for ~1 s), both timed off `Time.GetTicksMsec` and
    safety-restored on teardown. 4 `Loc` strings added (catalogue → 213). Build clean + 251 tests +
    `--validate` 0; full play boot clean (`errors: []`, arena streamed). The bar/intro/defeat *feel* is
    the maintainer's at-keyboard check (MCP can't drive `E`/combat).

- [x] **28D — Defeat → reward → corruption-gain loop** `[F/C]` ✅
  - **Goal:** wire the boss to corruption + loot.
  - **Tasks:** on defeat, grant a guaranteed reward (a placeholder divine-relic
    item `.tres`) and raise corruption via `CorruptionComponent` (absorbing his
    fragment). Author the reward + the "absorb the flame?" dialogue/choice beat.
    Add a placeholder music cue hook for Phase 31.
  - **Done when:** defeating the Iron King grants the relic and visibly raises
    corruption; the whole beat round-trips through save/load.
  - **Done:** `BossEncounterDirector` (the persistent boss coordinator) now, on the boss's death,
    grants `item.relic.iron_heart` ("Heart of the Iron King", new `data/items/IronHeartRelic.tres`,
    Legendary) to the player's inventory, sets the persisted story flag `flag.iron_king_defeated`,
    publishes a placeholder `MusicCueRequestedEvent("music.boss_defeat")` (the Phase 31 audio hook), and
    — after the slow-mo beat settles — opens the **"absorb the flame?"** dialogue
    (`data/dialogue/IronKingAbsorb.tres`) via `DialogueStartedEvent`. The *Absorb* choice uses the
    existing `DialogueEffect.AddCorruption` (+25 → crosses Untainted→Touched, firing
    `CorruptionTierChangedEvent` that the vignette/appearance react to); *Leave it* declines. The brazier
    (`BossSummonComponent`) now reads the flag and goes cold (empty prompt, `Interact` no-ops) so his
    defeat persists — no re-fight, no re-grant. **No save code added** — corruption, inventory and story
    flags are all existing `ISaveable`s, so the beat round-trips for free. 8 `Loc` strings (→221), 13
    items / 6 dialogues. Build clean + 251 tests + `--validate` 0 (dialogue graph reachable); boot clean.
    The defeat→relic→absorb→corruption→save/load chain is the maintainer's at-keyboard **Gate G0** pass.

> **🚩 Gate G0 — First Playable.** New game → creation → Ember Crown → core loop →
> defeat the Iron King slice → gain corruption → save/load intact, with corruption
> visibly changing something. (Roadmap §2.) Verify the full chain before opening
> Stage B.

---

# Stage B — Vertical Slice (→ G1)

> Everything in the slice is **ship-quality**. These sub-phases polish, not
> prototype.

---
