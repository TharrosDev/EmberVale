# World requirements: W-Spec-2 (tag spec2)

What the world workstream must author for the Pale Concord (missions 19-22), Act III (23-25) and Act IV (26-30) specs in `tools/campaign/specs/spec2_*.py` and `legacy.py`. Ids are exact. Cell ids are real (checked against `scenes/regions/**`); positions are given against props that exist today, and "empty cell" means the scene is a 25-line shell (`fallen_choir`, `godfall`, `sundered_stair`, `shattered_rim`, `void_edge`, `processional`) where the world picks points along the road in `tools/region_spec_celestial.py` / `region_spec_pale_concord.py`.

How an interactable is authored (pattern from `wilds_north.tscn` ClueScrape): a prop with a `Collider` (StaticBody3D + shape) and a child `Node` using `src/Dialogue/DialogueComponent.cs` with `DialogueId` and `InteractId`. The prompt shown is the dialogue's speaker name (so the "prompt text key" of each row is `dlg.<name>.speaker`, or `pale.dlg.<name>.speaker` for Pale ones, already generated). Each `InteractId` must be authored exactly once project-wide.

## (a) New Interact ids (21)

| WR | InteractId | DialogueId | Cell (scene) | Where | Flag the objective raises (not the scene) | Quest |
| --- | --- | --- | --- | --- | --- | --- |
| WR-spec2-01 | `interact.pale.landing_lamp` | `dialogue.pale_landing_lamp` | `pale_concord.landing` | the existing `Nav/QuayLamp3` lamp post (south end of the quay, x -40 z 71) | `flag.beat.pale_landing_lit` | pale_door |
| WR-spec2-02 | `interact.pale.dusk_registry` | `dialogue.pale_dusk_registry` | `pale_concord.city` | NEW lectern prop beside the door of `Nav/ClockTower` (at about x 20 z -34) | `flag.beat.registry_read` | vesperhold |
| WR-spec2-03 | `interact.pale.lamp_signing` | `dialogue.pale_lamp_signing` | `pale_concord.city` | existing `Nav/StreetLamp1` (30.5, -21), the south side of the plaza next to the Lamplighter's beat | `flag.beat.lamp_signing` | vesperhold |
| WR-spec2-04 | `interact.pale.lamp_cradle` | `dialogue.pale_lamp_cradle` | `pale_concord.city` | existing `Nav/StreetLamp4` (39, -32), east lane by the Well | `flag.beat.lamp_cradle` | vesperhold |
| WR-spec2-05 | `interact.pale.lamp_queen` | `dialogue.pale_lamp_queen` | `pale_concord.city` | existing `Nav/StreetLamp3` (30.5, -54), the north edge, last lamp before the processional street | `flag.beat.lamp_queen` | vesperhold |
| WR-spec2-06 | `interact.sunspire.reading_table` | `dialogue.sunspire_reading_table` | `sunspire.library` | existing `Nav/GreatLibrary/ReadingDesk` (world 45, -57.6): put the Dialogue node on the desk's collider | `flag.beat.pages_read` | sundering_pages |
| WR-spec2-07 | `interact.sunspire.stacks_door` | `dialogue.sunspire_stacks_door` | `sunspire.library` | NEW door prop (ruin wall slab with a lead seam) in the back wall behind `Nav/GreatLibrary/ShelfBack`, about (45, -63) | `flag.beat.stacks_open` | deep_stacks |
| WR-spec2-08 | `interact.sunspire.seal_plinth_a` | `dialogue.sunspire_seal_a` | `sunspire.library` | vault, NEW black plinth, first of a triangle of three ringing the codex | `flag.beat.plinth_a` | deep_stacks |
| WR-spec2-09 | `interact.sunspire.seal_plinth_b` | `dialogue.sunspire_seal_b` | `sunspire.library` | vault, second plinth | `flag.beat.plinth_b` | deep_stacks |
| WR-spec2-10 | `interact.sunspire.seal_plinth_c` | `dialogue.sunspire_seal_c` | `sunspire.library` | vault, third plinth | `flag.beat.plinth_c` | deep_stacks |
| WR-spec2-11 | `interact.sunspire.sundering_codex` | `dialogue.sunspire_codex` | `sunspire.library` | vault centre, NEW lectern with an open book (the `TomeStand` model at (41, -48.5) is the style reference) | `flag.beat.codex_read`, raised by the quest when the first page is turned (`flag.beat.codex_cards_seen`, set by the dialogue), not by the touch | deep_stacks |
| WR-spec2-12 | `interact.celestial.landing_beacon` | `dialogue.celestial_landing_beacon` | `celestial.landing` | existing `Nav/ColdBrazier` (52, 64): add the Dialogue node to it; when `flag.beat.landing_beacon_lit` is set swap/enable a lit-fire light (FlagVisibility, see (f)) | `flag.beat.landing_beacon_lit` | celestial_landing |
| WR-spec2-13 | `interact.celestial.five_graves` | `dialogue.celestial_graves` | `celestial.landing` | NEW row of five low cairns plus a taller blank sixth stone on the terrace's north edge, about 12 m north of `Nav/ArrivalPillarW` (31, 50); the worn hollow of two knees in front of the sixth stone is a decal or flat prop | `flag.beat.graves_read` | celestial_landing |
| WR-spec2-14 | `interact.celestial.anchor_a` | `dialogue.celestial_anchor_a` | `celestial.godfall` | empty cell: first of three split standing stones with bronze collars in a rough line up the broken terrace | `flag.beat.anchor_a` | godfall_choir |
| WR-spec2-15 | `interact.celestial.anchor_b` | `dialogue.celestial_anchor_b` | `celestial.godfall` | second anchor, half sunk in rubble | `flag.beat.anchor_b` | godfall_choir |
| WR-spec2-16 | `interact.celestial.anchor_c` | `dialogue.celestial_anchor_c` | `celestial.godfall` | third anchor, the highest, on a spur over the void | `flag.beat.anchor_c` | godfall_choir |
| WR-spec2-17 | `interact.celestial.winch_a` | `dialogue.celestial_winch_a` | `celestial.sundered_stair` | empty cell: black iron drum winch on the west side of the broken flights | `flag.beat.winch_a` | sundered_stair |
| WR-spec2-18 | `interact.celestial.winch_b` | `dialogue.celestial_winch_b` | `celestial.sundered_stair` | matching winch on the east side (jammed with ash: a few ash-pile props) | `flag.beat.winch_b` | sundered_stair |
| WR-spec2-19 | `interact.celestial.entropy_stele_a` | `dialogue.entropy_stele_a` | `celestial.ash_throne` | NEW stele on the dais rim, in the ring of pillars (`PillarW1`/`PillarW2` side) | (optional objective) | throne |
| WR-spec2-20 | `interact.celestial.entropy_stele_b` | `dialogue.entropy_stele_b` | `celestial.ash_throne` | NEW stele, east side (`PillarE1`/`PillarE2`) | (optional objective) | throne |
| WR-spec2-21 | `interact.celestial.entropy_stele_c` | `dialogue.entropy_stele_c` | `celestial.ash_throne` | NEW stele, south side near `PillarSW`/`PillarSE`, on the approach to the dais | (optional objective) | throne |

All three steles must be reachable and legible before the player lights the Morthul brazier or after he falls: place them on the rim, never in the fight circle ("the dais floor stays bare").

## (b) New NPC and talkable placements

Dialogue-only placements need a `DialogueComponent`; NPCs use an existing `TemplateId`. All are idle (no schedule).

| WR | DialogueId | Cell | TemplateId / model | Visibility | Why |
| --- | --- | --- | --- | --- | --- |
| WR-spec2-22 | `dialogue.pale_count_stone` | `pale_concord.processional` (empty cell) | a standing-stone prop (black, ringed with carved names) at the palace end of the processional, within `location.pale.court_approach`; DisplayName "The Count-Stone" | always | Mission 21 fork F5. The dialogue sets `flag.fork.queen_released`/`flag.fork.queen_kept` and `flag.pale.court_open` |
| WR-spec2-23 | `dialogue.hollow_queen_parley` | `pale_concord.palace` | an empty carved chair prop with a `DialogueComponent` (no NPC), at about (-31, 47), beside `Brazier` (-28.5, 49); DisplayName "The Hollow Queen" | always | Pre-fight: the truce offer at corruption >= 60 and the F5 variants. Post-fight: the Talk objective of `quest.main.hidden` (variant `after`, sets `flag.testimony.queen`). Do not hide it after her death |
| WR-spec2-24 | `dialogue.rival_library` | `sunspire.library` | the Ashen Knight: `npc.mercenary` with the heaviest armour model available (any unique look is fine), helm under his arm, standing at the vault's far end beyond the plinths | `VisibleWhenFlagId = flag.beat.library_defended` | A7 parley, mission 24 Talk objective. Stays after the talk (its `after_trust` / `after_defy` variants) |
| WR-spec2-25 | `dialogue.assembly_marshal` | `celestial.landing` | `npc.dawnwarden_captain` (Marshal Iselle Hearne, plate armour) near the arrival pillars at about (38, 56) | `VisibleWhenFlagId = flag.beat.landing_held` | Mission 26 Talk objective; later variant `later` |
| WR-spec2-26 | `dialogue.choir_echo` | `celestial.fallen_choir` (empty cell) | a pale stone singer statue/figure in the nave, DisplayName "The Choir's Echo" | always (it is also hidden by the set-piece: see (d)) | Corruption >= 40 route of mission 27: answering sets `flag.beat.choir_answered` |
| WR-spec2-27 | `dialogue.rival_concourse` | `celestial.sundered_stair` (empty cell) | the Ashen Knight's echo: same body as WR-spec2-24 but grey-ember tinted, no face, at the head of the last flight | `VisibleWhenFlagId = flag.beat.stair_held`, `HiddenWhenFlagId = flag.beat.stair_echo` | A9; the quest's Talk objective raises `flag.beat.stair_echo` |
| WR-spec2-28 | `dialogue.rival_gate` | `celestial.knight_gate` | the Ashen Knight kneeling at the ring's mouth, greatsword planted point-down, helm on the ground; near `Brazier` | `VisibleWhenFlagId = flag.main.sundered_stair_done`, `HiddenWhenFlagId = flag.beat.gate_vigil_done` | A10 / fork F6. He disappears when the vigil is done (the fight boss then spawns from the brazier) |
| WR-spec2-29..39 | `dialogue.ally_<stem>` (11) | `celestial.landing` | each a small non-combat NPC cluster of 2-4 figures at the edges of the terrace, spread over the pickets (W side near `BrokenWall` (20, 66), E side near `ArrivalPillarE` (45, 49), S side) | `VisibleWhenFlagId` = the fork flag in the table below | Fork payoff at the landing: one speech and a once-only gift (`flag.boon.<stem>`) |

Ally table (stem, TemplateId to reuse, `VisibleWhenFlagId`, gift the dialogue gives):

| Stem | TemplateId | VisibleWhenFlagId | Gift |
| --- | --- | --- | --- |
| `dray_spared` | `npc.dawnwarden_captain` (the placed Dray) | `flag.fork.dray_spared` | `item.armor.warden_aegis` x1 |
| `dray_pressed` | `npc.dawnwarden_serjeant` | `flag.fork.dray_pressed` | `item.potion.health` x3 |
| `hjalvar` | `npc.clan_quartermaster` | `flag.fork.succession_hjalvar` | `item.ammo.arrows` x60 |
| `halvar` | `npc.clan_exile` | `flag.fork.succession_halvar` | `item.potion.stamina` x3 |
| `herd_slain` | `npc.hunter_master` | `flag.fork.herd_slain` | `item.ammo.arrows` x40 |
| `herd_calmed` | `npc.ashen_headwoman` | `flag.fork.herd_calmed` | `item.food.field_ration` x4 |
| `flock_exposed` | `npc.sunspire_pilgrim` | `flag.fork.flock_exposed` | `item.potion.health` x3 |
| `flock_turned` | `npc.archive_reader` | `flag.fork.flock_turned` | `item.material.warding_chalk` x3 |
| `flock_kin` | `npc.emberbound_seeker` | `flag.fork.flock_kin` | `item.material.emberbloom` x3 |
| `queen_released` | `npc.undying_lamplighter` | `flag.fork.queen_released` | `item.food.bread` x4 |
| `queen_kept` | `npc.traveller` (a girl with a black ribbon at her wrist) | `flag.fork.queen_kept` | `item.material.grave_dust` x2 |

(Item ids were checked against `data/items`; if a gift id is renamed the generator warns.)

Existing placements this spec relies on, no change needed: the four Undying NPCs in `pale_concord.city` (`dialogue.undying_steward`, `_lamplighter`, `_baker`, `_clockkeeper`, extended in place with recoil / F5 / freed variants), `dialogue.archive_keeper` at `embermarket` (extended with the key hand-over and a guild-member shortcut), `dialogue.sunspire_archivist` at `sunspire.library` (start variants appended in `legacy.py`), `dialogue.ash_throne` in `celestial.ash_throne` (start variants for F6).

## (c) Brazier and boss gates

| WR | Scene | Node | Change | Quest |
| --- | --- | --- | --- | --- |
| WR-spec2-40 | `scenes/regions/pale_concord/palace.tscn` | `Brazier/Summon` | add `RequiredFlagId = "flag.pale.court_open"` and `LockedPromptKey = "pale.boss.challenge_locked_count"` (row exists in the spec2 block). The flag is raised by the count-stone | queens_count, hidden |
| WR-spec2-41 | `scenes/regions/celestial/knight_gate.tscn` | `Brazier/Summon` | APPLIED: `RequiredFlagId = "flag.beat.gate_vigil_done"` and `LockedPromptKey = "celestial.ashen_knight.challenge_locked_vigil"` (row exists). This makes fork F6 unmissable. Without it the gate stays as shipped and the fork is simply optional (all consumers treat absence as neutral); but then the kneeling Knight NPC (WR-spec2-28) stays visible during the fight, so the orchestrator should drop WR-spec2-28's HiddenWhen if this is not applied | celestial |
| WR-spec2-42 | `scenes/regions/celestial/ash_throne.tscn` | `Brazier/Summon` | unchanged (`RequiredFlagId = flag.ashen_knight_defeated`) | celestial |

The Iron King / Storm Tyrant / Beast Lord / Prophet gates belong to spec1. `quest.main.celestial` starts on `flag.main.sundered_stair_done`; with WR-spec2-28's visibility the Knight cannot be fought before the Stair is lowered, which protects the one-shot Kill objective.

## (d) Set pieces (`src/Enemies/SetPieceSpawnComponent.cs`; Defend objectives need pressure, Kill objectives need bodies)

Every row sets a `ClearedFlagId`. Template ids exist in `data/enemies`. "T" = `TriggerFlagId`, "R" = `RequiredFlagId`, "D" = `DespawnFlagId`.

| WR | Cell | Trigger | Templates | Count x waves, delay | Cleared flag | Serves |
| --- | --- | --- | --- | --- | --- | --- |
| WR-spec2-43 | `pale_concord.processional` | R `flag.main.vesperhold_done`, trigger box on the road 40 m short of the count-stone (24 x 8 x 30) | `enemy.hollow_husk` | 5 x 1 wave (they stand in the road, passive-looking) | `flag.beat.husks_cleared` | queens_count Kill 5 husks. The quest is NOT sequential: the husks stand 12 to 28 m short of the stone, so they are fought before the Reach radius at the stone is entered, and the Kill (flag `flag.beat.husks_down`) must already be live. The stone stays silent until that flag |
| WR-spec2-44 | `sunspire.library` (vault) | T `flag.beat.stacks_open` | `enemy.ward_golem` | 3 x 1 wave, ringed in the vault | `flag.beat.golems_cleared` | deep_stacks Kill 3 ward golems |
| WR-spec2-45 | `sunspire.library` (vault) | T `flag.beat.seals_broken` | `enemy.arcane_echo`, `enemy.grave_shade` | 3 x 4 waves, delay 12 s (about 48 s of pressure) | `flag.beat.vault_raid_cleared` | deep_stacks Defend 60 s |
| WR-spec2-46 | `celestial.landing` | T `flag.beat.landing_beacon_lit` | `enemy.bone_knight`, `enemy.cinder_thrall`, `enemy.cultist` | 3 x 4 waves, delay 12 s | `flag.beat.landing_raid_cleared` | celestial_landing Defend 45 s |
| WR-spec2-47 | `celestial.fallen_choir` | T `flag.beat.choir_reached`, D `flag.beat.choir_answered` | `enemy.arcane_echo` | 4 x 1 wave, standing in the nave | `flag.beat.choir_echoes_cleared` | godfall_choir Kill 4 echoes. Also hide WR-spec2-26 with `HiddenWhenFlagId = flag.beat.choir_silenced` |
| WR-spec2-48 | `celestial.godfall` | T `flag.beat.anchors_lit` | `enemy.bone_knight`, `enemy.cinder_thrall`, `enemy.cultist` | 3 x 5 waves, delay 11 s (about 55 s) | `flag.beat.godfall_raid_cleared` | godfall_choir Defend 60 s |
| WR-spec2-49 | `celestial.sundered_stair` | T `flag.beat.stair_unsealed` | `enemy.stone_sentinel` | 3 x 1 wave, on the broken flights | `flag.beat.sentinels_cleared` | sundered_stair Kill 3 sentinels |
| WR-spec2-50 | `celestial.sundered_stair` | T `flag.beat.sentinels_down` | `enemy.bone_knight`, `enemy.cinder_thrall` | 3 x 3 waves, delay 12 s | `flag.beat.stair_raid_cleared` | sundered_stair Defend 45 s |

Honest note: Defend objectives count seconds in the location; the Kill counts are by template id, so an existing ambient spawn of the same template also advances them. None of the realms' ambient tables spawn husks or sentinels outside these places.

## (e) New map locations (`tools/gen_map_locations.py`; locale rows are the world's)

Quests reference these exact ids. Pale ones carry `required_flag=PALE_REVEALED`.

```python
add("pale_concord/processional", "pale.court_approach", "Landmark", ".", "The Court Approach",
    required_flag=PALE_REVEALED)
add("sunspire/library", "sunspire.deep_stacks", "Dungeon", "<the new vault node>", "The Deep Stacks",
    tier="Secondary", desc="The vault under the great library where the gods' own record is kept.")
add("celestial/fallen_choir", "celestial.fallen_choir", "Landmark", ".", "The Fallen Choir")
add("celestial/godfall", "celestial.godfall", "Landmark", ".", "Godfall")
add("celestial/sundered_stair", "celestial.sundered_stair", "Landmark", ".", "The Sundered Stair")
```

Names for `location.pale.court_approach.name` and its description must live under `pale.*` keys per the atlas gate. Optional extras (no quest names them, so not required): `celestial.broken_concourse`, `celestial.shattered_rim`, `celestial.void_edge` as Landmarks at their cell centres. Existing locations the quests use and that need no change: `location.pale.landing`, `.city`, `.palace`, `location.sunspire.library`, `location.embermarket.annexe`, `location.celestial.landing`, `.knight_gate`, `.ash_throne`.

Cell adjacency used for the prose (checked in `tools/region_spec_celestial.py`): the assault road runs landing (SW) to concourse (S) over the Knight's bridge to the gate (centre); fallen_choir (W), sundered_stair (E) and godfall (NE, beyond the stair) are spurs off the gate terrace; the throne is north of the gate. Pale: landing (S), city, processional, palace (N).

## (f) Props that change with flags

- WR-spec2-12 cold beacon: the `ColdBrazier` fire light/emitter visible only with `VisibleWhenFlagId = flag.beat.landing_beacon_lit` (and the unlit state hidden by `HiddenWhenFlagId` on the same flag).
- The three plinths (WR-spec2-08..10): a cracked-ring variant visible after `flag.beat.plinth_a/b/c` (FlagVisibility pair). The three anchors (14..16) likewise with a white-fire line prop after `flag.beat.anchor_a/b/c`. The two winches and the Stair: a lowered-flight prop visible with `flag.beat.stair_held`.
- The Pale quay lamp and the three city lamps: a brighter flame variant after their `flag.beat.*` flags is welcome but optional.
- No portal or door changes: the gate in the library forecourt and the Pale door already exist (`PaleConcordPortalAnchor`, `CelestialPortalAnchor`), driven by `flag.pale_concord_revealed` and `flag.celestial_gate_open`.

## (g) Story data authored by this spec

- `data/story/rules/spec2.json`: five rules, `rule.spec2.seals_broken`, `.anchors_lit`, `.winches_turned`, `.choir_silenced`, `.choir_answered`. They derive `flag.beat.seals_broken`, `flag.beat.anchors_lit`, `flag.beat.stair_unsealed` and `flag.beat.choir_passed`, which the quests read as `CODE_FLAGS`.
- `data/story/reactions/spec2.json`: nine barks for Kael, Wren and Tessa (quay lamp, registry, cradle lamp, queen released, queen kept, codex, five graves, stair echo, gate kneel). Text rows are `bark.spec2_*` in the spec2 locale block.

## Counts

50 numbered requirements (WR-spec2-01..50): 21 interact ids, 18 dialogue placements (7 named NPCs/talkables + 11 allies), 3 brazier rows (1 required, 1 optional, 1 unchanged), 8 set pieces. Unnumbered: 5 new map locations (+3 optional), 5 story rules and 9 companion reactions (already authored as data).

## Epilogue key scheme (for W-Core's `EndingSequence` follow-up)

The spec2 block carries 13 epilogue rows, one card per resolved fork, played after the ending's own epilogue card. Keys: `ending.epilogue.<fork>.<a|b|c>` with
`dray` (a = flag.fork.dray_spared, b = flag.fork.dray_pressed), `succession` (a = succession_hjalvar, b = succession_halvar), `herd` (a = herd_slain, b = herd_calmed), `flock` (a = flock_exposed, b = flock_turned, c = flock_kin), `queen` (a = queen_released, b = queen_kept), `vigil` (a = flag.rival.gate_kneel, b = flag.rival.gate_draw). A fork whose flag is absent plays nothing. The ally dialogues read spec1's `landing.fork.<name>` row for the nine Act I-II forks (`landing2.fork.queen_released` / `queen_kept` are the two Pale ones, which spec1 has no row for) and a one-line `landing2.gift.<stem>` for the gift. The nine Act I-II epilogue cards carry spec1's `epilogue.fork.<name>` text under the `ending.epilogue.*` keys the engine reads.
