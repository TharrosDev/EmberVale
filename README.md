# Embervale

> An original hybrid first/third-person, open-world fantasy action-RPG (swap views at any time) built in **Godot 4.7**
> with **C# (.NET 8)**. A dying world whose magic is failing — explore it, fight
> with weight, master a deep spell system, and let a **corruption** system reshape
> how that world reacts to you, all the way to one of two endings.

Embervale is a solo-built, component-driven game developed **incrementally** and
kept **buildable and playable at every commit**. A working ugly prototype always
beats a beautiful broken feature — every system here is real and usable in-game the
moment it lands, never theoretical scaffolding.

> **Private / personal project** — not for sale or public release.

---

## What is Embervale?

Nyth, the goddess of magic, is dead, and the **Weave** that carries all magic is
fading. Into that failing world comes corruption — an easier, darker path to power
that the world can feel. Embervale is the game built on that premise: a hand-crafted
realm with its own lore, factions and history, where your choices (and how corrupt
you let yourself become) bend the world's reactions, the spells you can claim, and
how your story ends.

Under the hood it is a **data-driven sandbox**: actors are composed from components,
systems talk over an event bus, and nearly all content (spells, enemies, quests,
loot, regions, dialogue…) is authored as Godot `.tres` resources — so new content is
new data, not new code.

## Design pillars

Drawn from [`docs/DESIGN.md`](docs/DESIGN.md):

- **Combat with weight.** Poise & stagger are real and per-actor; hits land with
  freeze-frames, shake and feedback; commitment and timing matter. *No button
  mashing* — stamina gates offense and recovery punishes over-commitment.
- **Magic as the fading Weave.** Every school is meant to be a viable build spine,
  none a trap. Magic isn't bought — lost spellcraft is *recovered*, and the dying
  Weave makes ordinary magic weaker while the corrupted path grows stronger.
- **Breadth without a class lock.** Melee, ranged and magic share one stat spine;
  the build is authored by the player over time, not chosen at creation.
- **A corruption spine.** A single 0–100 meter threads combat, dialogue, the world's
  hostility toward you, gated abilities, and the two endings.
- **A hand-crafted, reactive world.** Streamed regions, a day/night clock, weather,
  NPC routines, factions and emergent events — not a procedural wallpaper.

## Feature tour

Everything below is implemented and playable today.

### Combat feel
Hit-stop / freeze-frames, camera shake, weapon trails, directional hit reactions,
crit / block / stagger / parry screen feedback, **dodge i-frames**, **parry & riposte**
windows, **lock-on** with soft targeting and target cycling, input buffering, and an
anti-mash **stamina & poise economy**. Damage flows through one pipeline
(`CombatMath`) with armor mitigation, crits and poise damage.

### Magic — Spellcraft & the fading Weave *(the marquee system)*
- **Cast archetypes** — every spell is **Instant**, **Charged** (hold to empower) or
  **Channeled** (a sustained beam at a mana-per-second cost).
- **Six school identities**, each playing differently rather than just re-tinting:
  **Fire** stacking ignite · **Frost** chill→freeze · **Lightning** chain-to-nearby ·
  **Nature** regrowth heal-over-time · **Necrotic** lifesteal (corrupted) · **Arcane**
  wards.
- **School mastery** — casting a school ranks a persistent mastery track that
  empowers it; a "hard to master" ceiling, not just bigger numbers.
- **Reactive combos** — cross-school reads: *Shatter* (Lightning into a Chilled foe),
  *Thermal Shock* (Fire into Chilled), each consuming the status it triggers on.
- **The fading Weave** — a per-region magic-potency dial: as the Weave fails,
  ordinary casts weaken and cost more while **corrupted** casts strengthen and
  cheapen. Lost spells are **recovered** from tomes/teachers, not vendored.
- **Enemy casters** — foes cast back: they hold range, **kite** when crowded, lob
  spells, ward themselves and heal wounded allies — reusing the *same* casting system
  the player does.

### The world
Distance-based **region streaming** (with a settle-gated loading screen), hard region
transitions + **fast travel**, a **world map** and HUD **compass**, a day/night
**world clock**, **weather** (clear/rain/storm/fog…), ambient **encounters** by time
of day, and announced **world events** (raids, supply caches, champion hunts).

### Character & loot
Six playable **races** + character creation, XP / levels with a **perk** tree,
**Diablo-style loot** (rarities + prefix/suffix **affixes** rolled on drop),
**equipment** with stat bonuses, and **crafting** at typed stations (forge, workbench,
alchemy, cooking).

### A living world
NPCs walk daily **schedules** and react to threats and conversation; **factions** with
reputation drive who's hostile; **quests** with kill/collect objectives and
prerequisite chains; node-graph **dialogue** with conditional choices and side
effects (start quest, set flag, add corruption); and the **corruption** meter with its
tier-gated abilities, the dread vignette, a per-tier appearance hook, and **two
endings**.

### Meta-shell & save
Main menu (New Game / Continue / Load / Settings), **multi-slot saves** with rich
headers, a full UI suite (HUD, inventory, character screen, crafting, dialogue, map,
quest log, settings), and a **localization** layer — every player-facing string goes
through `Loc.T(...)`.

## Content at a glance

| Content | Count | Examples |
| ------- | ----: | -------- |
| Spells | 15 | Firebolt, Fireball, Flame Lance (charged), Frost Nova, Blizzard, Storm Conduit (channeled), Lesser Heal, Arcane Shield, Ember Siphon (corrupted) |
| Status effects | 6 | Burning (stacking), Chill, Frozen, Decay, Regrowth (HoT), Arcane Ward |
| Creatures | 29 | 26 authored as data + 3 with bespoke factories: wolves, husks, golems, elementals, clan warriors, the Iron King |
| Bestiary entries / AI profiles | 29 / 13 | one page per creature · brute, pack-flanker, shielded, caster, ambusher, prey… |
| Regions | 2 | The Ember Crown, Frostfang Reach |
| Factions | 8 | Villagers, Goblins, Outlaws, The Fallen, The Hollow, Iron Syndicate, Wildlife, Frostfang Clans |
| Races | 6 | Human, Draekyn, Grondar, Sylthari, Umbral, Valari |
| Items / weapons | 17 / 11 | potions, materials, leather/steel gear, relics |
| Quests / dialogues | 13 / 12 | the Warband arc, the Frostfang clan rank chain, the Elder, Kael |
| Recipes / perks | 7 / 6 | iron ingot, steel sword, health potion · Might, Warding |
| Weather / encounters / events | 5 / 31 / 3 | storm, fog · goblin patrols, clan patrols · raid, cache, hunt |
| Affixes / loot tables | 11 / 13 | Keen, Sturdy, Of the Tiger, Of Warding |

## Build & run

**Prerequisites:** the **.NET / Mono build** of Godot 4.7+ and the .NET 8 SDK.

```bash
# Build the C# solution (Godot also builds it on first run)
dotnet build Embervale.sln

# Open in the editor and press Play (boots scenes/Main.tscn → the sandbox)
godot --path . --editor

# Headless content gate — validates all authored content, exits 0/1, enters no gameplay
godot --headless --path . -- --validate

# Pure-logic unit tests (~300 across 43 files)
dotnet test tests/Embervale.Tests
```

> Editing C#? Run `dotnet build` **before** launching — running the project does not
> recompile and will otherwise load a stale binary.

### Sandbox controls

| Input | Action | | Input | Action |
| ----- | ------ |-| ----- | ------ |
| `W/A/S/D` | Move | | `Q` | Cast prepared spell |
| Mouse | Look / orbit camera | | `F` | Cycle prepared spell |
| `Shift` | Sprint | | Middle mouse | Lock-on (wheel cycles target) |
| `Space` | Jump | | `E` | Interact (pick up, talk) |
| `Ctrl` | Dodge roll (i-frames) | | `I` / `J` / `M` | Inventory / Journal / Map |
| Left mouse | Melee attack | | `1`–`5` | Hotbar |
| Right mouse | Block | | `Esc` | Pause menu |
| `V` | Swap first ↔ third person | | | |

**Gamepad**: left stick move · right stick look · RT attack · LT block · A jump · B dodge ·
L3 sprint · R3 lock-on · RB cast · LB cycle spell · X interact · D-Left swap camera ·
Y inventory · Start pause. Remapping is still to come.

Opening any blocking menu (inventory, dialogue, crafting, bestiary) **pauses the world**. Cinematic
holds — the boss entrance, the opening narration — take the controls but keep the world running.

**Debug shortcuts** (sandbox only): `H` heal dummy · `R` respawn dummy · `X` +50 XP ·
`P` +10 corruption · `K` shift goblin reputation · `F5`/`F9` quick save/load ·
`F1` dev console · `F3` debug overlay · `F4` profiler.

## Developer tooling

- **Dev console (`F1`)** — 40+ commands: `corruption`, `learn`, `mastery`, `weave`,
  `spawn <n> <templateId>`, `region`, `travel`, `quest`, `validate-all`, `repro`, …
- **Content validator** — cross-references, well-formedness, and dialogue/quest graph
  reachability; runs on boot, via `validate-all`, or headless with `--validate`.
- **Overlays** — `F3` debug HUD (FPS, raw stats, corruption, active event), `F4`
  profiler.
- **World integrity checker** — silently watches runtime invariants (every 5s).
- **Repro harness** — record a seed + command sequence, replay deterministically.
- **Unit suite** — pure-logic xUnit tests for the load-bearing math (combat, mastery,
  the Weave, status cadence, save-key policy, dialogue-graph analysis, …).

## Architecture in brief

Component-based entities (`IEntity` / `Entity` / `CharacterEntity` + `EntityComponent`),
an **event-driven** core (a synchronous `EventBus`), and **resource-driven** content
(`.tres` indexed by auto-loading databases). Four autoloads form the spine: `EventBus`,
`ServiceLocator`, `GameManager`, `SaveManager`. Any system that holds gameplay state
implements `ISaveable`. See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full
systems reference.

```
.
├── project.godot            # Engine config + autoload registration
├── Embervale.sln/.csproj    # C# solution (net8.0, Godot.NET.Sdk 4.7.0)
├── scenes/                  # Godot scenes (Main.tscn is the entry point)
├── data/                    # Resource-driven content (.tres), one folder per domain
├── docs/                    # Architecture, design, lore, roadmap
└── src/
    ├── Core/                # Autoloads (EventBus, ServiceLocator, GameManager, SaveManager), pooling, input
    ├── Entities/            # Entity / CharacterEntity / EntityComponent framework
    ├── Stats/               # Stats, modifiers, attribute resources
    ├── Movement/  Combat/    # Locomotion motor; damage pipeline, hit/hurtbox, combat feel
    ├── Magic/               # Spells, cast archetypes, schools, mastery, combos, the Weave, status effects
    ├── Items/  Loot/         # Inventory, equipment, pickups; loot tables + affixes
    ├── Progression/         # XP, levels, perks
    ├── Quests/  Dialogue/    # Objectives + log; node-graph conversations + story flags
    ├── World/  Npc/          # Clock, weather, encounters, events, regions/streaming, fast travel; schedules
    ├── Crafting/  Factions/  # Recipes + stations; reputation + standing-driven hostility
    ├── Corruption/          # The corruption meter, tiers, appearance + dialogue hooks, endings
    ├── Races/               # Playable races + character creation
    ├── Player/  Enemies/     # Hybrid FP/TP controller + camera rig; perception-FSM AI (+ caster branch), spawning
    ├── Interaction/         # InteractableComponent (raycast interact)
    ├── Save/                # ISaveable, SaveManager, persistence directors
    ├── Localization/        # Loc string layer
    ├── UI/  Debugging/  Analytics/   # HUD/menus/theme; console, validators, overlays; event logging
    └── Bootstrap/           # GameBootstrap (assembles the sandbox)
```

## Status & roadmap

The 21-phase **systems roadmap is complete** — Embervale is a data-driven sandbox
that *can express* the game. The [**production roadmap**](docs/PRODUCTION_ROADMAP.md)
(Phases 22+) now carries it to launch through six hard gates:

| Gate | Bar | Status |
| ---- | --- | ------ |
| **G0 — First Playable** | one region, a boss, the corruption hook | ✅ Done (Phases 22–28) |
| **G1 — Vertical Slice** | a 30–60 min slice that looks & plays shipped | 🟢 Built — needs a play-through + export |
| **G2 — Alpha** | every system/mechanic exists | 🟢 Started (Phases 34 + 34.5 ✅) |
| **G3 — Beta** | all content in | ⏭ |
| **G4 — Release Candidate** | zero blocker bugs | ⏭ |
| **G5/G6 — Launch / Live** | shipped; then patches & content | ⏭ |

**Now:** everything through Phase 34.5 is built. **G1 is one play-through and one export away** —
both need a human at the keyboard, which is the only reason the gate is still open.

|              | Phase                                          |
| ------------ | ---------------------------------------------- |
| ✅ **Done**    | 22–32 + G0 First Playable — corruption, meta-shell & localization, region streaming/map/fast travel, races & creation, the Ember Crown, the Iron King, combat feel, spellcraft & the Weave, models, the UI/HUD overhaul, audio, companions |
| ✅ **Done**    | 34 — Enemy & Creature Roster, complete (34A–34G): AI profiles, 26 creatures as data, per-school damage resistances, every magic school's on-hit identity, Ashen corruption variants, and the Ash Hunters' bestiary |
| ✅ **Done**    | 34.5 — the Frostfang Clans, complete (34.5A–34.5C): their faction and clan hold (Frostfang Reach's first settlement), raider/beast-tamer/shaman archetypes who ignore you until you give them a reason, and a rank chain you can earn your way up — or sell out |
| ▶ **Current** | 33 — Vertical Slice Assembly: 33A–33C ✅, **33D/33E built but never played**. Play it and export it and G1 closes |
| ✅ Done | 35 — dragons: 35A ✅ the body — hit zones as data (head ×2, tail ×0.6), a turn rate slow enough that getting behind one means something, and jaws/wing/tail arcs. 35B ✅ it flies — a timed swoop cycle that takes off, closes from the air and lands. 35C ✅ it breathes — a channeled cone spell, so being behind it is a defence. 35D ✅ it has a lair — a roost in Frostfang Reach, a territory it will not chase you out of, and a hoard worth taking. 35E ✅ a second dragon, built entirely from that pipeline: the Ash dragon, Morthul's, with necrotic breath and no safe side. 35F ✅ the third one talks — an Ancient dragon in a northern aerie that holds a conversation instead of a grudge, gives a quest, and will teach you a word no one alive can sell you. You can also just kill it and take the word off its stand. 35G ✅ dragon country — frost drakes now wander the Reach on their own, an elder drake turns up as a hunt worth the walk, and the scales all of them drop forge into the first armour in the game that answers the cold. **Phase 35 complete** |
| ✅ Done | 37 — housing, complete (37A–37D): 37A ✅ you can own something. A deed post in the Ember Crown sells the Ashfall Cottage to anyone who has run the guild's goblin bounty and can find 600 gold, and it tells you which of those two you are missing rather than just refusing. The holding stays bought across saves and puts itself on your map as somewhere to travel back to. 37B ✅ and now it holds your things. A chest opens a two-panel window — your pack on the left, the stash on the right — and what you leave there is still there after a reload, affixes and all. It will not open for anyone who has not bought the place, and it says so. 37C ✅ and now you can build in it. Craft a forge, a workbench, an alchemy table, a brazier, a crate or a banner, then hold it against the world as a ghost that goes green where your yard allows it and red where it does not, and tells you which of the two it is. Set it down, use it, or take it back up and get the kit back. 37D ✅ and now you can show off what you killed for. Display stands take anything Epic or better — the Iron King's heart, or a sword you rolled yourself — and it stands there glowing in your house, still there when you come home. The cottage itself is finally a cottage: its own low, wide building with a walled room holding the stash and the stands, a deed post at its own door, and a yard you can build in. |
| ✅ Done | 37.5 — the UI overhaul (37.5A–G). 37.5A ✅ the foundation. The game had been rendering every menu in Godot's default font on flat grey boxes; it now has three real typefaces (carved capitals for titles, a book serif for anything you actually read, and a clean face with aligned digits for stats), panels made of aged parchment behind an engraved brass frame, and surfaces that read as recessed or raised so a screen stops being a wall of rows. Item rarity was stock MMO neon that belonged to a different game — it is now an ash-world ramp that gets *brighter* as it gets rarer, so you can still rank a drop in a screenshot or without colour vision. Nothing has moved yet; this is the palette every screen after it is painted with. 37.5B ✅ the HUD. Your health, stamina and mana sit in cut stone; status effects are small tinted chips instead of five framed boxes; an objective that wants ten of something now shows a bar instead of making you read "3/10". Boss fights get the only ornamented frame in the game — brass corners and a row of pips that burn down as you break through its phases. And the nameplate finally tells you whether the thing you are aiming at actually wants to fight, which matters now that whole clans and at least one dragon will leave you alone unless you start it. 37.5C ✅ your pack. The inventory is a grid you can actually look at instead of a wall of text: every item sits in its own framed slot, shaped by what it is and coloured by how rare it is, and the good stuff is visible from across the screen. Pick something up and the panel beside it tells you what it does, what it is worth, and — the part that was missing entirely — whether wearing it is better or worse than what you have on, stat by stat, with arrows. Sort by rarity or weight or value, filter to just weapons, and the chest and the forge now look and work the same way your pack does. 37.5C2 ✅ your character. Until now the game never showed you your own numbers at all — you could not find out your Armor, your crit, or how much fire damage you shrug off, anywhere. There is a proper stat sheet now, and the defence lines tell you what the number actually does ("Armor 8 — 7.4% reduced") instead of leaving you to guess at a curve. Levelling shows as a card with your unspent points called out where you can see them, and perks are cards with their rank as pips, so a glance down the list tells you what you have finished, what you can afford, and what is still shut to you. 37.5D ✅ magic got its own book. Press `T` and the screen goes cold — ink and tarnished silver instead of ash and firelight, with a slow rune diagram turning behind the six schools and sigils drifting across the page. Every spell is a card telling you its cost, its cooldown and how it is cast, and each school shows how far your mastery of it has come. Two things that were in the game but invisible are finally on screen: the order `F` cycles your spells in, so you are not guessing what `Q` will throw, and the combos — lightning into a frozen enemy has been shattering them since long before this, and nothing ever told you. 37.5E ✅ the world screens. Your journal separates the story you are following from the errands you picked up along the way, with a bar under any goal that wants more than one of something. The map finally shows your waypoints as places on the map instead of just names in a list, writes the region names across the plot, and turns you into an arrow that points where you are looking — so you can tell which way is forward without walking. Conversations read like a page from a book now, with the choices as cards rather than a stack of grey buttons, and the bestiary fills in like a field journal: sealed pages you have not met, half-written ones you have only glimpsed, and full entries for the things you have killed enough of to understand. 37.5F ✅ the front door. The title screen is the most ornamented thing in the game now — brass at the corners and a slow gleam moving across the word EMBERVALE, like light across gold leaf. Your saves are proper cards: where you were, what level, how corrupted, how long you played and when you left, laid out so you can compare two slots at a glance instead of reading a sentence. And the little notifications that slide in during play no longer print themselves in whatever colour the event was — the words stay readable and a coloured edge tells you what kind of thing just happened. 37.5G ✅ making it work for everyone. You can size the text on its own without blowing up the whole interface, turn on a high-contrast mode that strips the parchment grain and thickens every frame, and pick a colour-vision setting that genuinely re-tints the interface so things that would blur together stay apart — not a filter that shows you what colourblindness looks like, but one that fixes it. Colour is never the only signal anyway: rarity gets brighter and its frame thicker, upgrades carry arrows, finished objectives carry ticks. The whole UI was also checked on a Steam Deck-sized screen, where two of the screens built earlier in this overhaul turned out to run right off the edge. 37.5H ✅ the sweep. Settings and character creation were rebuilt properly — you pick your race from cards showing what each one trades away, instead of a dropdown and a paragraph you had to read after choosing, and every accessibility option now explains in a line what it actually does. A coverage audit caught thirteen more widgets still wearing the old heavy frames, five of them on the HUD at the same time. And you can finally leave a game and go back to the title screen to start a new character, instead of closing the whole thing |
| ✅ Done | Art — the whole game re-skinned onto one CC0 artist (Quaternius). The square stopped being the same house three times: it has a cottage, an inn, a smithy and a farmhouse. 18 props were re-sourced, and **29 enemies that were coloured capsules now have real, animated bodies** — the dragons most of all. Every model in the game is public domain and the project owes nobody a credit line. |
| ✅ Done | 36 — boss framework: 36A ✅ a boss fight is data. Phases with HP thresholds, the escalation each stage brings, the abilities it hands over, the colour it burns when it winds up, and an enrage fuse so a boss cannot be out-waited — all authored in `data/bosses/*.tres` and named by one field on the archetype. The Iron King's numbers moved out of code without changing one of them; the three dragons, which until now were healthbars with no fight structure behind them, gained phases the moment the file existed. 36B ✅ and then he stopped being special: the Iron King is an ordinary authored enemy now, his 133-line bespoke factory is gone, and there is one path through the boss pipeline instead of two. 36C ✅ you can see it coming, and you can stop it: every wind-up draws a ring on the ground for exactly as long as the blow takes to arrive, and a stagger landed inside that window cancels the swing outright — or the spell, or the breath. It works both ways, so getting hit mid-swing now costs you yours too. 36D ✅ and he stopped fighting alone: wounded, he calls cinder thralls, and from a third health his cultists keep coming until you finish him. The arena is in on it too — it declares where they arrive and lights its ember vents as he escalates, all in its own scene file rather than in code. 36E ✅ every boss now arrives and dies properly: an intro lock and a named healthbar (the dragons had neither), a defeat beat, and a guaranteed reward that belongs to the boss that dropped it. That last part was overdue — killing a dragon used to re-open the Iron King's ember offer and hand you another 25 corruption, every time. **Phase 36 complete.** |
| ⏳ In progress | 38 — economy and vendors: 38A ✅ money finally goes both ways. Until now gold only came in — you could earn it, and the one thing in the world that would take it was a cottage deed. A shop is now authored data: what it stocks, what it charges over an item's worth, and what fraction of that worth it pays you back. The window is the chest window with prices — your pack on one side, the wares on the other, every row showing what it costs and greying out with a reason when you cannot have it. Selling reads the same value the game already uses for an item, so a sword you rolled with two good affixes is worth more than a plain one without anyone authoring a second price for it. A merchant will not buy your quest items, and it will not take something off you for nothing rather than paying you zero and keeping it. The gap between what a shop charges and what it pays is deliberate and enforced twice over: getting it backwards is infinite money, so the build refuses to start if a shop is authored that way. ⚠️ **The three traders in town are still just conversations** — an in-world character can only carry one thing to interact with, and theirs is already a talk, so deciding whether trading replaces that or hangs off a "show me your wares" line is Phase 38E's job. Until then a shop opens from the developer console. 38B ✅ and shops stopped being vending machines. A merchant now has a *number* of each thing: five potions, eight bundles of herbs, and when they are gone they are gone until the next delivery — the row stays on the shelf, greyed, telling you it will be back rather than quietly vanishing. Ore and scrap are still endless, because a materials stall running dry only makes crafting feel like a shopping errand. And there is a rotating case of gear: a handful of rolled pieces that change every restock and get better as you level, so walking back into a shop after a few days is worth doing. The world also learned what a *day* is — it had only ever known the hour, which is why nothing before this could say "come back tomorrow". What is left on a shelf, and what the case happens to be holding, survive a reload; you cannot save and reload until the case offers you something legendary. 38C ✅ and now the town knows who you are. How a faction feels about you shows up on the price tag: run errands for the villagers and Aldreth knocks money off, kill enough of them and he charges you extra, and past a point he will not deal with you at all and says so at the door. The window tells you your standing and what it is doing to his prices, so a number that moved is never a mystery. Money finally has somewhere to go, too. Merchants carry their own coin — Aldreth has 250 gold, and once you have sold him that much you are done until he restocks, so a field of corpses is worth several trips rather than one. And fast travel now costs a fee: a little for a hop across the same region, more for crossing a realm, and **nothing at all to travel home to a house you own** — the first ongoing reason to own property rather than a one-off purchase. |

> A phase is "done" when it works in-game **and** round-trips through save/load.

## Documentation

| Doc | What it covers |
| --- | -------------- |
| [`CLAUDE.md`](CLAUDE.md) | Working agreement, conventions, gotchas, and content recipes |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Full systems reference |
| [`docs/DESIGN.md`](docs/DESIGN.md) | Design bible — pillars and intent |
| [`docs/LORE.md`](docs/LORE.md) | World/story bible |
| [`docs/PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) | The Phase 22+ plan and gates |
| [`docs/SESSION_PLAYBOOK.md`](docs/SESSION_PLAYBOOK.md) | Per-phase sub-task breakdown |
| [`docs/VERTICAL_SLICE_PLAN.md`](docs/VERTICAL_SLICE_PLAN.md) | Phase 33D build plan — the slice arc, gaps, task order |
| [`docs/IDS.md`](docs/IDS.md) | Content id naming scheme + audit |
| [`docs/STAGE_A_STATUS.md`](docs/STAGE_A_STATUS.md) | Stage-A (Phases 22–25) integration sign-off |
