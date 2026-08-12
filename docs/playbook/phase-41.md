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
- [ ] **41B — Escort + Defend/Survive objective types** `[F]`
  - **Done when:** escort and defend/survive objectives work with fail states.
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
