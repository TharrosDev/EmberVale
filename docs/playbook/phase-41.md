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
- [ ] **41C — Interact/Use + Timed + Stealth objective types** `[F]`
  - **Done when:** the remaining objective types are authorable and validated.
- [ ] **41D — Choice/Branch objectives + quest state graphs** `[F]`
  - **Done when:** quests can branch on story flags/dialogue effects into multiple
    paths/endings with failure states.
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
