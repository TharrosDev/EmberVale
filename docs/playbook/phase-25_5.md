## Phase 25.5 — Stage A Hardening & Stabilization `[F/P]` ✅ **complete (A–P)**

> A consolidation pass over **everything built to that point** — debug, optimize, harden, no new
> features — before races/boss/slice stacked on top. **25.5A–G** hardened the Stage A production
> work (22–25); **25.5H–P** were a fresh regression pass over the foundational systems 1–21.
> The integration sign-off, perf baselines and known-issues ledger live in
> [`STAGE_A_STATUS.md`](STAGE_A_STATUS.md); the durable engineering rules that came out of it are
> in `ARCHITECTURE.md` (§2.7 Save especially) and CLAUDE.md §7. This block is the log.

**Stage A band (22–25 hardening)**

- [x] **25.5A — Save/load integrity sweep** `[F]` — root-caused the recurring save warnings:
  components registered with the `SaveManager` unconditionally, so transient actors wrote volatile
  `stats:<runtimeId>` keys that could never be reclaimed. Fixed with the pure `SaveKeyPolicy` +
  `EntityComponent.RegisterSaveable()`, so **transient actors persist nothing**. Added the
  `savecheck` dev command. → `ARCHITECTURE.md` §2.7.
- [x] **25.5B — Region streaming stability & profiling** `[F/P]` — the post-transition loading
  screen no longer clears on a fixed 0.4 s timer (which popped cells in whenever a region needed
  more than the 1-cell/frame budget); it holds until the streamer reports idle.
- [x] **25.5C — Corruption system hardening** `[F]` — fixed a load desync where the tier event
  didn't re-fire after `Load`, leaving appearance/UI on the pre-load tier.
- [x] **25.5D — Meta-shell, settings & state-machine robustness** `[F]` — state-machine edges,
  settings round-trip, mouse recapture on resume.
- [x] **25.5E — UI/HUD interaction & input hardening** `[F/P]` — input/focus edges; the fast-travel
  trap and block-strand bugs.
- [x] **25.5F — Validator & analytics coverage** `[F]` — widened `ContentValidator` and the
  analytics sink over the Stage A systems.
- [x] **25.5G — Integration regression sweep & known-issues ledger** `[C/P]` — the sign-off pass;
  its output is `STAGE_A_STATUS.md`.

**Systems band (1–21 regression pass)**

- [x] **25.5H — Core, entity/component, events, stats & pooling** `[F]`
- [x] **25.5I — Player controller, locomotion & combat framework** `[F]`
- [x] **25.5J — Enemy AI, perception & spawning** `[F/P]`
- [x] **25.5K — Inventory, equipment & loot generation** `[F]`
- [x] **25.5L — Progression, quests & dialogue** `[F]`
- [x] **25.5M — Magic, status effects & combat math** `[F]`
- [x] **25.5N — World clock/weather/encounters, NPC schedules & procedural events** `[F]`
- [x] **25.5O — Crafting & faction/reputation systems** `[F]`
- [x] **25.5P — Legacy UI panels & HUD** `[P/F]` — also completed the `Loc` sweep over the four
  legacy panels (80 → 113 strings), leaving `DebugHud` exempt per CLAUDE.md §6.

> **Outcome.** Real bugs fixed — save-key collisions, corruption load desync, mouse recapture,
> a fast-travel trap, a lifecycle guard, respawn cadence, block-strand, a cross-transition spawn
> leak — and the load-bearing pure kernels pinned by **242 unit tests** (the suite that has since
> grown to 579). The repo stayed buildable, `--validate`-clean and booting `errors: []` throughout.

---
