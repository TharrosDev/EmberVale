# Runtime debugging and optimization audit — 2026-10-03

This audit reviews shared entity/stat paths, inventory use, save capture/restore, player reload
routes, world streaming/placement, and magic collision/status/delivery lifetimes. It preserves
authored world locations, the prepared-world pipeline, save keys and format version 3. The native
fixtures use SDK-isolated user data; they do not load or modify the maintainer's personal saves.

## Correctness changes

| Trigger | Previous behavior | Corrected behavior / regression |
| --- | --- | --- |
| A component throws during save capture, or two components share an id | A partial snapshot overwrites progress and reports success | Refuse commit, preserve previous bytes and withhold `GameSavedEvent`; `save-audit` |
| Save/load is called from a serialization/restore callback | Nested operations share staging and restore state | Refuse the nested operation; `save-audit` |
| Staged file write fails | Rename can proceed without checking write completion | Flush and check before replacing the authoritative file; `save-audit` |
| Character header composition | Chosen race/name, background and appearance are lost | Keep all four creator fields; `save-audit` and `save-reload` |
| A header has no player location | Default zero coordinates manufacture a location | Keep absent coordinates absent; `save-audit` |
| Fractional format version or a malformed object entry | Invalid state may enter restore | Validate before mutating live components; `save-audit` |
| A newly spawned saveable throws during restore, or location application throws | A partial load may report success | Count dynamic failures and refuse the whole load; `save-audit` |
| Earlier restore removes a registered actor | The snapshot can call a stale/queued component | Skip unregistered, invalid or queued nodes; `save-audit` |
| Empty persistent spawn manifest or restored automatic ids | Abandoned actors remain, or a new spawn aliases an existing id | Replace manifests, allocate unused ids and fail unrecreatable actors; `save-audit` |
| Legacy flat saves exist without a directory-form save | Continue/browser cannot discover a loadable save | Include legacy slots; `save-audit` |
| F9 or pause Load overlays an abandoned session | Removed authored actors stay gone and old character/region can survive | Rebuild through the application-owned coordinator; `save-reload` |
| Quick load is repeated, or its owner quits before the deferred callback | Reload can replace a newer session or reappear after quitting | Coalesce requests and cancel stale generations; `save-reload` |
| Capture/exported sessions process player quick keys | F5/F9 handler is absent | Keep player keys in every profile and gate overlays, cheats and combat logging; `save-reload` |
| A stale consumable instance shares a held template | Healing succeeds without removing the requested item | Secure the exact held unit before healing; `runtime-audit` |
| Healing callback reenters consumption, or owner is dead | One unit can heal twice, or be discarded on a corpse | Remove before callbacks and refuse dead use; `runtime-audit` |
| World focus moves away from the spawn | Pending cells retain spawn-centered priority | Sort around live/tool focus and prioritize required landings; `world-audit` |
| A cell scene has an invalid root | Orphaned roots and unbounded retries | Free the rejected instance and enforce the three-attempt policy; `world-audit` |
| Prepared production data is missing | Source cells can activate without their prepared ground | Keep activation blocked and unsettled; `world-audit` |
| Death/persistence frees a captured descendant | Tier transitions dereference freed native handles | Prune invalid collision/visual/navigation captures; `world-audit` |
| A dynamic actor is parented to a translated cell | Cell offset is applied twice; initialization sees the wrong position | Convert world to local before insertion; `world-audit` |
| A cell leaves Near while its spawned actors remain | Dynamic actors retain visible/collidable bodies | Retire transient cell actors with gameplay residency; `world-audit` |
| Safe placement fails for encounters/events/caches | Analytic terrain fallback can put actors in blocked geometry | Refuse materialization and retry restored events at one-second intervals; `world-audit` |
| Player loading exhausts its capsule-placement retry budget | An analytic fallback releases Playing inside blocked geometry | Abort the unsafe session and clear its completion; real clear/blocked-floor fixtures in `world-audit` |
| A compatible container-form prepared backdrop is loaded | Double instantiation leaks a root and later frees only its child | Instantiate once and own the complete root |
| Allied or enemy capsules share the world layer | Bolts burst on bodies; capsules shield burst targets | Use hurtboxes for actors and the existing actor-aware world ray for cover; `magic-core` |
| Solid cover uses the other physical world layer | Spells can miss that cover | Query both physical world layers; `magic-core` |
| A ray crosses more than four actors | An unseen wall can be reported clear | Bounded 64-actor skipping, conservative exhaustion; `magic-core` |
| A damage callback immediately respawns an actor | Removed effects keep catching up; lethal Kindle applies Burning at respawn | Recheck the live status instance, retain impact center and captured lethal result; `magic-core` |
| Caster death precedes delivery expiry | Old projectiles/ground/barriers/dashes/totems remain | Cancel through `SpellLifetime`, including immediate respawn; `magic-lifetime` |
| The first hit in a burst synchronously loads or kills/respawns its caster | Later victims and rider effects continue in the old timeline | Check the same cancellation authority between targets and callbacks; `magic-lifetime` |
| Extreme sweep distance/radius or DoT timing | Integer overflow weakens collision; additive catch-up can stall | Saturating sweep count and finite arithmetic DoT cadence; numeric tests |
| Repeated default stats creation crosses a native GC boundary | The C# resource script can be released and later construction crashes | Pin the script through `ResidentResources` and retain each component's distinct mutable attributes; `magic-lifetime` forced-GC fixture |
| Dash damage synchronously loads or kills/respawns the caster | Later dash victims, riders and final stun can continue | Share one lifetime authority through the whole dash; the 24-case `magic-lifetime` matrix |
| Different realms reuse a cell scene filename | The map probe associates a marker with another realm's cell | Resolve registered scene paths to exact cell ids; `map` |
| A decorative `Nav` container groups distant lamps | Scene audit mistakes their combined bounds for an unblocked building | Check individual placements and exclude only the grouping container; all-six-realm `scenes` and positive/negative `scene-audit-rules` |
| Storm Tyrant uses the shared humanoid animation library | Disabled silhouette correction crosses its hands through the body | Enable the model's retarget correction, preserve reviewed settings during adoption, and check native poses and rendered views |

## Allocation and scheduling changes

- Stat recalculation preserves the original multiplier order using a second pass over modifiers,
  avoiding the temporary multiplier list. The regression measures invalidation/recalculation after
  warmup and requires zero managed allocation.
- Direct component lookup preserves first matching direct-child order through indexed node access.
  It avoids materializing `GetChildren()` on the repeated lookup path. The native fixture compares
  the previous snapshot loop and production lookup in the same process.
- Homing projectiles reuse shape/query objects and select the preferred target without two per-frame
  lists. Their direct collision queries also remove the need for monitored overlap lists.
- World rays reuse excluded-RID buffers. Streaming reuses request-poll scratch storage and its
  comparator, and sorts pending loads only when their priority needs refreshing.

These are specific code-path improvements. Whole-game FPS claims require comparable rendered
scene measurements; headless timing and source inspection do not establish an FPS increase.

## Validation

Every SDK run writes its exact command, per-gate result, diagnostics and logs to
`artifacts/headless/<run-id>/summary.json`. The final master bake, build, tests, recursive
validation and consolidated native gates are complete. Rendered review scope is recorded below.

| Check | Result and local evidence |
| --- | --- |
| Final build / import | PASS — `20261003T161856-126dec4f1f` (20.8 s), zero build warnings/errors; `20261003T153012-937ea63ef4` (27.2 s), no import diagnostics |
| Normal lifecycle | PASS — `lifecycle` in `20261003T153108-fea7607efb`: three New Game/save/destroy/load round-trips, zero orphan and invariant violations |
| Player checkpoint reload in capture profile | PASS — `save-reload` in `20261003T153108-fea7607efb`: two round-trips, saved identity and migrated region, F9/pause routes, coalescing and stale-request cancellation; zero orphan and invariant violations |
| Final save capture/restore audit | PASS — `save-audit` in `20261003T162356-3cb6d72f7f`, including refused partial/reentrant saves and malformed/dynamic restore failures |
| Final runtime audit and magic lifetime | PASS — both gates in `20261003T162356-3cb6d72f7f`; includes inventory reentrancy, component lookup, callback cancellation, immediate respawn, projectile pooling, forced resource GC and the 24-case delivery lifetime matrix |
| Component lookup microbenchmark | 10,000 same-process calls after warmup: previous snapshot loop **2,252,864 managed bytes / 74.66 ms**, production indexed lookup **0 bytes / 3.59 ms**; `20261003T144851-ae0e3b55e8/01-runtime-audit.stdout.log`. This is one lookup path, not a whole-game FPS comparison |
| Final C# / Python tests | PASS — 3,025 C# tests with zero failures/skips and 34 Python tests; `20261003T161917-417422d182` (26.3 s) |
| Broad native engine coverage | `20261003T145053-b2ecf2472d` (720.2 s): **23 of 26 gates PASS**; map, scenes and animation-library FAILED. Passed coverage includes climate, lifecycle, building collision, step-up, mesh census, regressions, region transition, melee/action clips/sockets, locomotion/view switching/grounding, ranged/camera, combat offence/defence/feedback/ranged, magic learning/content, traversal and streaming stress |
| Scene audit repair | PASS — all-six-realm `scenes` and positive/negative `scene-audit-rules` gates in `20261003T150336-922e8e5a37`; the aggregate remained FAIL because its map gate still failed |
| Map / equipment sockets / animation-library repairs | PASS — all three gates in `20261003T152041-bea54f8d3b` (35.6 s), no diagnostics |
| Asset validation | PASS — `20261003T151851-18dd3e14d2` (39.3 s); asset inventory and findings are under that run's `assets/validate/` directory |
| Final world runtime audit | PASS — 41 checks in `20261003T162356-3cb6d72f7f`, including streaming priority/retries, prepared-data blocking, transient ownership, safe encounters and clear/blocked player landings. The earlier `20261003T153108-fea7607efb` fixture opened overlapping service scopes; SDK correctly failed that gate despite process exit 0. Sequential fixture cleanup now passes without invariant errors |
| Final master bake and source/output hash checks | PASS — `20261003T154601-cd594ae2de` (1,774.5 s): 145 cells plus six regions, 151 outputs; source signature `4e9963094eb60be92dcaacd9318c8440b147a2b5d80a859440ddb5e690bc487a`. The tool's final current-output check passed; existing navigation warnings only |
| Final recursive resource / semantic content validation | PASS — `20261003T161944-da53ba5f70` (132.2 s), zero diagnostics |
| Final world quality and native audit matrix | PASS — consolidated run `20261003T162356-3cb6d72f7f` (39.7 s): native save/world/runtime/magic audits, source/output hashes, architecture, shipping exclusions, and all-six-realm generation, seams, layout, composition, cell content/scenes, districts, maps and atlas |
| Revised Storm Tyrant captures and review | PASS — `20261003T162436-327fc3a6c7` (52.6 s), ten images generated. Six geometry angles visually reviewed; animation sample state verified, with those four sample images not all visually reviewed. The capture reported a startup frame-budget warning (120.96 ms versus 16.67 ms), existing navigation warnings and four ObjectDB instances on exit. This is visual/state evidence, not an FPS improvement claim |

## Remaining boundaries

- `WorldRecovery.Recover` retains an analytic-height fallback after every physics landing candidate
  fails. Replacing it safely needs a pending landing/streaming pin so a falling player cannot be
  stranded while collision becomes ready.
- The maintainer's full exported-game play-through, boss balance and eye-level visual reviews
  remain open in `NOW.md`. This audit does not substitute for those checks.
