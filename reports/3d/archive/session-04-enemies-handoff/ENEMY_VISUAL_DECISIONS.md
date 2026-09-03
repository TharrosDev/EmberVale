# Session 04 enemy visual decisions

This is the identity-level disposition for all 31 authored enemy resources. Decisions describe the
live production result, not whether an obsolete source GLB still exists in the library.

| Enemy | Decision | Live result |
| --- | --- | --- |
| Ancient Dragon | KITBASH | Proven 46-bone dragon rig; aged crown and restrained iron-brown hierarchy. |
| Arcane Echo | IMPROVE | Retained flying rig/material response; added orbiting arcane rings. |
| Ash Dragon | KITBASH | Reliable ancient-dragon rig, ember-dark body, broken crown and binding chains. |
| Ashfall Elk | KEEP | Functional animal rig retained; physically incorrect material response repaired. |
| Ash Maw | REPLACE | Cactus stand-in replaced by a low, four-legged charred-stone maw with ember core and dorsal vents. |
| Bandit | KITBASH | Modern stock silhouette removed from live data; hooded human foundation plus mantle and mask. |
| Barrow Wight | KITBASH | Duplicate ghost removed from live data; physical corpse silhouette, burial plate, gravecloth and corroded crown. |
| Bone Knight | KITBASH | Human martial foundation plus heavy ancient plate. |
| Cinder Thrall | KITBASH | Hooded humanoid foundation plus ash mark and decayed cowl. |
| Cinder Wisp | REPLACE | Cartoon slime replaced by suspended coal fragments around a restrained ember core. |
| Clan Beast Tamer | KITBASH | Human adventurer foundation plus hides and mask. |
| Clan Raider | KITBASH | Human martial foundation plus asymmetrical clan armour. |
| Clan Shaman | KITBASH | Duplicate wizard removed from live data; hides, bone mask, antlers and ritual totem. |
| Cultist | KITBASH | Hooded human foundation plus faction ash mark. |
| Dire Wolf | KITBASH | Wolf rig retained; larger mane/shoulder mass and pronounced fangs produce a distinct silhouette. |
| Frost Drake | KITBASH | Reliable dragon rig with reduced crest/dorsal ice treatment and cold material hierarchy. |
| Frost Stalker | KITBASH | Functional animal rig retained; ice ridge and skull-like face treatment. |
| Grave Shade | KITBASH | Duplicate ghost source remains archived, but live form is a floating veil/halo silhouette with minimal physical armour. |
| Hollow Husk | KITBASH | Generic ghost removed from live data; grounded townsman corpse foundation with hollow ash mark. |
| Hollow Necromancer | KITBASH | Duplicate wizard removed from live data; decayed dress/robe foundation, rib motif, cowl and occult focus. |
| Iron King | KITBASH | Hero-boss pass: layered dark plate, five-point crown, oversized pauldrons/back plate, chains, ember runes and custom axe. |
| Rime Shard | REPLACE | Slime replaced by fractured grounded ice/stone with a cold suspended core. |
| Ruin Crawler | REPLACE | Comic imp replaced by a low articulated ruin construct with iron limbs and stone carapace. |
| Soldier | KITBASH | Ninja stand-in removed from live data; martial human foundation, gambeson/plate harness and kettle helm. |
| Stone Sentinel | REPLACE | Source-pack construct replaced by a massive mossed stone guardian with rune core and iron bindings. |
| Storm Mote | REPLACE | Slime replaced by suspended iron/stone fragments around a lightning core. |
| Syndicate Enforcer | KITBASH | Punk/modern stock silhouette removed from live data; adventurer foundation, asymmetric dark armour and iron mask. |
| Thornback Boar | KITBASH | Bull stand-in removed from live data; low quadruped rig with broad maw, tusks, plated shoulders and thorned back. |
| Ward Golem | REPLACE | Source construct replaced by a chained rune-plated ward body with storm core. |
| Wild Dragon | KITBASH | Reliable dragon rig, natural horn crown/dorsal language and muted woodland material hierarchy. |
| Wolf | KEEP | Good 51-bone/24-clip animal foundation retained and material response repaired. |

## Duplicate disposition

The byte-level audit still reports the archived `enm_barrow_wight`/`enm_grave_shade` pair and
`enm_clan_shaman`/`enm_hollow_necromancer` pair. No live enemy resource uses those duplicate bodies.
Their actual production models and deterministic attachment profiles are structurally different,
as proven by the 230-frame live QA matrix. The historical GLBs remain only for provenance and do
not justify destructive removal from the approved source library.

## Rig and animation disposition

- Human, wolf, animal, dragon and Iron King rigs were preserved where they provide useful coverage.
- The seven replacements have repository-authored three-bone or seven-bone rigs and five required
  clips: idle, locomotion, attack, hit and death.
- `AnimationClips` now recognizes `HitReact` and `Idle_HitReact`, fixing the animal reaction slot.
- Rigid identity pieces follow animated bone deltas while retaining authored axes. They are cosmetic
  and have no collision; gameplay capsules and hitboxes remain authoritative.

