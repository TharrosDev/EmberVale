## Phase 23 — The Corruption System `[F]`

> The LORE's **defining mechanic**. The single most important new system in the
> whole production roadmap; the slice and all narrative gate on it. Build the core
> first, then wire one consequence per session.

- [x] **23A — `CorruptionComponent` core + events + save** `[F]`
  - **Goal:** the 0–100 meter and tier state, persistent.
  - **Tasks:** add `src/Corruption/CorruptionComponent.cs` (`EntityComponent`,
    `[GlobalClass]`, on the player). 0–100 value; `Add/Set` API; a `CorruptionTier`
    enum (Untainted → Touched → Marked → Ashbound → Embers) with thresholds.
    Fire `CorruptionChangedEvent` and `CorruptionTierChangedEvent` in a new
    `CorruptionEvents.cs`. Implement `ISaveable` (stable `SaveId`), register in
    `OnInitialize`, unregister in `OnTeardown`. Add to `PlayerFactory`.
  - **Done when:** corruption can be raised/queried in code, fires tier events at
    thresholds, and round-trips save/load. (docs/RECIPES.md "new component" + "new
    persistent system" + "new event".)

- [x] **23B — Corruption dev console + debug surface** `[F]`
  - **Goal:** make it testable before it has any visual.
  - **Tasks:** register a `corruption` console command (`get` / `set N` / `add N`
    / `tier`) per docs/RECIPES.md "new dev-console command," resolving the player via
    `ServiceLocator`. Add a line to the F3 debug overlay showing value + tier.
  - **Done when:** the maintainer can drive corruption from `F1` and watch it on
    F3.

- [x] **23C — Dialogue conditions/effects for corruption** `[F]`
  - **Goal:** let conversations gate and modify corruption.
  - **Tasks:** extend `DialogueEnums.cs` with `Condition` `CorruptionAtLeast` /
    `CorruptionBelow` and `Effect` `AddCorruption`. Wire evaluation in the dialogue
    session runner against `CorruptionComponent`. Author one test dialogue using
    each. (Extends docs/RECIPES.md "new conversation"; read `src/Dialogue/` first.)
  - **Done when:** a conversation visibly branches on corruption and a choice can
    raise it; `validate` understands the new enum values.

- [x] **23D — Corruption UI: character-screen gauge** `[F]`
  - **Goal:** the player can see their corruption.
  - **Tasks:** add a corruption gauge to the character screen via `UiTheme.Bar`
    (docs/RECIPES.md "new UI panel"). Label the current tier. Rebuild from a dirty
    flag in `_Process`, never in a signal handler.
  - **Done when:** the gauge reflects live corruption + tier through `UiTheme`.

- [x] **23E — Corruption HUD vignette at high tiers** `[F/P]`
  - **Goal:** ambient dread at Ashbound/Embers.
  - **Tasks:** add a subtle screen vignette/desaturation overlay in `GameHud` that
    fades in by tier (subscribe to `CorruptionTierChangedEvent`). Keep it through
    `UiTheme` palette; intensity is data-light and tweakable.
  - **Done when:** crossing into high tiers visibly shifts the screen; reverting
    lowers it.

- [x] **23F — `CorruptionAppearanceController` (hook stub)** `[F]`
  - **Goal:** the seam the future model/VFX work plugs into.
  - **Tasks:** add a `CorruptionAppearanceController` on the player that, per tier,
    swaps a placeholder material/emissive (eye glow, ash-vein tint) on whatever
    player mesh exists now. Drive it off the tier event. Designed so Phase 30 can
    replace placeholders with real materials without changing the wiring.
  - **Done when:** each tier shows a *distinct* placeholder appearance change;
    documented as the hook for Phase 30.

- [x] **23G — NPC reaction / global "dread" standing** `[F]` ✅
  - **Goal:** the world fears a corrupted player.
  - **Tasks:** have `ReputationComponent`/faction AI read corruption as a global
    standing modifier ("dread") so high corruption nudges NPC hostility/dialogue.
    Reuse the existing reputation math; don't add a parallel system. (Read
    `src/Factions/`.)
  - **Done when:** raising corruption measurably shifts at least one faction's
    standing/AI reaction; round-trips through save.
  - **Done:** `ReputationComponent` now derives a global `Dread` penalty from the
    sibling `CorruptionComponent`'s tier (Touched 5 · Marked 15 · Ashbound 30 ·
    Embers 50) and exposes `Effective(faction)` = earned `Get` − `Dread`, clamped.
    `TierOf`/`IsHostile` route through `Effective`, so the existing enemy-AI
    `PlayerIsTarget` gate makes factions turn on a corrupted player **live** (and
    stand down as corruption falls) with no new system. Earned standing and its
    persistence are untouched (dread is derived from the already-saved corruption,
    so it round-trips for free). Surfaced in the character-screen reputation panel
    (a "Dread −N" line + effective tiers), the F3 debug HUD, and the `corruption`
    dev-console command.

- [x] **23H — Corrupted ability gating + both-endings eligibility hook** `[F/C]` ✅
  - **Goal:** corruption unlocks corrupted variants and feeds the endings dial.
  - **Tasks:** add a corruption-tier gate option to `SpellResource`/`PerkResource`
    consumption (author one corrupted spell + one corrupted perk `.tres` gated by
    tier — docs/RECIPES.md recipes, no new system). Expose a
    `CorruptionComponent.EndingEligibility` read (Dawnfire vs Lord of Embers
    threshold) that Phase 49 will consume. Document the contract.
  - **Done when:** a tier-gated spell/perk is learnable only above its tier; an
    ending-eligibility value is queryable and saved.
  - **Done:** `SpellResource`/`PerkResource` gained a `MinCorruptionTier` export
    (default `Untainted`, so existing content is ungated). `SpellcastingComponent.Learn`
    and `PerksComponent.CanLearn`/`Learn` resolve the sibling `CorruptionComponent`
    (the 23G lazy pattern) and refuse content above the player's tier. Authored
    `data/spells/EmberSiphon.tres` + `data/perks/AshbornMight.tres`, both gated at
    Marked; the perk shows `[needs Marked]` in the character screen until then, and a
    `learn <id>` dev command verifies the spell gate. `CorruptionComponent.EndingEligibility`
    (`EndingPath` Undecided/Dawnfire/LordOfEmbers) is pure-derived from the saved meter
    via `CorruptionTiers.EligibilityOf` (Dawnfire <40, LordOfEmbers ≥60), unit-tested and
    surfaced in the `corruption` console output. Phase 23 (Corruption) is now complete.

---
