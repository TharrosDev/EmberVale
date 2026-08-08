## Phase 33 — Vertical Slice Assembly & Onboarding `[C/P]`

- [x] **33A — Opening sequence + new-game → creation → world flow** `[C/P]` ✅
  - **Done when:** new game runs creation → opening → Ember Crown as one seamless
    flow.
  - **Landed:** `OpeningSequence` — five narration cards over black carrying the
    LORE premise and closing on the player's own name, played *over the
    already-built world* so the last card lifts on the Ember Crown with nothing
    left to load. Input is held through `UiState` with the mouse still captured;
    interact/attack skip it (Esc deliberately doesn't, so one press can't also
    open the pause menu); it never plays on a load. Pacing lives in the pure
    `OpeningTimeline` (9 tests). `opening` dev command replays it.
- [x] **33B — Diegetic tutorial: movement/look/combat** `[C/P]` ✅
  - **Done when:** move/look/attack/block/dodge are taught via prompts/toasts,
    skippable.
  - **Landed:** `TutorialDirector` teaches look → move → sprint → attack → block →
    dodge by *watching the player play them* — nothing blocks input, gates a door,
    or waits on a modal. Completion reads real game state (a swing is
    `MeleeWeaponComponent.IsCommitted`, a dodge is `Locomotion.IsDashing`), so a
    keypress that did nothing teaches nothing. One self-hiding `TutorialHint` line
    above the hotbar is the whole visible footprint, with live key/gamepad glyphs.
    A **Show Tutorial Hints** setting switches it off live; progress persists so a
    reload never re-teaches. Pure `TutorialScript` (9 tests + ordinal pin);
    `tutorial <status|skip|restart>` dev command.
- [x] **33C — Diegetic tutorial: magic/interact/inventory/quests** `[C/P]` ✅
  - **Done when:** the remaining verbs are taught the same way; nothing blocks a
    veteran from skipping.
  - **Landed:** interact → inventory → journal → cast appended to the same script
    (interact first because it is the verb that makes anything happen; magic last
    because it is the only optional one). These are discrete moments rather than
    held inputs, so they arrive as events — two new ones, `InteractionPerformedEvent`
    (published where the interaction actually fires, not on the keypress) and
    `UiPanelToggledEvent` (any `UiPanel` open/close, reusable beyond onboarding) —
    plus the existing `SpellCastEvent`. Still nothing blocks input, and the Settings
    toggle / `tutorial skip` end the whole thing at any point.
- [~] **33D — Slice stitch: quest chain → guild taste → Iron King → corruption beat → cliffhanger** `[C/P]`
  - **Done when:** 30–60 min plays as one continuous, polished arc.
  - **Built:** the brazier is quest-gated with a prompt that says why; the elder names Kael once the
    bounty is done and carries a corruption warning before the arena; regions declare an
    `UnlockFlagId` so the Frostfang door stays out of the starting square until the Iron King falls;
    `SliceDirector` + `ClosingSequence` end the arc on a card that branches on whether the ember was
    taken; the auto-seeded sandbox quest is gone so the journal starts empty; the elder's
    conversation moved off literal strings. `DialogueContentTests` now validates **every** graph.
  - **Outstanding:** the arc has never been played — see `VERTICAL_SLICE_PLAN.md` §5.2.
  - **Plan:** see [`VERTICAL_SLICE_PLAN.md`](VERTICAL_SLICE_PLAN.md) — the locked design decisions
    (warband chain as spine with Kael woven through, hard-gated boss, closing card + Frostfang
    portal), the eight beats, the seven gaps to close, and a task-by-task build order with
    acceptance criteria.
- [~] **33E — Slice polish + external-build capture pass** `[P]`
  - **Done when:** a capture-ready external build candidate exists; rough edges in
    the slice path are gone.
  - **Built:** `BuildProfile` gates every piece of sandbox scaffolding — the training
    dummy, debug camp, loose loot, spell tome, F1/F3/F4 overlays and the single-key
    cheats — so an **exported build is the slice automatically**, with `--capture`
    giving the same experience from the editor. `export_presets.cfg` adds Windows and
    Linux presets. Capture checklist and the known cosmetic gaps are documented in
    `VERTICAL_SLICE_PLAN.md` §8.
  - **Built (local session, Blender MCP):** Kael has his own model —
    `assets/models/characters/npc_kael.glb`, 785 tris, built on the player's rig so he
    inherits the whole clip set and actually animates in combat. `Kael.tres` and the
    `town_hub.tscn` `Model` instance both point at it, closing §6.6 / §8.4 / §8.5.
    Also fixed: `--validate` never called `Loc.Initialize()`, so headless runs reported
    Kael's authored display keys as missing and the gate was red on `main`.
  - **Play-through (maintainer, 2026-07-30):** the §5.2 full arc was played locally and
    came back clean — no blocking findings. That closes `VERTICAL_SLICE_PLAN.md` §4.8
    Task 8 and the polish half of 33E.
  - **Outstanding:** the export presets have never been opened in Godot's export dialog
    (§8.2) — the last Gate G1 item, and one that needs a human in the editor.

> **🚩 Gate G1 — Vertical Slice.** A stranger plays 30–60 min that looks and feels
> shipped: real art/audio, weighty combat, a companion, a boss, the corruption
> payoff. (Roadmap §3.)

---

# Stage C — Alpha / Feature Complete (→ G2)

> After G2 we never invent a mechanic again. Front-load **all** remaining systems.

---
