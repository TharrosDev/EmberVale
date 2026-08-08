## Phase 35 — Dragons `[F/C]`

- [x] **35A — Dragon body: multi-hit-zone scalable boss actor** `[F]` ✅
  - **Done when:** a large multi-hurtbox dragon actor exists with tail/wing melee.
  - **Landed as data, not a dragon factory.** `HitZoneResource` + `HitZones`/`IsBoss`/
    `DirectionalMelee` on `EnemyArchetypeResource`, built by the one
    `EnemyArchetypeFactory`. 35D/35E/35F are now `.tres` and nothing else, and Phase 36
    inherits zones for free. `enemy.wild_dragon` is the first body that is not one volume:
    head ×2.0, wings ×1.4, body ×1.0, tail ×0.6.
  - **The bug this phase existed to fix.** `Hitbox._alreadyHit` and
    `SpellResolver.Detonate`'s `struck` both deduped **per hurtbox**. That was invisible
    while every actor had exactly one — with four, a single sword arc or fireball clipping
    three zones billed three full `DamagePacket`s. Both now route through
    `Combat/HitDedupe.cs`, keyed on the **owning entity** (the hurtbox itself is the
    fallback key, so `GameBootstrap`'s owner-less training dummy is unchanged).
  - **Zones replace the capsule, they don't overlap it.** Two hurtboxes over the same
    flesh would not double-damage any more, but whichever the physics query returned first
    would silently decide the multiplier.
  - **`AIProfileResource.TurnSpeedDegrees` is what makes the arcs real.** `FaceTowards`
    used `LookAt` — an instant snap — so a dragon would always be looking at you and only
    ever bite. The profile now slews at a turn rate (`0` = snap = every pre-35A archetype,
    byte-identical). `ai.dragon` turns at 55°/s, which is the dial to tune if flanking
    feels too easy or impossible.
  - **The greybox is generated from the zones**, one blob per hurtbox, weak points
    lightened. It cannot drift out of alignment with what is damageable — the trap a
    hand-placed placeholder sets. The `.glb` is a later art pass, as the Iron King got in
    30D.
  - **Verified:** `dotnet build` clean, `dotnet test` **628/628** (17 new across
    `HitDedupeTests` + `DragonMeleeTests`), `--validate` exits 0 with 30 templates / 30
    bestiary entries. The archetype was additionally load-checked headless (all four zones
    parse with their authored multipliers — a typed `Array[Resource]` is the kind of thing
    that fails to empty in silence), and `--play` boots into a live world with combat
    resolving and no errors.
  - **Still owed (maintainer, at the keyboard)** — the `F1` console:
    - `spawn 1 enemy.wild_dragon`, then hit the head and the tail: the numbers should
      differ by the authored multipliers, and **one swing must produce one number, not
      four**. Cast an AoE into it — same check, one tick.
    - Walk round it: the tail should answer from behind, the wing from a flank, and 55°/s
      should feel like a real turn rate rather than a stuck dragon.
    - The boss healthbar should appear (the `IsBoss` → `BossEntity` path).
  - **Known limits:** the dragon is spawn-only — nothing places it in the world until 35G,
    and it has no encounter entry. It walks; flight is 35B. It reuses `BeastLoot` and has
    no bespoke drops. Its greybox is a `Node3D`, not a `MeshInstance3D`, so
    `EnemyAIComponent.SetShadow`'s distance LOD silently no-ops on it (`_mesh` is null) —
    it costs a shadow at range until the model lands.
- [x] **35B — Aerial AI: flight pathing, takeoff/landing** `[F]` ✅
  - **Done when:** the dragon flies, lands, and takes off under AI control.
  - **Flight is the vertical axis and nothing else.** `LocomotionComponent.Flying`
    swaps gravity for a servo toward `TargetAltitude` at `ClimbSpeed`; horizontal
    movement is untouched, so `EnemyAIComponent` steers a flier with exactly the code
    that steers a walker. There is no second pathing system and no aerial branch in the
    FSM — that split is why the whole sub-phase is four narrow guards and one component.
  - **Tuning lives on the AI profile**, as `TurnSpeedDegrees` did in 35A:
    `TakeoffRange`/`HoverAltitude`/`ClimbSpeed`/`AirborneDuration`/`GroundedDuration`.
    `TakeoffRange = 0` is every other profile in the game and costs them one comparison.
    `ai.dragon` is 16 m / 12 m / 6 m·s⁻¹ / 4.5 s / 8 s. **No new archetype and no new
    enemy** — `enemy.wild_dragon` simply gained flight.
  - **The cycle is time-boxed on purpose.** `FlightDecision` (pure, unit-tested) runs
    `Grounded → TakingOff → Airborne → Landing → Grounded`; it takes off when the target
    is past `TakeoffRange` *or* after `GroundedDuration` of melee, and always lands. A
    dragon allowed to choose would stay up, and with no breath until 35C that is a fight
    where neither side can act. **That hover window is where 35C's breath goes.**
  - **Landing needs no raycast.** Descend with `Flying` still on, target an altitude below
    the floor, and `MoveAndSlide` stops the body — `IsGrounded` ends the phase. Uneven
    ground and landing higher than you took off are free.
  - **Four guards in the AI, all narrow.** Range is measured horizontally, so a dragon
    hovering overhead read as "in reach" and would swing at empty air — the swing is now
    gated on not being airborne. The navmesh is bypassed while flying (its corners route
    around obstacles it is flying over). Leaving combat, including dying, grounds it, so a
    corpse falls instead of hanging in the sky. Melee resumes during `Landing` — the
    descent is the swoop's payoff, not a helpless phase.
  - **Verified:** `dotnet build` clean, `dotnet test` **640/640** (12 new in
    `FlightDecisionTests`, including a full-cycle walk proving no phase is a dead end),
    `--validate` exits 0. `ai.dragon`'s five flight fields were load-checked headless
    (and `ai.boss` confirmed still at `TakeoffRange 0`), and `--play` boots into a live
    world with the walking roster fighting normally — the `Flying == false` path is every
    other enemy in the game.
  - **Still owed (maintainer, at the keyboard)** — the `F1` console:
    - `spawn 1 enemy.wild_dragon` and back away past 16 m: it should climb, close on you
      from the air, and land — not hover indefinitely, not fall out of the sky.
    - It must not swing while airborne, and must resume melee the moment it is down.
    - Kill it mid-flight: the corpse should fall.
    - Watch a full cycle for pacing. `AirborneDuration` and `GroundedDuration` are the
      dials, and 12 m may read as too high once there is a model to see.
  - **Known limits:** no flight animation — the greybox has no `AnimationPlayer`, so the
    clips land with the `.glb`. Nothing persists: a spawned dragon is transient and
    nothing on the enemy AI path is `ISaveable`, so a save mid-flight is not a case that
    exists yet. The climb is a constant-velocity servo, not accelerated — fine for a
    greybox, worth easing when the wings are real.
- [x] **35C — Breath attacks (cones/AoE) via SpellResolver** `[F]` ✅
  - **Done when:** breath attacks reuse `SpellResolver`/status for cone/AoE damage.
  - **Breath is a spell, not an attack.** `spell.dragon_breath` is `Delivery = Cone`,
    `CastMode = Channeled`, Fire school, 55° × 14 m, applying `status.burning` — so it goes
    through `SpellResolver`, school resistances, `SchoolIdentity` and the status pipeline
    exactly as any player spell does. The roadmap asked for this specifically, and it is why
    35E/35F's Ash and Ancient breaths cost a `.tres` each.
  - **`SpellDelivery.Cone` + `SpellResolver.Sweep`.** A cone is `Detonate`'s sphere query
    narrowed by one predicate, so both shapes share a single private `Resolve` rather than
    becoming two resolvers that must be kept in step. The geometry is the pure `SpellCone`;
    `ConeAngleDegrees` is the **full** width, which the tests pin — reading it as a half-angle
    would silently double every cone ever authored.
  - **Hurtbox position is the shape child's, not the Area's.** An `Area3D`'s origin is the
    actor's origin, so testing it would place a 35A dragon's head, wings and tail at the same
    point and let a cone take all four or none. `VolumeCentre` reads the `CollisionShape3D`,
    falling back to the Area for the ordinary single-shape hurtbox.
  - **The blocker this had to clear.** `TickCombat` branched to standoff/kiting on
    `_casting != null || _profile.IsStandoff` — so giving the dragon spells would have stopped
    it biting and turned 35A's melee arcs into dead code. The first half was **already
    redundant**: every spell-carrying actor with an `EnemyAIComponent` (the seven 34D/34E
    archetypes *and* the bespoke Ashen Acolyte) uses `ai.caster`, whose standoff range already
    sets `IsStandoff`; companions use a different AI entirely. Dropping it states the real rule
    — **a caster is a profile that stands off, not an actor that holds spells.**
  - **Aiming from 12 m up.** `Aim()` reads the `CastOrigin` node's forward and the AI keeps the
    body level, so a hovering dragon would have breathed straight over your head.
    `BreathComponent` points that node at the target before casting; every delivery shape
    inherits the pitch without knowing why.
  - **`BeginCastById` is the one thing enemies lacked** — `TryCastById` is instant-only, and a
    channel needs `BeginCast` → `UpdateCast` → `EndCast`. It mirrors the existing method rather
    than adding a parallel casting path.
  - **Grounded it must turn to breathe; airborne it need not.** On the ground the breath is gated
    on facing, so 35A's flanking denies it and the 55°/s turn rate is a real beat. In the air the
    dragon is overhead with its aim pitched down, where a facing gate on a level body would only
    make the hover window fire at random. `BreathWindow` is pure and pins the asymmetry.
  - **Verified:** `dotnet build` clean, `dotnet test` **655/655** (15 new across `SpellConeTests`
    and `BreathWindowTests`, plus the updated `SpellDelivery_Ordinals` — the test that exists to
    catch exactly this kind of enum edit), `--validate` exits 0 with the new cone and breath
    rules. `DragonBreath.tres` and `WildDragon.tres` were load-checked headless (delivery
    ordinal 3, cast mode 2, the loadout carrying the breath id), and `--play` boots into a live
    world with combat resolving and no script errors.
  - **Still owed (maintainer, at the keyboard)** — the `F1` console:
    - `spawn 1 enemy.wild_dragon`: stand in front and burn, stand behind and don't. Confirm the
      burning status applies and that resistances read as Fire.
    - Let it take off — it should breathe **down** at you from the hover, not overhead.
    - Confirm it still bites, wing-sweeps and tail-swipes between breaths. That is what the
      standoff-clause fix buys.
    - `spawn 1 enemy.hollow_necromancer` and confirm it still kites and casts exactly as before —
      that clause is the one edit here touching shipped behaviour.
  - **Known limits:** the cone greyboxes as four widening `SpellFlash` spheres along its axis —
    legible, but a real particle cone is an art pass. There is no wind-up telegraph: the breath
    starts the frame it is decided, which is a Phase 36 concern (`BossController` owns
    telegraphs) and will matter more once there is an animation to read. Mana is the only limiter
    besides the 6 s cooldown, and the dragon's 120 mana at 18/s means it cannot chain breaths
    indefinitely — worth re-checking once 35D/35E tune the variants.
- [x] **35D — Wild dragon variant (territorial world boss)** `[F/C]` ✅
  - **Done when:** a Wild dragon spawns as a territorial world boss.
  - **It has somewhere to be.** `scenes/regions/frostfang_reach/dragon_roost.tscn` — a third
    Frostfang cell at `(25, 0, −20)`, 90 m of open ground ringed with crags to break the breath
    cone against, glaciers and dead pines. Before this the dragon existed only as a dev-console
    `spawn`.
  - **"Territorial" was the missing mechanic, not a tuning value.** The AI had **no leash**:
    `_home` was read only by patrol and retreat, and `TickCombat` chased until line of sight
    broke — a flying dragon would have followed the player out of Frostfang entirely.
    `AIProfileResource.TerritoryRadius` (`0` = every other profile, unchanged) plus the pure
    `TerritoryLeash` and a new `EnemyState.Returning`. `ai.dragon` owns 45 m.
  - **Returning ignores the player the whole way home**, deliberately. An "unless it can see you"
    clause — which is what `Investigate` does, and why that state could not be reused — would let
    the player defeat the leash by standing in the doorway. Coming home clears `_provoked` and
    resets `_lastKnownPos`, or it would re-engage the instant it arrived.
  - **The hysteresis matters.** Re-engaging needs it back within `ReturnFraction` (0.75) of the
    radius. A single threshold makes a creature sitting on the boundary flicker between chasing
    and leaving every frame.
  - **The state came free from 35B/35C.** `EnterState` already grounds a flier and stops a breath
    on any non-Combat state, so a dragon that disengages mid-air lands and stops breathing with no
    new code. `EnemyState` is documented as not persisted and deliberately unpinned, so appending
    to it is safe.
  - **Persist the spawner, never the boss.** `CellPersistenceDirector` reconciles on
    `RegionCellLoadedEvent`, which `RegionStreamer` publishes *after* `AddChild(root)`
    (`RegionStreamer.cs:174,178`) — a dragon spawned that frame races the walk and a deferred one
    misses it outright, so a killed boss would return every time the valley reloaded.
    `LairSpawnComponent` is authored in the `.tscn` with a stable `PersistentId`, is `ISaveable`,
    and holds one bool. Both restore paths were traced: `SaveManager.Register` restores
    synchronously from an in-flight load *before* the deferred spawn, and
    `CellPersistenceDirector.Save` snapshots live cells so a save taken standing in the roost is
    complete.
  - **Placed west, not north.** North was the obvious spot and the wrong one — the glacier cell
    sits at `z = −60` and its props would have ended up inside the roost's floor, the same mistake
    34.5A had to undo. West butts the roost's floor against the hold's at `x = 70`: walkable the
    whole way, no overlap, no co-planar z-fighting, and the 45 m territory ends right at the hold's
    edge so the dragon will not follow you into it.
  - **It drops like a boss now** — `DragonLoot` replaces the `BeastLoot` placeholder 35A flagged as
    owed: 3–6 `item.material.dragon_scale` (a new Rare material), rubies, an affixed ring, and
    150–320 gold.
  - **Verified:** `dotnet build` clean, `dotnet test` **662/662** (7 new in `TerritoryLeashTests`,
    including that radius 0 never leashes — the property keeping every existing archetype
    unchanged), `--validate` exits 0. The cell scene was load-checked headless (it parses, the nest
    carries its `PersistentId`, the region reports three cells).
    **And the whole thing was seen working in `--play`:** the roost streamed in, the dragon spawned,
    fought, took damage at **8 / 16 / 27** per hit — the 35A zone multipliers live in a real fight —
    died, and dropped 3 items.
  - **Still owed (maintainer, at the keyboard):**
    - **Run away.** Past 45 m it must break off, walk home and drop aggro rather than following you
      to the clan hold. This is the phase's headline and the one thing no remote session can drive.
    - **Kill it, leave, come back**, then `F5`/`F9` a save round-trip: it must stay dead both times.
      The code paths are traced above but the round-trip itself is unrun.
    - Walk the roost for ground/props, and confirm the drops read as scales and gold rather than
      beast pelts.
  - **Known limits:** no map POI for the roost — you find it by walking west from the hold, with
    nothing on `M` to suggest it. No respawn, by choice: a world boss that returns is a balance
    call (Phase 56), not a 35D one. Frostfang is still gated behind `flag.iron_king_defeated`, so
    the roost is unreachable until the Iron King falls.
- [x] **35E — Ash dragon variant (corrupted elite)** `[F/C]` ✅
  - **Done when:** an Ash dragon exists as a corrupted elite enemy.
  - **The payoff phase.** `enemy.ash_dragon` is a second dragon built entirely from 35A–35D's
    pipeline: attributes, a breath spell, an AI profile, an archetype, loot, a bestiary entry, a
    lair scene and a region cell. **No new systems** — every field it uses already existed.
  - **Its own creature, not a corrupted Wild one.** 34F's rule is that a corrupted creature is the
    base archetype plus `AshenAffliction`, and that rule is right — for *the same creature*
    corrupted. `Afflict` deliberately never changes `TemplateId`, so an afflicted Wild dragon could
    never have its own bestiary page or lore. `LORE.md` gives Ash Dragons their own section
    alongside Wild and Ancient: they are a kind of dragon, not a tinted one.
  - **LORE says "among the most dangerous enemies in the game", so the numbers say it too** — 1900 HP
    to the Wild dragon's 1400, more power, 50 m of territory, and the **zone multipliers are
    deliberately flatter** (head ×1.6 not ×2.0, tail ×0.85 not ×0.6). A corrupted thing has no good
    side to be on, which makes it the harder fight before any stat is compared.
  - **Necrotic breath, not Fire.** `spell.ash_breath` is a wider (80°), shorter (11 m) cone applying
    `status.decay`. Fire resistance buys the player nothing, so the second dragon has to be prepared
    for differently rather than fought the same way.
  - **Placed east, mirroring the Wild roost west.** The hold sits between them: wild roost floor
    `x ∈ [−20, 70]`, hold `[70, 130]`, ash roost `[130, 230]` — three floors butted edge to edge,
    walkable across, none overlapping. Its territory is sized to its own floor exactly so a chase
    can never spill into the hold's safe zone.

  - **🐛 A 35D bug this phase exposed and fixed.** The maintainer saw the dragon spawn "way off its
    den and well into the void". `LairSpawnComponent` passed a **world** position to
    `EnemyTemplateRegistry.Create`, which sets a **local** one, and then parented the actor under the
    cell root — which the streamer had already moved to the cell centre. The offset applied twice:
    the wild roost's dragon landed at `(50, −40)` instead of `(25, −20)`, which its 90 m floor
    happened to cover, so **the bug shipped in 35D looking fine**. The ash roost at `x = 180` threw
    its dragon to `x = 360`, past the region bounds. Fixed by create-at-zero → add → set
    `GlobalPosition`, which is the order `BossSummonComponent` already used — the lair spawner was
    the deviation. `EnemySpawnDirector` had the same latent defect (harmless only because it sits at
    the world origin) and was aligned to the same order.
  - **Verified:** `dotnet build` clean, `dotnet test` **662/662** (no new tests — this phase adds no
    logic, and YAGNI applies to tests too), `--validate` exits 0 with 31 bestiary entries. The ash
    roost was load-checked headless — it parses, its `PersistentId` is distinct from the wild
    roost's (they share a `SaveId` prefix, so a collision would make killing one mark both), the
    breath reads school 6 / delivery 3 / mode 2, and the region reports four cells. **And it was seen
    fighting in `--play`:** the roost streamed in, the dragon spawned *in its den* after the fix, and
    took 24 / 28 / 53 / 96 per hit off 1900 HP — the flatter zone spread, live.
  - **Still owed (maintainer, at the keyboard):**
    - Fight it: the breath must apply **decay**, and fire resistance must not help.
    - Kill it, then confirm the **Wild** dragon in the west roost is still alive — the two lairs must
      persist independently.
    - Stay-dead across a cell reload and an `F5`/`F9` round-trip, for both dragons.
  - **Known limits:** a save taken while a roost is loaded logs
    `entry 'lair:…' had no live claimant on load (orphaned state)` if the cell is not streamed in at
    load time. Harmless — `CellPersistenceDirector` does the real restore when the cell arrives — and
    it is inherent to every cell-authored `ISaveable` (`ContainerLootComponent` registers the same
    way); worth a look if the orphan diagnostic is ever tightened. **Two hand-authored roost cells is
    fine; a third should promote the roost into a reusable scene rather than a third copy.** No map
    POI for either lair.
- [x] **35F — Ancient dragon: dialogue-capable quest/lore giver** `[F/C]` ✅
  - **Done when:** an Ancient dragon can hold a conversation (`DialogueComponent`)
    and give quests/lore.
  - **The first actor that is a boss and a conversation at once.** `enemy.ancient_dragon`
    (Vharyx the Unspoken) sits in a 90 m aerie north of the Wild dragon's roost, holds
    `dialogue.ancient_dragon`, gives `quest.ancient.kin`, and fights like the other two if you
    make it. Everything 35A–35E built is reused unchanged: hit zones, directional melee,
    flight, a cone breath, a territory leash, a persisted lair spawner.
  - **Four small code seams, all of them general:**
    - `EnemyArchetypeResource.DialogueId` → `EnemyArchetypeFactory` attaches a
      `DialogueComponent`. Nothing else was needed to make it reachable — the interact
      raycast is unmasked and resolves the owner from whatever collider it hits, so the body
      the creature already has is the target.
    - `DialogueEffect.LearnSpell` (**ordinal 8**) → `SpellcastingComponent.Learn`. This is the
      conversational half of 29.5E's recovery seam, where `SpellTomeComponent` was the
      found-object half, and it closes the roadmap's "earning one's favor teaches a recovered
      spell". `Learn` ignores `PlayerLearnable`, which is exactly why it works: the spell can
      be given but never bought.
    - `LairSpawnComponent.DefeatFlagId` → sets a story flag on the kill (and re-applies it on
      load). **Nothing in the game turned a kill into a flag before**, so "you have slain the
      boss" was not askable by a dialogue condition or a gated interactable. Every world boss
      gets it, not just this one.
    - `SpellTomeComponent.RequiredFlagId` → a tome that will not open until a flag is held.
      Together with the above, that is the defeat route: the hoard sits in the aerie from the
      start and yields the same word once its keeper is dead.
  - **🐛 A live UI defect this phase surfaced.** `InventoryPanel.BuildSpells` filtered the
    character screen on `PlayerLearnable`, so a spell the player had *actually learned* but
    could never buy rendered nowhere. The 35F reward would have been invisible in the one
    screen that lists your spells. Fixed at the filter (`|| _spellcasting.IsKnown(s)`); it was
    a latent bug for any recovered enemy-grade spell, not just this one.
  - **Neutral until provoked cost nothing.** `faction.dragons` — its own faction, deliberately
    not the Wild dragon's `faction.beasts` or the Ash dragon's `faction.fallen`, so clearing
    the wilds of wyrms does not make the one you can talk to draw breath on you. Default
    standing Neutral, `HostileThreshold` at Unfriendly: `EnemyAIComponent.PlayerIsTarget`
    already returns false above the threshold and `OnDamaged` already sets `_provoked`. **No
    AI code was written for this phase.**
  - **The roost debt is paid.** 35D and 35E both ended with "a third roost should promote the
    roost into a reusable scene rather than become a third copy", so it was promoted *before*
    the third one landed. `scenes/regions/roost.tscn` + `RoostCell.cs` own the nav region,
    baker, floor mesh/collider and the `Nest`/`Lair` markers; all three roosts are inherited
    scenes carrying only their floor knobs, their identity, their occupant and their props.
    The floor's mesh, shape and material are base-scene sub-resources shared by every roost,
    so `RoostCell` `Duplicate()`s each before touching it — otherwise sizing the third would
    have resized the other two. The Wild roost's floor roughness moved 0.9 → 0.95 in the
    merge; nothing else about either existing roost changed.
  - **One spell, not two.** `spell.elder_word` is the Ancient's breath *and* the thing it
    teaches — Arcane, so neither the Fire resistance the Wild dragon teaches you to carry nor
    the Necrotic one the Ash dragon does buys anything. Making the reward literally the weapon
    that was used on you is one `.tres` instead of two, and it reads better than either.
  - **The quest ties the three dragons together.** `quest.ancient.kin` is a Kill objective on
    `enemy.ash_dragon` — 35E's boss — so the favour route is earned by real work in the same
    region rather than by exhausting a dialogue tree, and Frostfang's three lairs are one story
    instead of three fights. There is no "return and tell it" objective (the quest system is
    Kill/Collect only), so the turn-in is a conversation the player has to remember to have.
  - **Verified:** `dotnet build` clean, `dotnet test` **670/670**, `--validate` exits 0 (29
    archetypes, 32 bestiary entries, 9 factions, 14 quests, 13 conversations, 18 spells, 620
    strings). All four roost scenes were **instantiated headless** — the base plus all three
    derived cells build with their own floor size (90 / 90 / 100 / 90), their own prop counts,
    distinct `PersistentId`s, the right occupant each, and the hoard + defeat flag only on the
    aerie. A headless `--play` into the Frostfang save booted clean and **streamed the
    re-expressed wild roost cell** (`RegionStreamer: loaded cell 'frostfang_reach.dragon_roost'`)
    with zero errors and no nav-bake warning — that is the 35F regression proof for Part 2.
  - **⚠️ Rebuild before you believe a scene check.** The first headless run reported "Cannot
    instantiate C# script … RoostCell.cs" on all four scenes. Not a scene bug — `Embervale.dll`
    predated the new file. This is the §2 stale-binary trap wearing a different costume: it
    looked exactly like a broken inherited scene.
  - **Still owed (maintainer, at the keyboard):** the aerie is ~117 m from where the save sits,
    so `--play` proved boot and save restore, **not** that the Ancient spawns or speaks.
    - Walk to the aerie: the cell streams, the dragon is *in* it, and `E` reads as talking, not
      fighting.
    - Take the quest, kill the Ash dragon, return: the favour branch appears, the Elder Word is
      taught, and it **shows and casts** from the character screen (the fix above).
    - Hit it: it turns hostile, and breaks off at its territory edge rather than following you.
    - Kill it instead: the hoard's tome opens (it must refuse before the kill), the other two
      dragons are unaffected, and all three lairs stay dead across a cell reload and `F5`/`F9`.
  - **Known limits:** the 35E orphaned-`ISaveable` warning on load is unchanged and still
    harmless. No map POI for any of the three lairs. No new unit tests — everything added is
    Godot-node-bound (a session, two components, a factory branch) and the pure-logic suite
    takes no nodes; the one thing that could silently corrupt saves, the new enum ordinal, is
    pinned in `EnumStabilityTests` (which now also pins the three companion effects it had
    been missing since 32C).
- [x] **35G — Dragon encounters in Frostfang + high-end world events** `[C]` ✅
  - **Done when:** dragon encounters seed Frostfang Reach and the world-event
    tables.
  - **The Reach became dragon country.** Every dragon before this was a fixed lair boss you
    travelled to; nothing dragon-shaped happened on its own. `enemy.frost_drake` now wanders
    Frostfang as an ambient encounter, an **Elder Drake** Hunt and a **Spilled Hoard** Cache give
    the event table its first late-game tier, and the scales all four dragonkin drop forge into
    **drakescale mail**.
  - **A lesser dragon, not the named three.** Pointing an encounter at `enemy.ash_dragon` would
    have put two of a one-of-a-kind creature in the world and made `quest.ancient.kin` farmable
    from a random roll. The drake is deliberately **not boss furniture** — no `IsBoss`, no hit
    zones, no directional melee. Those exist so a fight has geography, and geography is for a
    creature you travel to. Declining the zones also declined a whole new AI profile: zones
    without a turn rate leave 35A's flank arcs dead, so it reuses `ai.brute` unchanged.
  - **The champion tier is a multiplier, not a second archetype** — `MinCount = 1` +
    `HealthMultiplier`, the trick `event.goblin_champion` has used since Phase 17.
  - **🐛 A live 34.5B gap this phase closed.** `WorldEventResource` had **no `RegionIds`**. 34.5B
    gave encounters a region gate after frost stalkers prowled the Ember Crown for two phases, and
    the *other* director never got one — so goblin raids had been rolling in Frostfang Reach ever
    since the region existed, and a drake hunt would have rolled in the starting valley. Added the
    field, the `AllowedIn(regionId)` gate, the `RegionStreamer.ActiveRegionId` lookup
    `EncounterDirector` already did, and the validator's unknown-region check. **The three Phase 17
    goblin events were gated to `region.ember_crown` in the same pass** — fixing only the new
    entries would have left the live bug in place.
  - **Gear can carry a resistance now.** `EquippableItemResource` exposed seven `Bonus*` fields and
    not one of 34E's `*Resist` stats, so resistance was authorable on an `AttributeSet` only:
    enemies could shrug off a school and the player could not. `BonusFrostResist` is one export and
    one `yield return` — it is a `StatType`, so equipment, tooltips and the character screen pick
    it up untouched. Only Frost, because only this item needs it; the other five are two lines each.
  - **⚠️ Nothing in the game teaches a recipe.** `CraftingComponent.Learn` exists and **has no
    caller** — no tome, no dialogue effect, no quest reward — and unlike the bestiary, `--validate`
    has no reachability check for recipes. `recipe.leather_vest` has therefore been unreachable
    since Phase 15. `recipe.drakescale_mail` is seeded in `PlayerFactory` like the other six and
    gated on its **ingredient** instead: eight dragon scales, which only Frostfang's dragonkin drop.
    A `LearnRecipe` dialogue effect would be cheap now that 35F put `LearnSpell` next door.
  - **Verified:** `dotnet build` clean, `dotnet test` **670/670**, `--validate` exits 0 (30
    archetypes, 33 bestiary entries, 19 spells, 32 encounters, **5** world events, 8 recipes, 622
    strings). Checked by hand that no encounter or world event references `enemy.wild_dragon`,
    `enemy.ash_dragon` or `enemy.ancient_dragon` — those three are lair-only by design and nothing
    enforces it. **And the whole loop ran in a headless `--play`:** the Elder Drake hunt started in
    Frostfang, the drake built and fought and died, the event completed and paid out, and a Spilled
    Hoard followed — with no goblin event firing in the region, which is the gate doing its job.
  - **Tuned off that run.** The champion went in at `HealthMultiplier = 3.0` (the goblin's) and the
    log showed a **1260 HP** drake — within sight of the Wild dragon's 1400, on an event with a
    180 s hard timer. Dropped to 2.0. A boss you must beat on a stopwatch is a different thing from
    a hunt.
  - **Still owed (maintainer, at the keyboard):** the ambient `encounter.drake_flight` is the one
    piece the log did not catch — it is the lowest weight in the region's pool by design (0.25
    against clan patrols, rites, hunts, stalkers and rime drifts), so it needs a play session rather
    than an idle boot. Fight a drake and confirm the **breath actually fires** (34D's silent failure
    is a caster with no mana just standing there), then confirm the mail drops from the hunt, equips,
    and shows its frost resistance on the character screen.
  - **Known limits:** the 35E orphaned-`ISaveable` warning on load is unchanged. The drake has no
    model (capsule + `PlaceholderTint`) like most of the roster. No new unit tests — this phase is
    content plus two field-and-a-line seams, and the pure-logic suite takes no Godot nodes.

**Phase 35 (Dragons) is complete — 35A–35G.**

---
