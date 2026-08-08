## Phase 26 — Playable Races & Character Creation `[F]`

> Six LORE races as data-driven trait sets + a creator that writes them into the
> player at spawn.

- [x] **26A — `RaceResource` + `RaceDatabase`** `[F]` ✅
  - **Goal:** races are data.
  - **Tasks:** add `RaceResource` (`.tres`: id, name, `AttributeSet` deltas, innate
    perk/ability ids, starting reputation tweaks, appearance option ids) +
    auto-indexed `RaceDatabase` (mirror `ItemDatabase`). No new inheritance.
  - **Done when:** a `RaceResource` loads and indexes; the schema covers all six
    LORE races' needs.
  - **Done:** new `src/Races/` system — `RaceResource` (`[GlobalClass] : Resource`: `Id`, `DisplayName`,
    multiline `Description`, sparse `StatDeltas` [`RaceStatDelta` sub-resource = `StatType` + signed flat
    `Amount`], `InnatePerkIds`/`InnateSpellIds`/`AppearanceOptionIds` string arrays, `ReputationTweaks`
    [`RaceReputationTweak` = faction id + amount], with typed `StatDeltaList()`/`ReputationTweakList()`
    read-backs mirroring `ScheduleResource`). `RaceDatabase` copies `PerkDatabase` (auto-scans
    `res://data/races`, `Get`/`All`, dup-id warn) and registers in `ContentDatabases.InitializeAll`.
    `ContentValidator.ValidateRaces` gates innate perk→`PerkDatabase`, spell→`SpellDatabase`, and
    reputation faction→`FactionDatabase` refs (+ duplicate race ids). Schema covers all six LORE races'
    needs (Valari magic, Grondar strength, Sylthari survival, Draekyn dragon-ability seed, Umbral stealth
    + distrust, Human flexible). Proof `data/races/Human.tres` loads. Composition only — a new race is a
    `.tres`, no code. Build + **242 tests** + `--validate` exit 0 + boot logs `RaceDatabase loaded 1
    race(s)` (`errors: []`).

- [x] **26B — Author the six race `.tres`** `[C]` ✅
  - **Goal:** Human, Valari, Grondar, Sylthari, Draekyn, Umbral exist as data.
  - **Tasks:** author all six `data/races/*.tres` per LORE traits (Valari magic
    affinity, Grondar strength/endurance, Sylthari wildlife communion, Draekyn
    dragon ability seed, Umbral stealth, Human flexible). Reference existing
    perks/stats; create any small new perk `.tres` they need (docs/RECIPES.md "new
    perk"). Pure content.
  - **Done when:** six valid race `.tres`; `validate` green; traits reference real
    ids.
  - **Done:** authored the five remaining races (Human shipped in 26A) — **Valari** (+3 Int/+4 SpellPower/
    +20 Mana, innate `spell.firebolt`), **Grondar** (+5 Str/+4 End/+3 Vit/+20 HP/−0.4 Move, innate
    `perk.toughness`), **Sylthari** (+3 Dex/+2 Vit/+0.4 Move, innate `perk.endurance_training`),
    **Draekyn** (+2 Str/+2 SpellPower/+0.2 CritDmg, innate `spell.fireball` dragon-breath seed,
    `faction.villagers −10` feared), **Umbral** (+4 Dex/+0.4 Move/+0.03 Crit, innate `perk.precision`,
    `faction.villagers −15` distrusted). **No new perks needed** — innate spells + stat deltas + the three
    ungated perks (toughness/endurance_training/precision) cover every trait, so this stayed pure content.
    `AppearanceOptionIds` left empty (the catalogue lands in 26D). All traits reference real ids;
    `--validate` exit 0 (`ValidateRaces` green) + boot logs `RaceDatabase loaded 6 race(s)` (`errors: []`).
    242 tests unaffected (content-only).

- [x] **26C — `PlayerFactory` consumes a creation profile** `[F]` ✅
  - **Goal:** the chosen race actually shapes the player.
  - **Tasks:** add a `CharacterProfile` (race id, name, appearance, background) and
    have `PlayerFactory` apply race deltas as `StatModifier`s, seed innate perks,
    and apply reputation tweaks at spawn (CLAUDE.md §6 factory rules — set props
    before `AddChild`). Persist the profile in the save header.
  - **Done when:** spawning with different races yields different starting stats/
    perks/standing; the profile saves/loads.
  - **Done:** `CharacterProfile` (pure C# — `RaceId`/`CharacterName`/`AppearanceOptionIds`/`Background`,
    `Human` default, `ToHeaderFields`/`FromHeaderFields` round-trip). New `RaceComponent` added **last**
    in `PlayerFactory` (so Stats/Perks/Spellcasting/Reputation are initialized) applies the race in
    `OnInitialize`: stat deltas → flat `StatModifier`s sourced to itself (remove-then-add → idempotent,
    `RefillResources`), and on New Game grants innate perks (new free `PerksComponent.GrantFree`), `Learn`s
    innate spells, and `Add`s reputation tweaks. `PlayerFactory.Create(pos, profile, applyStartingGrants)`
    (parameterless overload keeps Human default). Bootstrap holds `_activeProfile` — New Game uses Human
    (26D's creator wires the chosen one here), Load reads the slot header → rebuilds the profile and spawns
    with `applyStartingGrants:false` (the save overlay restores the granted perks/spells/rep). Profile
    persists via `BuildSaveHeader` + `SaveSlotInfo` (`race_id`/`char_name`). Dev `race [id]` command
    live-applies a race for at-keyboard verification (stat swap + idempotent perk/spell re-grant; skips
    reputation to avoid accumulation). Build clean + **246 tests** (+4 `CharacterProfileTests` round-trip)
    + `--validate` exit 0 + boot through the load path logs `Loaded game … as Wanderer (race.human)`
    (`errors: []`). `AppearanceOptionIds`/`Background` carried + persisted but not yet consumed (26D).

- [x] **26D — `CharacterCreator` screen** `[F]` ✅
  - **Goal:** the new-game creation flow.
  - **Tasks:** build the creator (race pick with trait summary, appearance options,
    name, optional background) through `UiTheme`, fed by `RaceDatabase`, writing a
    `CharacterProfile`. Hook it into MainMenu → New Game → world spawn. All strings
    via `Loc`.
  - **Done when:** New Game → create a character → spawn into the world with the
    chosen race applied; flow round-trips through the save header.
  - **Done:** `CharacterCreator` (`CanvasLayer`, mirrors `SaveSlotPanel`, built via `UiTheme`): a
    `UiTheme.Dropdown` race picker over `RaceDatabase.All` with a live **trait summary** (the race's
    `Description`, each stat delta as signed amount + localized stat name, innate perk/spell `DisplayName`s,
    reputation tweaks by faction `DisplayName`), a name `LineEdit`, and an optional background `LineEdit`;
    Begin builds a `CharacterProfile` and Back returns to the title. `MainMenu` New Game → slot pick →
    creator → `NewCharacterRequested(slot, profile)`; `GameBootstrap.StartNewGame(slot, profile)` spawns
    from it (the 26C plumbing applies the race). New `StatNames` helper (localized `StatType` names) +
    15 `stat.*` and ~11 `create.*` keys (`strings.csv` 113→139). All strings via `Loc` (no literals).
    Build clean + **247 tests** (+1 `StatNamesTests`: every `StatType` → distinct non-fallback key) +
    `--validate` exit 0 + boot logs `loaded 139 string(s)`, `errors: []`. UI reviewed against the Godot 4.7
    C# API (the New Game → creator → spawn click-path is a windowed interaction, not headless-drivable).
    Appearance deferred (no catalogue/renderer until Phase 30 models). **Phase 26 complete.**

---
