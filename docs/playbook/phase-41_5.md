## Phase 41.5 — Divine Shrines & Blessings `[F/C]`

> The Seven Gods get a full LORE section and zero in-game presence beyond Morthul.
> This mechanizes the other six as shrine blessings.

- [x] **41.5A — `ShrineResource` + `BlessingComponent` core** `[F]` ✅
  - **Done:** `shrine.solaryn` grants Lightbearer's Guard (+10 Armor) through a real sandbox
    shrine interactable. `BlessingComponent` persists only claimed `shrine.*` ids and re-derives
    modifiers from `ShrineResource` on every wholesale load; `--shrine-shots` drives the real caller
    and captures front/back day/dusk evidence.
- [x] **41.5B — Author the six gods' shrines (current playable-world placement)** `[C]` ✅
  - **Done:** all six dead gods now have a distinct, map-linked in-world shrine and blessing;
    `--validate` enforces both the closed resource set and exactly one caller per blessing.
- [ ] **41.5C — Corruption-gated blessing refusal/curse** `[F/C]`
  - **Done when:** a high-corruption visit to at least one shrine triggers a
    refusal/curse variant instead of the blessing.

---

## 41.5B — six world bodies, one player-owned claim set

The sandbox witness is gone. Solaryn now stands in the Ember Crown town hub; Veyra at Ashfall
Homestead; Tharos at Crossway Post; Nyth in Embermarket; Drakar in the arena; and Elyndra at Tarn's
Landing. Each is an authored `Entity` with the existing Quaternius-derived waystone body, collider,
coloured aura, `ShrineComponent`, and generated Landmark pin parented to that exact body. The six
resources carry distinct bonuses: Armor +10, Health +25, Endurance +3, Mana +25, Strength +3, and
Nature resistance +12.

`ValidateShrines` closes the canonical six-god resource set and requires a unique, existing map
location per shrine. The scene-id scan rejects an unknown `ShrineId`; `ValidateShrineWorldBodies`
requires exactly one in-world caller for each resource. The negative suite breaks the set, removes a
body, and misspells a body id in turn. `--shrine-shots` now claims the final Solaryn body, exercises
replacement-load semantics, and writes twelve 1280×720 eye-level front/back frames across the six
compositions.

### Retrospective + traps

The locator generator is the source of truth for the six pins; never hand-write its `.tres`, locale,
or marker output. A shrine root belongs under `Nav` so its collider is baked with the cell. The first
render pass exposed a Tharos shrine hidden by caravan geometry: render every placement front and back
at actual play distance, then move it before calling the cell done.

### Two things worth carrying into the next sub-phase

1. ⚠️ **REFUSAL MUST PRECEDE CLAIMING.** 41.5C may choose a curse/refusal outcome, but it must not
   add a claimed shrine id or a blessing modifier on that branch; the player set remains the only
   authority.
2. ⚠️ **KEEP THE BODY STATELESS.** Corruption gating belongs in the interaction decision and
   player-owned save state, never as a flag or save component on any shrine entity.

---

## 41.5A — the player owns the blessing, not the shrine

`ShrineResource` is authored data (`Id`, name keys, one stat modifier), loaded through the central
content registry and guarded by `ValidateShrines`. `ShrineComponent` is only the in-world caller;
it resolves the resource and asks the player's `BlessingComponent` to claim it. That component owns
the `blessings:player` save entry, applies the stable-id-sourced stat modifier once, strips every
old blessing before loading, then re-derives the restored set. The first Solaryn resource is a
sandbox witness rather than a final placement so 41.5A is playable without stealing 41.5B's world
authoring pass.

`--shrine-shots` starts the newest save, claims the shrine through the actual interactable, exercises
an empty load followed by restoration in memory, and writes four eye-level frames. The pure suite
pins first-claim and replacement-set semantics; the negative suite mutates the modifier to zero and
proves `ValidateShrines` rejects it before restoring the file.

### Two things worth carrying into the next sub-phase

1. ⚠️ **A SHRINE'S WORLD BODY IS NOT ITS PERSISTENCE.** The player holds stable shrine ids and
   derives modifiers from them; do not add a claimed flag or save component to each of the six
   placements. A load must be able to replace one set and leave no modifier from the prior run.
2. ⚠️ **THE SANDBOX WITNESS IS NOT A SHRINE LOCATION.** 41.5B owns the six fiction-led, renderable
   placements. Because players can travel to them, each must ship with its map location in the same
   sub-phase; render front and back with the surrounding people and furniture before calling it done.

---
