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
| `V` | Swap first ↔ third person | | `Y` | Whistle up / step off your mount |

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
| ✅ Done | 35 — dragons: hit zones, flight, breath weapons, lairs — [what shipped](#phase-35--dragons) |
| ✅ Done | 37 — housing (37A–37D): deeds, a stash, placeable props, trophy stands — [what shipped](#phase-37--housing) |
| ✅ Done | 37.5 — the UI overhaul (37.5A–G): an original fantasy interface across every screen — [what shipped](#phase-375--the-ui-overhaul) |
| ✅ Done | Art — the whole game re-skinned onto one CC0 artist (Quaternius). The square stopped being the same house three times: it has a cottage, an inn, a smithy and a farmhouse. 18 props were re-sourced, and **29 enemies that were coloured capsules now have real, animated bodies** — the dragons most of all. Every model in the game is public domain and the project owes nobody a credit line. |
| ✅ Done | 36 — boss framework: a boss fight is authored data, telegraphed and interruptible — [what shipped](#phase-36--the-boss-framework) |
| ⏳ In progress | 38 — economy, vendors and services (38A–38O of 38A–38V done) — [what shipped](#phase-38--economy-vendors-and-services) |

> A phase is "done" when it works in-game **and** round-trips through save/load.

## Documentation

| Doc | What it covers |
| --- | -------------- |
| [`CLAUDE.md`](CLAUDE.md) | Working agreement, conventions, gotchas, and content recipes |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Full systems reference |
| [`docs/DESIGN.md`](docs/DESIGN.md) | Design bible — pillars and intent |
| [`docs/LORE.md`](docs/LORE.md) | World/story bible |
| [`docs/PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) | The Phase 22+ plan and gates |
| [`docs/playbook/`](docs/playbook/README.md) | Per-phase sub-task breakdown |
| [`docs/VERTICAL_SLICE_PLAN.md`](docs/VERTICAL_SLICE_PLAN.md) | Phase 33D build plan — the slice arc, gaps, task order |
| [`docs/IDS.md`](docs/IDS.md) | Content id naming scheme + audit |
| [`docs/STAGE_A_STATUS.md`](docs/STAGE_A_STATUS.md) | Stage-A (Phases 22–25) integration sign-off |


---

## What shipped, phase by phase

*Split out of the phase table in the agent-ergonomics pass — five table cells of up to 11,582
characters each, one line apiece, made every search of this file expensive.*

### Phase 38 — economy, vendors and services

- 38A ✅ money finally goes both ways. Until now gold only came in — you could earn it, and the one thing in the world that would take it was a cottage deed. A shop is now authored data: what it stocks, what it charges over an item's worth, and what fraction of that worth it pays you back. The window is the chest window with prices — your pack on one side, the wares on the other, every row showing what it costs and greying out with a reason when you cannot have it. Selling reads the same value the game already uses for an item, so a sword you rolled with two good affixes is worth more than a plain one without anyone authoring a second price for it. A merchant will not buy your quest items, and it will not take something off you for nothing rather than paying you zero and keeping it. The gap between what a shop charges and what it pays is deliberate and enforced twice over: getting it backwards is infinite money, so the build refuses to start if a shop is authored that way. The three traders in town were still just conversations at this point — an in-world character can only carry one thing to interact with, and theirs was already a talk — so a shop opened from the developer console until 38E.

- 38B ✅ and shops stopped being vending machines. A merchant now has a *number* of each thing: five potions, eight bundles of herbs, and when they are gone they are gone until the next delivery — the row stays on the shelf, greyed, telling you it will be back rather than quietly vanishing. Ore and scrap are still endless, because a materials stall running dry only makes crafting feel like a shopping errand. And there is a rotating case of gear: a handful of rolled pieces that change every restock and get better as you level, so walking back into a shop after a few days is worth doing. The world also learned what a *day* is — it had only ever known the hour, which is why nothing before this could say "come back tomorrow". What is left on a shelf, and what the case happens to be holding, survive a reload; you cannot save and reload until the case offers you something legendary.

- 38C ✅ and now the town knows who you are. How a faction feels about you shows up on the price tag: run errands for the villagers and Aldreth knocks money off, kill enough of them and he charges you extra, and past a point he will not deal with you at all and says so at the door. The window tells you your standing and what it is doing to his prices, so a number that moved is never a mystery. Money finally has somewhere to go, too. Merchants carry their own coin — Aldreth has 250 gold, and once you have sold him that much you are done until he restocks, so a field of corpses is worth several trips rather than one. And fast travel now costs a fee: a little for a hop across the same region, more for crossing a realm, and **nothing at all to travel home to a house you own** — the first ongoing reason to own property rather than a one-off purchase.

- 38D ✅ and the town has people worth walking up to. The innkeeper rents you a bed: pay, sleep, and you wake at eight with your health, stamina and mana full and a day gone by — which also means the shops have restocked. There is a smith's apprentice who will actually **teach** you something, and that is a bigger deal than it sounds: until now every recipe you could ever craft was one you woke up knowing on day one, because nothing in the game had any way to teach one. Drakescale mail is the first recipe you have to be shown. The guild will open you a vault for a hundred gold — paid once, then yours forever, and it holds sixty slots that survive every reload. And the stablemaster will sell you a horse; the riding comes in the next phase, but the horse is bought, paid for and remembered. What is *not* here is a repairsmith, and on purpose: nothing in the game wears out yet, and whether gear should is a design decision Phase 40 makes rather than one we quietly assume.

- 38E ✅ and the shops finally have people standing in them. Aldreth, Bryn and Mirela sell to you now, and you get there the way you would get there in life — you talk to them and ask what they have. That mattered more than it sounds: a character in this game can only carry one thing to interact with, and the smith and the apothecary already carry conversations with real quests in them, so the choice was to throw those away for a shop counter or to let trade be something you ask a person for. It is a line in the conversation, under whatever they wanted to talk to you about and above goodbye. Bryn stocks metal and keeps one good sword at a time; Mirela is the only reliable healing in the realm and prices it like someone who knows it; and Bryn pays better than Aldreth does for scrap and ore, because a smith knows what metal is worth — the first time in the game that *who* you sell to has changed what you get. Aldreth also stopped apologising for having nothing to do and started talking about the roads: the salt cart is nine days late, and the last driver came back white as a sheet. 38F–

- 38J ✅ and a merchant became a person with a trade rather than a shelf with prices. Each one deals in particular things and is expert in some of them — take a bundle of pelts to the tanner and he pays over the odds for them, take them to the bookseller and he has no use for them at all and says so. Sell one merchant forty goblin hides and she stops wanting them: the price falls off with each one until her next delivery, and dumping the lot in one go no longer beats selling sensibly. Some shelves are held back — behind a reputation you have to earn, behind something you have to do first, or behind money you can put into the business itself, which is permanent and raises what she can afford to pay you forever after. And the shops keep hours now: the forge is cold at two in the morning and the smith tells you so instead of the option simply doing nothing, while a wayfarer turns up one day in four with things nobody in town sells.

- 38K ✅ and the town got a second district. The Embermarket is one street south of the square — ten stalls around a crossroads, a bell tower you can see from the seam between the two, a well and benches off to one side, braziers throwing embers after dark, and a notice board that tells you what the town thinks of short-weighing. It was built twice: the first version was correct and completely lifeless, and the second is what a market looks like.

- 38L ✅ and twelve merchants stand in it. A provisioner, a fishmonger who is gone by one in the afternoon, a weaver, a bookseller with something under the counter he will not show a stranger, a collier, an ironmonger, a tanner, a jeweller who opens late and knows exactly what your loot is worth, a herbalist who walks for what the apothecary in the square only brews, a joiner who sells the furniture kits you could previously only make yourself, and two travelling traders on different cycles who bring goods from the other end of the realm. You reach every one of them the way you reach anyone — you walk up and talk. Making that mean anything needed **the shops to have something to sell**: the game had twenty-six items in it, and half a dozen trades came down to a single object each, so bread, salt, cloth, charcoal, pitch, books, spice, sapphires and eighteen other things were written before a single merchant was placed.

- 38M ✅ and the road north costs money. The wardens at the Crossway Post take twenty-five gold off anyone walking between the Ember Crown and the Frostfang Reach, and the gate tells you the price before you touch it — what they want, what you are carrying, and whether your papers are already in order. There are two ways to stop paying it. The warden sergeant sells a permit: two hundred and fifty gold once, and the road is yours for good, which is ten crossings before it pays for itself. Or you can have a quiet word with the man standing to the other side of the gate, who will let you through this once for ten — and the village hears about it. Do that three times and you are no longer someone the valley trusts, and every merchant in town starts pricing accordingly, which is a bill that never stops arriving. Fast travel is untouched on purpose: a jump across the realm already costs a fee, and paying twice for one journey would make the map the only sensible way to travel. As it stands the road is the cheap way and the wardens are competing with it.

- 38M2 ✅ and the gate is a real place. The Crossway Post sits at the top of the road north — two palisades either side of the way with the road running through the gap between them, watch towers over it, the wardens' compound on one side and a caravan yard on the other with carts, crates and a cook-fire for the people waiting. The crossing into the Frostfang Reach happens *there* now, on the far side of the gate, instead of a few paces from where you wake up: you walk the road, you pass the wardens, and you settle up before you cross. Two other things changed under the hood. A region now loads all at once rather than a piece at a time as you approach, so a district no longer appears as you crest the road toward it and the people in it are going about their business whether you are looking or not. And the world holds three times as many enemies at once as it used to, so the wilds between the town and the gate are somewhere you have to mean to cross.

- 38N1 ✅ and the valley has somewhere that *makes* something. The Emberdeep Mine is east past the arena — a yard of carts and crates under a timbered adit cut into the rock, ore heaps, pit props, a cook-fire for the shift. Two people work it, and they are the first pair in the game whose prices point in opposite directions. Bregan Holt weighs ore and sells it cheaper than anyone in the realm, because it comes out of the ground twenty feet behind him; bring him bread and he has no use for it. Marta Quill keeps the company store and pays over the odds for every loaf, sack of grain and block of salt on the mountain, because nothing grows in a hole in the ground — and she will not touch your ore. Walking goods between the two markets is now a thing the world has an opinion about. There is also a new command for the curious: ask the game to print the best buy-low/sell-high routes in the realm and it will, along with an honest note that none of them turns a profit yet, and what has to change before one does. The wilds also went back to holding five enemies at once rather than fifteen.

- 38N2 ✅ and the valley has a coast. Tarn's Landing sits on the water west of the wilds — a warehouse and two jetties standing out into a dark tarn, curing sheds and drying racks along the shingle, a fire on the shore. Wenna Tarn sells fish cheaper than anywhere in the realm because she pulls it out of the water behind her, and everything else the hamlet owns came up the road: salt, rope, cloth, iron. Odger Vane the chandler buys all of that at a good price and will not touch your fish, and tells you why before you ask. Between him and the quartermaster at the mine, carrying goods across the valley is now a thing with two ends and an opinion at each. Seven new things to trade in came first — fresh catch, salted eel, roe, cordage, sailcloth, caulking pitch and a card of hooks — because a fishing village with two kinds of fish in it is a shop with a theme. Two of the merchants in the market up the hill also stopped wearing a t-shirt and trainers.

- 38O ✅ and there is somewhere the valley does not talk about. Hollowreach is a wharf further down the same coast, reached by a waystone on a road through dead pines: two braziers on wet boards, a hull moored against the dock, and a locker that never shuts. Sedge Marrow trades only after dark and only in small bright things with no paper on them; Coyle Ferrin takes anything at all and pays less for it, and half the coast hears about it by morning. They are the only two people in Embervale who will buy what nobody else will touch — offer a stolen stone to the Embermarket's jeweller and she will point you somewhere quieter rather than name a trade. Selling here is worth real coin and costs real standing: the outlaws think better of you, the valley thinks worse, and the valley is who prices your bread. Carrying it back up the road is the other half of the bargain — the Crossway wardens will search you, take what they find, and hold it at the impound until you pay the fine, which is charged by the item.

- 38G ✅ and where you sell finally matters more than who you sell to. The mine is awash in ore, metal and charcoal and short of everything that has to be carried up the road to it; Tarn's Landing is the mirror, thick with fish and short of iron. Prices at those two places move with it, in both directions, and the merchant tells you which way before you trade. That makes carrying goods across the valley worth doing for the first time in the game: buy salted eel on the coast where they cannot give it away and sell it at the company store where nothing grows, then walk copper ore back the other way. It is a modest living rather than a fortune — a merchant still runs out of coin, and dumping twenty of anything still tanks the price — but the road between two settlements now pays, which every earlier version of the economy could prove was impossible.

- 38S ✅ and you can argue about the price. Three merchants will hear you out — Aldreth in the square, Gilda in the market, Marta up at the mine — and each of them once a day. Talk one of them round and everything they own is cheaper and everything you are carrying is worth more to them until tomorrow. Try it and get it wrong and they take it badly: the valley thinks a little less of you, which is a bill that arrives again at every other counter in town, and the same merchant will not hear another word about it today. You cannot reload your way to a better mood — they were always going to say what they said on that day, and the only thing a reload gives back is the chance not to ask. Aldreth is the easy one and the cheapest to annoy; Marta says no seven times in ten and remembers it hardest, which is the trade for her being the best payer in the realm.

- 38T ✅ and the roads can go wrong. Every few days the valley's trade turns over: the seam at the Emberdeep floods and ore is suddenly dear at the mine that digs it, the boats stay in at Tarn's Landing, or a supply train finally gets through and the thing everyone was short of goes cheap for a week. The caravan board at the Crossway posts what has happened and how many days it has left, so you can read the news before you load the cart rather than after. A shortage is also something you can do something about: haul enough of what they are short of up the road and you break it yourself, which pays twice — once at the counter, and once because you were the one who fixed it. None of it can be reloaded away. The same day always brings the same news, whatever save you open it in.

- 38U ✅ and no price is a mystery any more. Hover the gold figure on any row and the number comes apart into the reasons for it: what the thing is worth anywhere, what this place thinks of it, whether that opinion is the character of the town or a shortage that will be over by Thursday, the merchant's own margin, how they take to you, whether it is their trade, and whether you talked them down this morning. Sell a stack to someone already drowning in them and the line says so — and says the real sum, because their appetite falls as they count. A broker's window shows what her shelf will fetch and what she keeps. A master's commission splits into his fee and each material you did not bring, so walking in with half the recipe visibly saves you something. Even a waystone says why the jump costs fifteen and not forty, or nothing at all because the roof is yours.

- 38V ✅ and the phase closes. Nothing here is visible in game, which is the point of it: the economy's rules are now written down where someone would look for them, and — more usefully — they can be *re-proved*. `tools/negative_tests.py` breaks forty-two of them one at a time, checks that the right refusal fired, and puts the realm back. That matters because a rule proved once quietly stops being true: the merchant-spread check tightened twice after the day it was tested, and nothing re-checked it until now.

- 37E ✅ and the house is a home. What you could buy before was a sealed cottage-shaped rock beside a
roofless pen of grey walls; what you buy now is a plot of your own east of the market, with a stone
path up to a plaster-and-timber cottage you walk into. Inside there is a bed with linen on it, a
table you could sit at, a bookcase, a cabinet, shelves, a cauldron under the chimney and a chandelier
overhead — lit, warm, and yours. Outside: a well, a fenced garden, a woodpile, and a forge, a
workbench and an alchemy bench standing ready. The bed is free and it is the point — the inn takes
ten gold a night forever, and the house takes six hundred once.

- 37F ✅ and the Iron King has somewhere worth fighting him. The arena was a flat grey slab with three
walls and a cylinder for a fire; it is now a ruined stone amphitheatre — a flagstone floor, a ring
wall of real masonry, a broken outer tier, braziers burning on the rim, banners on the stone and dead
pines standing over it all. The fighting circle itself is still deliberately bare, because anything
you can mistake for cover in a boss fight is worse than nothing. Also fixed: the boss now has his
texture back, and two crashes that turned out to be the same crash.

- 37G ✅ and the Iron King looks like one. He was, for longer than anyone noticed, a man in an orange
bomber jacket and green shorts — the First Flamebearer, dressed for a coffee run. He is now a crowned
king in mail and plate, head and shoulders above any man in the realm, waiting in the middle of his
arena.

### Phase 39 — mounts

- 39A ✅ and the four hundred gold you gave the stablemaster finally buys something. Press `Y` and a
horse comes; press it again and you swing down. Riding is faster than walking, and holding sprint is
a gallop the horse pays for — about five seconds of it, then it drops to a walk however hard you ask,
and it will not run again until you have let it stop asking. The horse is a real animal at real scale,
you sit on it with the reins in your hands, and in first person you look out over its ears. It is
still there when you reload.

- 39B ✅ and you can fight from the saddle. A blow struck at a gallop lands harder — a walking horse
adds nothing, so the stamina you spend running is the same stamina that makes the first hit count.
You cannot dodge-roll off a horse, which is the one thing riding takes away. Waypoints stopped
charging you for a jump across your own realm when you own the horse that would have carried you —
crossing into Frostfang still costs, because that is a longer road than a horse fixes. And a hit
taken in the saddle no longer drops you through the animal you are sitting on.

- 39C ✅ and the ground can have steps in it again. Until now the whole realm was flat by necessity —
a kerb the height of your shin was an invisible wall you walked into, so every raised surface in the
game had been either deleted or flattened to a painted-on skin. You step up now, as high as the
townspeople's own paths allow, and the market plaza is a real dais again: you walk up onto it, the
well and the benches and the cook-fire stand on it, and it is the first piece of raised ground in
Embervale. Climbing and swimming are deliberately not here — nothing in the world needs them yet,
and the conditions under which they would be built are written down.

### Phase 39.5 — the map and the HUD

- 39.5B ✅ and the HUD tells you where you are. There is a minimap now — there never was one — in the
bottom-right corner, north-up, showing the ground you have walked and the places you have found
within about fifty metres, with your arrow turning inside it. It is the same map you open with `M`,
drawn by the same code from the same places, so the two cannot disagree about where anything is. The
quest tracker will now tell you how far your objective is and which way, and you can choose which
quest it follows from the journal instead of it always picking whichever one it happened to find
first. When something hits you, a short arc marks the side of the screen it came from. When you
finish an objective, the game says so. And the whole HUD now gets out of the way when you open a
menu — it used to sit on top of the pause screen and your inventory, still offering you a key to
press that the paused game would have ignored.
- 39.5A ✅ and the map knows what is in the world. It used to show three kinds of thing, because
those were the only three that had a position: the region, the cells you had walked into, and the
waystones. It could not show you a blacksmith, because nothing in the game recorded where one was.
Now sixty-three places across every cell in the realm are on it — every shop, every service, every
settlement, the mine, the arena, the roosts — and each one is pinned to the actual stall or counter
or keeper it names, so a pin cannot drift from the thing it points at. You can drag the map around
and zoom it, and it shows you more as you go in: the towns at any distance, the dungeons and gates
and waystones closer, the individual merchants once you are looking at a single market. You can
search it — type "blacksmith" and it finds The Iron Anvil, type a merchant's name and it finds their
stall. You can filter by what you care about, click a place to read what it offers and how far away
it is and which way, right-click anywhere to leave yourself a mark, and travel from it. The top of
the screen tells you where you are standing, and the land you have walked is drawn underneath it all.
What you have not found is not there yet.

### Phase 36 — the boss framework

- 36A ✅ a boss fight is data. Phases with HP thresholds, the escalation each stage brings, the abilities it hands over, the colour it burns when it winds up, and an enrage fuse so a boss cannot be out-waited — all authored in `data/bosses/*.tres` and named by one field on the archetype. The Iron King's numbers moved out of code without changing one of them; the three dragons, which until now were healthbars with no fight structure behind them, gained phases the moment the file existed.

- 36B ✅ and then he stopped being special: the Iron King is an ordinary authored enemy now, his 133-line bespoke factory is gone, and there is one path through the boss pipeline instead of two.

- 36C ✅ you can see it coming, and you can stop it: every wind-up draws a ring on the ground for exactly as long as the blow takes to arrive, and a stagger landed inside that window cancels the swing outright — or the spell, or the breath. It works both ways, so getting hit mid-swing now costs you yours too.

- 36D ✅ and he stopped fighting alone: wounded, he calls cinder thralls, and from a third health his cultists keep coming until you finish him. The arena is in on it too — it declares where they arrive and lights its ember vents as he escalates, all in its own scene file rather than in code.

- 36E ✅ every boss now arrives and dies properly: an intro lock and a named healthbar (the dragons had neither), a defeat beat, and a guaranteed reward that belongs to the boss that dropped it. That last part was overdue — killing a dragon used to re-open the Iron King's ember offer and hand you another 25 corruption, every time. **Phase 36 complete.**

### Phase 37.5 — the UI overhaul

- 37.5A ✅ the foundation. The game had been rendering every menu in Godot's default font on flat grey boxes; it now has three real typefaces (carved capitals for titles, a book serif for anything you actually read, and a clean face with aligned digits for stats), panels made of aged parchment behind an engraved brass frame, and surfaces that read as recessed or raised so a screen stops being a wall of rows. Item rarity was stock MMO neon that belonged to a different game — it is now an ash-world ramp that gets *brighter* as it gets rarer, so you can still rank a drop in a screenshot or without colour vision. Nothing has moved yet; this is the palette every screen after it is painted with.

- 37.5B ✅ the HUD. Your health, stamina and mana sit in cut stone; status effects are small tinted chips instead of five framed boxes; an objective that wants ten of something now shows a bar instead of making you read "3/10". Boss fights get the only ornamented frame in the game — brass corners and a row of pips that burn down as you break through its phases. And the nameplate finally tells you whether the thing you are aiming at actually wants to fight, which matters now that whole clans and at least one dragon will leave you alone unless you start it.

- 37.5C ✅ your pack. The inventory is a grid you can actually look at instead of a wall of text: every item sits in its own framed slot, shaped by what it is and coloured by how rare it is, and the good stuff is visible from across the screen. Pick something up and the panel beside it tells you what it does, what it is worth, and — the part that was missing entirely — whether wearing it is better or worse than what you have on, stat by stat, with arrows. Sort by rarity or weight or value, filter to just weapons, and the chest and the forge now look and work the same way your pack does.

- 37.5C2 ✅ your character. Until now the game never showed you your own numbers at all — you could not find out your Armor, your crit, or how much fire damage you shrug off, anywhere. There is a proper stat sheet now, and the defence lines tell you what the number actually does ("Armor 8 — 7.4% reduced") instead of leaving you to guess at a curve. Levelling shows as a card with your unspent points called out where you can see them, and perks are cards with their rank as pips, so a glance down the list tells you what you have finished, what you can afford, and what is still shut to you.

- 37.5D ✅ magic got its own book. Press `T` and the screen goes cold — ink and tarnished silver instead of ash and firelight, with a slow rune diagram turning behind the six schools and sigils drifting across the page. Every spell is a card telling you its cost, its cooldown and how it is cast, and each school shows how far your mastery of it has come. Two things that were in the game but invisible are finally on screen: the order `F` cycles your spells in, so you are not guessing what `Q` will throw, and the combos — lightning into a frozen enemy has been shattering them since long before this, and nothing ever told you.

- 37.5E ✅ the world screens. Your journal separates the story you are following from the errands you picked up along the way, with a bar under any goal that wants more than one of something. The map finally shows your waypoints as places on the map instead of just names in a list, writes the region names across the plot, and turns you into an arrow that points where you are looking — so you can tell which way is forward without walking. Conversations read like a page from a book now, with the choices as cards rather than a stack of grey buttons, and the bestiary fills in like a field journal: sealed pages you have not met, half-written ones you have only glimpsed, and full entries for the things you have killed enough of to understand.

- 37.5F ✅ the front door. The title screen is the most ornamented thing in the game now — brass at the corners and a slow gleam moving across the word EMBERVALE, like light across gold leaf. Your saves are proper cards: where you were, what level, how corrupted, how long you played and when you left, laid out so you can compare two slots at a glance instead of reading a sentence. And the little notifications that slide in during play no longer print themselves in whatever colour the event was — the words stay readable and a coloured edge tells you what kind of thing just happened.

- 37.5G ✅ making it work for everyone. You can size the text on its own without blowing up the whole interface, turn on a high-contrast mode that strips the parchment grain and thickens every frame, and pick a colour-vision setting that genuinely re-tints the interface so things that would blur together stay apart — not a filter that shows you what colourblindness looks like, but one that fixes it. Colour is never the only signal anyway: rarity gets brighter and its frame thicker, upgrades carry arrows, finished objectives carry ticks. The whole UI was also checked on a Steam Deck-sized screen, where two of the screens built earlier in this overhaul turned out to run right off the edge.

- 37.5H ✅ the sweep. Settings and character creation were rebuilt properly — you pick your race from cards showing what each one trades away, instead of a dropdown and a paragraph you had to read after choosing, and every accessibility option now explains in a line what it actually does. A coverage audit caught thirteen more widgets still wearing the old heavy frames, five of them on the HUD at the same time. And you can finally leave a game and go back to the title screen to start a new character, instead of closing the whole thing

### Phase 37 — housing

- 37A ✅ you can own something. A deed post in the Ember Crown sells the Ashfall Cottage to anyone who has run the guild's goblin bounty and can find 600 gold, and it tells you which of those two you are missing rather than just refusing. The holding stays bought across saves and puts itself on your map as somewhere to travel back to.

- 37B ✅ and now it holds your things. A chest opens a two-panel window — your pack on the left, the stash on the right — and what you leave there is still there after a reload, affixes and all. It will not open for anyone who has not bought the place, and it says so.

- 37C ✅ and now you can build in it. Craft a forge, a workbench, an alchemy table, a brazier, a crate or a banner, then hold it against the world as a ghost that goes green where your yard allows it and red where it does not, and tells you which of the two it is. Set it down, use it, or take it back up and get the kit back.

- 37D ✅ and now you can show off what you killed for. Display stands take anything Epic or better — the Iron King's heart, or a sword you rolled yourself — and it stands there glowing in your house, still there when you come home. The cottage itself is finally a cottage: its own low, wide building with a walled room holding the stash and the stands, a deed post at its own door, and a yard you can build in.

### Phase 35 — dragons

- 35A ✅ the body — hit zones as data (head ×2, tail ×0.6), a turn rate slow enough that getting behind one means something, and jaws/wing/tail arcs.

- 35B ✅ it flies — a timed swoop cycle that takes off, closes from the air and lands.

- 35C ✅ it breathes — a channeled cone spell, so being behind it is a defence.

- 35D ✅ it has a lair — a roost in Frostfang Reach, a territory it will not chase you out of, and a hoard worth taking.

- 35E ✅ a second dragon, built entirely from that pipeline: the Ash dragon, Morthul's, with necrotic breath and no safe side.

- 35F ✅ the third one talks — an Ancient dragon in a northern aerie that holds a conversation instead of a grudge, gives a quest, and will teach you a word no one alive can sell you. You can also just kill it and take the word off its stand.

- 35G ✅ dragon country — frost drakes now wander the Reach on their own, an elder drake turns up as a hunt worth the walk, and the scales all of them drop forge into the first armour in the game that answers the cold. **Phase 35 complete**
