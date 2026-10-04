# Campaign overhaul (2026-10-03): design and id registry

Source of truth for the main-story overhaul. Written at the start of the run from the approved plan; update it when ids change.
Workstream locale blocks in `data/locale/strings.csv` are bracketed by `# --- BEGIN campaign:<ws> ---` / `# --- END campaign:<ws> ---`; each workstream edits only its own block.



## Context
Embervale (`C:\Users\magnu\Embervale`, Godot 4.7 / C#) is "finished" but its story is thin: the opening is 5 narration cards then free roam; Act I is four kill/collect errands with no Iron King objective; Acts II-IV are 4 generated quests (7 Kill + 1 Talk); Act III is one skippable Archivist chat; guilds, companions, reputation and corruption are never read by `quest.main.*`; the quest UI shows one static summary per quest. Goal: a 30-mission, 4-act campaign built on existing systems (quests, dialogue, companions, guilds, factions, corruption, bosses, flags), a recurring Ashen Knight rival, real realm arcs, a playable Act III investigation, a multi-mission Celestial assault, a quest UI/UX upgrade (journal, tracker, toasts, banners, compass, map, dialogue), and a final New Game -> both endings audit. Save compatibility and stable ids preserved.

Decisions (user): full creative authority over world content; ~6 major forks plus reactive variants; UI scope = journal + tracker + toasts + banners + map + compass + dialogue panel.

Research basis: four read-only maps (story data, RPG systems, world/lore, quest UI) plus a design pass. Full design is copied into `docs/playbook/campaign.md` in step P0 (id registry + beat sheet) so implementers have one source.

## Ground rules
1. Never rename/reorder: `quest.warband.{bounty,forge,remedies,heart}` and `quest.main.{gathering,hidden,truth,celestial}` stay; edits are append-only (objectives are saved positionally).
2. New main quests are linear and auto-started: each has `CompletionFlagId = flag.main.<slug>_done`; the next quest's `AutoStartFlagId` is that flag (auto-start does not fire on prerequisite completion). A quest whose own completion flag is already held never auto-starts (makes legacy catch-up trivial).
3. Fork flags default to "absent = neutral variant"; companion/guild content is only `IsOptional` objectives or extra dialogue branches, never required.
4. Pale Concord text only under `pale.*` keys (atlas gate); generator enforces it.
5. Neutral commits (no AI trailer, per memory). `git pull` and verify HEAD==origin/main first; work in a fresh worktree/branch off main (current checkout is `magic/integration`, 26 commits behind main). Max ~4 parallel agents; gates one at a time; one master bake; no review fan-out at the end (verify inline).

## Lore resolutions (canon for all new text)
- **The Sundering** = divine war that broke the Celestial Realm; **the Cataclysm** = its fallout on the mortal world. Retire "Shattering".
- Beast Lord is **he** (fix `vision.beast_lord.*`).
- Timeline: Cataclysm = year 0; the Six climbed the Stair ~year 20; the five who fell were flung into realms and have ruled ~380 years; the Concord sealed ~400 years ago.
- Ashen Knight = sixth of the Six, only one to reach the top, could not sit nor leave the throne empty, knelt; **throne's keeper, not Morthul's servant**. Morthul = "what the throne makes of whoever sits on it". The five graves on the Stair name the five Flamebearers.

## Campaign beat sheet (30 missions; ids final-ish, verify NPC/enemy/location ids against data before use)
Legend: K kill, C collect, R reach, T talk, I interact, D defend(s), E escort, M milestone (new type), opt = optional. Flags `flag.main.<slug>_done`.

**Act I: Embers at the Crown (Ember Crown)**
| # | Id | Beats |
|---|---|---|
| 01 | `quest.main.smoke_over_the_square` | Opening hook (replaces 3 of 5 cards): T Elder -> I alarm bell -> D square 45s (set-piece raid waves) -> T Elder (raider carries black-iron token); opt: carry wounded |
| 02 | `quest.main.the_pass_kept` | Kael/Toren: R Ashfall pass (rival silhouette A1) -> I Toren's cairn -> I black token -> T Kael; opt: E Kael (if in party) |
| 03 | `quest.warband.bounty` (legacy) | existing Kill 3 goblins at index 0; append I burn warband banner; autostart on 02 done |
| 04 | `quest.main.crossway_whispers` | R Crossway -> T Capt. Fenn -> I impound crate (sigil, A2) -> M armed-by-known via opt Syndicate broker talk or opt ledger heist (rep forks) |
| 05 | `quest.warband.forge` (legacy) | existing Collect 3 ore; append T Bryn hand-in |
| 06 | `quest.warband.remedies` (legacy) | existing Collect 4 herb; append I wounded scout (postern hint), T apothecary |
| 07 | `quest.main.citadel_approach` | R Iron Citadel -> K sentries (respawning) -> I douse signal brazier -> T captive Marshal Orsolo Dray (**Fork F1**) |
| 08 | `quest.warband.heart` (legacy) | existing Kill 5 goblins; append R arena; `flag.frostfang.passage_open` finally has a reader (starts 09) |
| 09 | `quest.main.iron_king` | K Iron King -> T rival at arena gallery (A3, first words) -> T Elder (names the five, the throne) |

**Act II (three arcs, any order; `quest.main.gathering` stays as hidden "ledger" umbrella, `IsLedger`)**
| # | Id | Beats |
|---|---|---|
| 10 | `quest.main.closed_hold` | Frostfang hook: R Clan Hold -> T chief, quartermaster -> I three scout cairns -> D hold vs storm-mote raid |
| 11 | `quest.main.succession` | **F2** Hjalvar vs exile Halvar One-Hand; route objectives gated by fork flag (D Stormcrown vs I Aerie winches) |
| 12 | `quest.main.storm_tyrant` | K Storm Tyrant (brazier gated `flag.arc.frostfang_ready`); opt duel1 (A4); T aftermath variant |
| 13 | `quest.main.last_hearth` | Ashen hook: R Last Hearth -> T Maeve -> D beast herd -> T Ada; opt C herbs |
| 14 | `quest.main.herd_and_hearth` | investigate: R Hunters' Rise -> T Hask -> I three beast tracks -> R herd trail -> **F3** slay herd vs calm it |
| 15 | `quest.main.beast_lord` | K Beast Lord (gated `flag.arc.ashen_ready`); T aftermath |
| 16 | `quest.main.dry_wells` | Sunspire hook: R Wells -> T wellkeeper -> I three well gauges (ash-poisoned) -> R caravan gap -> T caravan master, pilgrim |
| 17 | `quest.main.prophets_flock` | **F4** expose / convert / kin (corruption >= 40 only) |
| 18 | `quest.main.crimson_prophet` | K Prophet (gated `flag.arc.sunspire_ready`); opt duel2 (A6); on-screen Pale reveal beat after all three |

**Pale Concord (secret; `pale.*` text)**
| # | Id | Beats |
|---|---|---|
| 19 | `quest.main.pale_door` | reveal banner/toast/door appears; T Archivist -> R landing -> I landing lamp |
| 20 | `quest.main.vesperhold` | investigation: T Ismene, Oswin, Maren, Corwen -> I dusk-count registry -> I three lamps; corruption >= 40 recoil variants |
| 21 | `quest.main.queens_count` | R court approach -> K husks -> I count-stone -> **F5** release vs keep; sets `flag.pale.court_open` (Queen brazier gate) |
| 22 | `quest.main.hidden` (legacy) | existing Kill Queen at index 0; append T aftermath; Queen parley dialogue (truce at corruption >= 60) |

**Act III: Truth (Sunspire library + Embermarket annex)**
| # | Id | Beats |
|---|---|---|
| 23 | `quest.main.sundering_pages` | five testimony flags gather; R library -> T Archivist -> I reading table -> R annex -> T Keeper Ysolde Marr |
| 24 | `quest.main.deep_stacks` | I stacks door -> K ward golems -> I three seal plinths -> D 60s -> T rival (A7 parley) -> I Sundering codex |
| 25 | `quest.main.truth` (legacy) | existing Talk at index 0 now gated behind `flag.beat.codex_read` (fixes skippable one-chat), reading sets `flag.celestial_gate_open` |

**Act IV: The Celestial Assault**
| # | Id | Beats |
|---|---|---|
| 26 | `quest.main.celestial_landing` | D landing at beacons -> T Assembly Marshal -> I five graves (A8); fork allies appear as variant NPC clusters |
| 27 | `quest.main.godfall_choir` | godfall anchors + D; choir passage: K echoes (<40 corruption) or T choir echo (>=40) |
| 28 | `quest.main.sundered_stair` | winches + D, opt E Kael, K sentinels, T rival echo (A9), R Knight's Gate |
| 29 | `quest.main.celestial` (legacy) | existing Kill Knight then Morthul; pre-fight rival_gate dialogue **F6** kneel vs draw; opt entropy stele interacts appended |
| 30 | `quest.main.throne` | T `dialogue.ash_throne` (gating unchanged: <40 Dawnfire, >=60 Embers) -> `flag.game_complete`; per-fork epilogue cards |

**Forks:** F1 Warden's Mercy (spare/press Dray), F2 Stormbound Succession, F3 Herd and Hearth, F4 Prophet's Flock (incl. corruption-gated Kin), F5 Queen's Question, F6 Knight's Vigil; plus the existing per-ember absorb. Each has a concrete consequence in-arc (rep, adds, shortcuts, items, corruption), again at the Celestial landing (non-combat ally NPCs + boons), and in the epilogue.

**Ashen Knight ledger:** A1 Ashfall ridge glimpse (02), A2 sigil clue (04), A3 arena gallery words (09), A4 duel1 Stormcrown (12), A5 Beast Lord hearsay (15), A6 duel2 Mission (18), A7 library parley (24), A8 five graves (26), A9 concourse echo (28), A10 gate fight + last words that read duels/library/vigil (29).

## System extensions (append-only, save-safe)
- `ObjectiveType.Milestone = 8` (completes when `TargetId` flag set; evaluated on flag change and on unlock). Objective fields: `CompletionFlagId`, `ActivatedFlagId`, `IsOptional`, `HintKey`, `JournalEntryKey`. Quest fields: `ChapterKey`, `OrderInAct`, `RegionId`, `GiverNameKey`, `RecommendedLevel`, `DetailKey`, `StartFlagId`, `FailFlagId`, `IsLedger`. Events `QuestObjectiveActivatedEvent`, `QuestStageChangedEvent`, `ChapterStartedEvent`, `StoryBeatEvent`. Validator arms for every new key; guard against a quest with zero live objectives completing instantly (relevant to gated Talk in #25).
- Dialogue: conditions `ReputationAtLeast`, `CompanionInParty`, `HasItem`; effects `AddReputation`, `GiveItem`, `TakeItem`, `PlayCards`, `TrackQuest`, `Banner`; `Condition2/Effect2` pairs on choices, `OnEnterEffect` on nodes, `StartVariants` on resources; keep `DialogueContentTests` regex shapes (or update in same change).
- `QuestLogComponent`: `StartQuest` effect also tracks; main-first tracking fallback; skip auto-start if completion flag held; `CampaignCatchUp` (pure, idempotent) runs at top of `OnGameLoaded` before `AutoStart` and maps legacy flags/quests to new done flags (table in `campaign.md`); fork flags never back-filled.
- New components: `SetPieceSpawnComponent` (Area trigger + flag gates, built from `LairSpawnComponent`), `StoryRuleDirector` + `data/story/story_rules.json` (opening-done from `OpeningFinishedEvent`, all-testimonies, party flag mirror, Pale reveal beat), `CompanionReactionDirector` (data table, once-per-save barks via `flag.bark.*`), `ChapterBanner`, per-fork epilogue cards in `EndingSequence`, `BossResource.EpithetKey/IntroLineKey`, `FlagVisibilityComponent.VisibleWhenFlagId`.
- Generator: replace `tools/gen_main_story.py` with data-driven `tools/gen_campaign.py` + `tools/campaign/*.py` writing quests, dialogue, locale rows between marker comments, and `data/story/campaign_graph.json`; `gen_main_story.py` becomes a wrapper.
- Fix known bugs: `SliceDirector` fires on any region change; `HiddenRealmReveal` silent; tracker destination skips Reach objectives; map reveals all objective locations at quest start (spoilers); Frostfang door doc drift (door stays open, fix `finish.md`); `frostfang.passage_open` now read; update `HiddenRealmReveal.RequiredFlags`, `EndingSequence.AbsorbFlags`, `VisionSequence.Visions`, `HeadlessStory`, `negative_tests.py` mutations.

## Quest UI/UX (all reusing UiTheme Band/Card/Chip/SectionRule/IconLabel/Bar/Action, UiPanel, UiMotion, accessibility settings)
- **Journal** (`src/UI/QuestLogPanel.cs`): chapter groups, region and level chips, `DetailKey` prose, stage log with ticks from `JournalEntryKey`, current objective + hint, optional chips, full rewards (items, faction), updated-dot (optional save key, absent = seen), LB/RB tab nav, timer, `ApplyScreenInset` in Rebuild, no hard-coded "J to close".
- **HUD tracker** (`GameHud.cs`): main-first fallback, pinning, chapter label, optional rows, retint spine for side quests, fix Reach destination; hide ledger quests.
- **Toasts** (`Notifications.cs`): objective-start, next-objective on every stage change, quest-updated, optional-done.
- **Chapter banner**, boss epithet card, new audio cues (`ui.quest.started/updated/completed`, `ui.chapter.title`, `ui.objective.optional`) registered in `AudioLibrary.Build`.
- **Compass** (`CompassStrip.cs`): QuestMain colour, active/optional states, portal-aware pointing for cross-region objectives.
- **Map/minimap** (`MapScreen`, `MapView`, `MinimapHud`, `MapService.cs:249`): main vs side pin styling; spoiler-safe reveal (only unlocked incomplete objectives).
- **Dialogue** (`DialoguePanel.cs`): consequence tags derived from effect enums (quest, corruption +/-, reputation, story), quest-context line, last-3-lines history.
- Add `--panelshots`/`--hudshots` entries for each; pure rules (stage log selection, notice priority) Godot-free with xUnit.

## Workstreams (<=4 agents; orchestrator owns merges)
- **P0** orchestrator: pull main, worktree+branch, baseline gates, pre-insert per-workstream marker blocks in `data/locale/strings.csv`, write `docs/playbook/campaign.md`.
- **P1a/P1b** W-Core: data-model fields/enums/events, dialogue extensions, auto-start/track changes, SetPiece, StoryRuleDirector, CatchUp, generator framework (+ xUnit, validators). P1a lands first so UI compiles.
- **P2 parallel:** W-Spec-1 (Acts I-II specs/dialogue/locale), W-Spec-2 (Pale, Act III, Act IV, rival dialogues), W-World (cell-scene props, NPCs, set-piece nodes, new map locations via `gen_map_locations.py`; new cells only if a realm genuinely needs them), W-UI (Part above).
- **P3** orchestrator merge order Core -> UI -> specs -> World; resolve `GameSession.cs`, `ContentValidator.cs`, `HeadlessStory.cs`, locale conflicts.
- **P4** one master `world_bake.py --bake` (never concurrent).
- **P5** audit + fixes + docs (`NOW.md`, `finish.md`), commit, push to main.
- File ownership: W-Core owns `src/Quests`, `src/Dialogue`, `ContentValidator.cs`, `GameSession.cs`, `gen_campaign.py`; specs own their `tools/campaign/*.py`; W-World owns `scenes/regions/*` and `gen_map_locations.py`; W-UI owns quest UI files; orchestrator owns docs and merges.
- Gate order, one at a time: `dotnet build Embervale.sln` -> `gen_*.py --check`, `world_atlas.py --check`, `negative_tests.py` (clean tree) -> `dotnet test tests/Embervale.Tests` -> `godot --headless -- --validate` -> `-- --story` -> bake -> validate+story again -> `--panelshots`/`--hudshots`. Absolute godot/python paths are in `docs/NOW.md`.

## Tests and audit
- xUnit `CampaignGraphTests` over `campaign_graph.json`: every main quest reachable; no dead ends/handoff gaps; every flag read has an earlier writer on every path; targets/locale keys exist; Pale text only in `pale.*`; `CampaignCatchUpTests` (mapping, idempotence); enum-stability for new members; dialogue variants/second pairs resolve; optional objectives ignored by completion rules; held completion flag blocks auto-start.
- Rewrite `HeadlessStory` to drive real systems (real `DialogueSession` choices, real `EntityDiedEvent`/`InteractionPerformedEvent`, Reach/Defend advance): Run A low corruption + option A forks -> Dawnfire; Run B all embers + option B -> Lord of Embers; Run C legacy fixture replay lands on the right mission; per-fork and corruption-gated route assertions.
- Final story-flow audit (New Game -> credits, both endings): dead time (>3 min without progress), unclear objectives (tracker/compass/map alone), abrupt transitions, weak motivation, pacing, sequence breaks (realm early, skipped aftermath talk), fork effects visible, finishable with no companion/guild, legacy saves; fix everything found.

## Verification
Run the full gate order above after each phase and at the end; headless `--story` all three runs pass, both inside the editor build and the exported build (`build/windows/Embervale.exe --headless -- --story`); `--panelshots`/`--hudshots` regenerated and eyeballed; `world_bake.py --check` and `world_atlas.py --check` green; manual walk-through using dev console (`quest advance`, `region goto`) for sequence-break spot checks. Not verifiable by me: a human play-through and boss balance (state this in the final report).

## Risks / items to verify while implementing
- Verify every id used above exists (e.g. `enemy.iron_mark` is a unique fugitive, not a sentry; `dialogue.undying_*` actual ids; shared dialogue ids; celestial cell adjacency in `tools/region_spec_celestial.py`; companion party API for `flag.party.*`).
- Gated objectives with no live objective left could complete a quest instantly; add guard + test.
- Collect does not count granted items; avoid Collect for gifts. Scene-authored `BossSummonComponent` flags are unvalidated; add a validator arm cross-checking the campaign graph.
- Bake is ~27 min and fragile; validate Interact ids and scene flags before baking.
- Appending objectives to legacy quests can leave an in-progress save with one extra unmet step (acceptable, noted).
- Prose volume is the real constraint: generator + locale blocks keep it tractable; Pale secrecy enforced by the generator.
- Fork allies at the landing are non-combat NPCs with dialogue boons (no ally AI). Frostfang door stays open (doc fixed).
