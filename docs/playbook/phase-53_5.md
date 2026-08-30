## Phase 53.5 — Photo Mode [P] — OPTIONAL

- [ ] **53.5A — Go/no-go gate** [P]
  - **Go only if:** Phase 53 is on track, G3 buffer remains, camera/input/privacy/platform capture rules are
    known and player demand justifies cost. **No-go:** any required art/accessibility/QA item is at risk.
    Record no-go and leave no menu stub.
  - **Done when:** signed scope/budget/rollback decision exists.

- [ ] **53.5B — Safe photo session lifecycle** [F/P]
  - **Build / Author:** only on Go: enter from allowed states, pause policy, detached bounded camera, hide HUD,
    restore transform/FOV/input exactly, forbid gameplay interaction/save state, controller/remap prompts.
  - **Verify:** combat/cinematic/dialogue/loading restrictions, region edge/water, pause/resume/load/quit and cameras.
  - **Done when:** photo mode cannot alter gameplay or strand camera/UI.

- [ ] **53.5C — Composition controls, filters and acceptance** [P]
  - **Build / Author:** minimal FOV/focus/exposure/approved two filters, reduced-motion/color accessibility,
    screenshot path/platform seam and reset defaults; no gameplay shader mutation.
  - **Verify:** all realms/states, ultrawide/controller, repeated captures, perf/memory and filter restoration.
  - **Done when:** scoped controls work, output is recoverable and required milestones were not displaced.

---
