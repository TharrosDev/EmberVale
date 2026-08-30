## Phase 44 — Alpha Content Pass: all five realms blocked out `[C]`

> **Read `docs/WORLD_AUTHORING.md` before every sub-phase.** Edit
> `tools/region_spec_<region>.py`, not generated `data/regions/*.tres`; start new realms from
> `tools/region_spec_template.py`; close each region slice with
> `python tools/world_quality_check.py <region>`. Continuous terrain, directed traversal,
> canonical map locations, non-swimming water, whole-region residency and intentional empty
> country are non-negotiable.

### Realm acceptance contract

Each realm needs a disjoint generated specification; macro landform silhouette; route graph; a
culture-appropriate hub/settlement pattern; major story/guild POIs; boss territory/lair; intentional
transitional country; distinct biome/atmosphere/ecology; NPC/encounter plan; map/discovery layer; and
a measured resident performance budget. This is Alpha extent, not Beta density or final art. Phase 50
fills approved categories and Phase 53 replaces presentation placeholders. Never fill every cell.

- [ ] **44A — Five-realm atlas, coordinate bands and content budget** `[C/P]`
  - **Goal:** freeze the macro atlas and ownership before adding new generated realms.
  - **Build / Author:** region extents/portals/hidden entry/horizons, travel-distance bands, provisional
    cell/settlement/POI/empty-cell budgets and performance budgets derived from Ember Crown/Frostfang
    measurements. Inventory every 42/42.5/47/50/51 location hook and assign one phase/cell owner.
  - **Verify:** no overlap, unexplained absolute coordinate or unowned hook; label measured versus
    provisional targets.
  - **Done when:** one atlas table controls every spec without requiring dense POI cells.

- [ ] **44B — Ember Crown spec/extent reconciliation** `[C]`
  - **Goal:** preserve closed geography while proving it covers future Alpha needs.
  - **Build / Author:** audit the 16-cell generated spec, routes, six empty cells, hubs, guild/story
    anchors and Iron King territory; add only missing future anchors through the spec pipeline.
  - **Do not:** redo completed landforms, POI circulation, terrain quality or approved empty space.
  - **Verify:** generation/seams/layout/traversal/map/step-up/census, coordinate-save risk and visuals.
  - **Done when:** every future hook has an owner and completed geography is untouched.

- [ ] **44C — Ember Crown population and Alpha critical path** `[C]`
  - **Goal:** support guild/Act I/II testing without Beta density.
  - **Build / Author:** required guild actors, story contacts, encounter ecology and Iron King
    approach/arena handoff; map additions in the same change; mark each transitional cell
    remain-empty or environmental-story-only.
  - **Verify:** schedules, quest ids, boss route, factions, map, foot/mount traversal and performance.
  - **Done when:** critical path is playable and all six transitional cells retain their purpose.

- [ ] **44D — Frostfang spec/vertical-route reconciliation** `[C]`
  - **Goal:** preserve closed alpine geography while assigning Storm Tyrant/guild content.
  - **Build / Author:** audit 10 cells, Clan Hold, dragon country, water/recovery and five empty cells;
    reserve boss, Ash Hunter/Archive and settlement anchors without filling high traverses.
  - **Verify:** generation, directed traversal, mount/dismount, map, cold-atmosphere captures, baseline.
  - **Done when:** every hook fits existing topography and no completed landform is reopened.

- [ ] **44E — Frostfang population, ecology and Storm Tyrant territory** `[C]`
  - **Goal:** create a scattered-clan social layer and distinct boss journey.
  - **Build / Author:** named hold/waystation NPCs, regional encounters, boss approach/lair/arena and
    main/guild contacts; protect dragon dispositions and empty snowfield rhythm.
  - **Verify:** factions, vertical schedules, encounter filters, foot/mount boss route, weather/map/QA.
  - **Done when:** full Alpha route works without an Ember-Crown city reskin.

- [ ] **44F — Ashen Wilds macro geography and region spec** `[C]`
  - **Goal:** make Cataclysm scars geography, not rings of rock props.
  - **Build / Author:** new spec with disjoint warped plateaus/ravines/craters, corrupted forest pockets,
    graded routes, dead country, AshWaste/BurnedHeath/ecology and ash atmosphere; data-declared water.
  - **Verify:** template/generation/seams/layout/grades/off-route/backdrop/spawn/portal and initial budget.
  - **Done when:** empty terrain alone reads as the Ashen Wilds and is traversable end to end.

- [ ] **44G — Ashen Wilds routes, wilderness and map skeleton** `[C]`
  - **Goal:** legible dangerous travel without a POI corridor.
  - **Build / Author:** terrain-following primary/secondary routes, remote view/story dead ends with no
    loot pins, canonical hub/ruin/forest/anomaly/lair locations and justified travel nodes.
  - **Verify:** foot/mount both directions, discovery order, compass/map, path expectation, empty budget.
  - **Done when:** long-risk travel has intentional ends and protected wilderness.

- [ ] **44H — Ashen Wilds settlement and major POIs** `[C]`
  - **Goal:** author the minimum survivor/civilization destinations.
  - **Build / Author:** defensible hub, limited outpost, ruin, corrupted forest/anomaly, Ash Hunter and
    Emberbound hooks; Quaternius assets, stable NPC ids/schedules and map records.
  - **Verify:** layout/nav/doors, schedules, map transforms, approach/reverse captures and budget.
  - **Done when:** every POI owns a named future beat and large tracts remain wilderness.

- [ ] **44I — Ashen Wilds encounters and Beast Lord territory** `[C]`
  - **Goal:** complete corruption ecology and boss approach without a new anomaly mechanic.
  - **Build / Author:** regional creature/event tables, anomalies via existing weather/VFX/spells,
    Beast Lord ground/lair/arena and Act II contacts.
  - **Do not:** add survival meters, puzzles, traps or bespoke corruption ecology.
  - **Verify:** melee/magic/ranged/stealth answers, caps/filters, boss route, recovery, map and QA.
  - **Done when:** coherent Alpha ecology and boss journey pass the whole quality suite.

- [ ] **44J — Sunspire macro geography and region spec** `[C]`
  - **Goal:** combine desert, lost jungle and buried civilization as continuous country.
  - **Build / Author:** desert basins/mesas, water-supported jungle belt, temple/library landforms, old
    trade route, open desert, hot/haze atmosphere and semantic terrain layers.
  - **Verify:** generation, shoreline/recovery, grades/traps, disjoint band, horizon and resident budget.
  - **Done when:** terrain/air identify Sunspire before POIs exist.

- [ ] **44K — Sunspire routes, water, wilderness and discovery** `[C]`
  - **Goal:** caravan/jungle travel while keeping the fifth realm secret.
  - **Build / Author:** caravan road, jungle/river crossings, temple/library spurs, empty dune cells,
    canonical locations/travel nodes; no neighbour/map/search record hints at Pale Concord.
  - **Verify:** foot/mount, water recovery, discovery/travel, open-country views and secrecy search.
  - **Done when:** known-realm routes work and Pale Concord has zero UI/data leak.

- [ ] **44L — Sunspire civilization, libraries and cult footprint** `[C]`
  - **Goal:** concentrate ancient civilization, scholarship and religious control into distinct spaces.
  - **Build / Author:** major library/capital hub, route/jungle settlement, Archive site, public cult
    mission, concealed outpost and economy hooks; carry 42.5 ids exactly.
  - **Verify:** cover/hostility, schedules, shops/services/factions, interiors, map, captures/performance.
  - **Done when:** three identities read clearly without uniform density.

- [ ] **44M — Sunspire encounters and Crimson Prophet territory** `[C]`
  - **Goal:** support the Prophet with a credible empire and two approach modes.
  - **Build / Author:** cult/civilian/creature distribution, controlled/wild territory, Prophet
    approach/lair/arena, infiltration/open-assault routes and Act II contacts.
  - **Verify:** all 42.5 terminal states, filters, boss fallback access, map/QA/worst hub scene.
  - **Done when:** every infiltration state reaches the Alpha boss encounter anchor.

- [ ] **44N — Pale Concord fiction-rule decision gate** `[F/C]`
  - **Goal:** decide how stasis is expressed before building the hidden realm.
  - **Build / Author:** audit stalled sky/time, no ripening/rotting, undying residents and corpse
    persistence against WorldClock, schedules, death, saves and atmosphere. Prefer local presentation,
    dialogue and actor rules; name keep/cut/expression/owner for each candidate.
  - **Do not:** stop the global clock or create survival/corpse-management gameplay.
  - **Verify:** player-observable value and bounded tests for any approved mechanic; add it to 45A.
  - **Done when:** no implementation session must redesign time/death and every rule has one authority.

- [ ] **44O — Pale Concord hidden macro spec** `[C]`
  - **Goal:** build a preserved-at-dusk realm found by story, not advertised.
  - **Build / Author:** disjoint spec, still fields/canals, immaculate empty roads, preserved city,
    hidden entry, dusk atmosphere, substantial empty country and Hollow Queen territory; no normal
    neighbour/fast-travel exposure.
  - **Verify:** QA suite, absence from map/travel/search/state before unlock, safe direct QA entry,
    traversal/water/performance.
  - **Done when:** structurally healthy and inaccessible in normal play.

- [ ] **44P — Pale Concord settlement, population and reveal layer** `[C]`
  - **Goal:** express preservation as a social/travel rhythm.
  - **Build / Author:** discovery entry, preserved city/hub, frozen-routine villages/fields,
    environmental story and map records registered only after reveal; implement 44N content rules.
  - **Verify:** hidden/revealed maps, schedule/time/undying behavior, save before/after reveal, captures.
  - **Done when:** place and behavior communicate the bargain and secrecy holds everywhere.

- [ ] **44Q — Pale Concord encounters and Hollow Queen territory** `[C]`
  - **Goal:** conflict without making every resident ordinary undead fodder.
  - **Build / Author:** distinguish native undying, hostile servants and 34D undead; Hollow Queen
    approach/lair/arena, release/preservation hooks and 47E handoff.
  - **Verify:** dispositions/rules, boss after reveal only, targets, save/load and QA/performance.
  - **Done when:** Act II route and Alpha boss encounter preserve the moral premise.

- [ ] **44R — Five-realm travel/map/quality integration** `[C/P]`
  - **Goal:** close world extent as one navigable Alpha product.
  - **Build / Author:** known-realm portal/fast-travel graph, hidden unlock seam, safe landings,
    state/map/search and main/guild skeleton; collect per-realm quality/performance reports.
  - **Verify:** every edge both ways on foot/mount, save at all realms/transitions, no overlap, secrecy,
    all region QA runs, generation/negative battery and eye-level review.
  - **Done when:** the whole Alpha shape is traversable, modern gates pass, deltas are measured and
    intentional empty country remains intact.

---
