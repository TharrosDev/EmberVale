# Embervale — Session Playbook

**Split from one 373 KB / ~95k-token file into one file per phase (agent-ergonomics pass).** Nothing
was reworded or removed: every retrospective is verbatim in its phase file. It was split because a
single `grep` for a sub-phase id used to drag the whole history — 78% of which is phases nobody
opens any more — through the context window.

> ## ⚠️ How to use this
>
> 1. **[`docs/NOW.md`](../NOW.md) says where the project is.** Read that first, always.
> 2. Open **only your phase's file** below, and read the two entries above yours — the "two things
>    worth carrying into the next sub-phase" lines are the cheapest bug prevention in the repo.
> 3. Entries marked `[ ]` are **plans**; `[x] ✅` are **retrospectives** written afterwards and are
>    the authority on what actually happened and what it cost.
> 4. ⚠️ **"First unchecked" is the WRONG way to find what is next**, and this note exists because it
>    once misled a tool. A `[ ]` with a ⏸ in its line is **parked**, not next — `38G` is the live
>    example. Trust `NOW.md`, not the first empty checkbox.

# Embervale — Session Playbook (the day-by-day breakdown)

> **What this is.** [`PRODUCTION_ROADMAP.md`](PRODUCTION_ROADMAP.md) lays out the
> *phases* (22–66) and the five gates. Each of those phases is far too large to
> finish in a single Claude Code session — they were written as milestones, not
> work units. **This document breaks every phase into lettered sub-phases
> (22A, 22B, 22C …)**, each one sized to fit comfortably inside a *single
> session/context window* and to leave the repo **buildable and playable at the
> end** (CLAUDE.md §1).
>
> Work it **top to bottom**. Open a session, pick the next unchecked sub-phase,
> do *only* that sub-phase, satisfy its **Done when** bar, commit, and stop.
> One sub-phase ≈ one session ≈ one small PR (or one commit on the phase's PR).
> ---
>

> ---

---

## 0. How to use this playbook

### 0.1 The session loop (do this every time)

1. **Pick** the next unchecked `[ ]` sub-phase in order. Do not skip ahead — the
   ordering encodes dependencies.
2. **Read** the sub-phase's *Goal*, *Tasks*, and *Done when*. Read the linked
   docs/RECIPES.md recipe and the relevant `docs/ARCHITECTURE.md` section **before**
   touching code.
3. **Do** only that sub-phase. If you discover it's two sessions of work, split
   it: do the first half, append a new lettered sub-phase for the remainder, and
   stop.
4. **Verify** the *Done when* bar for real. This environment **can** build and run
   (CLAUDE.md §2): `dotnet build Embervale.sln`, `dotnet test tests/Embervale.Tests`,
   and `godot --headless --path . -- --validate`. Run all three; a phase that
   changes content is not done until `--validate` exits 0. Two things still need a
   human at the keyboard — the `F1` dev console (no CLI equivalent) and anything
   behind `F5`/`F9` — so say plainly which checks you *ran* and which you are
   handing over. Reserve "verified" for output you actually captured.
5. **Persist** — if the sub-phase added stateful data, it implements `ISaveable`
   and round-trips *before* you call it done (CLAUDE.md §1).
6. **Commit** with a clear message; tick the box here and update the phase's row
   in `PRODUCTION_ROADMAP.md` §11 if the whole phase closed. Push; open/append the
   draft PR (CLAUDE.md §9).
7. **Stop.** Don't roll two sub-phases into one session unless the second is
   trivially small (a doc tweak, a `.tres` you already have all the data for).

### 0.2 Sub-phase sizing rules (what fits in one session)

A sub-phase is correctly sized when it is **one** of:

- **One new component/service** + its events + its save hook + wiring into *one*
  factory/scene. (Not three components.)
- **One new resource type** (`XxxResource` + its `XxxDatabase` + auto-index) with
  *one* authored example `.tres` and the recipe doc entry.
- **A batch of pure content** (`.tres` only, no code) — e.g. "author 6 enemy
  `.tres` against the existing factory" — capped so the batch is reviewable.
- **One UI panel/widget** built through `UiTheme`.
- **One integration/QA pass** over a bounded slice (one region, one quest line).

If a task needs *new code in three+ systems at once*, it is a phase, not a
sub-phase — split it.

### 0.3 Tags (carried from the roadmap)

**[F]** new engine/feature code · **[C]** content authoring (mostly `.tres`) ·
**[P]** production craft (art/audio/UX/perf/ship). Most sub-phases blend; the tag
marks the centre of gravity. **[C]** sub-phases are the cheapest sessions (data,
no code) — batch them when momentum is good.

### 0.4 Legend

- `[ ]` not started · `[~]` in progress (split mid-session) · `[x]` done.
- **DoD** = the phase-level Definition of Done in `PRODUCTION_ROADMAP.md` §0.3.
  Every sub-phase inherits it; the **Done when** line is the sub-phase's *extra*
  bar on top of "it builds, it's playable, it saves, `validate` is green."

---

# Stage A — Pre-production & First Playable (→ G0)

---

## Phases

| Phase | Lines |
| --- | --- |
| [Phase 22 — Production Bible & Content Pipeline `[F/P]`](./phase-22.md) | 82 |
| [Phase 23 — The Corruption System `[F]`](./phase-23.md) | 100 |
| [Phase 24 — Meta-Shell & Localization Spine `[F]`](./phase-24.md) | 189 |
| [Phase 25 — Region Streaming & World Map `[F]`](./phase-25.md) | 188 |
| [Phase 25.5 — Stage A Hardening & Stabilization `[F/P]` ✅ **complete (A–P)**](./phase-25_5.md) | 50 |
| [Phase 26 — Playable Races & Character Creation `[F]`](./phase-26.md) | 91 |
| [Phase 27 — First Playable Region: Ember Crown `[C/P]`](./phase-27.md) | 181 |
| [Phase 28 — First Boss: a Fallen Flamebearer (Iron King slice) `[F/C]`](./phase-28.md) | 107 |
| [Phase 29 — Combat Feel & Game Juice `[F/P]`](./phase-29.md) | 83 |
| [Phase 29.5 — Spellcraft & the Fading Weave `[F]`](./phase-29_5.md) | 126 |
| [Phase 30 — Animation, Models & Visual Identity `[P]`](./phase-30.md) | 312 |
| [Phase 30.5 — UI & HUD Overhaul `[P/F]`](./phase-30_5.md) | 181 |
| [Phase 31 — Audio Foundations `[F/P]`](./phase-31.md) | 60 |
| [Phase 32 — Companion System `[F]`](./phase-32.md) | 59 |
| [Phase 33 — Vertical Slice Assembly & Onboarding `[C/P]`](./phase-33.md) | 81 |
| [Phase 34 — Enemy & Creature Roster `[F/C]` ✅ **complete**](./phase-34.md) | 107 |
| [Phase 34.5 — Frostfang Clans & Beast-Race Factions `[F/C]` ✅ **complete**](./phase-34_5.md) | 199 |
| [Phase 35 — Dragons `[F/C]`](./phase-35.md) | 392 |
| [Phase 36 — Boss Framework & Encounter Design `[F]`](./phase-36.md) | 185 |
| [Phase 37 — Housing & Player Property `[F]`](./phase-37.md) | 194 |
| [Phase 37.5 — AAA Fantasy UI Overhaul `[F/C]`](./phase-37_5.md) | 355 |
| [Phase 38 — Economy, Vendors & Services `[F/C]`](./phase-38.md) | 367 |
| [Phase 39 — Mounts & Traversal `[F]`](./phase-39.md) | 12 |
| [Phase 39.5 — World Map & Location Intelligence `[F/C]`](./phase-39_5.md) | 2 |
| [Phase 40 — Survival & Needs (scoped decision) `[F]`](./phase-40.md) | 11 |
| [Phase 40.5 — Dungeon & Puzzle Framework `[F]`](./phase-40_5.md) | 22 |
| [Phase 41 — Quest Authoring at Scale & Branching `[F/C]`](./phase-41.md) | 21 |
| [Phase 41.5 — Divine Shrines & Blessings `[F/C]`](./phase-41_5.md) | 17 |
| [Phase 42 — Guild & Faction Questlines `[C]`](./phase-42.md) | 15 |
| [Phase 42.5 — The Crimson Cult `[F/C]`](./phase-42_5.md) | 16 |
| [Phase 43 — Cinematics & Scripted Sequences `[F]`](./phase-43.md) | 15 |
| [Phase 43.5 — Flamebearer Vision Sequences `[F/C]`](./phase-43_5.md) | 17 |
| [Phase 44 — Alpha Content Pass: all five realms blocked out `[C]`](./phase-44.md) | 34 |
| [Phase 44.5 — World State: Realm Decay & Restoration `[F]`](./phase-44_5.md) | 21 |
| [Phase 45 — Alpha Hardening & Feature Freeze `[F/P]`](./phase-45.md) | 27 |
| [Phase 46 — Main Story, Act I: Awakening `[C]`](./phase-46.md) | 12 |
| [Phase 47 — Main Story, Act II: Gathering the Flame `[C]`](./phase-47.md) | 20 |
| [Phase 47.5 — The Ashen Knight: Rival Duels `[C]`](./phase-47_5.md) | 14 |
| [Phase 48 — Main Story, Act III: Truth of the Gods `[C]`](./phase-48.md) | 11 |
| [Phase 49 — Main Story, Act IV: The Celestial War + Endings `[C]`](./phase-49.md) | 14 |
| [Phase 50 — Side Content, Activities & World Density `[C]`](./phase-50.md) | 15 |
| [Phase 50.5 — Lore Codex & Compendium `[F/C]`](./phase-50_5.md) | 17 |
| [Phase 51 — Itemization, Loot & Reward Economy Pass `[C]`](./phase-51.md) | 19 |
| [Phase 51.5 — Enchanting & Relic Socketing `[F/C]`](./phase-51_5.md) | 14 |
| [Phase 52 — Full Audio & Music Production `[P]`](./phase-52.md) | 12 |
| [Phase 53 — Art Complete & World Beautification `[P]`](./phase-53.md) | 13 |
| [Phase 53.5 — Photo Mode `[P]`](./phase-53_5.md) | 13 |
| [Phase 54 — Accessibility & Input `[F/P]`](./phase-54.md) | 13 |
| [Phase 55 — Content-Complete Integration & First Full Playthrough `[C/P]`](./phase-55.md) | 21 |
| [Phase 56 — Balance & Difficulty Tuning `[C/P]`](./phase-56.md) | 13 |
| [Phase 57 — Performance & Memory Cert `[P]`](./phase-57.md) | 13 |
| [Phase 58 — Save/Load Hardening & Migration `[F]`](./phase-58.md) | 11 |
| [Phase 59 — Bug Triage, QA & Soak `[P]`](./phase-59.md) | 12 |
| [Phase 60 — Localization Completion & Culturalization `[C/P]`](./phase-60.md) | 11 |
| [Phase 61 — Platform Compliance & Storefront `[P]`](./phase-61.md) | 12 |
| [Phase 62 — Release Candidate & Gold Master `[P]`](./phase-62.md) | 15 |
| [Phase 63 — Launch `[P]`](./phase-63.md) | 13 |
| [Phase 64 — Launch Response & Stabilization `[P]`](./phase-64.md) | 6 |
| [Phase 65 — Post-Launch Content (the long tail) `[C/F]`](./phase-65.md) | 8 |
| [Phase 66 — Expansion / DLC Framework `[F/C]`](./phase-66.md) | 65 |
