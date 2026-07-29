# Vertical Slice Plan — Phase 33D

**"The Warband and the King"** — the implementation plan for stitching Phases 22–33C into one
continuous, polished 30–60 minute arc, ending at 🚩 **Gate G1 — Vertical Slice**.

> **Read this first if you are picking up 33D cold.** It is written to be executed without the
> conversation that produced it. It records what already exists (with file paths and ids), the three
> design decisions that are **locked**, the beat-by-beat arc, and a task-by-task build order with
> acceptance criteria. Where a decision is still open it says so explicitly.
>
> Companion documents: `SESSION_PLAYBOOK.md` (the sub-phase checklist), `PRODUCTION_ROADMAP.md`
> (§Phase 33 and Gate G1), `ARCHITECTURE.md` (the systems this leans on), `LORE.md` (canon).
>
> **Status: tasks 1–7 are built** (see §4 — each is marked). What remains is **§5.2, the manual
> play-through**, which could not be run in the session that built this: that environment had
> `dotnet` but no Godot binary, so every runtime claim below is *reviewed against the Godot 4.7 C#
> API*, not observed. **The arc has never been played.** Treat §5.2 as the outstanding work.

---

## 0. Locked decisions

These were decided with the maintainer. **Do not re-litigate them mid-build**; if one turns out to be
wrong, raise it before writing content against the alternative.

| # | Decision | Rationale |
| - | -------- | --------- |
| **A** | The **warband chain is the spine**, with Kael woven through it. | It is the only quest chain already prerequisite-linked end to end, and it already points at the two authored talkers (the elder, the guild board). Kael becomes character depth *along* the spine rather than a parallel track competing with it. |
| **B** | **Hard-gate the boss.** The brazier stays inert until `quest.warband.heart` is complete. | The arena cell is reachable from minute one, so today a fresh player can light the brazier at level 1 and be flattened by the Iron King before the slice has said anything. The gate costs the player nothing they want. |
| **C** | The slice **ends on a closing card + the Frostfang portal revealed**. | Ending on a door rather than a wall. A hard "to be continued" is cleaner for a capture build but worse to actually play, and G1 is judged by a stranger playing it. |

---

## 1. What already exists

Verified against the repository at the time of writing. **Check these still hold before building** —
if a path moved, the plan's instructions move with it.

### 1.1 Cells (all authored, navmeshed, streaming)

| Cell id | Scene | Centre | Role in the slice |
| ------- | ----- | ------ | ----------------- |
| `ember_crown.town_hub` | `scenes/regions/ember_crown/town_hub.tscn` | `(0, 0, -10)` | Hub: elder, guild board, vendors, smith, apothecary, Kael, crafting stations, waystone |
| `ember_crown.wilds_west` | `scenes/regions/ember_crown/wilds_west.tscn` | `(-55, 0, -10)` | Early goblins, herb gathering |
| `ember_crown.wilds_north` | `scenes/regions/ember_crown/wilds_north.tscn` | `(0, 0, -65)` | Iron, the warband, the difficulty step-up |
| `ember_crown.arena` | `scenes/regions/ember_crown/arena.tscn` | `(55, 0, -10)` | The Iron King. Brazier node is `Brazier`, with a `Summon` child (`BossSummonComponent`) |

Region resource: `data/regions/EmberCrown.tres` — spawn point `(0, 1.2, 5)`, safe zone radius `34`
centred on the hub, neighbour `region.frostfang_reach`.

### 1.2 Quests (`data/quests/`)

| Id | File | Prereq | Objective |
| -- | ---- | ------ | --------- |
| `quest.warband.bounty` | `WarbandBounty.tres` | — | Kill 3 × `enemy.goblin` |
| `quest.warband.forge` | `WarbandForge.tres` | `bounty` | Collect `item.material.iron_ore` |
| `quest.warband.remedies` | `WarbandRemedies.tres` | `forge` | Collect `item.material.healing_herb` |
| `quest.warband.heart` | `WarbandHeart.tres` | `remedies` | Kill 5 × `enemy.goblin` |
| `quest.kael.oath` | `KaelOath.tres` | — | Kill 4 × `enemy.goblin` |
| `quest.kael.brother` | `KaelBrother.tres` | `kael.oath` | Collect 4 × `item.material.goblin_hide` |
| `quest.cull_goblins` | `CullTheGoblins.tres` | — | **Legacy sandbox quest.** See §6.1 |
| `quest.gather_iron` | `GatherIron.tres` | — | **Legacy sandbox quest.** See §6.1 |

### 1.3 Conversations (`data/dialogue/`)

`dialogue.elder`, `dialogue.guild_board`, `dialogue.smith`, `dialogue.apothecary`,
`dialogue.vendor_stub`, `dialogue.kael`, `dialogue.iron_king_absorb`.

### 1.4 The corruption beat — **already built**

This is the single most important thing to know before planning content: **the slice's thesis beat
exists.** On the Iron King's death, `BossEncounterDirector` (`src/Enemies/BossEncounterDirector.cs`):

1. slows time (`DefeatTimeScale 0.35` for `1000 ms`),
2. grants the relic `item.relic.iron_heart` and sets `flag.iron_king_defeated`,
3. then opens `dialogue.iron_king_absorb` — **absorb his ember (`Effect = 4` AddCorruption, `+25`)
   or decline**.

There is also corruption *foreshadowing* already authored in `dialogue.elder`: choices gated on
`CorruptionAtLeast 30` / `CorruptionBelow 30`, one of which adds `+10`. So the arc already has a
quiet setup and a loud payoff. **33D's job is to make sure the player meets them in the right order,
not to invent them.**

### 1.5 Onboarding + prologue (33A–33C, done)

- `OpeningSequence` (`src/UI/OpeningSequence.cs`) — five narration cards over the already-built
  world, skippable, new-game only. **Reuse this renderer for the closing card** (§4.6).
- `TutorialDirector` (`src/Onboarding/`) — teaches look → move → sprint → attack → block → dodge →
  interact → inventory → journal → cast by observation. Never blocks input.

---

## 2. The arc — beat by beat

Target ~45 minutes for a player who reads. A veteran who skips dialogue should still take 25+.

### Beat 1 — Arrival (0–5 min)

Prologue lifts on the Ember Crown. Onboarding teaches movement and looking as the player walks. The
elder is the only interactable with a marker. He names the problem: goblins on the roads, and
something organising them.

**Teaches:** look, move, sprint, interact.
**Ends when:** the player has spoken to the elder and been pointed at the guild board.

### Beat 2 — The guild's taste (5–12 min)

Guild board → `quest.warband.bounty`. First combat in `wilds_west`, first loot, first level-up. Back
to the board, paid.

**Teaches:** attack, block, dodge, inventory, journal.
**Ends when:** `quest.warband.bounty` completes.

### Beat 3 — Kael (12–18 min)

The elder (or the guild board) points at the knight in the hub. `quest.kael.oath` asks for 4 goblins
— **deliberately overlapping the bounty's hunting ground**, so it costs no extra travel. He joins.

**Everything after this point has a companion in it**, which is what Gate G1 asks for.
**Ends when:** Kael is recruited (`CompanionRoster.IsRecruited("companion.kael")`).

### Beat 4 — Supplying the town (18–28 min)

`quest.warband.forge` (iron, from the smith) → `quest.warband.remedies` (herbs, from the apothecary).
Crafting station use, both wilds cells, Kael fighting alongside the player.

`quest.kael.brother` sits here as **optional depth** — signposted, never required.

**Ends when:** `quest.warband.remedies` completes.

### Beat 5 — Break the warband (28–36 min)

`quest.warband.heart` in `wilds_north`. The elder's payoff and the difficulty step-up before the
boss. This is where the player should feel the companion earning their place.

**Ends when:** `quest.warband.heart` completes → **this is the boss gate (decision B)**.

### Beat 6 — The Iron King (36–44 min)

The elder reveals the arena. The brazier now lights. Boss fight, with Kael.

**Ends when:** `flag.iron_king_defeated` is set.

### Beat 7 — The corruption beat (44–46 min)

The ember offer fires automatically (§1.4). Absorb (+25, visible tier change and appearance shift) or
decline. **No new content needed — only make sure the player arrives here having been set up.**

### Beat 8 — Cliffhanger (46–48 min)

Closing narration card: the Iron King's fall is felt in the north, and something answers. The
Frostfang portal becomes active. Stepping through ends the slice.

---

## 3. The gaps — what 33D must actually build

Everything above except these points already works. **This section is the real scope.**

| # | Gap | Why it matters |
| - | --- | -------------- |
| **G1** | Nothing gates the brazier | A level-1 player can reach the slice's climax in minute three and lose |
| **G2** | Kael is not connected to the spine | His quests exist in isolation; no one mentions him |
| **G3** | The Frostfang portal is spawned **at the player's feet on spawn** | `GameBootstrap.SpawnRegionPortals` places it at `SpawnPoint + (0, -1.2, -4)` — four metres in front of the player, from minute one. The cliffhanger is currently standing in the starting square |
| **G4** | No slice ending exists | No closing card, no completion state, no "you have finished the slice" |
| **G5** | The corruption offer can land cold | The elder's foreshadowing choices are gated on corruption the player does not have yet, so a clean player never sees a warning |
| **G6** | Legacy quests pollute the journal | `quest.cull_goblins` / `quest.gather_iron` are sandbox-era and compete with the spine |
| **G7** | `dialogue.elder` mixes raw English with `Loc` keys | Older nodes have literal strings (`"What do you need?"`), newer ones use keys. Violates CLAUDE.md §6 |

---

## 4. Build order

Each task is independently committable and leaves the repo playable. Do them in order — later tasks
assume earlier ones.

### 4.1 Task 1 — Gate the brazier (G1) ✅

**Files:** `src/Enemies/BossSummonComponent.cs`, `data/locale/strings.csv`

Add an exported `RequiredQuestId` (default `quest.warband.heart`). In `Interact`, before summoning,
resolve the player's `QuestLogComponent` and return early unless `IsCompleted(RequiredQuestId)`.
`Prompt` must change too — an inert brazier that gives no reason reads as a bug:

- gate not met → `Loc.T("boss.challenge_locked")` — *"The brazier is cold. The warband still holds the north."*
- gate met → the existing `boss.challenge_prompt`
- already defeated → empty (existing behaviour)

**Keep the export.** A hardcoded quest id would make the gate untestable and un-reusable for the
Phase 34+ bosses.

**Acceptance:** with no quests done, `E` on the brazier shows the locked prompt and summons nothing;
after `quest.warband.heart` completes, it summons normally. `companion recruit` + `quest` console
commands make this checkable in under a minute.

### 4.2 Task 2 — Weave Kael into the spine (G2) ✅

**Files:** `data/dialogue/Elder.tres`, `data/dialogue/GuildBoard.tres`, `data/locale/strings.csv`

Add to the elder a choice gated `QuestCompleted quest.warband.bounty` (Condition `3`) +
`CompanionNotRecruited companion.kael` (Condition `10`) — **remember one choice carries one
condition**, so chain through an intermediate node the way `dialogue.kael` does for its own
two-condition gates.

Copy intent: the elder names Kael as the man who lost his order at Ashfall and has been sharpening a
blade in the square ever since. This is a *pointer*, not a quest — Kael's own conversation still
carries his arc.

**Acceptance:** a player who finishes the bounty and talks to the elder is told where Kael is,
without having to stumble on him.

### 4.3 Task 3 — Gate the Frostfang portal (G3) ✅

**Files:** `src/Bootstrap/GameBootstrap.cs` (`SpawnRegionPortals`), plus a new small component or an
exported flag on `RegionTransitionComponent`

The portal must be **inert and invisible until `flag.iron_king_defeated` is set**. Two options:

1. *(Recommended)* Give `RegionTransitionComponent` an exported `RequiredFlagId`. When set, the
   portal hides itself and disables its collider until the player's `StoryFlagsComponent` has the
   flag. `GameBootstrap.SpawnRegionPortals` passes `flag.iron_king_defeated` for the Frostfang portal
   during the slice.
2. Don't spawn it at all until the flag fires, then spawn on the event. Simpler to write, worse to
   own — the spawn path then has two triggers instead of one.

Take option 1. It is the same shape as `CompanionRecruiterComponent`'s hide/show (see
`src/Companions/CompanionRecruiterComponent.cs`) — **reuse that pattern, including remembering the
authored collision layer rather than assuming layer 1.**

**Acceptance:** on a new game there is no glowing torus in front of the player. After the Iron King
dies it is there.

### 4.4 Task 4 — Seed the corruption warning (G5) ✅

**Files:** `data/dialogue/Elder.tres` or `data/dialogue/Smith.tres`, `data/locale/strings.csv`

One optional, always-available choice before the arena: what happened to the last person who took
power from a fallen one. No mechanical effect, no flag — it exists so the ember offer in Beat 7 lands
on a player who has been warned rather than surprised.

Keep it to three or four lines. **Do not gate it on corruption** — the point is that a clean player
sees it.

### 4.5 Task 5 — Slice completion state (G4) ✅

**Files:** new `src/Narrative/SliceDirector.cs` (or extend `BossEncounterDirector`),
`src/Core/Events/CoreEvents.cs`

Add `SliceCompletedEvent`. Raise it when the player steps through the Frostfang portal **after**
`flag.iron_king_defeated` — i.e. subscribe to `RegionTransitionRequestedEvent` and check the flag.
Set a `flag.slice_complete` so it fires once.

Keep this small. It exists to give the closing card something to hang off and to give a capture build
a clean stopping point — it is **not** an ending system (that is Phase 44).

### 4.6 Task 6 — The closing card (G4/C) ✅

**Files:** `src/UI/OpeningSequence.cs` (generalise) or a sibling `ClosingSequence`,
`data/locale/strings.csv`

`OpeningSequence` already does exactly this job: cards, fades, skip, input lock, `UiState`
integration. **Generalise it rather than copying it** — extract the card list into a parameter so the
same class plays either script, or lift the shared renderer into a small base class. The pure
`OpeningTimeline` needs no changes and its tests already cover the pacing.

Two or three cards, on `SliceCompletedEvent`:

1. The Iron King is dead / his ember is (yours | refused).
2. In the north, something older than him turns its attention south.
3. *"Embervale — Vertical Slice. Your story continues in Frostfang Reach."*

Card 1 should **branch on whether the player absorbed** — check corruption tier or add a flag in the
absorb dialogue. That single branch is what makes the slice feel like it noticed the choice.

**Acceptance:** the card plays once, is skippable, and does not fire on a reload.

### 4.7 Task 7 — Journal hygiene (G6) and string hygiene (G7) ✅

- Remove `quest.cull_goblins` (`data/quests/CullTheGoblins.tres`) and `quest.gather_iron`
  (`data/quests/GatherIron.tres`) from the seeded/startable set, or repoint the elder's old nodes at
  the warband chain. **Do not delete the `.tres` files** without first checking
  `GameIds.Quests.CullGoblins` (`src/Core/GameIds.cs`) and the elder's dialogue, which references
  `quest.gather_iron` in four choices — `ContentValidator` will flag the dangling references, but it
  is cheaper not to create them.
- Convert the remaining literal strings in `dialogue.elder` to `Loc` keys. Mechanical but touches
  many lines; keep it as its own commit so it does not obscure the design work.

### 4.8 Task 8 — Full-arc verification pass ⬜ **outstanding — needs Godot**

See §5. This is a task, not an afterthought — budget real time for it.

---

## 5. Verification

**None of this can be verified without Godot.** The remote sessions that built 32A–33C had `dotnet`
but no engine binary, so every runtime claim in those phases is "reviewed against the Godot 4.7 C#
API", not observed. 33D is content and sequencing — **it must be played**.

### 5.1 Automated (cheap, run constantly)

```
dotnet build Embervale.sln
dotnet test tests/Embervale.Tests
godot --headless --path . -- --validate      # cross-refs, graph reachability, locale
```

The validator already checks dialogue reachability, quest completability, prerequisite cycles, and
that every companion/quest/dialogue id resolves. **New authored content is only as safe as this pass
is green.**

Consider extending `KaelContentTests` (`tests/Embervale.Tests/KaelContentTests.cs`) into a general
slice-content test: it parses `.tres` files textually and checks node reachability, `Goto` targets,
locale keys and enum ordinals **without Godot**, which is exactly the safety net a content-heavy
phase wants.

### 5.2 Manual — the full arc, start to finish

Play it as a stranger would, in one sitting, with tutorials **on**:

1. New game → creation → prologue → does the world lift cleanly?
2. Elder → guild board → bounty → do the hints teach without nagging?
3. Kael pointed at, recruited, fights alongside you?
4. Forge → remedies → does crafting read as worth doing?
5. Heart → is the difficulty step-up felt, and is the party useful?
6. Brazier gate: is the locked prompt understandable?
7. Iron King → win → is the absorb offer legible under the time-slow?
8. Absorb, and separately decline — **both branches**.
9. Portal appears → closing card → correct branch text?
10. Save/load at three points (mid-chain, after recruiting, after the boss) — party, loyalty, flags,
    quests all survive?

Then repeat once **with tutorials off and dialogue skipped**, timing it. If that run is under 20
minutes the slice is too thin for G1.

---

## 6. Notes, risks and things that will bite

### 6.1 Legacy sandbox content

`quest.cull_goblins`, `quest.gather_iron`, the training dummy, the debug goblin camp, the loose spell
tome and `H`/`R`/`K` debug keys in `GameBootstrap` are all **sandbox-era**. They do not break the
slice but they clutter it. 33E is the right place to decide what a capture build shows; note them
now, don't quietly delete them mid-33D.

### 6.2 One condition per choice

`DialogueChoice` carries exactly one `Condition` and one `Effect`. Every "A **and** B" gate must
chain through an intermediate node — `data/dialogue/Kael.tres` shows the pattern twice (the recruit
gate and the loyalty payoff). Budget nodes accordingly; the graph gets wide fast.

### 6.3 Enum ordinals are append-only

`DialogueCondition` / `DialogueEffect` / `TutorialStep` / `LoyaltyTier` are persisted as ints in
`.tres` and saves. Append only, never reorder. `EnumStabilityTests` fails the build if you do.
Current relevant ordinals:

- **Conditions:** `0` Always · `1` QuestAvailable · `2` QuestActive · `3` QuestCompleted ·
  `4` QuestNotStarted · `5` HasFlag · `6` MissingFlag · `7` CorruptionAtLeast · `8` CorruptionBelow ·
  `9` CompanionRecruited · `10` CompanionNotRecruited · `11` CompanionLoyaltyAtLeast
- **Effects:** `1` StartQuest · `2` SetFlag · `3` ClearFlag · `4` AddCorruption · `5` RecruitCompanion ·
  `6` DismissCompanion · `7` AddCompanionLoyalty

### 6.4 `.tres` files do not take comments

A `;` comment line will fail the resource parser. Keep authoring notes in this document instead.

### 6.5 The safe zone

`EmberCrown.tres` sets a 34m safe-zone radius on the hub. Encounter spawns respect it. If the slice
wants pressure near town, that radius is the knob — but shrinking it makes the tutorial beats
hostile, so change it last, if at all.

### 6.6 Kael's model is the player's — ✅ closed

~~`data/companions/Kael.tres` points `ModelPath` at `chr_player_base.glb`.~~ Closed: Kael has an
authored model, `assets/models/characters/npc_kael.glb`. See §8.5.

### 6.7 Scope discipline

33D is *assembly*. If a beat needs a new system, the beat is wrong — every gap in §3 is closable with
authored content plus small, local code changes. Anything larger belongs in the Alpha phases (34+),
not here.

---

## 7. Definition of done

Ticked items were verified by build + the 521-test suite + the textual content tests; the unticked
ones need the engine.

33D is complete when:

- [ ] The eight beats play in order, start to finish, without console commands. *(needs a play-through)*
- [x] The brazier is gated and says why (`boss.challenge_locked`).
- [x] Kael is discoverable through the spine — the elder names him once the bounty is done.
- [x] The Frostfang portal is absent until the Iron King falls (`RegionResource.UnlockFlagId`).
- [x] Both absorb and decline reach a closing card that reflects the choice.
- [x] `dotnet test` is green (521). ⬜ `--validate` still needs Godot.
- [ ] A save/load at three points in the arc restores party, loyalty, flags and quests. *(needs a play-through)*
- [ ] A skipping veteran's run still takes 20+ minutes. *(needs timing)*

Then 33E: polish, art/audio gaps (§6.6), the sandbox cleanup (§6.1), and the external-build capture
pass — and 🚩 **Gate G1**.

---

## 8. Phase 33E — polish & the capture build

### 8.1 The build profile (built)

The project grew up as a sandbox, and that scaffolding was still in the slice path: a training dummy
in the square, a debug goblin camp, a loose loot pile, a spell tome on a plinth, the F1 console, the
F3 debug HUD, the F4 profiler, and single-key cheats (`H` heal, `R` respawn, `X` level, `P`
corruption, `K` reputation). None of it belongs in front of a stranger.

`BuildProfile` (`src/Core/BuildProfile.cs`) gates all of it on one question:

| Run | Sandbox props | Developer tools | Cheat keys |
| --- | ------------- | --------------- | ---------- |
| Editor / `dotnet` debug run | ✅ present | ✅ present | ✅ active |
| **Exported build** | ❌ | ❌ | ❌ |
| Dev run with `--capture` | ❌ | ❌ | ❌ |

An exported build is therefore the slice **automatically**, with no flag to remember and no risk of
shipping a capture with a training dummy in shot. `--capture` gives the same experience from the
editor, so the capture build can be checked without exporting first:

```
godot --path . -- --capture
```

`F5`/`F9` quick save/load stay in every build — they are player conveniences, not developer ones.
The `WorldIntegrityChecker` goes with the developer tools: it exists to shout at the developer, and
it costs a scan every five seconds.

### 8.2 Export presets (authored, unverified)

`export_presets.cfg` defines **Windows Desktop** and **Linux** x86_64 presets writing to `build/`
(already git-ignored), excluding `tests/`, `docs/` and markdown from the pack.

> ⚠️ **These were authored without Godot.** They follow the standard 4.x preset format but have not
> been opened in the export dialog. Before trusting them: install the **.NET export templates** for
> 4.7 (`Editor → Manage Export Templates`), open `Project → Export`, confirm both presets load
> without complaint, and export once. Expect to adjust `application/icon` — no `.ico` exists yet.

### 8.3 The capture checklist

Run the game with `--capture` (or from an exported build) and confirm:

- [ ] No training dummy, debug camp, loose loot pile or spell-tome plinth anywhere in the hub.
- [ ] `F1`, `F3`, `F4` do nothing; `H`/`R`/`X`/`P`/`K` do nothing.
- [ ] The journal is empty on a new game until the guild board bounty is taken.
- [ ] No Frostfang portal in the starting square.
- [ ] The prologue plays, is skippable, and lifts on the town.
- [ ] Tutorial hints appear and clear; they can be switched off in Settings.
- [ ] Frame time is stable in the hub and the arena (check on the min-spec target, not the dev box).
- [ ] Audio: music transitions between explore/combat/boss; no missing-cue warnings in the log.

### 8.4 Known cosmetic gaps at capture time

These are **known and deliberate**, not oversights. Decide which are acceptable in a capture rather
than discovering them in the footage:

| Gap | Detail |
| --- | ------ |
| ~~**Kael wears the player's body**~~ | ✅ **Closed.** Kael has his own model (`npc_kael.glb`); both `Kael.tres` `ModelPath` and the `town_hub.tscn` `Model` instance point at it. See §8.5. |
| Placeholder NPC models | The elder, vendors and innkeeper share three authored meshes. (Kael no longer reuses the guild-rep mesh.) |
| No app icon | `application/icon` is unset in both presets. |
| Music/ambience are procedural | Phase 31 shipped real CC0 SFX but the music and ambience beds are still generated placeholders (carried to Phase 52). |

### 8.5 Kael's model — ✅ done

Authored in a local session with the Blender MCP connected and exported to
`assets/models/characters/npc_kael.glb`. `Kael.tres` `ModelPath` and the `Model` instance in
`scenes/regions/ember_crown/town_hub.tscn` both point at it; nothing else changed, as predicted.

**How it was built.** Kael is derived from `chr_player_base`'s rig, not modelled from zero. The head
was removed (a helm replaces it), the shoulders and chest widened by bone weight, and the body
decimated to ~455 tris; the armour was then built on top. Reusing the rig is what makes him *animate*
— he inherits the player's clip set verbatim, so `CharacterAnimationComponent` resolves every prefix
it looks for. A static Kael would have T-posed and slid, which is worse than the placeholder was.

| Property | Value |
| --- | --- |
| Triangles (LOD0) | 785 — inside the ~800 NPC band (ART_STYLE §3) |
| Height / origin / facing | 1.73 m · origin at the feet · +Z (the factory's `RotateY(PI)` handles Godot) |
| Skeleton | the player's 17 bones, so retargeting stays free |
| Clips imported | `attack, block, cast, channel, death, hit, idle, run` (Godot strips the `-loop` suffix) |
| Materials | 6, all ART_STYLE §2 palette; one ember accent, nothing emissive |

**The silhouette** does the work, since the player spends hours behind him: crested visored helm,
heavy pauldrons, ash-grey surcoat, half-cape off the right shoulder, and a shield slung on his back
carrying the single ember-orange Emberguard device. Dark leather legs read against the player's olive.

### 8.5a A note for whoever rebuilds him

The Blender part of this is fiddly in ways worth writing down:

- Parts authored as separate objects are created at the **world origin**, so joining them into a body
  parked at +3 m along X silently welds the armour 3 m off the character. Set each part object's
  location to the parking offset *before* the join.
- Iterate with the armature in **rest position** (`pose_position = 'REST'`, `use_nla = False`). The
  imported NLA tracks otherwise evaluate a pose, and every proportion judgement is made against a
  distorted body.
- Remap material slots by capturing each face's target index **before** `materials.clear()` — clearing
  resets every polygon to index 0, so a remap written afterwards silently paints the whole mesh in one
  material.
- Flat panels for cloth read as floating slabs from the side. The surcoat is a closed revolved skirt
  for that reason — continuous silhouette is a style rule (ART_STYLE §1.1), not a preference.

### 8.6 What 33E does *not* cover

The polish half of 33E — "rough edges in the slice path are gone" — **cannot be done without playing
the arc**. Pacing, difficulty, dialogue that lands wrong, a beat that drags, a fight that is trivial
with a companion: all of that comes out of §5.2 and none of it is visible from the code. Treat 33E as
half-built until the play-through has happened and its findings have been fixed.
