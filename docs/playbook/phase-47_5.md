## Phase 47.5 — The Ashen Knight: Rival Duels `[C]`

- [ ] **47.5A — Rival duel rules and non-lethal boss outcome** `[C/F]`
  - **Goal:** reuse Phase 36 without misreporting an escape/draw as death.
  - **Build / Author:** authored health/phase escape threshold, intro/outro sequence, reward/kill-credit
    suppression and duel result flags. Add feature code only under 45K if G2 did not prove the outcome.
  - **Verify:** burst past threshold, stagger/adds, player death, save/load, no loot/vision/realm state.
  - **Done when:** a duel ends once as escape/draw and ordinary boss defeat remains unchanged.

- [ ] **47.5B — Mid-Act-II first duel** `[C]`
  - **Build / Author:** entry based on completed realm count/flag rather than one fixed order; banter variants
    for corruption/companion; distinct arena and exit that restores quest travel.
  - **Verify:** realm order permutations, companion absent/present, skip, death/retry, early trigger.
  - **Done when:** rivalry begins on every valid Act II route and sets one outcome flag.

- [ ] **47.5C — Act III escalated duel and Act IV handoff** `[C/P]`
  - **Build / Author:** different attack/arena pressure, dialogue reading first duel and major choices,
    escape with explicit 49B inputs; sequence-break matrix for missed first duel.
  - **Verify:** first duel outcomes/missed, builds, corruption/loyalty variants and reload at escape.
  - **Done when:** escalation is mechanically visible and 49B can consume every duel history.

---
