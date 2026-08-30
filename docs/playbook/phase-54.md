## Phase 54 — Accessibility & Input [F/P]

> **Extend existing work:** Settings already persist difficulty, subtitles toggle, UI scale, independent
> text scale, high contrast, color-vision adaptation, reduced motion, FOV/sensitivity/invert Y, controller
> gameplay/menu bindings and device-aware prompts. Phase 45A/E must freeze any launch-required remapping,
> difficulty and aim-assist foundations before G2; this phase completes coverage, UX and acceptance. It
> does not introduce a mechanic after feature freeze.

- [ ] **54A — Coverage audit, option taxonomy and accessibility test personas** [P]
  - **Build / Author:** matrix motor/vision/hearing/cognitive comfort across gameplay/UI/cinematics; mark existing,
    incomplete and missing. Define defaults, dependencies, reset, preview and “does not gate content” rules.
  - **Verify:** every setting has live reader, persistence owner, UI and play test.
  - **Done when:** no duplicate/no-op option and every gap has an owner.

- [ ] **54B — Keyboard/mouse + controller remapping completion** [F/P]
  - **Build / Author:** audit the frozen G2 remapping seam across every action/surface; complete primary/secondary
    UX, conflict/unbound messaging, reserved actions, axis/deadzone/inversion, reset and prompt refresh.
  - **Verify:** bind/unbind/conflict, keyboard-only/controller-only, hot-plug, locale layouts, menu/gameplay.
  - **Done when:** every player action is operable/remappable and recovery from a broken map is guaranteed.

- [ ] **54C — Controller navigation/glyph and device-switch acceptance** [F/P]
  - **Build / Author:** audit all panels/dialogue/cinematics/photo optional, focus order/escape, scrolling and glyph
    naming; platform glyph packs conditional on approved targets.
  - **Verify:** controller-only full game route, hot switching, no focus trap and captures.
  - **Done when:** no required interaction needs a mouse or hard-coded wrong glyph.

- [ ] **54D — Subtitles, captions and text readability completion** [F/P]
  - **Build / Author:** speaker names, size/background/width, cinematic timing, important non-speech caption decision,
    long-line wrapping and existing text/UI scale coverage; VO scope from 52A.
  - **Verify:** subtitles off/on, max scale, long locale, overlapping speakers, skip/pause and no VO.
  - **Done when:** all scoped spoken information has synchronized readable text.

- [ ] **54E — Color-independent information and contrast/readability audit** [P]
  - **Build / Author:** inspect HUD/panels/world telegraphs/map/loot/spells/factions; pair hue with shape/text/motion,
    reuse daltonization only at UI token authority, add contrast alternatives where art cannot.
  - **Verify:** grayscale/three color modes/high contrast, critical combat and map comprehension.
  - **Done when:** no required decision depends only on color.

- [ ] **54F — Aim, lock-on and combat-assist completion** [F/P]
  - **Build / Author:** expose and acceptance-test the frozen G2 assists for ranged/lock-on/timing as approved;
    complete explanations, reticle feedback and camera-shake/reduced-motion interactions. Any missing core assist
    returns through 45K and invalidates the relevant G2 evidence.
  - **Verify:** melee/magic/ranged, mouse/controller, multiple/large/flying targets and difficulty combinations.
  - **Done when:** assists improve access without hidden auto-play or broken targeting.

- [ ] **54G — Difficulty/accessibility option presentation and acceptance** [F/P]
  - **Build / Author:** expose the frozen G2 dials allowed by DESIGN—damage dealt/taken, aggression and approved
    timing/assist options—with clear effects/defaults; complete mid-game change messaging and settings behavior.
  - **Do not:** gate quests/regions/endings, reduce telegraph readability or add survival needs.
  - **Verify:** min/max/default, switching during fights/boss/cinematic, load and UI explanation.
  - **Done when:** each option has an observable tested effect and no content divergence.

- [ ] **54H — Full input/accessibility acceptance campaign** [P]
  - **Build / Author:** representative start→combat→quest→travel→inventory→boss→cinematic→save route for each
    persona/device; target-hardware checks conditional on approved platforms, including Deck-sized UI if retained.
  - **Verify:** settings migrate/reset/persist, combinations, performance, no unreachable UI/action.
  - **Done when:** matrix passes with documented limitations and zero accessibility blocker.

---
