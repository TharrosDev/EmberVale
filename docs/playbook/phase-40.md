## Phase 40 — Survival & Needs ❌ **NOT WANTED — STRUCK 2026-08-12**

> **Maintainer direction, verbatim in intent: "I don't want any survival needs in this game."**
> Struck, not deferred and not condition-gated. **There is no condition that revives this phase**, and
> a future session that thinks a needs meter would solve something should read §"Why not" below first.

- [x] ~~**40A — Design decision recorded in `docs/DESIGN.md`**~~ ❌ **Answered from outside.** 40A's
  only deliverable was recording adopt-or-cut for each need. All four are **cut**; the record is the
  table below and `DESIGN.md` §6.
- [x] ~~**40B — Implement the adopted need(s) only**~~ ❌ **Nothing survived 40A, so nothing to build.**
  40A's own brief allowed for this: *"An empty build is a valid outcome."*

---

### ⚠️ THE RULE THIS PHASE NAMED OUTLIVES THE PHASE

**"A cut system leaves no stub" is cited across the repo as *40B's rule*** — in `RECIPES.md`,
`MapService.cs`, `MapLocationResource.cs`, `HudVisibility.cs`, `PRODUCTION_ROADMAP.md` and the
phase-38/39/39.5 entries. **Those citations are still correct and were deliberately not rewritten.**
This entry is where the rule was named; **`docs/NOW.md` invariant 28 is where it now lives.** Striking
a phase does not retract the rule it contributed — and this cut is itself the rule's largest worked
example, because it deleted a stub rather than just ticking a box.

### The decision, need by need

| Need | Call | Why |
| --- | --- | --- |
| **Durability / repair** | ❌ Cut | A maintenance tax on every fight. Would need per-`ItemInstance` save state, a condition channel on every equip slot in the UI, and a repair service — against an economy that already carries **seventeen** sinks. 38D deferred it here and shipped no `ServiceKind.Repair`; that absence is now permanent. |
| **Food / hunger** | ❌ Cut | The three food items stay exactly what they are — instant-heal consumables wearing a `food` trade tag, which is what lets the provisioner and the fishmonger overlap without being the same shop (38L). No meter, no `GrantsStatusId`, no well-fed buff, no cooking. |
| **Rest** | ✅ Already exists — as a **sink**, not a need | `ServiceKind.Inn` and `service.ashfall.bed` already move the clock and refill every resource. Nothing to add and nothing to remove. ⚠️ **Do not read this row as a survival system already in the game** — a bed you pay for is a purchase, and a fatigue meter that forces you into it is the thing that was cut. |
| **Temperature** | ❌ Cut | No weather-driven temperature concept exists anywhere. Adopting one would gate the Frostfang Reach behind purchased gear, which is a **Phase 44 world-layout** question and not a needs one. Nothing existed, so nothing was removed. |

### What the cut actually removed

⚠️ **One stub, and `NOW.md` had been holding it for this phase specifically.**
`CraftingStationType.Cooking` was orphan #3 of the 2026-08-11 feature-continuity audit, flagged with
*"Phase 40A owns the food/cooking decision — do not delete it blind."* The decision is made, so it was
no longer blind: **zero recipes** named it (`data/recipes/*.tres` carry only `Station = 1/2/3`), zero
scenes placed one, and it was the **last** member of an append-only enum, so removing it shifted no
ordinal. Deleted along with its `CraftingStations.Label` arm, its two `[InlineData]` rows and its
`EnumStabilityTests` pin. **Ordinal 4 stays retired; the next station appends at 5.**

⚠️ **That safety came from it being last and unauthored, and nothing else.** Do not read this as
licence to delete an append-only enum member with data behind it — `.tres` files store ordinals.

Seven dangling *"pending 40A"* pointers were settled in the same pass, because a struck phase that
still has six files promising it will decide something is exactly the stub this rule forbids:
`DESIGN.md` §6 (the sink-table row **and** the prose), `ServiceKind.cs`, `EnumStabilityTests.cs`,
`CLAUDE.md` §10, `RECIPES.md`, `README.md`, `PRODUCTION_ROADMAP.md`.

### Why not — the argument, recorded so it is not re-litigated

**A needs meter is a clock that punishes not playing.** Every one of these systems works by draining
on a timer and demanding an interruption, and the interruption is the same one every time: stop, open
a bag, click a thing. Embervale's combat pillars (§1) are about *weight, timing and commitment*, and
its economy intent (§6) is about **scarcity you feel when you choose**, not upkeep you pay when you
forget. A hunger bar does not deepen either; it adds a chore between them.

⚠️ **The specific trap to guard against: reaching for durability as an economy patch.** Repair would
have been a recurring-drip sink, and if Phase 56 finds gold accumulating too fast, "add wear" is the
obvious-looking fix. It is the wrong one — §6's own rule is *sinks the player wants to spend on*. The
table already has one drip (the inn, charged nightly) and sixteen purchases; the intent never rested
on an eighteenth.

### Two things worth carrying into the next sub-phase

1. ⚠️ **STRIKING A PHASE IS WORK, AND MOST OF IT IS OUTSIDE THE PHASE.** The build had zero survival
   code, so "cut it" looked like two checkboxes — but the phase had **one live stub and seven files
   pointing at a decision it would make**, and a struck phase that still has pointers is worse than an
   open one, because the pointers now name something that will never resolve. **The grep for the
   phase's own name is the deliverable**, not the checkbox. `grep -rn "pending 40A\|40A decides"` came
   back empty, and that is what made this real.
2. ⚠️ **A DECISION MADE FROM OUTSIDE STILL NEEDS ITS REASONING WRITTEN DOWN.** The maintainer's
   direction was one sentence and it is authoritative on its own — but the next session to meet a
   balance problem will reach for the nearest untaken lever, and durability is a famous one. **Record
   *why not*, not just *no*** — a bare ❌ invites re-litigation, and "Phase 40 was struck" answers a
   different question than "wear is the wrong fix for that."

---
