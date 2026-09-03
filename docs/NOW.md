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

- **NEXT: 42C — Dawnwardens recruitment and probation.** The first arc to walk through a door 42B
  built: join/refuse dialogue plus a Defend/Reach probation pair, and rank one earned.

Read [`docs/WORLD_AUTHORING.md`](WORLD_AUTHORING.md) before touching a cell. `data/regions/*.tres`
is **generated** — edit `tools/region_spec_<region>.py` and run `python tools/gen_regions.py`.

## Last verified (2026-09-01 — Session 5 architecture kit)

| Check | Result |
| --- | --- |
| Build | `dotnet build Embervale.sln` — 0 warnings, 0 errors |
| Tests | `dotnet test tests/Embervale.Tests` — **1713 passing**, from a clean checkout |
| `--validate` | exit 0, with the new `ValidateModelAssets` arm |
| `--state` | 2 regions, 26 cells, **48 dialogues**, **31 schedules**, **75 map locations**, 13 factions |
| Negative battery | `negative_tests.py` — **112/112 caught**, including the model-manifest drift rule |
| `world_quality_check.py --mode engine` | all **19** gates PASS, including architecture structure/material/reference validation and real-capsule building collision |
| `--mode visual` | PASS, 260/260 world frames after inspecting and merging only the five intentionally changed settlement cells |
| Architecture views | PASS, 15 important buildings × six required angles = **90/90 frames** |
| Permanent 3D audit | self-test PASS; **193 models** classified into five rig families, manifest matches disk |
| `--guild-shots` | 12 frames: five hubs front and back at eye level with their officers, plus the same captain greeting a stranger and a member |
| Persistence | the harness stages membership on every officer, then **loads a save taken before any of it** and proves every leader is back to the stranger greeting — a load replays no events |
| `--play` | boots, restores `auto1`, reaches `Playing` and live combat, 0 errors |
| `assets.py validate` | 5/5 gates PASS, incl. the retarget probe over all **33** humanoids |
| `assets.py audit` | 193 models, **118 findings — down from 147**; the 5 not in the last archived run are all Meshy-wave assets it predates |

`--mode full` passes all 21 pass/fail gates after the implementation commit; the negative battery
catches and restores **111/111** deliberately broken rules, and the performance report is recorded
in the Session 5 handoff. `--economy` remains out of scope because this pass touches neither prices
nor trade.

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
    `WorldLandformResource` and the props dress what the terrain already says.
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
22. ⚠️ **A BUILDING VARIANT CHANGES STRUCTURE, NOT JUST DRESSING.** Footprint, floor count, roof
    direction/form, access, wall family, porch/awning/balcony or ruin state must change. Use shared
    material families and authored prefabs; a cosmetic prop swap is not a new building.

## Commands worth knowing

```text
dotnet build Embervale.sln
dotnet test tests/Embervale.Tests
python tools/gen_regions.py            # data/regions/*.tres is GENERATED; --check gates it
godot --headless --path . -- --validate
godot --headless --path . -- --state
godot --path . -- --play
python tools/negative_tests.py          # refuses to run while data/ or scenes/ is dirty — commit first
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
