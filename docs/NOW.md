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

- **NEXT: 42C — Dawnwardens recruitment and probation.** The first arc to walk through a door 42B
  built: join/refuse dialogue plus a Defend/Reach probation pair, and rank one earned.

Read [`docs/WORLD_AUTHORING.md`](WORLD_AUTHORING.md) before touching a cell. `data/regions/*.tres`
is **generated** — edit `tools/region_spec_<region>.py` and run `python tools/gen_regions.py`.

## Last verified (2026-09-04 — the infrastructure overhaul)

| Check | Result |
| --- | --- |
| Build | `dotnet build Embervale.sln` — **0 warnings, 0 errors, `TreatWarningsAsErrors=true`** |
| Shipping build | `dotnet build Embervale.csproj -c ExportRelease` — 0 warnings, 0 errors |
| Shipping contents | `python tools/check_shipping_assembly.py` — PASS; no MCP addon, no `*Shots`, no `ReproHarness` |
| Tests | `dotnet test tests/Embervale.Tests` — **1807 passing** (1743 before this pass + 64: 9 service-scope, 2 session-reset, 53 AI rules) |
| `--validate` | exit 0 |
| `--lifecycle` | **exit 0 — 3 new-game + load round trips, 0 surviving services, 0 surviving subscriptions, 0 stranded saveables, 0 orphan nodes, 0 invariant violations** |
| `--state` | 2 regions, 26 cells, 75 map locations — unchanged |
| `--worldgen` | Ember Crown **42.8 m relief**, Frostfang **98.3 m** — unchanged |
| `gen_regions.py --check` | clean |
| `debug_pass_regressions.gd` | **44/44** — including two that were FAILING on `main` (the spawn-ground pair above) |
| `negative_tests.py` | 112 rules broken and restored, each caught |
| `--play` | boots to Playing, restores 34 objects, streams all 16 Ember Crown cells, no errors, no invariant violations |

⚠️ **GODOTMCP was not available for this pass** — the server was running but no Godot editor was
open, so no `mcp__ai-game-developer__*` tools registered. Verification was the shell spine, which
CLAUDE.md §2 already names as the real one. **No visual check was made**, and none was needed: no
scene, model, material or shader changed.

⚠️ **There is still no `export_presets.cfg`.** "The shipping build" is proved by `ExportRelease`
compiling clean plus the assembly scan, not by a real export artifact.

⚠️ **`ai.skirmisher` has zero archetype users** and is left alone deliberately — deleting authored
content is a designer's call, not a refactor's. It is a profile waiting for an archetype.

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

## Commands worth knowing

```text
dotnet build Embervale.sln
dotnet test tests/Embervale.Tests
python tools/gen_regions.py            # data/regions/*.tres is GENERATED; --check gates it
godot --headless --path . -- --validate
godot --headless --path . -- --lifecycle  # session/world teardown; a gate, exit 1 on any leak
godot --headless --path . -- --worldgen   # what the generator makes: relief, regimes, field
                                          # deciles, steepest routes, wet authored anchors
godot --headless --path . -- --state
godot --path . -- --play
python tools/negative_tests.py          # refuses to run while data/ or scenes/ is dirty — commit first
godot --headless --path . --script res://tools/debug_pass_regressions.gd   # 44 runtime checks
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
