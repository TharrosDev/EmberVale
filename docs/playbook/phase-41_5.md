## Phase 41.5 — Divine Shrines & Blessings `[F/C]`

> The Seven Gods get a full LORE section and zero in-game presence beyond Morthul.
> This mechanizes the other six as shrine blessings.

- [x] **41.5A — `ShrineResource` + `BlessingComponent` core** `[F]` ✅
  - **Done:** `shrine.solaryn` grants Lightbearer's Guard (+10 Armor) through a real sandbox
    shrine interactable. `BlessingComponent` persists only claimed `shrine.*` ids and re-derives
    modifiers from `ShrineResource` on every wholesale load; `--shrine-shots` drives the real caller
    and captures front/back day/dusk evidence.
- [ ] **41.5B — Author the six gods' shrines (one per realm + placement)** `[C]`
  - **Done when:** six shrines exist, each with a distinct domain-flavored
    blessing; `validate` green.
- [ ] **41.5C — Corruption-gated blessing refusal/curse** `[F/C]`
  - **Done when:** a high-corruption visit to at least one shrine triggers a
    refusal/curse variant instead of the blessing.

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
