## Phase 49 — Main Story, Act IV: Celestial War + Endings `[C]`

> The final state graph is explicit: Act III handoff → assault → Ashen Knight final outcome →
> Morthul outcome → final choice eligibility → Dawnfire or Lord of Embers → epilogue/world finalization.
> Neither ending may be silently locked by optional guild/companion content.

- [ ] **49A — Ending prerequisite/state graph and postgame decision** `[C/P]`
  - **Build / Author:** exact corruption/loyalty/relic/alliance inputs, branch explanations and fallback;
    decide post-ending free roam versus return-to-title before authoring finalization; epilogue variant budget.
  - **Verify:** truth table at every threshold and at least one reachable build for both endings.
  - **Done when:** every terminal state and unavailable choice has an explainable path.

- [ ] **49B — Celestial Realm assault entry and route** `[C]`
  - **Build / Author:** ruined-realm transition, alliance/guild/companion contributions as variants, staged
    battles/checkpoints/map rules and recovery from quit/death/load.
  - **Verify:** minimum-alliance solo path, all major allies, travel disabled/allowed policy, save checkpoints.
  - **Done when:** assault reaches rival confrontation with no optional dependency.

- [ ] **49C — Ashen Knight final confrontation** `[C]`
  - **Build / Author:** final boss using duel history, intro/banter/phase changes, defeat vision, mercy/kill
    only if supported by ending graph, reward and explicit Morthul handoff.
  - **Verify:** missed/each duel result, companion variants, death/skip/reload, reward/vision exactly once.
  - **Done when:** rivalry history materially changes presentation and every outcome advances.

- [ ] **49D — Morthul confrontation and final-choice staging** `[C]`
  - **Build / Author:** encounter/boss, throne transition, dialogue showing eligibility and consequences,
    autosave/checkpoint before irreversible choice; no choice effect before confirmation.
  - **Verify:** both eligible, one eligible, threshold boundaries, death/load, confirmation cancel.
  - **Done when:** choice screen truthfully reflects the state graph and cannot half-apply an ending.

- [ ] **49E — Dawnfire finalization and ending sequence** `[C/P]`
  - **Build / Author:** reject power, relic resolution, atomic Dawnfire flag + 44.5 realm vector, sequence,
    achievement seam and postgame/return transition.
  - **Verify:** repeat/skip/reload, all relic vectors, no loyal companion, finalization idempotence.
  - **Done when:** Dawnfire is reachable, complete and leaves one coherent save state.

- [ ] **49F — Lord of Embers finalization and ending sequence** `[C/P]`
  - **Build / Author:** embrace power, Morthul death/throne claim, atomic Ember flag + realm vector, sequence,
    achievement seam and postgame/return transition.
  - **Verify:** eligibility threshold, repeat/skip/reload, alliance variants, idempotence.
  - **Done when:** Lord of Embers is equally complete and coherent, not a failure cutscene.

- [ ] **49G — Choice-specific companion/guild/realm epilogues** `[C]`
  - **Build / Author:** bounded matrix: ending × each companion outcome × each guild finale × each realm
    result; prioritize material variants and use default narration for unapproved combinations.
  - **Verify:** missing/dead/unrecruited companions, all guild terminals, locale/subtitles and no orphan key.
  - **Done when:** every matrix row resolves to content or an explicit default.

- [ ] **49H — Both-endings sequence-break and reload campaign** `[C/P]`
  - **Verify:** clean starts branched at safe fixtures; both endings; every dangerous save point; early boss/
    flag mutations; skipped sequences; full inventory; postgame/title continuation; orphan flags.
  - **Done when:** both endings complete from legitimate play, no terminal flag vector is impossible, and
    the next action after credits is always defined.

---
