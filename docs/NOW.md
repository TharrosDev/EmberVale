# NOW — where the project is

**This is the single source of project state. Rewrite it; do not append to it.**

## Where we are

- **Stage C.** Economy (38), mounts/traversal (39), map intelligence (39.5), quest authoring (41)
  and divine shrines (41.5A–C) are closed. **Phases 40 and 40.5 are struck, not deferred:** this game
  has no survival needs, durability, hunger, encumbrance, puzzle, trap, or vault system. A cut system
  leaves no stub.
- **42A ✅ CLOSED (2026-08-30).** A guild is a `FactionResource` with ranks — no `GuildResource`, no
  `GuildComponent`, no guild save record. `GuildRules` derives the whole flag vocabulary from the
  faction id and resolves state in one ordered function; the character screen grew a **Guilds** tab.
  **Membership persists for free** — `StoryFlagsComponent` was already the authority.
- **42B ✅ CLOSED (2026-08-31).** **All five orders now have a home, a door and three officers
  standing at it.** A hub is a `MapLocationResource` and an officer is a placed `Entity`, so
  `FactionResource` grew five id fields (`HubLocationId` + a four-role roster) and nothing else was
  invented. Fourteen new officers, five hubs, five leader routines, five map pins.
  - **The Wardens' Watch** (Dawnwardens, Crossway Post), **the Ledger House** (Iron Syndicate,
    Hollowreach), **the Deadfall Lodge** (Ash Hunters, northern wilds), **the Annexe** (Veiled
    Archive, Embermarket) and **the Undercroft** (Emberbound, in the Emberdeep pit). Five different
    kinds of structure, one region, no hall cloned.
  - **`DialogueCondition` gained `GuildRankAtLeast` (14) and `GuildNotMember` (15)**, resolved
    through `GuildRules`, so a membership-aware greeting never hand-writes a derived guild flag.
  - The Iron Syndicate's contact is **Wren Halloway**, who was already at the Crossway hiring post.
    A roster entry may name an actor who already exists; it is not a licence to rewrite them.
- **The world-geography overhaul (2026-08-29) ✅ CLOSED — out of band, maintainer-directed.** The
  ground is one continuous heightfield per region with real elevation, real collision and real
  navigation, and there is no seam fade anywhere. Ember Crown: **16 cells, 330 × 440 m**; Frostfang
  Reach: **10 cells, 340 × 380 m, x 260..600**, disjoint. ⚠️ **This is still NOT Phase 44** — that
  phase blocks out all five realms and is ahead of us.
- **The world-quality pass (2026-08-30) ✅ CLOSED — out of band.** Terrain materials, region
  atmosphere, water, off-route safety and the region authoring framework. A future region starts from
  `tools/region_spec_template.py`, and one command says whether it is healthy.
- **The runtime debugging pass (2026-08-30) ✅ CLOSED — out of band.** Every route into the world now
  goes through one loading gate that holds until the region is whole *and* the physics server reports
  collision under the player.
- **The architecture-kit pass (2026-09-01) ✅ CLOSED — out of band.** The useful existing cottage,
  inn, blacksmith, houses and first modular kit were retained; 21 compatible shared modules and ten
  structurally distinct authored prefabs now cover cottages, farms, shops, workshops, townhouses,
  inns, longhouses and ruins. Five live settlements use the new forms. See
  `docs/3D_ASSETS.md` → ARCHITECTURE.
- **The environment/props pass (2026-09-02) ✅ CLOSED — out of band.** The vegetation system was
  judged good and **kept**; what was wrong was underneath it. Every prop GLB embedded its own copy
  of a shared texture *and* the importer extracted a second copy beside it, so `assets/models/props`
  was **84 MiB and is now 49 MiB** with zero embedded images. 138 prop materials sat at the pack's
  0.4 metallic with no metallic map — wood, hay, bark, leaves and fire — which `ART_STYLE.md`
  forbids; real metal now goes up and everything else down. The realm's entire stone cover was one
  pebble at 210/100 m²; it is four species at 191. Eleven new rock and ice assets plus a rebuilt
  brazier fill the gap no vendored bundle covers, and `prp_glacier` — one mesh instanced fifteen
  times across three cells — is retired.
  ⚠️ **The world visual gate is nondeterministic and its result is advisory until the capture clock
  is pinned.** It renders on a GPU-less runner under xvfb, which is not the renderer its baselines
  were captured on — it failed thirteen Frostfang frames on ground shading alone and the step-up
  probe aborted with SIGABRT *after* printing PASS, neither about the repository. Run
  `python tools/world_quality_check.py` locally, where a frame can be looked at.
- **The 3D pipeline consolidation (2026-09-03) ✅ CLOSED — out of band.** No model changed and no
  visual changed; what changed is that the pipeline is now knowable. `docs/3D_ASSETS.md` is the one
  contract (it absorbed four documents and the pipeline half of `ASSET_POLICY.md`, and resolved five
  contradictions between them); `python tools/assets.py` is the one entry point —
  `status`/`validate`/`adopt`/`audit`/`build`, with the two orderings that were previously source
  comments now encoded. `assets/models/manifest.json` is derived from the files on disk and names
  the five rig families that were always there but never written down: **33 HUMANOID** retargeted to
  `GeneralSkeleton`, **15 QUADRUPED** that keep their own rigs and clips, **3 VIEWMODEL**,
  **43 ARCHITECTURE**, **99 STATIC PROP**. `src/Core/ModelAssets.cs` holds the paths gameplay names,
  and a new `ContentValidator` arm fails `--validate` when one stops resolving or drifts out of the
  manifest. `reports/3d/` is now `reports/3d/archive/` and is **not required reading**.

- **The world-generation replacement (2026-09-03) ✅ CLOSED — out of band.** The ground stopped being
  two octaves of value noise with hand-authored mounds on it. `WorldHeightfield` is now a staged
  generator — warped continentalness, mountain systems, erosion and valley shaping, rolling relief,
  micro detail — carved by a cached D8 drainage solve, with the authored landforms, roads and yards
  stamped over it in that fixed order. **Ember Crown gains 43 m of relief and Frostfang 98 m**, from
  ONE generator and two art-directed profiles in `data/world_gen/`. Biome borders are ecotones driven
  by generated moisture, wetness and alpine weight rather than per-cell rectangles; scatter layers
  can declare a climate band, a riparian affinity and a curvature limit; rivers are real and come
  under the existing non-swimming safety contract.
  - ⚠️ **THE LESSON, AND IT COST THREE SEPARATE DEBUGGING ROUNDS: an authored TARGET height must be
    an offset, not a world Y.** A ground area's elevation, a levelling landform's height and a
    waterline were all absolute, all authored against a field that never left −1.5..1.5 m, and each
    broke differently once the ground moved — a pad became a step, a 12 m shelf became 16.5 m and
    failed the walk limit, and a fen waterline flooded its own shoreline and the next cell along.
    All three now carry `ElevationMode`, default relative. `docs/WORLD_AUTHORING.md` §3 and §5.
  - ⚠️ **Authored circulation calms the generator around itself** over an art-directable radius,
    which is why 142 authored routes stayed walkable. The continental tilt is deliberately NOT
    calmed, so a town still sits on a hillside rather than in a flat disc.
  - Region load costs more and frame time does not: the macro fields are cached on a grid derived
    from their own finest octave, and every cell's vertices, normals and collision faces are built on
    worker threads under an epoch stamp.

- **The infrastructure overhaul (2026-09-04) ✅ CLOSED — out of band, maintainer-directed.** No
  gameplay changed. What changed is that the application layer now has owners, and a session can
  end.
  - **`GameBootstrap` is gone** — 1503 lines and twenty-three responsibilities, replaced by
    `ApplicationRoot`, `GameShellController`, `SessionLifecycleCoordinator`, `GameSession`,
    `WorldHost`/`WorldSessionDirector`, `UICompositionRoot`, `PlayerHost`, `LoadingCoordinator`,
    `DeveloperToolsHost` and `SaveHeaderComposer`. `GameSession.Build()` stays ONE ordered list
    because the order always was load-bearing.
  - ⚠️ **QUITTING TO THE TITLE NO LONGER RELOADS THE SCENE.** It used to, and the pause menu's own
    comment said why: there was no way to dismantle a world in place, several services registered
    and never unregistered, and a freed registrant is a hard `gchandle.is_released` crash.
    `DestroySession()` removes the session node synchronously, so every `_ExitTree` has run before
    it returns. **A second New Game now starts in the same process, which it could not before.**
  - **Services have lifetimes, and a lifetime is a place in the tree.** `ServiceScope` holds one
    lifetime's services (Application / Session / World) and `ServiceScope.For` finds a node's owner
    by walking its ancestors, so *where a service is parented decides how long it may live*.
    `RegisterOwned` ties the registration to the node's own `TreeExiting`, which deleted all 21
    hand-written `Unregister` calls. **A stale registration has nowhere to survive**; the locator's
    silent freed-object drop became an `Invariant` violation.
  - **`PlayerController` (729 lines) is six components** — `PlayerPhysicsQueries`,
    `PlayerCameraRig`, `PlayerLookInput`, `InteractionSensor`, `AimController`,
    `PlayerInputRouter` — and is deleted. The router keeps one `_PhysicsProcess` because the order
    is load-bearing and Godot would otherwise make it a consequence of child order.
  - **`EnemyAIComponent` 1229 → 712 lines**, with `EnemySenses`, `EnemyCasterTactics` and a
    `AiNavigator` **shared with the companion brain** — which had its own drifted copy that ran a
    navigation-server query every frame where the enemy paced it to 4 Hz. `AIProfileResource` and
    all 16 `.tres` are unchanged: the AI was already data-driven and that was never the problem.
  - **The shipping build carries no tooling.** `EmbervaleTooling` (false under `ExportRelease`)
    excludes the Godot-MCP addon, its two NuGet packages and the `*Shots` harnesses — and with them
    the assembly-wide `CS0618` suppression. `TreatWarningsAsErrors` is now on unconditionally.
  - **New gate: `godot --headless -- --lifecycle`.** Three New Game → Playing → save → destroy →
    Load → Playing → destroy round trips, asserting after every teardown that no session, service
    registration, event subscription, `ISaveable` or unfreed node survives.
  - ⚠️ **THE LESSON, AND THE PROBE FOUND IT: NEW GAME COULD NOT REACH PLAYING AT ALL.** A region's
    `SpawnPoint.Y` is an offset, not a world Y (invariant 23, learned yet again). Both authored
    spawns are `y = 1.2`, the capsule's resting height from when every floor's top face was y = 0;
    the generator put the ground under Ember Crown's spawn at **−1.81 m**, so the player hung
    **3.01 m** in the air — one centimetre outside the loading gate's 3 m ground probe.
    `SafeLanding` could not catch it, because it lifts and never lowers and the player was *above*
    the ground. `tools/debug_pass_regressions.gd` had been failing this check on `main` and nothing
    was reading it: no headless route had ever taken the New Game path, because `--play` loads a
    save. `WorldSessionDirector.RegionSpawn` now reads the authored Y as the clearance it meant.

- **The combat, animation, camera and player-presentation overhaul (2026-09-05) - CLOSED, out of
  band, maintainer-directed.** Ten stages on one branch. The through-line: **gameplay owned time and
  animation only watched**, and everything else followed from that.
  - **ONE AUTHORITATIVE TIMELINE.** `MeleeWeaponComponent`'s `double` stopwatch is deleted.
    `ActionDefinitionResource` authors gameplay windows as **fractions of the action's own
    duration**; `CharacterActionComponent` reads its progress off the AnimationTree's playback
    position. A `Duration` of 0 means the clip decides; a positive one warps the clip to fit. Proved
    on `chr_player_base`: a 1.533 s clip warped to 0.90 s runs in 0.933 s and to 0.35 s in 0.383 s,
    with the hit inside the window the animation shows each time.
  - **A FULL-BODY SHARED LIBRARY.** `anim_meshy.res` - 25 clips, full body, named for Embervale's own
    gameplay slots. The old `anim_library.res` **cannot** do this: its extractor strips every position
    track and all eight leg bones, because its Rigify source and the Quaternius bodies disagree about
    where the hips sit. Both are kept and both are needed - the old one still owns `ride`, `sitting`,
    `interact` and `swim`. Cost 72 credits: `meshy_animate` ran against an EXISTING rig task, so no
    character was regenerated and no rig re-paid for. `strip_anim_glb.py` took the set from 190 MB to
    1.4 MB.
  - **A REAL ANIMATIONTREE.** Blend space over signed speed, state machine, and a bone-masked
    upper-body layer that retires 39B's mounted-attack workaround (the rider used to stand up inside
    the horse, so the swing had its animation removed entirely).
  - **TRUE FIRST PERSON.** The camera rides the body's own head bone and the body stays visible.
    `FirstPersonArmsComponent` and all three `fp_arm_*.glb` are deleted, along with the VIEWMODEL rig
    family. One skeleton, one action state, one set of equipment.
  - **ONE SOCKET CONTRACT.** `EquipmentSockets` + `EquipmentPresentationComponent` replace five
    attachment implementations. Equipping a weapon now changes what is in the hand, which it never
    did.
  - **RANGED EXISTS.** `EquipmentSlot.Ammo` appended, a Hunting Bow and Arrows stocked at the smith,
    and an `Arrow` that sub-steps its flight - at 42 m/s it covers 0.7 m a frame against a 0.12 m
    radius, so a single-step projectile passes through bodies and it reads as unreliable hit
    detection.
  - **MAGIC WAITS FOR ITS OWN ANIMATION.** A cast used to fire on key-down, so the bolt was across
    the room before the arm moved.
  - **FEET, AND WARPING.** `FootIkComponent` plants feet on real terrain (on a 12 degree slope they
    sit at 0.121 and 0.194 rather than level). `MotionWarp` closes the last of a committed attack's
    gap, bounded and **swept**, so it can never pass a wall. It is NOT root motion: the Meshy clips
    carry none - a walk's hips travel 0.4 cm in 4.2 s.
  - **THE CAMERA STOPPED FIGHTING ITSELF.** `CameraBlocker` is its own layer, so walls retract the
    camera and people no longer do. `CameraProfile` gives five contexts a shape each, as multipliers
    on the player's own settings. One impulse queue; `CameraShake._restRotation` no longer stale.
  - **POISE IS A MODEL, NOT A DURATION.** `PoiseReaction` resolves flinch/stagger/heavy/knockdown by
    `ReactionClass`; a flinch does not interrupt, and a boss is never knocked down or pushed.
  - WARNING: **THE ARMS-CROSSED BUG WAS PRE-EXISTING AND THIS WORK EXPOSED IT.** Player, Kael and the
    goblin swung both arms through the torso into an X. `chr_player_base` does it on the OLD library
    too: several bodies were generated in an A-pose while the shared clips are authored against the
    profile's T-pose, and `fix_silhouette` was off on every import. It hid while the library only
    supplied block/cast/channel; the moment it drove locomotion it was on screen constantly. Enabled
    on all 31 bodies and 25 animation sources, and gated.
  - WARNING: **`npc_innkeeper.glb` SHIPS ZERO ANIMATIONS**, so Godot made no AnimationPlayer and it
    could never receive the library - Gilda Ironmonger stood in the Embermarket in her bind pose
    since she was placed. A rigged body with no clips now gets a created player. Embermarket:
    16/17 -> 17/17.
  - WARNING: **THE LESSON, AND IT COST FOUR FALSE PASSES: A PROBE THAT CANNOT FAIL IS NOT A GATE.**
    Four separate probes reported PASS while testing nothing - a `Vector3?` that does not marshal
    aborted a script mid-function; an arrow parented to a null `CurrentScene` was never in the tree;
    bodies instantiated but never added to the tree posed no bones; and a wall test passed because
    the warp it was measuring never fired. Every one was found by adding a **control case** or by
    negative-testing the gate itself. Do that first, not last.
  - WARNING: **A GODOT `Resource` CANNOT BE CONSTRUCTED IN THE PURE SUITE**, and a test that tries
    does not fail - it takes the whole run down and reports 27 passing tests instead of 1892, looking
    green. Pure helpers take primitives; that is why.
- **The world-production overhaul (2026-09-05) ⛔ NOT CLOSED.** The branch
  `codex/world-production-overhaul` now has a deterministic offline bake (`tools/world_bake.py`),
  28 hash-verified prepared artifacts, predictive Near/Mid/Far/Backdrop cell residency, staged
  activation, one collision/navigation contract, one safe-placement service, cell-owned actors and
  abstract persisted world events. Runtime terrain, collision, navigation and biome scatter all read
  the prepared field instead of rebuilding separate versions of the world.
  - **The source/generated authority is mechanical.** `tools/world_bake.py --check` fingerprints the
    region specs, generator profiles, authored resources, scenes, relevant world code and import
    metadata; it reports each stale, missing, unexpected or modified output. The fast quality suite
    runs this gate and is green.
  - **Do not merge this branch yet.** The 2026-09-05 engine suite passed content, transition, mesh,
    scene, map, collision and streaming-stress gates, but failed lifecycle, step-up, regression and
    traversal. Prepared terrain colliders currently report a missing `WorldStatic` layer at runtime;
    the Salt Steps player falls instead of climbing; failed-cell settlement semantics regressed; and
    three authored traversal probes fail (Wilds West capsule snag plus Ash Roost and Aerie Ascent NPC
    paths). The engine also reports the existing two-edge navigation raster warning. Per maintainer
    direction, testing stopped after this repeated failure instead of beginning another repair loop.

- **NEXT: 42C — Dawnwardens recruitment and probation.** The first arc to walk through a door 42B
  built: join/refuse dialogue plus a Defend/Reach probation pair, and rank one earned.

Read [`docs/WORLD_AUTHORING.md`](WORLD_AUTHORING.md) before touching a cell. `data/regions/*.tres`
is **generated** — edit `tools/region_spec_<region>.py` and run `python tools/gen_regions.py`.

⚠️ **TWO PASSES LANDED ON THE SAME DAY AND THEY DID NOT VERIFY THE SAME THINGS.** The character overhaul below is closed and its gates are green. The world-production overhaul that merged alongside it is **not** closed and four of its gates are red. Neither table supersedes the other; a green character suite does not make the world suite green, and the world failures are not caused by the character work.

## Last verified (2026-09-05 - the combat/animation/camera overhaul)

| Check | Result |
| --- | --- |
| Build | `dotnet build Embervale.sln` - **0 warnings, 0 errors, `TreatWarningsAsErrors=true`** |
| Shipping build | `dotnet build Embervale.csproj -c ExportRelease` - 0 warnings, 0 errors |
| Shipping contents | `python tools/check_shipping_assembly.py` - PASS, 2329 KiB, no dev tooling |
| Tests | `dotnet test tests/Embervale.Tests` - **1916 passing** (1807 before this pass + 109) |
| `--validate` | exit 0 - new arms over attack windows (weapons AND boss phases) and both animation libraries, each negative-tested |
| `--lifecycle` | exit 0 - 0 surviving services, 0 subscriptions, 0 orphans, 0 invariant violations |
| `debug_pass_regressions.gd` | **44/44** |
| `python tools/assets.py validate` | all gates, 218 models |
| `--state` | 2 regions, 26 cells, 75 map locations - unchanged |
| `--play` | boots to Playing, **0 errors**, 0 invariant violations |

**Nine new engine gates**, all registered in `tools/world_quality_check.py`:
## Last verified (2026-09-05 — world-production branch, required gates red)

| Check | Result |
| --- | --- |
| Build | `dotnet build Embervale.sln` — **0 warnings, 0 errors, `TreatWarningsAsErrors=true`** |
| Shipping build | `dotnet build Embervale.csproj --no-restore -p:EmbervaleTooling=false` — 0 warnings, 0 errors |
| Shipping contents | `python tools/check_shipping_assembly.py` — PASS; no MCP addon, no `*Shots`, no `ReproHarness` |
| Tests | `dotnet test tests/Embervale.Tests -p:EmbervaleTooling=false` — **1818 passing** |
| World bake | **PASS** — 28 artifacts; `tools/world_bake.py --check` reports source `403b67636e53` current |
| Fast quality | **PASS** — `artifacts/quality/20260905T004533Z/summary.json` |
| `--validate` | exit 0 |
| Engine quality | **FAIL** — lifecycle, step-up, regression and traversal; `artifacts/quality/20260905T004618Z/summary.json` |
| Streaming stress | **PASS** — rapid traversal, boundary oscillation, readiness and unload soak |
| `--lifecycle` | **FAIL** — Playing reached with an unsettled streamer in all three New Game cycles |
| `gen_regions.py --check` | clean |
| `debug_pass_regressions.gd` | **FAIL** — failed-cell reporting/settlement semantics regressed |
| `stepup_probe.gd` | **FAIL** — Salt Steps falls below terrain |
| `world_traversal_probe.gd` | **FAIL** — one collision snag and two missing NPC paths |

⚠️ **GODOTMCP was not available for this pass** — the editor/relay probe could not establish a live
connection, so no `mcp__ai-game-developer__*` tools registered. Verification used the shell spine.
**No live-editor or human visual sign-off was made.** Prepared `.scn` world artifacts did change, so
that sign-off remains required before this branch can close.

| Probe | Proves |
| --- | --- |
| `melee_probe.gd` | the hit opens inside its own active window, once per swing, and the authored chain advances by id |
| `action_clip_probe.gd` | on a real rigged body the CLIP is the clock, warped or natural |
| `equipment_socket_probe.gd` | all six humanoid sockets resolve on all 31 rigs, and a hung weapon lands on the hand bone |
| `anim_library_probe.gd` | the library is whole, keeps its legs, moves a real body - and no rig crosses its arms |
| `locomotion_tree_probe.gd` | the blend space interpolates, the upper-body mask holds, the action clock survives the tree |
| `view_switch_probe.gd` | a first/third swap preserves the action, combo and equipment |
| `grounding_probe.gd` | feet meet a 12 degree slope; a warp closes 1.585 m of its 1.6 m budget and stops at a wall |
| `ranged_probe.gd` | arrows leave on the release frame, hit once, spare allies, and do not tunnel |
| `camera_probe.gd` | a wall retracts the camera and restores it; a companion does not |

WARNING: **GODOTMCP was available at the start of this pass and not at the end** - the editor was
closed partway through. The visual checks that mattered were made with the repo's own
`--enemy-shots` harness (230 frames), which needs no MCP. The arms-crossed fix was confirmed both
numerically and in a render.

WARNING: **There is still no `export_presets.cfg`.** "The shipping build" is proved by
`ExportRelease` compiling clean plus the assembly scan, not by a real export artifact.

## Live invariants

1. **Gameplay state persists, and Load REPLACES live collections.** Save ids are stable primary
   keys; clear before restore, including every false/empty branch. A partial restore is a failed load.
2. **One surface owns each fact.** Never add a second ledger for something already owned elsewhere.
3. **A gate belongs at the choke point, not in the caller.**
4. **All player-facing text uses `Loc.T()` and a `strings.csv` key.**
5. **If the player can go there, map it in the same sub-phase.** A map location's position is the
   transform of its `MapLocationComponent` parent in a cell scene, never a resource coordinate.
6. **Render world changes at eye level, front and back, with people and furniture around them.**
   ⚠️ **And the camera must be raycast onto the real ground** — `world_shots.gd` does it and
   `GuildShots.OnGround` does it, because a literal 1.75 m eye height photographs the inside of a
   hill and the baseline passes.
7. **Before authoring content of an existing kind, read an existing `.tres` header.**
8. **An authored numeric range fails silently at both ends.** Every new range needs a validator arm
   and a negative case in *each* direction.
9. **An event that fires conditionally is not automatically the event that means the thing.**
10. **A cache key is a subscription.** Any newly drawn fact must be part of every cache/signature
    that renders it, not merely an event listener.
11. **A SEAM IS GENERATED, NOT ARITHMETIC.** `tools/gen_regions.py` refuses to write a region whose
    row bands do not tile its extent exactly, and every seam route is authored ONCE as a world point.
12. **A layout constraint written as a coordinate outlives its reason.** Author dependencies as
    offsets and ids, never as absolute points. A schedule uses `ScheduleResource.Origin` and
    cell-local destinations.
13. ⚠️ **TERRAIN MAKES GEOGRAPHY; PROPS ONLY DETAIL IT.** If a shape needs to exist it goes in
    `WorldLandformResource` and the props dress what the terrain already says. ⚠️ **And the GENERATOR
    makes the geography the landforms sit on** — a region without a `WorldGenerationProfileResource`
    has none, and `--validate` fails it. One generator, one profile per realm, never a fork.
23. ⚠️ **AN AUTHORED TARGET HEIGHT IS AN OFFSET, NOT A WORLD Y.** `GroundArea.Elevation`, a
    `Landform.Height` with `Flatten` over 0.5, and `WorldWaterResource.SurfaceY` all carry
    `ElevationMode`, default `RelativeToBase`. Absolute is for the rare case where a specific world
    height is genuinely the point. This rule was learned three times in one pass, and the waterline
    was the expensive one: eight metres of generated ground under Hollowreach turned a 0.05 m fen
    surface into a flood across two cells.
25. ⚠️ **A BUILDING IS RIGID; THE GROUND UNDER IT MUST BE LEVEL.** `WorldTerrainConform` is already
    load-time gravity - an authored Y is a clearance and the ground is added back - but it samples a
    node's OWN origin, and a structure's walls inherit that one sample because bending a building to
    a hillside would tilt its walls and split its roof. So a level `GroundArea` is the fix, its
    `SurfaceBlend` has to be high enough to actually be a floor, and two pads over one footprint must
    agree on elevation. ⚠️ **A building on a road cannot be levelled at all** - road beats yard, so
    the pad is suppressed where the road runs and a pad there makes it worse. Move the building.
    `--worldgen` ranks every structure and prints the pad to author; `--validate` fails one over 1 m.
24. ⚠️ **GENERATED WATER WETS YOUR BOOTS; AUTHORED WATER DROWNS YOU.** The generator's depth is
    capped under `DrownDepth`, never appears on a road or a yard, and never draws over a declared
    body. Anything deeper or more dangerous is a `WorldWaterResource` a designer wrote down.
    `--validate` fails a pad, route end, spawn or portal under more than `WadeDepth`.
14. ⚠️ **SOME OF THE WORLD MUST CONTAIN NOTHING.** Eleven of the realm's twenty-six cells carry a
    road, weather, vegetation and landform and no gameplay beat at all. Do not fill them in.
15. ⚠️ **DEEP WATER IS NOT A TRAP BECAUSE IT IS DECLARED, NOT BECAUSE IT IS SHALLOW.** Declare a
    `WorldWaterResource` on the cell; a water mesh authored in a `.tscn` is invisible to all of it.
16. ⚠️ **A SHALLOW HOLE IS A BUG; A DEEP ONE IS A HAZARD.** `--validate` sweeps the lattice as a
    directed graph and fails on ground the player can WALK into and not climb out of.
17. ⚠️ **A SPECIES DECLARES THE GROUND IT STANDS ON.** `MaxSlope` defaults to 0.7; `Clumping` is the
    companion rule, because even spacing is the most recognisable pattern there is.
18. ⚠️ **A GUILD IS A FACTION WITH RANKS, AND ITS FLAGS ARE DERIVED, NEVER AUTHORED.** Membership,
    rank, refusal and departure are `guild.<slug>.*` story flags built by `GuildRules` from the
    faction id. `GuildRules.Resolve` is the only reader and `StoryFlagsComponent` the only writer.
    Ranks are cumulative and **a gap does not promote**; `CanJoin` is where the rejoin policy lives.
    ⚠️ **That extends to dialogue:** a membership-aware line uses `GuildRankAtLeast` /
    `GuildNotMember`, never `HasFlag` with a `guild.*` string.
19. ⚠️ **AN HLOD TIER IS A SILHOUETTE CONTRACT.** The proxy is the same mesh at a fraction of the
    density; cones and boxes keep the mass and throw away the outline.
20. ⚠️ **A CELL'S `agent_*` DIMS ARE DERIVED FROM ITS VOXEL GRID, NEVER COPIED.** `agent_height` and
    `agent_radius` CEIL, `agent_max_climb` FLOORS. `--validate` fails an off-grid dim. The player
    steps 0.5 m and an NPC is pathed up the cell's floored climb, so raised ground taller than that
    is player-only ground.
21. ⚠️ **A LEVELLED PAD IS USUALLY A ROAD, AND A BUILDING PUT ON ONE BLOCKS IT.** `GroundArea`s exist
    where a settlement needed flat ground, which is where its road already runs — the Crossway
    compound is 12 m deep and 7 m of that is `Path_crossway_compound` plus shoulder. 42B put two
    hubs on their pads, `--validate` and the layout gate both passed, and the **traversal probe** was
    the only thing that said four authored routes had no navigation path through them. Author a new
    `Yard` beside the road; do not build on the pad because the pad is flat.
26. ⚠️ **A SERVICE LIVES AS LONG AS THE NODE IT IS PARENTED UNDER, AND NOT ONE FRAME LONGER.**
    `ServiceScope.For` finds a node's owner by walking its ancestors, so moving a service to a
    different host in the tree is the whole of changing its lifetime. Register with
    `ServiceScope.RegisterOwned(this, this)`; never write an `Unregister` — the node's own
    `TreeExiting` does it. A freed service found in a live scope is an `Invariant` violation, not
    something to absorb.
27. ⚠️ **ANYTHING IN A `static` THAT DESCRIBES A SESSION MUST BE IN `ResetSessionStatics`.** The
    scene reload used to clear them for free and nothing does now. `SessionResetTests` finds every
    static class with a parameterless `Clear`/`Reset`/`ClearAll` by reflection and fails on one the
    list does not name, so this cannot be forgotten quietly.
22. ⚠️ **A BUILDING VARIANT CHANGES STRUCTURE, NOT JUST DRESSING.** Footprint, floor count, roof
    direction/form, access, wall family, porch/awning/balcony or ruin state must change. Use shared
    material families and authored prefabs; a cosmetic prop swap is not a new building.
28. ⚠️ **A PRODUCTION WORLD CELL IS BAKED, HASHED AND OWNED.** Region specifications and authored
    resources are inputs; `data/world_bake/` is output; `manifest.json` is the exact bridge between
    them. Normal gameplay may load prepared cells but may not silently regenerate them.
29. ⚠️ **RESIDENCY IS NOT GAMEPLAY ACTIVATION.** Backdrop/Far/Mid/Near are one streamer's decision.
    Only Near owns full actors, physics and navigation; a cell-owned actor is destroyed or abstracted
    with its cell unless it is deliberately promoted to persistent session ownership.
30. ⚠️ **AN ACTOR ENTERS A WORLD POSITION ONLY AFTER REAL COLLISION IS READY.** Loading, New Game,
    respawn, fast travel and teleports use `SafePlacementService`; an arbitrary timer or a second
    spawn-correction implementation is not an acceptable substitute.

## Commands worth knowing

```text
dotnet build Embervale.sln
dotnet test tests/Embervale.Tests
python tools/gen_regions.py            # data/regions/*.tres is GENERATED; --check gates it
python tools/world_bake.py --bake       # rebuild deterministic prepared cells and manifest
python tools/world_bake.py --check      # fail on stale, missing, unexpected or modified outputs
godot --headless --path . -- --validate
godot --headless --path . -- --lifecycle  # session/world teardown; a gate, exit 1 on any leak
godot --headless --path . -- --worldgen   # what the generator makes: relief, regimes, field
                                          # deciles, steepest routes, wet authored anchors
godot --headless --path . -- --state
godot --path . -- --play
python tools/negative_tests.py          # refuses to run while data/ or scenes/ is dirty — commit first
godot --headless --path . --script res://tools/debug_pass_regressions.gd   # 44 runtime checks
godot --headless --path . --script res://tools/world_streaming_stress_probe.gd
dotnet build Embervale.csproj -c ExportRelease && python tools/check_shipping_assembly.py
godot --path . -- --guild-shots         # the five guild hubs, front and back, plus both greetings
godot --path . -- --panelshots          # every screen, incl. the Guilds tab
godot --path . --script res://tools/world_shots.gd      # add -- --update-world-baseline AFTER inspecting
godot --path . --script res://tools/world_perf_probe.gd
python tools/world_quality_check.py --mode engine       # what CI runs on a PR
python tools/world_quality_check.py --mode visual       # ditto, second job
python tools/world_quality_check.py --mode full         # + the negative battery; weekly
python tools/assets.py status           # 3D: what exists, which rig family, what drifted
python tools/assets.py validate         # 3D: every hard gate, in the required order
python tools/assets.py adopt <src> <dest>               # source model -> validated production asset
python tools/assets.py audit            # full Blender + Godot inspection -> reports/3d/runs/
python tools/compose_building.py <name> <w> <d> <storeys> [--hollow | --open | --ruined] [kit options]
python tools/check_architecture_kit.py
godot --headless --path . --script res://tools/building_collision_probe.gd
godot --path . --resolution 960x720 --script res://tools/architecture_shots.gd -- --output <dir>
python tools/gen_guild_dialogue.py <key> <dialogue.id> <faction.id> "<Speaker>"
```

`godot`/`python` are not on this shell PATH. The 4.7.1 console executable at
`C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe`
and Codex's bundled Python work when launched with the elevated project environment. The configured
Godot MCP relay (`localhost:23630`) is still down; the whole verification spine above runs without it.
