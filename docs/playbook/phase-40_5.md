## Phase 40.5 — Dungeon & Puzzle Framework ❌ **NOT WANTED — STRUCK 2026-08-12**

> **Maintainer direction**, same call as Phase 40 and made in the same breath. The **whole phase**
> goes — the trap and vault arms were offered separately and were struck too, explicitly.

- [x] ~~**40.5A — `PuzzleComponent` + lever/pressure-plate primitive**~~ ❌ Not wanted.
- [x] ~~**40.5B — Sequence + light/shadow puzzle primitives**~~ ❌ Not wanted.
- [x] ~~**40.5C — Trap primitives (spikes/darts/collapsing floor)**~~ ❌ Not wanted. ⚠️ **Offered as a
  keep and declined** — see the consequence below.
- [x] ~~**40.5D — Relic-trial vault convention + one authored example**~~ ❌ Not wanted.
- [x] ~~**40.5E — `docs/RECIPES.md` recipe + `ContentValidator` checks**~~ ❌ Nothing to document.

**Nothing was built and nothing was removed** — no `PuzzleComponent`, `TrapComponent` or vault
convention ever existed, so unlike Phase 40 this cut left no stub to delete. The only work was the
status sweep and this entry.

---

### ⚠️ THIS PHASE HAD TWO DOWNSTREAM CONSUMERS AND BOTH NOW OWE AN ANSWER

Naming them is the whole reason this entry is longer than a checkbox. **A struck phase that other
phases were written against is a debt, not a deletion**, and the debt is discovered at the worst
moment if nobody writes it here.

| Owed by | What it was going to inherit | What it must now do |
| --- | --- | --- |
| **Phase 50** (dungeon authoring) | *"Lands before Phase 50 authors dungeons against it"* was 40.5's stated reason to exist | Dungeons become **rooms with encounters and loot** — the existing `EncounterResource`, `LootTable` and cell-scene tooling, which are built and proven. ⚠️ **Do not quietly reinvent a hazard system inside Phase 50** because a room feels empty; that is this phase coming back through the side door. If a dungeon genuinely needs a hazard, that is a conversation with the maintainer. |
| **Phase 51E** (relics) | 40.5D's *"relic-trial vault convention"* — one puzzle + one guardian per vault, authored once as the template | The guardian half is already expressible (`LairSpawnComponent`, the Ash dragon is the worked example); **the trial half has no answer and needs one when 51E lands.** The likeliest shape is that a relic is *won from a fight or a quest* rather than from a trial, which needs no new system at all. |

### Why the trap arm went too, since it was the arguable one

40.5C was the one sub-phase with no puzzle in it — spike/dart/collapsing-floor hazards routed through
the existing `DamagePacket` pipeline, which is genuinely cheap and genuinely reusable. It was put to
the maintainer as a keep and **declined with the rest.** Recorded because "traps are basically free,
we should just do them" is the obvious thing for a future session to think, and the answer is that it
was already asked and already answered.

⚠️ **A hazard is not free, and the pipeline being ready is the smaller half.** A trap needs placement
in authored scenes, a telegraph the player can read (36C's whole lesson), a reset that survives a
save/load, and a `--validate` arm for dangling triggers. The `DamagePacket` call is the one part that
was already built.

### Two things worth carrying into the next sub-phase

1. ⚠️ **WHEN A PHASE IS STRUCK, THE PHASES THAT WERE WRITTEN AGAINST IT ARE THE DELIVERABLE.** This
   phase existed *for* Phases 50 and 51E — its own roadmap entry says so. Striking it silently would
   have left Phase 50 opening a playbook entry that promised tooling nobody would ever build, and the
   discovery would happen mid-session with dungeons half-authored. **Follow the arrows out of a struck
   phase before closing it**, and write what each consumer must now do instead.
2. ⚠️ **"IT'S CHEAP, WE SHOULD KEEP THAT BIT" IS A DECISION, SO RECORD THAT IT WAS ASKED.** The trap
   arm was offered as a partial keep and declined. Without that sentence the next session sees only a
   struck phase containing an obviously-cheap component and re-opens the question — costing the
   maintainer the same conversation twice, which is precisely what this playbook exists to prevent.

---
