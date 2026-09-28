# Embervale

An original open-world fantasy action RPG, played in first or third person (swap at any time), built
in **Godot 4.7** with **C# (.NET 8)**. A dying world whose magic is failing: explore it, fight with
weight, recover lost spellcraft, and let **corruption** reshape how the world answers you, all the way
to one of two endings.

> **Private / personal project** — never sold or published.

## The game

The gods are dead and the **Weave** that carries magic is fading. Seven **Flamebearers** once held
the gods' fire; all seven have fallen, and their embers are what is left of divine power. The player
walks out of Ashfall into the Ember Crown and, realm by realm, puts the fallen to rest — each time
choosing whether to take the ember (power, and corruption) or refuse it.

### Realms

| Realm | What is there |
| --- | --- |
| **The Ember Crown** | the human heartland: the capital under the Iron Citadel, market towns, the Tarn, the mine, the Iron King's arena |
| **Frostfang Reach** | alpine clan country north of the Crown Pass: the Clan Hold, three dragon territories, the Storm Tyrant at Stormcrown |
| **The Ashen Wilds** | the Cataclysm scar east of the Ashen Breach: Last Hearth's survivors, the Ash Hunters' station, the Beast Lord |
| **The Sunspire Dominion** | desert south of the Southmarch Gate: Saffra Wells, the great library and its Archivist, the Crimson Mission and the Crimson Prophet |
| **The Pale Concord** | a hidden realm held at dusk: Vesperhold's undying residents, the Hollow Court, the Hollow Queen |
| **The Celestial Realm** | the ruin of the dead gods: the Knight's Gate, the Ash Throne, the Ashen Knight and Morthul |

### Story

- **Act I — Awakening.** Ashfall, Kael, the goblin warband, the Iron King.
- **Act II — Gathering the Flame.** The Storm Tyrant, the Beast Lord and the Crimson Prophet, in any
  order; their fall reveals the hidden realm and its Hollow Queen.
- **Act III — Truth of the Gods.** The Archivist's reading at the Sunspire library opens the gate.
- **Act IV — The Celestial War.** The Ashen Knight, Morthul, and the Ash Throne: low corruption
  offers only **Dawnfire**, high corruption only **Lord of Embers**, and in between the choice is
  yours. An epilogue reflects how many embers you took; after the credits the world stays open.

### Systems

Weighty melee with poise, parries, dodges and lock-on; bows; a deep magic system (charged and
channelled casts, school identities, mastery, combos, the fading Weave); corruption tiers that change
your body, your dialogue and the spells you can claim; companions with orders and loyalty; six
playable races; housing; a full economy (shops, services, tolls, fences, contracts); mounts; a map
that discovers places by sight; branching quests; divine shrines; five joinable guilds; dragons;
day/night, weather and NPC routines; save/load everywhere.

## Build and run

Prerequisites: the **.NET build** of Godot 4.7.1 and the .NET 8 SDK.

```bash
dotnet build Embervale.sln                     # build C# (running the game never recompiles)
godot --path . --editor                        # open, press Play: Main.tscn boots to the title
godot --path . -- --play                       # continue the newest save straight into the world
godot --headless --path . -- --validate        # content gate, exit 0/1
godot --headless --path . -- --story           # the main story wired end to end, exit 0/1
dotnet test tests/Embervale.Tests              # pure-logic unit tests
```

Controls: `WASD` move · mouse look · `Shift` sprint · `Space` jump · `Ctrl` dodge · `LMB` attack ·
`RMB` block · `Q` cast · `F` cycle spell · middle mouse lock-on · `E` interact · `V` swap view ·
`I` inventory · `J` journal · `M` map · `B` bestiary · `C` party order · `Y` mount · `1`–`5` hotbar ·
`F5`/`F9` quick save/load · `Esc` pause. A gamepad plays the whole game. Developer builds add `F1`
(console), `F3` (debug overlay) and `F4` (profiler).

## Documentation

| Doc | For |
| --- | --- |
| [`CLAUDE.md`](CLAUDE.md) | how to work in this repo: rules, environment, gotchas |
| [`docs/NOW.md`](docs/NOW.md) | where the project is, live invariants, commands |
| [`docs/PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) | stages, phase status, what is left |
| [`docs/HISTORY.md`](docs/HISTORY.md) | what every phase produced, and the lessons |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) / [`docs/RECIPES.md`](docs/RECIPES.md) | how the systems work / how to add content |
| [`docs/WORLD_AUTHORING.md`](docs/WORLD_AUTHORING.md) / [`docs/WORLD_ATLAS.md`](docs/WORLD_ATLAS.md) | building regions / where the realms are |
| [`docs/TOOLING.md`](docs/TOOLING.md) | the developer SDK (`python tools/embervale.py`) |
| [`docs/3D_ASSETS.md`](docs/3D_ASSETS.md) / [`docs/ASSET_POLICY.md`](docs/ASSET_POLICY.md) | the model pipeline / sourcing |
| [`docs/DESIGN.md`](docs/DESIGN.md) / [`docs/LORE.md`](docs/LORE.md) | design intent / the world's story |
| [`docs/ART_STYLE.md`](docs/ART_STYLE.md) / [`docs/UI_STYLE.md`](docs/UI_STYLE.md) / [`docs/RENDERING.md`](docs/RENDERING.md) | look and feel |
| [`docs/IDS.md`](docs/IDS.md) / [`docs/SAVE_FORMAT.md`](docs/SAVE_FORMAT.md) | id scheme / persistence contract |
