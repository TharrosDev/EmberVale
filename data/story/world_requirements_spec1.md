# World requirements: W-Spec-1 (Acts I and II, missions 01 to 18)

Written by the spec1 workstream for the W-World workstream. The quests and dialogues are already generated
(`tools/campaign/specs/act1.py`, `frostfang.py`, `ashen.py`, `sunspire.py`, hand extensions in `_s1_hand*.py`); this file lists
what the scenes, map data and spawners must provide so every id they name exists. Positions are cell-local, in the scene's own
coordinates (the same numbers `scenes/regions/<region>/<cell>.tscn` uses), approximate on purpose: place by the named props
and render it (docs/WORLD_AUTHORING.md). Every entity gets a stable `PersistentId` of the form `<region>.<cell>.<slug>`.

Conventions used below:

* **Prop clue** = a Node3D with a Collider (StaticBody3D, small shape) plus a child `Dialogue` node
  (`DialogueComponent`, `DialogueId`, `InteractId`). One interactable per entity. The prompt reads `Talk to <speaker>`
  (`interact.talk_to`); set `SpeakerName` only if the dialogue's speaker is wrong for the prompt.
* **NPC** = the usual actor (TemplateId, Faction, Dialogue, optional Schedule). A TemplateId is suggested from the existing
  `npc.*` set; any civilian or soldier template with a fitting model is fine.
* **FlagVisibility** = `FlagVisibilityComponent` with `VisibleWhenFlagId` / `HiddenWhenFlagId`.
* Quests reference ids by name only; no quest needs a coordinate.

---

## A. New Interact ids (16) and the prop that carries each

Every id is unique project-wide and must be authored exactly once (`ValidateInteractIdsArePlaced`).

| WR | InteractId | DialogueId | Cell | Where | Visibility | Referenced by |
| --- | --- | --- | --- | --- | --- | --- |
| WR-spec1-01 | `interact.ember_crown.alarm_bell` | `dialogue.alarm_bell` | `ember_crown.town_hub` | New bronze bell on a post at (9.5, 0, 3.5): 3.5 m west of the Elder (13, 4.5), 2 m north-west of the Waystone (11, 6.5). Entity name "The Alarm Bell". | always | quest.main.smoke_over_the_square obj `bell` |
| WR-spec1-02 | `interact.ashen_breach.toren_cairn` | `dialogue.toren_cairn` | `ember_crown.ashen_breach` | Shoulder-high cairn of black stones, no mortar, at (92, 0, 4): 4 m east of `Dx_BreachRubble` (88, 8), at the western mouth of the cut. A sword-belt buckle set in the cap-stone. Entity name "A Cairn of Black Stones". | always | quest.main.the_pass_kept obj `cairn` |
| WR-spec1-03 | `interact.crossway.impound_crate` | `dialogue.impound_crate` | `ember_crown.crossway_post` | Wax-sealed crate beside the Impound Clerk (44, 14): at (42, 0, 12), next to `CrateA` (41, 10). Entity name "The Sealed Crate". | always | quest.main.crossway_whispers obj `crate` |
| WR-spec1-04 | `interact.citadel.signal_brazier` | `dialogue.signal_brazier` | `ember_crown.citadel` | Basin of black iron on three legs, fed by a coal pipe, at the foot of the west ramp: at (-34, 0, 36), 2 m from `Dx_RampBrazierW` (-36, 34.5). Its flame and light node carry `HiddenWhenFlagId = flag.beat.signal_doused` (the fire goes out). Entity name "The Signal Brazier". | always | quest.main.citadel_approach obj `brazier` |
| WR-spec1-05 | `interact.frostfang.scout_cairn_south` | `dialogue.scout_cairn_south` | `frostfang_reach.clan_hold` | Flat grey stones, stone lantern bowl on top, at the hold's northern edge where the north road leaves it, near `BrazierNE` (22, -4) but outside the ring of braziers (about (24, 0, -34) if the cell reaches that far; otherwise on the first rise of the north road). Lantern light `VisibleWhenFlagId = flag.beat.scout_cairn_south_seen`. Entity name "The South Scout Cairn". | always | quest.main.closed_hold obj `cairn_s` |
| WR-spec1-06 | `interact.frostfang.scout_cairn_mid` | `dialogue.scout_cairn_mid` | `frostfang_reach.glacier` | On a wind-scoured saddle about 15 m north of `GlacierWaystone` (-12, 2), a split lantern bowl with a mitten frozen to the rim (a small glove prop). Lantern light `VisibleWhenFlagId = flag.beat.scout_cairn_mid_seen`. Entity name "The Middle Scout Cairn". | always | quest.main.closed_hold obj `cairn_m` |
| WR-spec1-07 | `interact.frostfang.scout_cairn_north` | `dialogue.scout_cairn_north` | `frostfang_reach.glacier` | At the head of the pass, 40 m north of the cell's `MapPin` (-20, -15) (or in `frostfang_reach.glacier_head` if that is nearer the ice): lantern bowl inside a block of blue ice. Light `VisibleWhenFlagId = flag.beat.scout_cairn_north_seen`. Entity name "The North Scout Cairn". | always | quest.main.closed_hold obj `cairn_n` |
| WR-spec1-08 | `interact.frostfang.aerie_winch_a` | `dialogue.aerie_winch_a` | `frostfang_reach.stormfall` | Iron drum taller than a man with a worn crank, cable running up the cliff, at (50, 0, 74): 11 m west of the `Dx_StormcrownStoneA` pillar (61, 79) that carries the `location.frostfang.stormcrown` pin. Entity name "The First Winch". | `VisibleWhenFlagId = flag.fork.succession_halvar` (only the Halvar route uses it) | quest.main.succession obj `winch_a` |
| WR-spec1-09 | `interact.frostfang.aerie_winch_b` | `dialogue.aerie_winch_b` | `frostfang_reach.stormfall` | Second drum and lift platform at the cliff foot, at (66, 0, 66), 13 m north-east of the first. Entity name "The Second Winch". | same as WR-spec1-08 | quest.main.succession obj `winch_b` |
| WR-spec1-10 | `interact.ashen.beast_track_a` | `dialogue.beast_track_a` | `ashen_wilds.hunters_rise` | Red-painted stake in a trampled patch of ash at (27, 0, -8), at the foot of the rise 9 m west of the station fire (32, 3). Entity name "A Staked Track". | `VisibleWhenFlagId = flag.beat.hask_briefed` (Hask stakes them after the briefing) | quest.main.herd_and_hearth obj `track_a` |
| WR-spec1-11 | `interact.ashen.beast_track_b` | `dialogue.beast_track_b` | `ashen_wilds.hunters_rise` | Second stake higher on the rise, at (38, 0, -14), where the road bends towards the wall. | same | obj `track_b` |
| WR-spec1-12 | `interact.ashen.beast_track_c` | `dialogue.beast_track_c` | `ashen_wilds.lair_approach` | Third stake where the scar road meets the plateau ramp, beside the new `location.ashen.herd_trail` pin (WR-spec1-40). A ring of small stones set round a single antler lies in the dust. Entity name "A Staked Track". | `VisibleWhenFlagId = flag.beat.hask_briefed` | obj `track_c` |
| WR-spec1-13 | `interact.sunspire.well_gauge_east` | `dialogue.well_gauge_east` | `sunspire.wells` | Tablet of pale stone set into the oasis wall, seven scored lines, Oda's chalk beside them. Place on the east face of the wall, near `CaravanseraiCot` (49, -57) at (50, 0, -50). Entity name "The East Gauge Stone". | always | quest.main.dry_wells obj `gauge_e` |
| WR-spec1-14 | `interact.sunspire.well_gauge_mid` | `dialogue.well_gauge_mid` | `sunspire.wells` | Same tablet shape above the cistern overflow, 3 m from `Well` (37, -54), at (38, 0, -52). Child's letters scratched under it: THE WATER TALKS AT NIGHT (a decal or a small plaque prop). Entity name "The Middle Gauge Stone". | always | obj `gauge_m` |
| WR-spec1-15 | `interact.sunspire.well_gauge_west` | `dialogue.well_gauge_west` | `sunspire.wells` | Tablet nearest the road north, at (24, 0, -50), 6 m east of the `Pilgrim` (20, -43). Entity name "The West Gauge Stone". | always | obj `gauge_w` |
| WR-spec1-16 | `interact.sunspire.sanctum_seal` | `dialogue.sanctum_seal` | `sunspire.temple` | Disc of red wax hanging on a cord from the latch of the sanctum door, at the sanctum wall behind the chapel: about (44, 0, -34), 5 m north of the `Brazier` "Crimson Altar Fire" (45, -29). Entity name "The Sanctum Door". | `VisibleWhenFlagId = flag.fork.flock_kin` | quest.main.prophets_flock obj `seal` |

Gauge stones are read in the order east, middle, west (the quest is sequential), so keep them visually grouped along the oasis wall.

## B. New NPCs and dialogue-only props

| WR | Entity | DialogueId | Cell and position | Visibility and notes |
| --- | --- | --- | --- | --- |
| WR-spec1-20 | NPC "Joss Harrow, Carter" (injured, seated against a trough; toppled cart prop beside him) | `dialogue.wounded_carter` | `ember_crown.town_hub`, (24, 0, -3), north edge of the square, TemplateId `npc.road_warden` model is fine (civilian) | `VisibleWhenFlagId = flag.beat.alarm_rung`, `HiddenWhenFlagId = flag.beat.carter_helped` (he is carried in). Quest: smoke_over_the_square optional obj `carter`. |
| WR-spec1-21 | Prop "A Black-Iron Token" (small disc lying on top of the cairn cap-stone, 0.1 m above it) | `dialogue.black_token` | `ember_crown.ashen_breach`, on the cap-stone of WR-spec1-02 | `VisibleWhenFlagId = flag.beat.toren_cairn_seen`, `HiddenWhenFlagId = flag.beat.black_token_taken`. Taking it sets `flag.rival.glimpsed` and the milestone `flag.beat.black_token_taken`. |
| WR-spec1-22 | Prop "The Warband's Banner" (pole of lashed deadfall, goblin-hide banner, black-iron mail patch in the corner) | `dialogue.warband_banner` | `ember_crown.wilds_north`, (11, 0, 31): in the goblin ruin, 3 m east of `Crate` (8, 31) and 2 m north-east of `IngotPickup` (9, 29) | always. Burning sets `flag.beat.banner_burned`. After it is set, swap the banner mesh for a charred pole (a second node `VisibleWhenFlagId = flag.beat.banner_burned`, banner node `HiddenWhenFlagId = flag.beat.banner_burned`). Quest: quest.warband.bounty appended milestone `banner`. Goblin camp encounter stays as authored. |
| WR-spec1-23 | Prop "The Carter Ledger" (open ledger under a paperweight spur on the Search Table) | `dialogue.carter_ledger` | `ember_crown.crossway_post`, (27, 0, 13), on the Search Table beside `SearchWarden` (28, 14) | always. Optional heist path; sets `flag.beat.armed_heist`. Torn page prop optional (`VisibleWhenFlagId = flag.beat.armed_heist`). |
| WR-spec1-24 | NPC "Ilse Marrow, Scout" (prone, bolt in her calf, cloak over her head, in the ditch below the Kingsway) | `dialogue.wounded_scout` | `ember_crown.kings_approach`, in the roadside ditch about 6 m off the Kingsway centreline, on the stretch where it starts to climb towards the spur. World picks the exact point from the route; keep it within 120 m of the `location.ember_crown.iron_citadel` pin and out of sentry sight. TemplateId any Dawnwarden template (`npc.dawnwarden_partner` model). | `HiddenWhenFlagId = flag.main.citadel_approach_done`. Quest: quest.warband.remedies appended optional milestone `scout` (set by giving her a health potion, `flag.beat.postern_known`). |
| WR-spec1-25 | NPC "Marshal Orsolo Dray" (chained to a wall ring in a small stockade cell) | `dialogue.dray` | `ember_crown.citadel`, new stockade cell prop at (-1, 0, 47), 5 m east of `Dx_CourtWell` (-6, 44.5) and 12 m west of `Dx_GarrisonHall` (14, 38.5). Marshal's coat without rank cords, TemplateId `npc.dawnwarden_serjeant` model recoloured is acceptable. | `HiddenWhenFlagId = flag.fork.dray_spared` (he walks out). He stays seated after `flag.fork.dray_pressed`. Quest: citadel_approach obj `dray`, milestone `decide`. Start variants already handle the empty cell and the pressed state. |
| WR-spec1-26 | NPC "The Rider in Black Iron" (Ashen Knight, visor down, very tall; uses the Ashen Knight model without weapons drawn) standing in the royal box | `dialogue.rival_arena` | `ember_crown.arena`, north gallery, on `TierN1` (14, -31) raised to the tier height, facing the sand | `VisibleWhenFlagId = flag.iron_king_defeated`, `HiddenWhenFlagId = flag.rival.met1`. Not hostile (no AI, no faction hit box). Quest: iron_king milestone `rival` (completes when the dialogue sets `flag.rival.met1`). |
| WR-spec1-27 | Prop "The Empty Royal Box" (a round black-iron token lying on the rail, warm to the touch) | `dialogue.rival_arena` (start variant `after`) | `ember_crown.arena`, same spot as WR-spec1-26 | `VisibleWhenFlagId = flag.rival.met1`. One entity per interactable, hence a separate node. |
| WR-spec1-28 | NPC "Deacon Haddan Voll" (red robe, ladle, ledger of names) at the chapel door | `dialogue.flock_deacon` | `sunspire.temple`, at the door of `MissionChapel` (14, -29), 3 m east of it, at (17, 0, -29) | `HiddenWhenFlagId = flag.beat.flock_decided`. Quest: prophets_flock obj `deacon`, milestone `decide`. |
| WR-spec1-29 | Prop "The Hanging Ladle" (ladle on a nail, chapel door ajar) | `dialogue.flock_deacon` (start variant `after`) | `sunspire.temple`, on the chapel door at (15, 0, -29) | `VisibleWhenFlagId = flag.beat.flock_decided`. |
| WR-spec1-30 | Prop "The Moot Stone" (waist-high stone worn smooth by hands) | `dialogue.frostfang_moot_stone` | `frostfang_reach.clan_hold`, inside the moot ring: (14, 0, -15), 4 m north-east of `ClanChief` (11, -12) and 7 m south-east of `MootHall` (17, -22). | always. Quest: succession obj `stone`, milestone `decide`. |
| WR-spec1-31 | Prop group "The Herd" (a grey-eyed elk cow, a dire wolf lying like a hound at a hearth, a calf, boar and maw wolves standing shoulder to shoulder, about forty animals as static or idle meshes, no AI, no collider except the interactable one) | `dialogue.ashen_herd_choice` | `ashen_wilds.lair_approach`, around the new `location.ashen.herd_trail` pin (WR-spec1-40) on a shallow of ash at the foot of the plateau ramp | `VisibleWhenFlagId = flag.main.last_hearth_done`, `HiddenWhenFlagId = flag.fork.herd_slain`. After `flag.fork.herd_calmed` the animals lie down (a second prop group `VisibleWhenFlagId = flag.fork.herd_calmed`, `HiddenWhenFlagId = flag.fork.herd_slain`, same dialogue, start variant `after`). Quest: herd_and_hearth obj `herd`, milestone `decide`. |
| WR-spec1-32 | Decals "The Cull" (trampled ash, dark patches, scattered carcasses) | none | same spot as WR-spec1-31 | `VisibleWhenFlagId = flag.fork.herd_slain`. Visible consequence of fork F3. |
| WR-spec1-33 | NPC "Rafe Sarn" (Oda's grandson: thin, faded red robe, sits on the cistern lip) | `dialogue.oda_grandson` | `sunspire.wells`, on the cistern lip at (36, 0, -55), 2 m from `Wellkeeper` (39, -57) | `VisibleWhenFlagId = flag.beat.flock_decided`, `HiddenWhenFlagId = flag.fork.flock_kin`. Start variant shows the "among the grey cloaks" line when `flag.fork.flock_turned`. Optional, no quest reads it; Oda's variants refer to him. |
| WR-spec1-34 | Extras "Grey Pilgrims" (about 24 plain-grey cloaked figures with empty jars, queuing at the cistern; Tamsin Reed leads them) | none (decor only) | `sunspire.wells`, around `Well` (37, -54) and `Pilgrim` (20, -43) | `VisibleWhenFlagId = flag.fork.flock_turned`. Tamsin (`dialogue.sunspire_pilgrim`, existing NPC) has a variant for this. |
| WR-spec1-35 | NPC "Halvar One-Hand" duplicate on the moot stone (sits with his stump on his knee) | `dialogue.clan_exile` (existing; start variant `cq_st_after`) | `frostfang_reach.clan_hold`, on the moot stone prop WR-spec1-30, (14, 0, -15) | `VisibleWhenFlagId = flag.beat.stormend_halvar`. Exile's original NPC (at `Exile` (33, 12)) gets `HiddenWhenFlagId = flag.beat.stormend_halvar`, and his `ExileFire` (35, 14) node gets the same. |
| WR-spec1-36 | Props "Clan Banners and Shield Wall" (eight clan banners on poles, a double row of round shields) | none | `frostfang_reach.stormfall`, track-head under the spire, around (56, 0, 60) | `VisibleWhenFlagId = flag.fork.succession_hjalvar`. The Hjalvar route's shield wall. |
| WR-spec1-37 | Props "Syndicate Rope Crew" (coiled rope, grease pots, two crew idling at the winches, TemplateId any `npc.syndicate_*` extra) | none | `frostfang_reach.stormfall`, next to the winches WR-spec1-08 and 09 | `VisibleWhenFlagId = flag.fork.succession_halvar`. |
| WR-spec1-38 | Props "Last Hearth Herd" (a dire wolf lying by the fence at (-14, 0, 33), a sleeping calf beside it) | none | `ashen_wilds.last_hearth`, by `HearthFire` (-14, 34) | `VisibleWhenFlagId = flag.fork.herd_calmed`. Maeve's variant mentions them. |

## C. Boss brazier gates (edit the scene node, nothing else)

Arc gates are derived flags: `data/story/rules/spec1.json` sets `flag.arc.frostfang_ready` when `flag.main.succession_done`
holds, `flag.arc.ashen_ready` when `flag.main.herd_and_hearth_done` holds and `flag.arc.sunspire_ready` when
`flag.main.prophets_flock_done` holds (CampaignCatchUp also sets all three for a legacy save that beat the Iron King).
The scenes read the arc flag, never the quest.

| WR | Scene | Node | Change |
| --- | --- | --- | --- |
| WR-spec1-50 | `scenes/regions/frostfang_reach/stormcrown.tscn` | `Brazier/Summon` ("Lightning Rod") | `RequiredFlagId = "flag.arc.frostfang_ready"` (was `flag.iron_king_defeated`), `LockedPromptKey = "boss.storm_tyrant.arc_locked"` |
| WR-spec1-51 | `scenes/regions/ashen_wilds/beast_lair.tscn` | `Brazier/Summon` ("Blighted Brazier") | add `RequiredFlagId = "flag.arc.ashen_ready"` (currently ungated), `LockedPromptKey = "boss.beast_lord.arc_locked"` |
| WR-spec1-52 | `scenes/regions/sunspire/temple.tscn` | `Brazier/Summon` ("Crimson Altar Fire") | add `RequiredFlagId = "flag.arc.sunspire_ready"` (currently ungated), `LockedPromptKey = "boss.crimson_prophet.arc_locked"` |
| WR-spec1-53 | `scenes/regions/ember_crown/arena.tscn` | `Brazier/Summon` | no change: it keeps the default `RequiredQuestId = quest.warband.heart`, and quest.main.iron_king auto-starts on `flag.frostfang.passage_open` (the heart quest's completion) |
| WR-spec1-54 | `stormcrown.tscn` `RivalBrazier/Summon`, `temple.tscn` `RivalBrazier/Summon` | | no change: they already gate on `flag.storm_tyrant_defeated` / `flag.crimson_prophet_defeated`, which the optional duel objectives mirror |

The three `*.arc_locked` rows are in the spec1 locale block.

## D. Set-piece spawners (`SetPieceSpawnComponent`)

| WR | Cell | Trigger and gate | Spawns | Ends |
| --- | --- | --- | --- | --- |
| WR-spec1-60 | `ember_crown.town_hub`, entering from the north gate of the square, spawn points 14 to 18 m north of the Waystone (11, 6.5) | starts on `flag.beat.alarm_rung` | `enemy.goblin`: wave 1 three at 0 s, wave 2 four at 14 s, wave 3 five at 28 s plus one extra at 36 s. Raiders are hostile to everyone; the town's guard NPCs keep working | stops when the party leaves the square or 60 s elapse; sets `flag.beat.square_raid_cleared` when the last raider dies (informational; the quest's Defend objective is time-based, 45 s) |
| WR-spec1-61 | `ember_crown.citadel`, ramp pairs at `Dx_RampBrazierW` (-36, 34.5) and `Dx_RampBrazierE` (-16, 38.5) | active while the player is within 60 m, `flag.beat.signal_doused` absent, `flag.beat.postern_known` absent | `enemy.soldier` in pairs, keep up to 4 alive, a new pair 20 s after any pair dies; the quest needs 6 kills | stops permanently on `flag.beat.signal_doused` |
| WR-spec1-62 | `ember_crown.citadel`, the court around `Dx_CourtWell` (-6, 44.5) | active while the player is within 40 m of the court, `flag.beat.postern_known` held, `flag.beat.signal_doused` absent | `enemy.soldier` three, no respawn (the postern route needs 3 kills) | n/a |
| WR-spec1-63 | `frostfang_reach.clan_hold`, around the moot ring (`ClanChief` (11, -12)) | starts on `flag.beat.cairns_lit` | `enemy.storm_mote`: four at 0 s, six at 20 s, eight at 40 s from the north edge, fast and fragile; the clan NPCs fight beside you | sets `flag.beat.hold_raid_cleared` when the last dies (informational; Defend is time-based, 60 s) |
| WR-spec1-64 | `frostfang_reach.stormfall`, track-head under the spire near the shield wall (56, 60) | starts on `flag.beat.succession_decided` when `flag.fork.succession_hjalvar` is held | `enemy.storm_mote`: four at 0 s, five at 20 s, six at 40 s, six at 60 s (the Defend objective is 75 s) | sets `flag.beat.stormcrown_raid_cleared` (informational). Not spawned on the Halvar route |
| WR-spec1-65 | `ashen_wilds.last_hearth`, a ring of radius 28 m around `HearthFire` (-14, 34) | starts on `flag.beat.hearth_raid` | `enemy.wolf`: six at 0 s, six more at 20 s; `enemy.dire_wolf` two at 35 s; they come in a ring and hold at 10 m for two seconds before charging | informational `flag.beat.hearth_raid_cleared` when the last dies; Defend is time-based, 60 s |
| WR-spec1-66 | `ashen_wilds.lair_approach` near the herd trail pin | starts on `flag.fork.herd_slain` | `enemy.dire_wolf` four first (the quest counts four), then `enemy.wolf` six and `enemy.thornback_boar` two at 10 s intervals; no fear, they charge in a wave | sets `flag.beat.cull_done` when all are dead (informational) |
| WR-spec1-67 | `ashen_wilds.lair_approach` near the herd trail pin | starts on `flag.fork.herd_calmed` | `enemy.wolf` two at 10 s, which test the player once and then lie down; no other spawns | the quest's Defend (30 s) is time-based |
| WR-spec1-68 | `sunspire.temple`, the chapel and the nave in front of `MissionChapel` (14, -29) | starts on `flag.fork.flock_exposed` | `enemy.cultist` four, kneeling until the player approaches within 8 m, then hostile | sets `flag.beat.zealots_down` (informational) |

Fork consequences the world should also express (optional but intended; all keyed on fork flags, no quest reads them):

* Iron King arena `AddSpawns` wave 2: three fewer `enemy.soldier` when `flag.fork.dray_spared` (Dray warned the garrison).
* Storm Tyrant `AddSpawns` first wave of motes skipped when `flag.fork.succession_halvar` (the winch ascent surprises the spire).
* Beast Lord phase 2 pack summon: four fewer wolves when `flag.fork.herd_slain` (the pack he would have called is dead).
* Crimson Prophet nave adds: half the zealots start passive when `flag.fork.flock_kin` (the sanctum door is open to you).

## E. New map location

| WR | Id | Category | Name key and text | Cell | Notes |
| --- | --- | --- | --- | --- | --- |
| WR-spec1-40 | `location.ashen.herd_trail` | `Landmark` (`add("ashen_wilds/lair_approach", "ashen.herd_trail", "Landmark", "<pin node>", "The Herd Trail", ...)` in `tools/gen_map_locations.py`) | `location.ashen.herd_trail.name` = "The Herd Trail"; desc = "A road trampled hand-deep by wolf and elk and boar, running to the foot of the plateau ramp." | `ashen_wilds.lair_approach` | A `MapLocationComponent` on a pin node at the shallow of ash at the ramp foot. No `RequiredFlagId`. `RevealWithCell = false`. Referenced by quest.main.herd_and_hearth objectives `track_c`, `trail`, `herd`, `decide`, `cull`, `smoke` (Reach and Defend radius is the map service's default). |

Every other `location.*` the quests name already exists (`location.crossway.post/watch/impound`, `location.ember_crown.town/iron_citadel/arena/ashen_breach`,
`location.wilds.north`, `location.frostfang.clan_hold/glacier/stormcrown`, `location.ashen.last_hearth/station/beast_lair`,
`location.sunspire.wells/caravan_gap/mission`). Note `location.frostfang.stormcrown` is pinned in cell `frostfang_reach.stormfall`
(the track-head), not in the `stormcrown` duelling-ground cell: the succession quest's Defend and winch objectives use it on purpose.

## F. Props that change with flags (summary of the FlagVisibility pairs above)

* Citadel signal brazier flame and light: hidden at `flag.beat.signal_doused` (WR-spec1-04).
* Black token: visible after the cairn is read, gone once taken (WR-spec1-21). Goblin banner: charred pole after burning (WR-spec1-22).
* Scout cairn lanterns: lit at each cairn's `*_seen` flag (WR-spec1-05 to 07).
* Winches, shield wall, Syndicate rope crew by succession choice (WR-spec1-08, 09, 36, 37). Halvar moves from his fire to the moot stone at `flag.beat.stormend_halvar` (WR-spec1-35).
* Herd by `flag.fork.herd_slain` / `flag.fork.herd_calmed` (WR-spec1-31, 32, 38).
* Chapel door: deacon gone and ladle on its nail at `flag.beat.flock_decided` (WR-spec1-28, 29). Grandson and grey pilgrims by flock choice (WR-spec1-33, 34). Sanctum seal only for the kin route (WR-spec1-16).
* Rival: rider appears at `flag.iron_king_defeated`, leaves a token at `flag.rival.met1` (WR-spec1-26, 27).
* No portal or door changes. The Frostfang door stays open (docs/playbook/finish.md note).

## G. Story data authored by this workstream

| WR | File | Contents |
| --- | --- | --- |
| WR-spec1-70 | `data/story/rules/spec1.json` | 24 rules in the shared format: `rule.opening_done` (`on: opening_finished` sets `flag.main.opening_done`, the auto-start of quest 01), armed-by-known paths (honest, broker, heist), Dray decided, arc gates `flag.arc.{frostfang,ashen,sunspire}_ready`, per-arc "decided" milestones, per-boss "aftermath" flags (`flag.beat.stormend_*`, `beastend_*`, `prophetend_*`) and `flag.beat.arcs_complete` (all three arc quests done; read by the Elder) |
| WR-spec1-71 | `data/story/reactions/spec1.json` | 10 companion reactions (Kael, Wren, Tessa), once per save each. Text keys `bark.<slug>` are in the spec1 locale block |

Code-set flags the specs declare (`CODE_FLAGS`): `flag.main.opening_done`, `flag.beat.armed_by_known`, `flag.beat.dray_decided`,
`flag.arc.frostfang_ready`, `flag.beat.succession_decided`, `flag.beat.stormend_hjalvar/halvar`, `flag.arc.ashen_ready`,
`flag.beat.herd_decided`, `flag.beat.beastend_slain/calmed`, `flag.arc.sunspire_ready`, `flag.beat.flock_decided`,
`flag.beat.prophetend_exposed/turned/kin` (all rules in WR-spec1-70), `flag.party.companion.kael` (the party mirror),
`flag.rival.duel1_won` and `flag.rival.duel2_won` (BossEncounterDirector, existing).

## H. Fork flags for W-Spec-2 (landing and epilogue)

| Flag | Written by | Landing row | Epilogue row |
| --- | --- | --- | --- |
| `flag.fork.dray_spared` | `dialogue.dray` | `landing.fork.dray_spared` | `epilogue.fork.dray_spared` |
| `flag.fork.dray_pressed` | `dialogue.dray` | `landing.fork.dray_pressed` | `epilogue.fork.dray_pressed` |
| `flag.fork.succession_hjalvar` | `dialogue.frostfang_moot_stone` | `landing.fork.succession_hjalvar` | `epilogue.fork.succession_hjalvar` |
| `flag.fork.succession_halvar` | `dialogue.frostfang_moot_stone` | `landing.fork.succession_halvar` | `epilogue.fork.succession_halvar` |
| `flag.fork.herd_slain` | `dialogue.ashen_herd_choice` | `landing.fork.herd_slain` | `epilogue.fork.herd_slain` |
| `flag.fork.herd_calmed` | `dialogue.ashen_herd_choice` | `landing.fork.herd_calmed` | `epilogue.fork.herd_calmed` |
| `flag.fork.flock_exposed` | `dialogue.flock_deacon` | `landing.fork.flock_exposed` | `epilogue.fork.flock_exposed` |
| `flag.fork.flock_turned` | `dialogue.flock_deacon` | `landing.fork.flock_turned` | `epilogue.fork.flock_turned` |
| `flag.fork.flock_kin` | `dialogue.flock_deacon` | `landing.fork.flock_kin` | `epilogue.fork.flock_kin` |

Within each fork the two or three flags are mutually exclusive in practice (the decision node is shielded by a start variant once
`flag.beat.<fork>_decided` holds). Absent flag means the neutral variant: a legacy save that skipped an arc has none of them, so the
landing and epilogue must treat each fork as optional. The landing rows describe a non-combat ally cluster; the epilogue rows are
one or two sentences each, for the ending cards.
