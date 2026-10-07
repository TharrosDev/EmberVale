# Embervale — Roadmap

Current-state roadmap (rewritten 2026-09-27; finish run closed 2026-09-28). Live status and verification
numbers are in [`NOW.md`](NOW.md); the finish run's contract is [`playbook/finish.md`](playbook/finish.md);
what each phase produced, and the lessons, are in [`HISTORY.md`](HISTORY.md).

## 1. What the game is now

Embervale is a complete, personal (never published) open-world action RPG in Godot 4.7 / C#:

- **Six realms**, each a generated region streamed from a deterministic offline bake: the Ember
  Crown (52 cells) and Frostfang Reach (36), and the compact finish-run realms — the Ashen Wilds (16),
  the Sunspire Dominion (20), the hidden Pale Concord (12) and the Celestial Realm (9).
- **Seven Flamebearers**, all data-driven bosses behind `BossSummonComponent` braziers: the Iron King,
  the Storm Tyrant, the Beast Lord, the Crimson Prophet, the Hollow Queen, the Ashen Knight and Morthul.
  Each defeat but Morthul's offers its ember (absorb: +25 corruption), drops a relic and plays a
  three-card vision. The Ashen Knight also fights two optional, yielding rival duels in Act II.
- **A main story from New Game to credits**, four acts chained by quest auto-start flags, ending in
  a corruption-decided throne choice (Dawnfire or Lord of Embers), an epilogue and free roam under a
  sky the ending chose.
- **The full systems stack**: combat and magic, companions, housing, the economy, mounts, the map,
  quests with branches, shrines, five guilds (each to rank three and a finale), corruption, save/load,
  and a six-tier item catalogue with sets, unique effects, a crafting skill and reforging.
  [`MECHANICS.md`](MECHANICS.md) lists every mechanic; `ARCHITECTURE.md` is the reference.

Deliberately absent: survival needs (Phase 40) and puzzles/traps (40.5), both struck by the
maintainer; storefront, platform, launch, live ops and extra locales (60–66), because the build is
personal and never published.

## 2. Stages and gates

| Gate | Bar | Status |
| --- | --- | --- |
| G0 First Playable | one region, one boss, the corruption hook end to end | ✅ reached (Stage A) |
| G1 Vertical Slice | the Act I slice at ship quality, played and exported | ⏳ slice built and played by the maintainer 2026-07-30; an export now exists (2026-09-28); not yet signed off on it |
| G2 Feature Complete | every mechanic exists | ✅ in practice (no further systems planned); the Phase 45 audit was never run |
| G3 Content Complete | main story start to finish, both endings | ⏳ built and proved headlessly by `--story`; needs the maintainer play-through |
| G4 Release Candidate | no known blockers | ⏳ Windows export built and smoke-tested (`--story` passes inside it); play-through outstanding |
| G5 Launch / G6 Live | shipped to players | out of scope |

## 3. Phases

✅ done · ◐ partial · ⬜ not built (not needed to finish) · ❌ cut · — out of scope

| # | Phase | | # | Phase | |
| --- | --- | --- | --- | --- | --- |
| 1–21 | Systems foundation | ✅ | 42 | Guild questlines | ✅ all five to rank three + finale |
| 22 | Production bible | ✅ | 42.5 | Crimson Cult | ⬜ (story role via the Prophet) |
| 23 | Corruption | ✅ | 43 | Cinematics | ⬜ (narration cards) |
| 24 | Meta-shell + localization | ✅ | 43.5 | Flamebearer visions | ✅ narration cards (`VisionSequence`) |
| 25 | Streaming + map | ✅ | 44 | All realms built | ✅ |
| 25.5 | Stage A hardening | ✅ | 44.5 | Realm decay | ✅ scoped: ending skies + NPC after-lines |
| 26 | Races | ✅ | 45 | Feature-complete audit | ⬜ |
| 27 | Ember Crown | ✅ | 46 | Act I | ✅ |
| 28 | Iron King | ✅ | 47 | Act II | ✅ |
| 29 | Combat feel | ✅ | 47.5 | Rival duels | ✅ two optional Act II duels |
| 29.5 | Spellcraft | ✅ | 48 | Act III | ✅ |
| 30 | Visual identity | ✅ | 49 | Act IV + endings | ✅ |
| 30.5 | UI/HUD overhaul | ✅ | 50 | Side content pass | ◐ (guild finales) |
| 31 | Audio foundations | ✅ | 50.5 | Lore codex | ⬜ |
| 32 | Companions | ✅ | 51 | Itemization pass | ◐ built 2026-10-06 (catalogue, sets, loot tiers); unplayed |
| 33 | Slice assembly | ✅ (G1 unsigned) | 51.5 | Enchanting | ◐ scoped to reforging; sockets ❌ struck |
| 34 | Enemy roster | ✅ | 52 | Audio production | ⬜ |
| 34.5 | Frostfang clans | ✅ | 53 | Art complete | ◐ (all Meshy bosses adopted; the outdoor world generated 2026-10-06, unplayed; no final pass) |
| 35 | Dragons | ✅ | 53.5 | Photo mode | ⬜ |
| 36 | Boss framework | ✅ | 54 | Accessibility | ◐ remapping, captions and a real difficulty setting built 2026-10-06; unplayed, no audit |
| 37 | Housing | ✅ | 55 | G3 acceptance | ◐ (`--story` + play-through) |
| 37.5 | UI overhaul | ✅ | 56 | Balance | open |
| 38 | Economy | ✅ | 57 | Performance cert | ◐ optimization pass built, unmeasured; cert not run |
| 39 | Mounts | ✅ | 58 | Save hardening | ◐ format 4, backup, blocks; gates in `NOW.md` |
| 39.5 | Map intelligence | ✅ | 59 | QA and soak | ⬜ |
| 40 | Survival and needs | ❌ | 60 | Localization completion | — |
| 40.5 | Puzzles and traps | ❌ | 61–63 | Platform, RC, launch | — |
| 41 | Quest authoring | ✅ | 64–66 | Live ops, post-launch, DLC | — |
| 41.5 | Divine shrines | ✅ | | | |

## 4. What's left

The finish run is done: all seven Meshy bosses adopted, guild finales, visions, rival duels, ending
skies, and a Windows export that passes `--story` inside itself. What remains:

1. **The maintainer play-through** (G1/G3/G4) of the exported build: New Game to credits, both
   endings, from legitimate play.
2. **Eye-level visual review** of the four new realms, the seven arenas, the duel braziers and both
   ending skies (front/back, with people).
3. **Balance tuning** of the six new boss fights and the two duels.
4. **Re-baseline the world visual baseline** (it predates the 2026-09 rebuild and the new realms);
   add the new realms to the per-region GDScript probes (traversal, census, scene audit, shots).
5. **The item, crafting and save upgrade (51, 51.5, 58) was written without the engine and has not
   been played.** Gate results are in [`NOW.md`](NOW.md), with the list of what is unverified: the
   catalogue's balance, the reforge prices, the new panels and the drop presentation.

6. **The performance pass (57) is built and unmeasured.** A Performance tier, first-run detection,
   texture budgets and a sweep of per-frame CPU, HUD and streaming work are on
   `claude/perf-integration`. What it needs: a Godot import, one full master bake, the gates, the
   after-measurement against the recorded baseline, and every tier looked at on a screen.
   [`NOW.md`](NOW.md) has the baseline table and the unverified list.

7. **The UI, HUD and meta-shell upgrade is built, gated and rendered, and has not been played.**
   Every screen and the HUD were rebuilt on `claude/ui-upgrade`, with key and gamepad remapping,
   HUD options, subtitles, a death screen, a credits screen and a difficulty setting that scales
   damage taken. What it needs: a played session with a keyboard and a real gamepad, the
   high-contrast and reduced-motion renders, and a HUD cost measurement (Steam Deck is no longer a
   target; PC and laptop only). [`NOW.md`](NOW.md) has the verification record and the unverified list;
   [`UI_STYLE.md`](UI_STYLE.md) §13 maps the code.

8. **The Meshy world overhaul is built and has not been played, fought, walked or measured.** The outdoor
   world, six NPC bodies, four dragons, three beasts and the iron sword are generated, the cosmetic kits
   are gone, ten bosses are drawn larger than they fight and the haze was reworked, on
   `claude/meshy-world-overhaul`. What it needs: the integrator's gates, one master bake, a fight with
   every scaled boss and dragon, a walk of the narrowed spots, a render of every monument and every
   realm vista, scatter exclusions around the 57 monuments, the delete of the retired Quaternius files,
   and weak-laptop frame times. [`NOW.md`](NOW.md) has the leftover list and the unverified list.

Not planned unless the maintainer asks: 42.5, 43, 50.5, 52, 53.5, 59, and the formal certification
half of 57 (a target-hardware matrix and a signed 16.67 ms budget; the pass above is not that).
Never: 40, 40.5, 60–66, and the socket half of 51.5.

## 5. Standing rules for any further work

- The repo stays buildable and playable at every commit; stateful systems implement `ISaveable`.
- Content is `.tres` data on existing systems; new code only for a genuinely new mechanic.
- `--validate` stays green; every player-facing string goes through `Loc.T`.
- If the player can go there, it is on the map in the same change.
- A cut system leaves no stub.
