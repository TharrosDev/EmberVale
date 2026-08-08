## Phase 30 — Animation, Models & Visual Identity `[P]`

> Art-heavy. Model authoring (30B, 30D, 30H) is built in Blender via the Blender MCP
> (`mcp__blender__*`, CLAUDE.md §2) and exported to glTF under the Phase 19/57 import/LOD
> conventions; the human still supplies whatever the MCP doesn't cover (rig finishing,
> hand-painted texture passes, audio). Each sub-phase integrates one asset class against
> existing states.

- [x] **30A — Art-direction style guide** `[P]` ✅
  - **Done when:** `docs/ART_STYLE.md` pins the dying-world language (ash, faded
    colour, embers) + import/LOD conventions feeding Phase 19/57.
  - **Done:** `docs/ART_STYLE.md` written. Direction (maintainer-pinned): **low-poly but
    detailed — Skyrim's grounded, weathered fantasy realism in clean faceted geometry**
    ("carved, not sculpted"): silhouette-first, facets-as-feature, detail via material
    layering/painted wear (no photo textures — sourced PBR must be repainted/posterized).
    Pins the three-layer dying-world language (faded base / ash / ember accents) with a
    hex master palette + saturation discipline, per-realm grading for all five realms,
    the corruption-tier body arc (23F/30I hook), and the school VFX tint law off
    `SpellSchools.Color`. Conventions: per-class triangle + texel budgets and LOD
    thresholds (Steam-Deck-aware, the Phase 19/57 seam), material/vertex-color rules,
    1u=1m + 0.5 m modular-kit grid, fog-as-material atmosphere, and the Blender-MCP →
    `.glb` → `assets/models/<class>/` pipeline incl. collision suffixes, rig limits, and
    an `assets/CREDITS.md` licensing rule for sourced assets. Ends with a 7-point
    per-asset review checklist. Doc-only; no code touched.
- [x] **30B — Player character model** `[P]` ✅
  - **Goal:** the player has a real mesh to rig, not a placeholder capsule.
  - **Tasks:** built in Blender via the Blender MCP — base mesh + texture set matched
    to `ART_STYLE.md` (30A); modular gear/weapon attach points for the equipment the
    player can visibly wear/wield; export to glTF.
  - **Done when:** a static, importable player mesh with equip sockets exists in-engine.
  - **Done:** `assets/models/characters/chr_player_base.glb` (~1.6k tris, 1.8 m, origin at feet),
    built in Blender via the MCP as an **organic connected low-poly body** (skin-modifier over a
    stick skeleton → subsurf → decimate, smooth-shaded with ~60° sharp edges) after the first
    boxy draft was rejected as too Roblox — the maintainer's "low-poly but realistic-ish, never
    blocky" call is now pinned in `ART_STYLE.md` §1.1. Palette materials per region (skin, tunic,
    trousers, leather boots/belt/pads, steel bracers, gold buckle — flat colours, no textures) with
    slim faceted hard-surface gear; **five equip sockets** (`socket_hand_r/l`, `socket_back`,
    `socket_hip_l`, `socket_head`) as empties inside the glb. `PlayerFactory` now instantiates the
    model as `BodyMesh` (turned 180° for glTF→Godot forward, capsule fallback kept if the asset is
    missing), and `CorruptionAppearanceController` was generalized from one-material tinting to
    claiming **every surface material** under the body root (uniquely duplicated, per-material base
    albedo remembered) so each palette colour ashes toward the same dark tone per tier — the 23F
    contract unchanged. Two live-playtest fixes from the maintainer: the corruption emissive made
    the whole model glow red on a corrupted save → the ember-vein emissive now applies to **skin
    materials only** (names survive glTF import), dimmer and ember-toned per ART_STYLE §2.2, with
    clothes only ashing; and the belt buckle floated off the body → embedded into the waist mesh.
    Build + 313 tests green; headless `--import` + in-engine runs clean (the maintainer drove the
    world live during verification). Rigging/animation is 30C.
- [x] **30C — Third-person character + weapon rig integration** `[P]` ✅
  - **Done when:** the rigged player character (30B's mesh) + a weapon play
    attack/block/idle driven by combat states.
  - **Done:** the 30B body is now **rigged and animated**: a 17-bone humanoid armature
    (pelvis/spine/chest/neck/head + arm/leg chains) skinned with automatic weights in Blender
    (via the MCP) and six authored clips baked into the same glb through NLA tracks —
    `idle-loop`, `run-loop`, `block-loop`, `attack` (overhead right swing), `hit` (flinch),
    `death` (crumple). New `assets/models/weapons/wpn_sword_iron.glb` (~176 tris) hangs off the
    right-hand bone via a `BoneAttachment3D` (`PlayerFactory.AttachWeaponVisual`, bone found by
    name scan) so it follows every clip — purely cosmetic, hit timing stays with
    `MeleeWeaponComponent`. New `src/Animation/CharacterAnimationComponent.cs` drives the
    `AnimationPlayer` from existing state with **no new gameplay wiring**: `AttackPerformedEvent`
    → attack one-shot, `EntityDamagedEvent` → hit flinch (suppressed while blocking),
    `StatsComponent.IsAlive` false → latched death (unlatches on respawn),
    `CombatComponent.IsBlocking` → block loop, else horizontal velocity picks run/idle with a
    0.15 s crossblend. Clip names resolve by prefix so the importer's `-loop` handling can't
    break it, and any humanoid shipping those clip names (the 30F enemy sets) reuses the
    component unchanged. Build + 313 tests + `--import` green; verified in-engine live — the
    maintainer fought goblins with the rigged character during the run, no errors.
- [x] **30D — Core enemy + key-NPC model set** `[P]` ✅
  - **Goal:** the slice cast named in the Phase 30 header (core enemies, key NPCs,
    the boss) has real meshes, not the goblin-only placeholder.
  - **Tasks:** built in Blender via the Blender MCP — goblin model (+ one variant),
    the Iron King boss model, and the key Ember Crown NPCs from Phase 27 (vendor,
    innkeeper, guild rep) — each matched to `ART_STYLE.md` (30A); export to glTF.
  - **Done when:** each listed actor has a distinct, importable mesh/texture set.
  - **Done:** six organic low-poly models (the 30B skin-modifier technique, parameterized by
    height/bulk/head-scale/hunch) authored via the Blender MCP and exported: **goblin** (1.1 m,
    hunched, big-headed, mossy skin + loincloth, ~1.3k tris) and **goblin brute** variant (1.45 m,
    bulky) under `assets/models/creatures/`, the **Iron King** (2.4 m armored colossus — painted
    sabatons/faulds/gauntlets plus fitted pauldrons-with-spikes, a crown band sunk onto the skull,
    inset ember eyes, an embedded chest ember-core and girdle gem, ~1.5k tris; rebuilt once after
    the maintainer flagged hovering pieces — armor is now placed against measured mesh landmarks),
    and **vendor / innkeeper / guild rep** NPCs (distinct tunic/apron/robe palettes + hair) under
    `assets/models/characters/`. In-engine wiring where placeholders lived: `EnemyFactory` and
    `BossFactory` instantiate their models as "Mesh" (capsule fallbacks kept); `BossController`'s
    phase/telegraph glow generalized to claim the model's first **emissive surface** (the ember
    core) instead of requiring a capsule MaterialOverride; and `HitReactionComponent.FindMesh` now
    prefers the conventional `BodyMesh`/`Mesh` root — fixing the player hit-recoil that silently
    broke when 30B nested meshes under a skeleton. NPC meshes get placed when the town actors
    exist (30H dressing); goblin/boss animation sets are 30F. Build + 313 tests + `--import` +
    boot-to-menu run green.
- [x] **30E — Spell-casting animations + cast VFX by school** `[P]` ✅
  - **Done when:** casting plays animations and school-tinted VFX matched to
    `SpellSchools`.
  - **Done:** the player rig gained two clips (re-imported into Blender via the MCP, authored,
    re-exported — the glb now ships 8): **`cast`** (a left-palm thrust with chest turn, 12f
    one-shot) and **`channel-loop`** (a sustained two-hand reach with tremble). A stray
    `Icosphere` that had slipped into the 30C export was removed. `CharacterAnimationComponent`
    now subscribes to `SpellCastEvent`: an instant/charged release plays the cast one-shot **and
    pops a `SpellFlash` at the casting hand** (left-hand bone world pose via the skeleton, chest
    fallback) tinted `SpellSchools.Color(spell.School)` — the existing detonate flash reused as
    cast VFX, so every school's cast reads in its colour with no new VFX system. While
    `IsCharging`/`IsChanneling` the channel-loop pose wins the state pick (above block), and a
    channel's per-tick cast events skip the one-shot/flash spam. Build + 313 tests + `--import`
    green; in-engine run clean with the maintainer playing live. Real per-school particle VFX
    remain 30I's status/impact library pass.
- [x] **30F — Core enemy animation set (goblin + Iron King)** `[P]` ✅
  - **Done when:** locomotion/attack/hit/death sets (driving 30D's meshes) drive
    the existing AI/combat states for the slice cast.
  - **Done:** the goblin and Iron King glbs are now **rigged and animated** via a parameterized
    Blender-MCP pass (the 30C 17-bone humanoid armature scaled to each body's height/bulk/hunch,
    automatic weights) with five clips each baked through NLA tracks: `idle-loop`, `run-loop`,
    `attack` (goblin = wild right swipe; Iron King = two-hand overhead slam), `hit`, `death`.
    Bone-heat weighting failed wholesale on the Iron King's disjoint armor islands (all 1980
    verts unweighted) — fixed with **rigid nearest-bone binding** (point-to-bone-segment scan),
    which suits plate armor anyway. Both factories add the 30C `CharacterAnimationComponent`
    (`BodyMeshPath = "Mesh"`) — zero new gameplay wiring, since enemies already publish
    `AttackPerformedEvent` through the same `MeleeWeaponComponent`, take `EntityDamagedEvent`,
    and keep a death timer before `QueueFree` that gives the death clip room to play. Build +
    313 tests + `--import` green; verified in-engine live — the maintainer fought animated
    goblins and quick-saved repeatedly, no new errors (WASAPI audio-device noise pre-exists).
- [x] **30G — Third-person body for cutscenes/reflections** `[P]` ✅
  - **Done when:** a TP body exists for the Phase 43 cutscenes and corruption
    appearance (23F) hangs off it.
  - **Done:** satisfied by 30B/30C with no additional work — the rigged
    `chr_player_base.glb` **is** the third-person body (always visible under the TP camera,
    full clip set for Phase 43 to sequence), and `CorruptionAppearanceController` already
    hangs off it per-surface (30B): skin-only ember veins + whole-body ashing per tier.
    Recorded here so the checkbox reflects reality rather than re-doing the work.
- [x] **30H — World/environment model set for the Ember Crown slice** `[P]` ✅
  - **Goal:** the Phase 27 Ember Crown slice has real dressing, not greybox.
  - **Tasks:** built in Blender via the Blender MCP — town-hub building kit (inn,
    guild presence, vendor stalls, crafting stations, a housing-plot exterior) +
    wilds POI dressing (rocks, ruins, foliage), matched to `ART_STYLE.md` (30A);
    export to glTF.
  - **Done when:** the Ember Crown walkable slice can be dressed with real meshes
    instead of placeholder primitives.
  - **Done:** a 10-piece environment kit built via the Blender MCP (60–360 tris each, palette
    materials): `assets/models/architecture/bld_house_a.glb` (timber-framed gabled house fitted
    to the 6×5×8 greybox footprint — door, windows, beams) and `bld_house_b.glb` (the inn:
    stone base, chimney, ember sign, fitted to 8×6×6), plus `props/` — waystone monolith with an
    ember rune band, forge (stone hearth + ember bed + anvil), workbench, alchemy table
    (glowing retort), guild banner, rock cluster, ruin pillar, dead pine. **The town hub is
    dressed**: `town_hub.tscn`'s greybox `BoxMesh` visuals were stripped (all `StaticBody3D`
    colliders, entities, dialogue/schedule/station components untouched) and the kit instanced
    in their places — and the five capsule NPCs (elder, three vendors, innkeeper) now wear the
    **30D character models** (guild-rep robe for the elder, vendor/innkeeper bodies). The wilds
    get an augmentation pass: `wilds_north.tscn` gains scattered pines, rock clusters, and two
    ruin pillars (one tilted) over its existing greybox. `--import` + `--validate` (cell scene
    paths re-resolve) + in-engine streaming run green — the maintainer walked the dressed town
    and wilds live, both cells stream in/out with no errors.
- [x] **30I — Status/impact VFX library + corruption materials** `[P]` ✅
  - **Done when:** status effects + corruption tiers (replacing 23F placeholders)
    use real materials/VFX.
  - **Done:** **status VFX** — new `src/Magic/StatusEffectVfxComponent.cs`, a purely cosmetic
    component driven by the existing `StatusEffectAppliedEvent`/`RemovedEvent`: while a status
    afflicts an actor, a small looping school-tinted `GpuParticles3D` swirl (ring-emitted
    billboarded emissive quads, built in code — no scene asset) hangs on the body, so burning
    reads orange, chill ice-blue, regrowth green at a glance; on removal it stops emitting and
    fades out. Wired onto the player, goblin, Iron King, Ashen Acolyte and the training dummy.
    Impact/melee VFX already existed (`ImpactEffect`, Phase 29). **Corruption materials** — the
    23F linear-lerp placeholder is replaced with the ART_STYLE §2.2 **per-tier arc** in
    `CorruptionAppearanceController`: Touched = faint violet skin veining + violet emissive ·
    Marked = ash-grey skin patches + dim ember · Ashbound = charred skin + rising ember glow ·
    Embers = banked-coal skin + bright ember; clothing/gear only gathers ash, skin carries the
    tint + emissive. Also this sub-phase: CLAUDE.md §2 gains the maintainer's **Blender scene
    hygiene rule** (never leave models stacked at origin — lay assets out side by side so
    they're reviewable in the viewport). Build + 313 tests + `--validate` (exit 0) green;
    in-engine run clean with the maintainer playing. Final particle/material art is Phase 53.
    **Phase 30 (Animation, Models & Visual Identity) is complete (30A–30I).**
- [x] **30J — Maintainer art revision: poly bands, creature redesign, full greybox sweep** `[P]` ✅
  - **Goal (maintainer-directed, 2026-07-02):** (1) new triangle bands — player/key bosses
    ~1500, enemies/NPCs ~800, buildings/hero props 550–800, heavily instanced world props
    exempt ("looks good" is the bar); (2) enemies must read as **fantasy creatures, not
    humanoids**, goblins at ~2/3 player height; (3) **zero greyboxes** left anywhere in the
    world; (4) all existing models deep-upgraded to the bands.
  - **Done:** `ART_STYLE.md` §3 rewritten to the new bands. **Creatures** — goblin (1.12 m)
    and brute (1.4 m, horned) rebuilt as hunched, long-armed, knuckle-dragging creatures with
    snouts, ears/horns, tails, bone-pale back spikes, claws and digitigrade legs (~730–750
    tris), rigged (18 bones incl. tail) with creature clips (tail-wag idle, scamper, lunging
    double-claw attack, flinch, sideways-crumple death). **Player** rebuilt at exactly 1500
    tris and re-rigged with all 8 clips; **Iron King** rebuilt at 1500 with extra armor
    (knee plates, chimney-capped crown) and re-rigged. A mid-session player-deform bug the
    maintainer caught ("glitched and stretched") was root-caused to Blender's bone-heat
    solve silently producing garbage weights — all rigs now use **deterministic
    nearest-two-bone inverse-distance binding** (rigid snap when one bone clearly owns a
    vert), pose-verified in the viewport before export. **NPCs** rebuilt at 800 tris each.
    **New: Ashen Acolyte model** (752 tris, hooded robe, ember face-void/belt, rigged with
    cast clip) wired into its factory + `CharacterAnimationComponent` — no capsule enemies
    remain. **Env kit upgraded** into band (houses gain porches/ridge beams/side windows/
    chimney caps, waystone 12-facet + base stones + second rune ring, stations gain bellows/
    tools/stools/vials/shelves, banner gains finial/fray). **Greybox sweep** — new models
    (`prp_ruin_wall`, `prp_arena_wall` crenellated, `prp_brazier`, `prp_glacier`,
    `prp_training_dummy`, `prp_cache_chest`, `prp_tome_stand`, `prp_crate`, `prp_tent`,
    `prp_campfire`, `prp_relic`) replace every remaining primitive visual: wilds_north
    (ruin wall, rocks, fallen pillar, crate), wilds_west (rocks, tents, campfire + pines),
    arena (3 crenellated walls + challenge brazier), frostfang glacier cell (two faceted ice
    masses), town relic, and the bootstrap's training dummy/supply cache/Ashen Tome (all
    with fallbacks; colliders and gameplay untouched; ground planes remain terrain, item
    pickups remain glowing markers). Build + 313 tests + `--import` + `--validate` (exit 0)
    green; in-engine live run clean — the maintainer fought the new creature goblins and
    walked the dressed camps during verification.
- [x] **30K — Maintainer design pivot: first-person gameplay** `[P/F]` ✅
  - **Goal (maintainer-directed, 2026-07-02):** main gameplay is **first-person**; the
    third-person body/rig/clips are retained for cutscenes.
  - **Done:** the camera pivot moved to eye height (1.62 m) with the camera riding it
    directly (near plane 0.08); mouse-look mechanics unchanged (yaw on body, pitch on
    pivot), so aim/interact/lock-on all work as before. The player's own body renders
    **shadows-only** in first person — except the held-weapon subtree, so sword swings
    still sweep through view — and the full third-person rig + 8 clips stay intact.
    The seam is `PlayerController.SetFirstPerson(bool)`: `false` swings the camera back
    to the old orbit (offsets kept as `PlayerFactory.ThirdPerson*`) and re-shows the
    body — the Phase 43 cutscene director's hook. Identity docs updated (CLAUDE.md,
    README, ARCHITECTURE, LORE, ROADMAP: "first-person … third-person body retained for
    cutscenes"). Build + 313 tests green; verified live in-engine — the maintainer
    played first-person combat during the run, no errors.
- [x] **30L — Maintainer playability pass: lamps, FP arms, loot chests, visibility** `[P/F]` ✅
  - **Goal (maintainer-directed, 2026-07-02):** finish the playability batch cut short by a
    session limit — village/map lighting, first-person hands, a roomier inventory, and
    enemies readable through the fog (without losing the fog's mood).
  - **Done:** six `prp_lamp_post` instances placed around the town hub square (navmesh-carving
    colliders + warm `OmniLight3D` at the lantern, shadows off), plus firelight on the
    wilds-west campfire and the arena challenge brazier — the world's first point lights.
    First-person viewmodel (`FirstPersonArmsComponent`): `fp_arm.glb` pair riding the camera,
    right hand carrying the sword model, all-procedural motion (speed-driven walk bob,
    `AttackPerformedEvent` slash arc alternating with combo index, raised guard while
    blocking); the skeleton-held sword is now shadows-only in FP so it isn't doubled.
    Inventory/character screen fills the view with a 70 px gutter (full-rect anchors, scroll
    expands) instead of the old 440 px column. Fog weather density 0.04 → 0.025 so enemies
    resolve at combat range while the ashen veil (haze floor untouched) keeps the mood —
    pairs with 30J's emissive goblin eyes. Also landed from the cut-short session:
    `ContainerLootComponent` (E loots the supply cache's inventory into the player's, seeded
    potions + gold on fresh spawns). Build + 313 tests green; boot + `ContentValidator` OK
    in-engine; all three edited cells headless-instantiated clean (6+1+1 lights verified).
    The FP-arm motion itself is reviewed against the Godot 4.7 C# API — maintainer playtest
    pending.
- [x] **30M — Maintainer design pivot: hybrid first/third person** `[P/F]` ✅
  - **Goal (maintainer-directed, 2026-08-03):** the game is **both**. 30K's retained
    third-person rig becomes a real playable mode the player swaps to at any time from the
    settings menu, not just a cutscene pose.
  - **Done:** the settings toggle, the live `SettingsAppliedEvent` path and the body
    shadows-only swap all already existed from 30K — what was missing was everything that
    made third person *playable*. New `CameraRigMath` (pure, 14 tests) owns the three:
    an eased mode blend (0.18 s, snapped on initialize so a save resumed in TP opens there),
    the wall spring (sphere `CastMotion` pivot→camera, **instant pull-in / eased push-out**
    at 6 m/s, so the camera is never inside geometry — the gap 30K's own doc comment flagged),
    and the aim direction. `CameraRestPosition` now returns the *live* blended+sprung offset,
    so `CameraShake` follows it for free. Third person is over-the-shoulder: a 0.6 m shoulder
    offset joins the existing back/rise, body yaw still equals camera yaw in both modes, so
    combat, lock-on, dodge and melee reach needed no changes at all. **Aim parity:** the
    interact raycast now starts at the camera (not the head) with its reach measured from the
    *character*, so TP can never reach further than FP; spells aim along a new `AimPoint` node
    re-aimed each frame at the crosshair's convergence point. Both are exact no-ops in first
    person — the invariant the tests pin. `V` (`GameInput.ToggleCamera`) flips the persisted
    setting rather than a local flag, so the key and the panel can never disagree. Build +
    720 tests + `--validate` (exit 0) green; verified live in-engine — the maintainer fought
    goblins and looted during the `--play` runs, no errors.
  - **Follow-up (maintainer-requested, same session):** camera **distance** (2–6 m slider) and
    **shoulder side** (right / left / centred dropdown) exposed in the settings panel's Gameplay
    section, both applying live so they can be judged while being dragged. `ThirdPersonRest` reads
    them off `_settings.Current` per frame; `Settings.ShoulderOffset()` delegates to
    `CameraRigMath.ShoulderOffset` so the mapping is pinned by tests (including a nonsense side id
    falling back to the right shoulder rather than throwing). `SetFirstPerson` gained a
    no-change early-out, because the panel re-applies on every drag frame and the body-mesh
    shadow walk is not free. 724 tests, `--validate` exit 0, 4 clean `--play` runs. **FOV** (60–110°)
    followed in the Graphics section — applied by `PlayerController`, not `SettingsService`, since
    it belongs to the player's camera rather than the engine. The catch worth knowing:
    `FirstPersonArmsComponent` scales the arms by the ratio of the world and viewmodel FOV
    half-angle tangents, so it now re-derives that scale whenever the camera's FOV changes —
    otherwise the slider silently undoes the whole trick and the hands read undersized.
  - **Code-only deep debug pass (maintainer-requested, 2026-08-04):** a static audit of all 345
    source files. Six issues found and fixed:
    1. **Blocking menus suspended the player but not the world.** `GetTree().Paused` was set only
       for `GameState.Paused`; every modal `UiPanel` merely set `UiState.MenuOpen`, whose only
       gameplay consumer froze the *player* — and `DropHeldInput` drops the guard and cancels casts.
       So reading the inventory or talking to an NPC mid-fight left a frozen, un-blocking,
       un-dodging player taking free hits with DoTs still ticking. Fixed at the root:
       `GameManager.RefreshPause()` is now the single writer of the paused flag, answering
       `State == Paused || UiState.WorldPaused`, driven by a new `UiState.Changed` event.
       `UiState.Open(owner, pausesWorld: true)` is the default; the boss intro, the opening
       narration and the dev console pass `false` because the world must keep playing under them.
       `UiPanel` gained `ProcessMode.Always` or a modal panel would freeze itself on open.
       *(The per-system `MenuOpen` check was the tempting fix and is the one that had already
       failed — only `HitStopDirector` and `CompanionRoster` ever remembered it.)*
    2. **Far-LOD enemies barely advanced their wall-clock timers.** The sleep early-out returned
       before `_stateTimer += delta`, so those timers advanced one *frame* per 0.5 s sleep interval —
       a 12 s provoke memory ran for ~6 real minutes and the enemy never stood down. Slept time is
       now banked and applied as `wall`; movement and turn slew still use `delta`, since stepping a
       sleeping actor by half a second of motion would teleport it.
    3. **Wounded enemies ping-ponged Combat↔Retreat forever.** Nothing heals them, so the re-engage
       ending a retreat tripped the same `RetreatHealthFraction` check that started it. New
       `AIProfileResource.RetreatCooldown` (default 10 s, `0` restores the old behaviour) gates it.
    4. **`ServiceLocator` could hand out a freed node.** Six services register without ever
       unregistering and 11 of 25 read sites never checked `IsInstanceValid` — latent today only
       because `_sandboxBuilt` allows one world build per process. Fixed in the one read path
       (`Resolve`) rather than at 11 call sites: a freed registrant is dropped and reported.
    5. **Gamepad had no gameplay bindings at all** — it could open every menu and not walk out of
       the first room. Added sticks for move/look, RT/LT attack/guard, A/B jump/dodge, L3/R3
       sprint/lock-on, RB/LB cast/cycle, D-Left camera swap. Right-stick look is polled per frame
       (a stick is a held deflection, not a delta) through the new pure `SettingsMath.StickLookStep`
       — squared response past a 0.15 deadzone, framerate-independent, 8 tests.
    6. **README documented a 9-slot hotbar**; there are 5. Docs corrected (maintainer kept 5).
    740 tests, `--validate` exit 0, 6 clean `--play` runs.
  - **Trap paid on the way (now a CLAUDE.md §7 gotcha):** the rig was first hoisted *above*
    `PlayerController`'s not-playing guard so the camera would keep settling during a load. That
    dereferences the injected camera/pivot/aim nodes while a teardown is freeing them, and it
    produced an intermittent `gchandle.is_released` fatal on exit — **2 runs in 10** against
    **0 in 9** on the unmodified tree, with nothing in the gameplay log to point at it. Moving it
    back inside the guard (still above the menu-open return, so it settles with the inventory up)
    took it to 0 in 8.

---
