## Phase 39 — Mounts & Traversal `[F]`

- [x] **39A — `MountComponent`: summon/dismount + mounted locomotion** `[F]` ✅
  - **Done when:** summon/mount/dismount works with mounted move/sprint/stamina.
- [ ] **39B — Mounted-combat rules + fast-travel integration** `[F]`
  - **Done when:** combat-while-mounted rules are defined and mounts integrate with
    fast travel.
- [ ] **39C — Traversal verbs the world needs (climb/swim/ledge)** `[F]`
  - **Done when:** only the verbs region design (44) requires are added and tuned.

---

## 39A — `MountComponent` `[F]` ✅

*38D sold a mount for 400 gold and set a story flag; nothing read it. This is the pass that makes
the flag mean something. Maintainer decisions taken up front: rider-state rather than a second body,
a whistle key usable anywhere, `horse.glb`, and **combat inputs left alone** while mounted (39B owns
those rules, and leaving them untouched writes no half-rule).*

- **Landed:** `MountComponent` (+ pure `MountRules`, 8 tests), `mnt_horse.glb`, a `ride` and a
  `gallop` slot in `AnimationClips` (4 more tests), `Y` / the `mount [own]` dev command, two
  `--validate` rules, four `Loc` keys, and `tools/mount_shots.gd`.
- **The mount is a state of the rider, not a second body.** The player's own `CharacterBody3D` keeps
  moving, wearing a horse: no navigation, no second persistence record, no dismount-placement search,
  no "where does the horse stand while you are in a shop". ⚠️ **The cost is invariant 16 and it is
  written into the class doc rather than discovered in play** — the capsule stays the player's, so a
  mounted horse climbs exactly what a man on foot climbs. **39C is the phase that owns that.**
- ⚠️ **THE SEAT IS THREE NUMBERS AND NOT ONE OF THEM IS DERIVABLE FROM THE FILE.** The pack's glTF
  accessors read **4.8 m tall** for a horse because its armature carries a 100x scale — measuring
  them would have shipped a horse the height of a two-storey house, which is exactly what the first
  render showed. Measured in the engine instead: **2.41 m to the ears, 2.84 m nose to tail** at
  `nodes/root_scale = 0.5`. Then the seat itself (`0.86` up, `0.52` forward) took **four renders**,
  because a saddle height is only wrong on screen.
- ⚠️ **AND `global_transform` LIES IN `_initialize`.** A node added there is not yet inside the tree,
  so it returns identity *with an error line*, and the AABB came back as the raw bind-pose box —
  **0.057 m**, reported confidently, in the same format as the right answer. Invariant 19 in engine
  costume: two `await process_frame`s are the fix and the harness now carries the comment.
- 🎯 **THE RIDER POSE WAS A CHOICE BETWEEN TWO CLIPS AND THE RENDER MADE IT.** The 38A library has no
  riding clip. It has `Sitting_Idle` (hands in the lap — a passenger) and `Driving` (hands out front
  — reins). Both are chair poses and only one reads as a rider. **`AnimationClipsTests` pins
  `Driving` rather than asserting non-empty**, because "some seated clip resolved" is what a passing
  test would have said about the wrong one.
- ⚠️ **`gallop` HAD TO BE ITS OWN SLOT.** `run`'s alias list puts `walk` ahead of `gallop` — correct
  for humanoids, where a jog is this game's run — so asking `run` for a gallop on a horse returns
  **Walk**, and the horse ambles while the player holds sprint. Nothing about that looks broken
  enough to report.
- ⚠️ **THE EXHAUSTION LATCH IS THE ONLY REASON `MountRules` IS A TYPE, AND THE FIRST VERSION OF IT
  WAS WRONG.** Clearing the latch at the recovery mark alone still sawtooths: gallop 1.25 s, walk
  1.8 s, repeat, forever, for a player who never releases sprint — the stutter the latch existed to
  kill, at a period slow enough to look deliberate. It now clears only when the player **stops
  asking**. ⚠️ **Two tests caught this, and one of them was itself wrong**: `Assert.Equal(0f, Stamina)`
  after a blow-out asserts the wrong mechanism (the pool refills while blown; the *latch* is what
  refuses), and it passes for exactly one frame.
- ⚠️ **THE CAMERA PIVOT HAD TO MOVE AND ALMOST DID NOT.** The shipping mode is first person, the eye
  is at 1.62 m, and mounted that is **inside the horse's neck**. It is not visible in any exterior
  render, in the `.tscn` or in a review of the component — only in a shot taken *from the seat*, which
  is why `mount_shots.gd` renders both camera seats and not just pretty angles.
- **`--validate` gains two rules and only one of them is worth much.** The model-path check is the
  archetype rule copied. The real one asserts the stablemaster's `UnlockFlagId` **is** the flag
  `MountComponent` reads: that string lives in two files that never meet, and a rename leaves both
  halves individually correct — the service still charges 400 gold, the component still checks a
  flag — while the horse never comes. It is case 43 in `tools/negative_tests.py`.
- ⚠️ **AND THE MODEL RULE HAS A HOLE, PROVED BY TRYING IT BOTH WAYS.** Moving the `.glb` out of the
  tree leaves `--validate` **passing**, because the `.import` sidecar and the cached `.scn` still
  satisfy `ResourceLoader.Exists`. It catches a wrong *path*, not a deleted *file*. The enemy
  archetype rule it copies has always had that hole. Naming it beat claiming a proof it did not earn.
- ⚠️ **A RESTORED FILE WITH AN OLD TIMESTAMP DOES NOT REBUILD.** Undoing the deliberate path break
  with `mv file.bak file` and rebuilding printed **"Build succeeded"** and then validated the broken
  binary — CLAUDE.md §2's stale-binary trap arriving through a door it does not name. `touch` first.
- Build clean, **0 warnings** + **1308** tests (12 new) + `--validate` exit 0 +
  `tools/negative_tests.py` **43/43** + `--state` unchanged at 15 cells / 15 services + a `--play`
  boot with 32 objects restored and **zero project errors**.
- ⚠️ **What was NOT verified, and it is more than usual.** **No key was ever pressed.** `Y`, the
  toggle, the gallop drain in motion, the toasts and the dev command are proved by unit test and by
  reading — the dev console needs a keyboard and `--play` cannot drive one. The renders are the
  component's transforms and clips **reproduced by a harness**, not the game assembling them: the
  harness posts the same model, the same seat offsets and the same two clips, but it is not
  `MountComponent` doing it. **"The seat is right" is verified; "the component puts the rider
  there" is reviewed.** ⚠️ A `--play` boot also logs `no usable entry for 'mount:player'` — expected
  and self-healing (every new `ISaveable` says it once against an older save), but it means the
  **Load path has not run against real saved data**, only against its own written shape.
- ⚠️ **One thing found that is not mine and needs the maintainer.** All 42 `.claude/skills/*/SKILL.md`
  were regenerated on disk pointing at **`https://ai-game.dev/mcp/...`** instead of
  `http://localhost:23630` — the vendor's hosted cloud, which CLAUDE.md §2 says is not to be switched
  to without asking. **Reverted, not committed.** It is also the likeliest reason `godot-cli status .`
  reported the local server unreachable while the editor was running.
- Two things worth carrying:
  1. ⚠️ **RENDER FROM THE SEAT, NOT AT THE OBJECT.** 37E's carry was *render the approach*; this is
     the next turn of it. Every exterior shot of this horse was correct while the first-person eye
     was inside its neck, and no amount of looking at the thing would have found it — the defect
     lives at a camera position, so the camera position is what has to be rendered.
  2. ⚠️ **A TEST THAT ASSERTS THE WRONG MECHANISM PASSES FOR ONE FRAME AND THEN LIES.** "The pool is
     empty" and "the horse refuses" are different claims about a latch, and only the second is the
     rule. When a test fails, check which of the two you wrote before changing the code.
