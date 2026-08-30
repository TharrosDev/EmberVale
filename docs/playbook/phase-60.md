## Phase 60 — Localization Completion & Culturalization [C/P]

> Shipped languages are a product decision, not assumed here. Existing Loc/CSV discipline is the source
> pipeline. Translation begins only after 60A signs scope/content lock.

- [ ] **60A — Language/scope/vendor decision and source-string freeze** [P]
  - **Build / Author:** choose launch locales from audience/budget/font/QA/support evidence; decide VO versus text
    per locale and update cadence; approve glossary/style guide, variables/markup/context format and vendor workflow.
  - **Done when:** languages, responsibilities, budget and freeze rules are signed—no unapproved CJK/platform promise.

- [ ] **60B — Extraction/key/context audit** [C/F]
  - **Build / Author:** scan UI/resources/scenes/code for player-facing literals, missing/duplicate/obsolete keys,
    concatenation/plural/gender/context hazards and multiline CSV correctness; generate translator package with source,
    context, screenshot/character limits and variables.
  - **Verify:** pseudo-locale, locale audit, no runtime fallback in shipped scope.
  - **Done when:** source catalogue is complete, stable and context-rich.

- [ ] **60C — Translation import/incremental update pipeline** [C]
  - **Build / Author:** import approved translations without key/order loss, reject missing variables/markup, report
    untranslated/fuzzy/stale strings and preserve translator notes; deterministic rebuild/check.
  - **Verify:** round-trip sample including commas/newlines/quotes/format args and incremental source changes.
  - **Done when:** each scoped locale imports reproducibly with 100% approved status or explicit waiver.

- [ ] **60D — Font, glyph, shaping and fallback coverage** [P]
  - **Build / Author:** per-approved-language font/fallback/size/licence, required scripts/diacritics, controller glyph
    separation and cinematic/UI embedding; only add CJK/RTL handling if chosen.
  - **Verify:** corpus glyph scan, missing-glyph sentinel, shaping/line break/numerals and build packaging.
  - **Done when:** every shipped character renders correctly on every required surface.

- [ ] **60E — UI/subtitle/quest overflow LQA** [C/P]
  - **Verify:** pseudo-long and every locale across HUD/panels/map/dialogue/subtitles/cinematics/settings/store text,
    max text/UI scale and target viewports; clipping, overlap, truncation, reading speed and semantic line breaks.
  - **Done when:** zero blocker/critical LQA and all approved exceptions are intentional/reviewed.

- [ ] **60F — In-context linguistic/cultural QA and sign-off** [C/P]
  - **Verify:** full critical path/both endings plus sampled side/guild/companion content per locale; terminology, tone,
    lore names, gender/number, input prompts, cultural/ratings risks and credits. Fixes update translation memory/source.
  - **Done when:** coverage report complete, no untranslated fallback and native review signs each locale.

---
