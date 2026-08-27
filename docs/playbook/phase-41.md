## Phase 41 — Quest Authoring at Scale & Branching `[F/C]`

- [x] **41A — Reach/Explore + Talk objective types** `[F]` ✅ *(2026-08-12)*
  - **Done when:** both new `ObjectiveResource` types are event-driven like the
    existing two and authorable.
  - **Landed:** `ObjectiveType.Reach` (ordinal 2) and `ObjectiveType.Talk` (ordinal 3), both feeding
    the same `QuestLogComponent.Advance` choke point the existing two use. Talk subscribes
    `DialogueEndedEvent` — **no new event type**; Reach polls at 4 Hz. Two branches in
    `ObjectiveLocator`, filling the seam 39.5C left by name in its class comment (*"future objective
    types (Talk → nearest NPC, Reach → a POI) add a branch here, not a new system"*). Four
    `--validate` arms, five negative cases, and one authored quest that uses both.
  - ⚠️ **REACH IS PROXIMITY, AND THE OBVIOUS IMPLEMENTATION IS THE WRONG ONE.** `MapService` already
    tracks discovery, and driving Reach off it looks like free reuse — the whole feature for one
    subscription. It is a trap: a location authored `RevealWithCell` is discovered **on entering the
    REGION** (invariant 1), so a discovery-driven Reach objective ticks complete the moment the player
    crosses into the Ember Crown, from anywhere in it. The player watches an objective they never
    travelled to satisfy itself. Reach distance-tests `MapService.PositionOf` instead, in
    `QuestLogComponent` — **not** in `MapService`, because `src/World` has no business knowing what a
    quest is and the map would have to poll 64 locations forever to answer a question usually about
    none of them.
  - ⚠️ **`ArrivalRadius` is deliberately its own constant, not `MapService.DiscoveryRadius`** — even
    though both are ~the same size today. Spotting a place and arriving at it are different
    questions; sharing the constant couples them silently the first time either is tuned.
  - ⚠️ **NPCs HAVE NO FACTORY, AND THAT CHANGED WHERE THE LOOKUP GROUP IS JOINED.** Enemies and
    pickups are spawned by code, so their factories call `AddToGroup` — three enemy factories do it in
    three places. All seventeen NPCs are authored **directly as nodes in seven cell `.tscn` files**.
    Adding the group there meant editing seventeen stanzas and remembering it for every NPC ever
    authored after. `DialogueComponent` joins `ObjectiveLocator.NpcGroup` itself, in one line, and
    covers the roster now and later.
  - **`quest.cull_goblins` deleted** (maintainer call), with `GameIds.CullGoblins`. Unstartable since
    Phase 33D dropped the sandbox auto-start, and `quest.warband.bounty` already covers *slay goblins
    for a reward* — reachable and localized. **`QuestGiverComponent` deleted** too: zero references,
    superseded by `DialogueEffect.StartQuest`, and it carried two hard-coded player-facing strings so
    placing it would have shipped untranslated text. Both were `NOW.md` orphans 1 and 2.
  - ⚠️ **THE MOST VALUABLE THING THIS SUB-PHASE SHIPPED WAS NOT AN OBJECTIVE TYPE** — see below.
- [x] **41B — Escort + Defend/Survive objective types** `[F]` ✅ *(2026-08-19)*
  - **Done when:** escort and defend/survive objectives work with fail states.
  - **Landed:** `ObjectiveType.Escort` (4) and `Defend` (5) through the same
    `QuestLogComponent.Advance` choke point, plus the state the quest log never had —
    `QuestStatus.Failed` (2), `QuestFailedEvent`, `QuestLogComponent.Fail`, a journal FAILED section
    and a toast. Four `--validate` arms, four negative cases (66 → 70), the hold accumulator
    extracted pure and tested, and two authored quests: `quest.hollowreach.ledger` (escort,
    continuing 41A's courier thread off Sedge Marrow) and `quest.warband.hold_north` (defend, posted
    on the guild board beside the standing bounty).
  - ⚠️ **41A ASKED THE RIGHT QUESTION AND THE ANSWER WAS "NOTHING IN THIS GAME CAN BE HURT".** *What
    counts as a fail, and which event says so* was answered before any branch was written — and for
    escort the answer forced the whole design. All seventeen authored NPCs are `Entity` nodes with a
    static collider, **no `StatsComponent` and no hurtbox**: an enemy cannot damage one. So an escort
    built on an NPC has no honest fail state at all, and the choice was between building a damageable
    villager and using the damageable ally that already exists. `CompanionFactory` assembles health,
    combat, follow AI, a leash and persistence **from a `.tres`**, and `DialogueEffect.RecruitCompanion`
    already puts one beside the player — so the escortee is `companion.tessa`, the mechanic is two
    dialogue choices, and `CompanionDownedEvent` is the event that means *your charge has fallen*.
  - ⚠️ **THE FAIL STATE FOR A HOLD IS DYING, AND NOTHING ELSE MAY BE.** Defend accumulates seconds in
    the 4 Hz poll Reach already runs; leaving the site **stops the clock and keeps what was earned**.
    A hold that silently rewound would be a fail state wearing no label — the player watches the
    number fall having been told nothing. Walking away costs the time it costs.
  - ⚠️ **A QUEST-STARTED RAID WAS CONSIDERED AND REFUSED, AND IT IS 41A'S TRAP ONE TYPE LATER.**
    `WorldEventDirector.ForceStart` exists and a `DialogueEffect.TriggerWorldEvent` would have been
    eight lines, giving the hold real attackers on cue. But `WorldEventEndedEvent` carries an event
    **id**, not an instance — a randomly rolled raid of the same id is indistinguishable from the
    quest's own, so an unrelated timeout would fail the player's quest. The pressure is geography
    instead: `location.wilds.north` sits ~107 m out, far outside the region's 34 m `SafeZoneRadius`,
    which is exactly where the `EncounterDirector` is already allowed to spawn.
  - ⚠️ **FAILURE IS RETAKEABLE, AND THAT IS ONE CONDITION OF CODE AND ONE PARAGRAPH OF DECISION.**
    `CanStart` refuses a quest already in the log **unless it is `Failed`**. Because every giver's
    offer is gated on `QuestAvailable`, which routes through `CanStart`, **the offer reopens with no
    authoring change** — Sedge asks again. Permanent failure would delete content from a save on one
    bad fight, with no warning and the giver still standing there.
  - ⚠️ **`ObjectiveResource` NOW HAS TWO FIELDS WHOSE MEANING CHANGES WITH THE TYPE, AND BOTH ARE
    GATED.** `LocationId` is **forbidden** on Reach (its target is already the destination) and
    **required** on Escort (its target is a person). `RequiredCount` is a tally everywhere except
    Defend, where it is **seconds** — so the authoring default of 1 is a quarter-second hold that
    completes before the player stops walking, and `--validate` refuses anything under ten.
  - ⚠️ **THE RENDERED FRAME CAUGHT A DEFECT THE BUILD, THE TESTS AND THE VALIDATOR ALL PASSED OVER** —
    see below.
  - **Not exercised in-world:** nothing recruited Tessa, walked her to Embermarket or downed her, and
    nothing stood in the north wilds for a minute. The mechanism is verified by frames, gates and
    tests; **the escort playthrough is owed and is named rather than implied.**
- [x] **41C — Interact/Use + Timed + Stealth objective types** `[F]` ✅ *(2026-08-19)*
  - **Done when:** the remaining objective types are authorable and validated.
  - **Landed:** `ObjectiveType.Interact` (6) and `Stealth` (7), plus timed quests as a **quest-level**
    `QuestResource.TimeLimitSeconds` with a saved countdown and a HUD readout. Five `--validate` arms
    including a new scene scan, five negative cases (70 → 75), both ordinals pinned, and one authored
    caller — `quest.emberdeep.tally` off Coyle Ferrin — that exercises **all three at once**, because
    separately they mean very little: a clock with no route is arithmetic, and a stealth condition
    with no reason to hurry is a walk.
  - ⚠️ **THE OBVIOUS STEALTH EVENT IS CONDITIONAL ON AN AUTHORED KNOB, AND THAT IS 41A'S TRAP IN NEW
    CLOTHES.** `EnemyAlertedEvent` reads like the signal for *you have been spotted* — and
    `EnemyAIComponent` publishes it **only when the profile's `AlertRadius > 0`**, which an ambusher
    profile sets to 0 on purpose so the pack is not given away. A stealth rule riding it would
    silently never fire against exactly the enemies designed to catch you unawares, through a green
    build, green tests and a green validator. `EnemyStateChangedEvent` is published on **every** state
    entry with no condition, and since the brain only ever targets the player, `Combat` means *seen,
    or you swung first*.
  - ⚠️ **A STEALTH OBJECTIVE STARTS ALREADY MET, AND THAT ONE DECISION MOVED THE WHOLE DESIGN.** There
    is nothing to *do* to achieve not being seen. Seeding the count in `QuestProgress`'s constructor
    means `AllObjectivesMet`, the journal, the tracker and the save all stay exactly as they were —
    but it also means **41B's `FailQuestsWith` would have skipped it**, because that method ignores
    objectives already complete. Without the `alreadyMetStillCounts` path the entire type ships as a
    no-op that passes every gate. **A seeded state is invisible to every rule written for an earned
    one.**
  - ⚠️ **TIMED IS NOT AN OBJECTIVE TYPE, AND REFUSING TO MAKE IT ONE IS THE POINT.** `QuestProgress`
    stores one `int` per objective — a count — with nowhere to put a per-objective clock, and two ways
    to express a deadline is invariant 5 waiting to happen. It is a property of the errand, mirroring
    `WorldEventResource.TimeLimitSeconds`. ⚠️ **It stops while the tree is paused**, so reading the
    journal costs nothing — that falls out of the countdown living in an ordinary component's
    `_Process`, and it is written down so nobody "fixes" it into a wall clock.
  - ⚠️ **THE INTERACT ID IS THE SECOND SCENE-AUTHORED ID WITH NO DATABASE BEHIND IT.** All thirteen
    `InteractableComponent` subclasses carry their own domain id (`SpellId`, `PropertyId`, `ShopId`,
    `StationName`…) and a waystone, a container or a trophy stand carries none — so a quest that
    wanted to name *the thing you use* had nothing to name. One `InteractId` on the base class covers
    the family now and later, and `ValidateInteractIdsArePlaced` scans the cell scenes in **both**
    directions, exactly as `ValidateMapMarkersArePlaced` does. ⚠️ **The duplicate arm is the half a
    one-way check misses:** two nodes sharing an id both advance one objective, so the errand is
    completed by whichever the player reaches first.
  - ⚠️ **A GATE ONLY A HUMAN CAN OPEN IS A GATE NO INSTRUMENT SEES BEHIND** — see below.
  - **Not exercised in-world:** nothing walked the tally run, tripped an enemy into `Combat`, or let a
    deadline expire in a live session. Named rather than implied, and still owed alongside 41B's
    escort walk.
- [x] **41D — Choice/Branch objectives + quest state graphs** `[F]` ✅ *(2026-08-19)*
  - **Done when:** quests can branch on story flags/dialogue effects into multiple
    paths/endings with failure states.
  - **Landed:** three authored fields and no new resource type. `ObjectiveResource.RequiredFlagId` /
    `ForbiddenFlagId` make an objective **inert**; `QuestResource.SequentialObjectives` orders them.
    One predicate answers both — `QuestProgress.IsObjectiveActive`, over a pure Godot-free
    `ObjectiveProgress.IsActive` / `AllLiveMet` that carries **eight new tests** (1440 → 1448; 41C
    could add none). Five `--validate` arms, five negative cases (75 → 80), six drawing surfaces
    re-asked, and one authored caller — `quest.hollowreach.barrels` off Sedge Marrow, the first quest
    in the game with two paths.
  - ⚠️ **NO NEW SAVE STATE, AND THAT WAS THE DESIGN RATHER THAN A SAVING.** The branch is not stored;
    it is **re-derived** from a story flag that `StoryFlagsComponent` has persisted since Phase 10.
    So `docs/SAVE_FORMAT.md` needed no edit, `QuestProgress.Save` did not change shape, and every
    pre-41D save loads into the new code as an unbranched quest by construction. The alternative —
    a `chosenPath` int on the progress — is a second answer to a question the flag already answers
    (invariant 5), and it would have needed a migration.
  - ⚠️ **AN INERT OBJECTIVE IS A THIRD STATE, AND SIX SURFACES WERE WRITTEN WITHOUT IT.** Every
    existing filter reads `!IsObjectiveComplete(i)` — correct for two states and wrong for three.
    Three of the six were the loud kind: `CompassStrip.ResolveObjectiveTarget`,
    `MapScreen.TrackedObjectiveLocationId` and `GameHud.UpdateQuestDestination` would each have
    aimed the player **down the branch they had just declined**, with the needle, the pin and the
    distance readout all agreeing with each other and all wrong. This is 41C's carried lesson turned
    inside out: *a seeded state is invisible to every rule written for an earned one* became **a
    state that is neither met nor pending is invisible to every rule that knows only those two.**
  - ⚠️ **THE TRACKER CACHED THE FORK, AND IT IS 41B'S DEFECT IN A CACHE KEY.** `GameHud.UpdateQuest`
    rebuilds its rows only when a signature of quest id + counts changes — and **a flag change moves
    no count**, so the tracker would have kept listing the abandoned path forever. Its sibling was
    exactly 41B's: `QuestLogPanel` subscribed to three quest events and a story flag is not one, so a
    fork chosen with the journal open left the card showing the road not taken. 41B's rule said *the
    grep is not "who draws this" but "who subscribes to the sibling event"*; 41D's amendment is that
    **a cache key is a subscription too**, and it is the one no `Subscribe<>` grep will ever find.
  - ⚠️ **BOTH FLAGS SET IS THE STATE THE AUTHORING HAS TO MAKE UNREACHABLE.** A `DialogueChoice`
    carries one `Effect`, so the fork sets the flag and the *next* node's choice starts the quest —
    which leaves a real window where a player has picked a side and walked off with no quest. Come
    back and the other fork is still on offer, and now **both paths run at once**. The two fork
    choices therefore gate on each other's absence (`MissingFlag`), which closes it and, for free,
    gives the walked-off player their own route back to the accept node.
  - ⚠️ **ZERO LIVE OBJECTIVES IS NOT COMPLETION, AND VACUOUS TRUTH PAYS OUT REWARDS.** A quest whose
    objectives all belong to branches has nothing live until a flag lands, and `AllObjectivesMet` over
    an empty set is trivially true — so the natural one-line filter completes the quest, with gold and
    XP, on the frame it is accepted. `AllLiveMet` refuses it. **41C's only-stealth-objectives trap is
    the same bug with a different empty set.**
  - ⚠️ **THE SEQUENTIAL SCAN STEPS OVER SHUT GATES, AND THAT ONE LINE IS THE WHOLE COMPOSITION.**
    Without it, an ordered branching quest locks itself forever behind the path not taken — the
    journal shows two rows, neither advances, and nothing anywhere says why.
  - **Per-outcome rewards were declined** (maintainer call). The ending is the **flag**, and its
    consumers already existed: `flag.hollowreach.barrels_hushed` opens a gated shelf on
    `shop.hollowreach.hull` through `ShopStockEntry.RequiredFlagId`, live since 38I. A reward table
    per outcome is a second home for rewards; a shelf that stays open for the rest of the game is a
    consequence the player can walk back to.
  - **Not exercised in-world:** nobody has held the conversation, walked either road, or reloaded a
    save mid-branch in a live session. What is proven: the fork renders, re-derives from the flag
    across a frame with no quest event in it, and is gated in five directions. Owed alongside 41B's
    escort walk and 41C's tally run — **three named playthroughs now, and that is the debt to say out
    loud rather than let accumulate quietly.**
- [ ] **41E — Quest-driven world changes** `[F]`
  - **Done when:** a quest can change the world (an NPC dies, a region opens),
    persistently.
- [ ] **41F — Quest-debug console + `ContentValidator` extension** `[F]`
  - **Done when:** `quest start/advance/complete/reset` exist and `validate-all`
    covers the new objective/branch types.

---

## 41A — the defect that was already shipped

⚠️ **TWO OF FOURTEEN QUESTS AUTHORED LITERAL ENGLISH WHERE TWELVE AUTHORED LOCALE KEYS, AND NOTHING
HAD EVER SAID SO.** `GatherIron.tres` carried `Title = "Gather Iron"`; `CullTheGoblins.tres` carried
`Title = "Cull the Goblins"`. Every other quest carries `quest.warband.bounty.title`.

**It was invisible because `Loc.T` returns the key unchanged on a miss.** So `Loc.T("Gather Iron")`
renders "Gather Iron", the quest looks perfect in the journal, on the HUD tracker and in every
screenshot ever taken of it — and it breaks on the first non-English locale. ⚠️ **`quest.gather_iron`
is live**: the Elder hands it out, and it had been wrong since Phase 12.

This is invariant 33's family — *a value can be wrong and nothing will ever say so* — and the shape is
worth naming, because it is not the usual missing-key bug. **A missing key is loud** (the player sees
`quest.foo.title` on screen). **A missing key whose name happens to be readable English is silent**,
and it stays silent through builds, tests, validators, code review, and a rendered frame. The only
instrument that catches it is a rule that asks *is this string a key* rather than *does this string
render*.

`ValidateQuestStringsAreKeys` checks **presence in the catalogue**, not "looks dotted", which is what
makes it worth having: it catches a mistyped key too — the failure that survives a rename.

⚠️ **The same fallback lived in `ObjectiveResource.ShortLabel()`.** It built a line out of the target
(`$"Slay {TargetId}"`), so the first objective authored without a `Description` would have put
`enemy.goblin` on screen — §46 and §72/73 at once. It had never fired because all fourteen quests
authored one, **which is exactly why it survived: a fallback nothing reaches is a defect nothing
reports.** It now returns a generic, and the validator makes the authored key a gate, so the path is
unreachable by rule rather than by luck.

### Two things worth carrying into the next sub-phase

1. ⚠️ **A NEW OBJECTIVE TYPE IS CHEAP; KNOWING WHICH EVENT MEANS WHAT IS THE WHOLE JOB.** Both types
   landed in a few lines each, because `Advance(type, targetId)` was already the one choke point and
   both events already existed. The entire difficulty was in one question — *does "discovered" mean
   "arrived"?* — and the answer was no, in a way that would have shipped a quest completing itself on
   region entry. **41B's escort and defend objectives have the same shape of question**: what exactly
   is a "fail", and which event says so. Answer that before writing the branch.
2. ⚠️ **A HARNESS SHOT IS ONLY EVIDENCE IF IT DRIVES THE THING YOU CHANGED.** `--panelshots` starts a
   quest to give the journal a card, and it took the first startable one — which would have been a
   Kill quest, proving nothing about either new type. Pointing it at the courier quest turned the
   journal shot into the **only** check that Reach is proximity rather than discovery: `--panelshots`
   reveals all 64 locations before shooting, so a discovery-driven Reach renders `1/1` with the player
   standing 53 m away in the town hub. It renders `0/1`. **Extend the shot list to cover the change,
   or the frame is decoration.**

---

---

## 41B — three surfaces, two answers

⚠️ **THE JOURNAL KEPT SHOWING A FAILED QUEST UNDER *ERRANDS*, STILL LABELLED *TRACKED*.**

`QuestLogPanel` marked itself dirty on `QuestStartedEvent`, `QuestObjectiveAdvancedEvent` and
`QuestCompletedEvent`. A new terminal state arrived and nobody told the panel — so a quest that
failed while the journal was open sat there as live work until some *other* quest event happened to
rebuild it. Meanwhile the toast said "Quest failed" and the HUD tracker had already moved on.
**Three surfaces drawing one fact, and two of them were right.**

Everything was green: `dotnet build --warnaserror` clean, 1440 tests passing, `--validate` exit 0,
70/70 negative cases caught. **The only instrument that could see it was the screenshot this
sub-phase had just added for a different reason** — the FAILED section — and the frame came back
with no FAILED section in it at all.

This is invariant 8 meeting invariant 7: *a UI change that has not been captured is not verified*,
and *when a sub-phase adds a state, ask what every existing thing does IN that state*. The second
question, asked of the journal, would have found it without the render. It was not asked, because
the journal was where the new state was being **built** — the blind spot is the file you are already
editing.

### Two things worth carrying into the next sub-phase

1. ⚠️ **THE ANSWER TO "WHICH EVENT MEANS THIS" CAN BE "NO EVENT, BECAUSE THE STATE CANNOT HAPPEN".**
   41A said knowing which event means what is the whole job; 41B's escort found that the event did
   not exist and *could not*, because nothing in the build can damage an NPC. The useful move was not
   to build a damageable NPC — it was to notice that the damageable ally already existed and was
   authored entirely in data. **Before adding the actor a feature needs, check whether the feature
   can be expressed with the actor that already carries the state.** 41C's Interact/Use and Stealth
   types have the same shape of question: *is there already something in this game that knows the
   player did that?*
2. ⚠️ **A NEW STATE HAS TO REACH EVERY SURFACE THAT DRAWS THE OLD ONE, AND THE SURFACE YOU ARE
   EDITING IS THE ONE YOU WILL MISS.** Failure reached the journal's *rendering* (a FAILED section
   was written deliberately) and not the journal's *refresh*. The grep that would have caught it is
   not "who draws quests" but **"who subscribes to `QuestCompletedEvent`"** — every listener of the
   new state's sibling is a candidate listener of the new state. 41C adds no state, but 41D adds
   branching and 41E adds world changes, and both are exactly this shape.

### Two findings, recorded rather than fixed

⚠️ **`WorldEvent.ObjectiveLabel()` and `WorldEventResource.DisplayName` are literal English on a
player-facing surface.** `GameHud.cs:1054` builds the event banner as
`$"★ {Resource.DisplayName} — {worldEvent.ObjectiveLabel()}"`, and `ObjectiveLabel` hard-codes
`"Defeat the raiders (n/N)"`. That is invariant 34's family (CLAUDE.md §6: no hard-coded
player-facing strings), it is **live today**, and it is not 41B's to fix — recorded here so the next
session that opens `src/World` finds it named rather than rediscovering it.

⚠️ **`CompanionResource.RecruitQuestId` is validated and read by nothing** — invariant 37's exact
shape, *a knob you validate is a claim that the knob works*. It is descriptive metadata on all three
companions and harmless today, but it is not a behaviour, and the first author who expects it to gate
recruitment will get a green gate and no effect.

---

## 41C — the prerequisite that hid the whole sub-phase

`quest.emberdeep.tally` was first authored behind `PrerequisiteQuestId = "quest.hollowreach.word"`,
which is good narrative sequencing: the courier quest is how a player learns Hollowreach exists, and
Coyle stands on that wharf.

**It also made every new mechanic in 41C unreachable by any instrument.** `--panelshots` starts a
quest and photographs the journal; `CanStart` refuses a quest whose prerequisite is not *completed*,
and nothing in a screenshot harness can walk to Hollowreach and hold a conversation. So the tracker's
countdown, the Interact row and the Stealth row would all have shipped with a green build, green
tests, a green validator, 75 negative cases — **and not one frame of any of them.**

The gate came off, for a reason that stands on its own: Coyle is two metres from Sedge, so anyone who
has reached him has already found Hollowreach, and a merchant refusing an errand over an unrelated
errand reads as a bug. But the second reason is the one worth carrying: **when a piece of content is
the only caller of a new mechanic, its availability gate is part of the mechanic's testability.**

The frame then did its job immediately — it is the only evidence that a seeded Stealth objective
renders as *satisfied* (`✓ … 1/1`) rather than as `0/1`, which is a distinction invisible to every
other gate, and that the countdown resolves through the same tracker that draws the distance readout.

### Two things worth carrying into the next sub-phase

1. ⚠️ **AN EVENT THAT FIRES CONDITIONALLY IS NOT AN EVENT THAT MEANS THE THING.** 41A asked *which
   event means this*; 41B found *no event can*; 41C found an event that means it **only when a data
   knob says so**. `EnemyAlertedEvent` is published behind `AlertRadius > 0`. Before riding an event,
   read the line that publishes it and ask **what has to be true for it to fire at all** — the answer
   is sometimes authored in a `.tres` a hundred files away. 41D branches on story flags and dialogue
   effects, where the same question is *who else writes this flag*.
2. ⚠️ **A SEEDED STATE IS INVISIBLE TO EVERY RULE WRITTEN FOR AN EARNED ONE.** A Stealth objective
   starts complete, so `FailQuestsWith`'s perfectly correct "you cannot fail what you have already
   finished" guard stepped over the entire type. Every helper that filters on *unmet*, *incomplete*
   or *not yet done* is a candidate to be wrong about a state that begins in the finished position —
   and 41D's branch objectives will have the same shape the moment one path is pre-satisfied.

---

## 41D — the branch that three surfaces pointed away from

⚠️ **AN OBJECTIVE THE PLAYER CANNOT DO IS NOT AN OBJECTIVE THEY HAVE NOT DONE YET, AND NOTHING IN
THIS CODEBASE KNEW THE DIFFERENCE.**

Before 41D an objective had two states, and every consumer was written as a single negation:

```
if (!progress.IsObjectiveComplete(i)) { ...point the player at it... }
```

Six files spelled some version of that. It is correct while "not complete" means "still to do" —
and a branch gate makes it mean *"still to do, or belonging to the road you turned down"*. Three of
the six then answer the question **"where should the player go next?"**: the compass needle
(`CompassStrip.ResolveObjectiveTarget`), the map's quest pin (`MapScreen.TrackedObjectiveLocationId`)
and the tracker's distance readout (`GameHud.UpdateQuestDestination`).

**All three would have agreed with each other, and all three would have been wrong** — needle, pin
and "97 m · N" pointing at the Crossway impound for a player who had just told Sedge to send word to
Odger instead. Invariant 5 says one surface owns each fact; it does not save you when the one fact
is itself computed from a stale premise. Every one of them shares a single predicate now
(`IsObjectiveActive`) rather than a shared *answer*.

### The two that no `Subscribe<>` grep finds

The journal and the tracker draw objective **rows**, and both were stale for reasons that look
nothing alike:

- **`QuestLogPanel`** subscribed to `QuestStartedEvent`, `QuestObjectiveAdvancedEvent`,
  `QuestCompletedEvent`, `QuestFailedEvent` and `GameLoadedEvent`. A story flag is none of those, so
  a fork chosen while the journal was open left the card showing the abandoned path until some
  unrelated quest event rebuilt it. **This is 41B's defect, in the same file, one sub-phase later** —
  and 41B's own rule ("the grep is who subscribes to the sibling event") would have caught it, if
  anyone had thought of `StoryFlagChangedEvent` as a sibling of `QuestObjectiveAdvancedEvent`. It is
  not a quest event. It changes what a quest looks like.
- **`GameHud.UpdateQuest`** does not subscribe to anything; it rebuilds when a **signature** of quest
  id + counts changes. **A flag change moves no count.** So the tracker would have cached the fork
  permanently, and the fix is a term in a cache key rather than a subscription. ⚠️ **A cache key is
  a subscription with no `Subscribe<>` to grep for**, and that is the genuinely new half of this.

Everything was green throughout: `dotnet build --warnaserror` clean, 1448 tests passing, `--validate`
exit 0, 80/80 negative cases caught. **The only instrument that could see either was the pair of
frames this sub-phase added** — and the pair is the point. One frame of a branch proves nothing; two
frames of *the same quest instance* under opposite flags is the only evidence that the branch is
re-derived rather than frozen, which is in turn the entire justification for adding no save state.
The tracker's distance readout flipping **97 m · N → 61 m · NW** with no count changing is that
proof in one number.

### Two things worth carrying into the next sub-phase

1. ⚠️ **WHEN A NEW STATE IS DEFINED BY A NEGATION, GREP FOR THE NEGATION, NOT FOR THE FEATURE.**
   41A asked *which event means this*; 41B found *no event can*; 41C found *an event that means it
   only when a knob says so*; 41D's question was different in kind — the events were fine, and the
   damage was in **six pre-existing boolean tests that had been correct for thirty phases.** The
   productive grep was not "quest" or "branch" but `!IsObjectiveComplete` and `Counts`. **41E adds
   quest-driven world changes, which is a new state on the WORLD** — so the grep there is every
   place that asks whether a thing exists, is alive, or is passable, and the ones written when the
   answer could never change are the ones that will be wrong.
2. ⚠️ **A CACHE KEY IS A SUBSCRIPTION, AND IT IS THE ONE NOTHING LISTS.** `GameHud` keeps a
   signature so it does not rebuild nodes every frame (§50), which is right — and it means the HUD
   has an opinion about *what can change* that lives in a `StringBuilder` rather than in an event
   handler. Any sub-phase that adds a fact a surface draws must ask whether that fact is in the
   signature. 41E's world changes are drawn by the map, the minimap and the compass, **all three of
   which cache**.

### One finding, recorded rather than fixed

⚠️ **`ObjectiveLocator.Locate` is still called with objectives the caller has already filtered, and
it has no opinion of its own about branch state.** That is correct today — every caller filters
first — but it is `RecruitQuestId`'s shape (41B's finding, invariant 37): a helper that looks like it
answers the whole question and does not. If a fourth caller ever appears without the filter, it will
locate an inert objective and nothing will say so.

---

## 41E — the world fact that already had a save home

**A quest completion can now author one persistent world consequence.** `QuestResource.CompletionFlagId`
is written at `QuestLogComponent.TryComplete`'s existing completion choke point, before
`QuestCompletedEvent` is published. The flag lives in the player's `StoryFlagsComponent`, which already
implements `ISaveable`; there is deliberately no quest-world ledger to merge over a restored run.

`quest.warband.heart` sets `flag.frostfang.passage_open`, which the Frostfang region gate already reads.
`quest.emberdeep.tally` sets `flag.emberdeep.tally_delivered`; Coyle's new
`FlagVisibilityComponent` re-derives his departure from it on both the change event and `GameLoadedEvent`.
It disables collision with the mesh, so the absent merchant cannot leave a ghost interact prompt.

**The roadmap's NPC death example is not faked.** Invariant 38 remains true: no authored NPC can take
damage, so 41E ships a departure, not a death event with no body behind it. The new component is a
general actor-presence reader for when future authored state has an honest reason to remove someone.

`ValidateStoryFlags` now counts completion flags as writers, rejects non-`flag.*` completion ids, and
scans scene visibility readers. The negative suite proves both refusals; pure tests cover one-shot
writes and the visibility decision.

### Two things worth carrying into the next sub-phase

1. ⚠️ **A WORLD CHANGE MUST DERIVE FROM ONE PERSISTED FACT.** A second save record for "departed"
   beside the flag would be a second authority and a load-merge hazard. If the fact cannot be named as
   an existing persistent state, establish that state deliberately before building its presentation.
2. ⚠️ **HIDING A BODY IS NOT ONLY VISUAL.** Any state-driven world removal must account for collision
   and prompts, and must refresh after a wholesale load because loads do not replay individual flag
   events.
