# Embervale architecture kit

Session 5 turns the existing Quaternius Medieval Village MegaKit foundation into a reusable
authored building family. It deliberately does not add runtime procedural-building logic.

## Preserved foundation

The original cottage, inn, blacksmith, houses, plaster/timber walls, doors, windows, gables,
floors, corners, roofs, timbers, and chimneys remain available. The five retained monolithic GLBs
had only their embedded material JSON corrected: timber, plaster, stone, glass, and roof surfaces
are now nonmetallic with appropriate roughness; geometry and binary mesh payloads are unchanged.

## Shared material language

| Family | Kit use |
| --- | --- |
| Bone/earth plaster | `MI_Plaster`, pale inhabited wall fields |
| Fieldstone / ruined stone | `MI_UnevenBrick`, ground storeys, workshops, longhouses, ruins |
| Dressed stone | existing rock trim and foundations |
| Dark/weathered timber | `MI_WoodTrim`, framing, balconies, shutters, supports |
| Roof tile | `RoundTile` roof modules, warm dark roofs |
| Moss/weathering | sparse `mod_vine`; use as an accent, not general nature dressing |

All adopted pieces reuse shared production textures. Do not fork textures per prefab for cosmetic
variation.

## Authored prefab family

| Scene | Role | Structural distinction | Collision |
| --- | --- | --- | --- |
| `bld_cottage_shuttered.tscn` | cottage | compact, shutters, offset chimney | solid shell |
| `bld_farmhouse_long.tscn` | farm | long one-storey footprint, dormer | solid shell |
| `bld_shop_awning.tscn` | shop | deep footprint, street awning | solid shell |
| `bld_townhouse_balcony.tscn` | townhouse | two storeys, balcony, exterior stair | solid shell |
| `bld_townhouse_wide.tscn` | townhouse | wide frontage, transverse roof | solid shell |
| `bld_workshop_open.tscn` | workshop/forge | open working front, stone ground floor | wall/floor pieces |
| `bld_longhouse_stone.tscn` | farm/clan hall | long stone footprint, transverse mass | solid shell |
| `bld_inn_courtyard.tscn` | inn | largest footprint, two storeys, awning/balcony | solid shell |
| `bld_ruin_house.tscn` | house ruin | one-storey broken wall plan, no roof | surviving walls only |
| `bld_ruin_tower.tscn` | tower ruin | two-storey broken vertical shell | surviving walls only |

`bld_ashfall_house.tscn` remains the proven enterable two-storey assembly. Solid-shell prefabs are
intentional exterior set pieces; do not imply that their decorative door opens. Hollow/open/ruined
assemblies are the enterable forms and must retain per-wall collision.

## Offline generation

```text
python tools/compose_building.py <name> <wide> <deep> <storeys>
  [--hollow | --open | --ruined]
  [--wall-family plaster|stone-ground|stone]
  [--roof-axis x|z] [--door-index N] [--chimney left|right]
  [--shutters] [--dormer] [--awning] [--balcony] [--stairs] [--weathering]
```

Every generated scene embeds its exact regeneration command. Width/depth are module counts on the
2 m wall grid. Meaningful variants change footprint, storeys, roof direction, wall family, access,
or attached structure; do not publish a tiny prop swap as a new building.

## QA and collision

```text
python tools/check_architecture_kit.py
godot --path . --resolution 960x720 --script res://tools/architecture_shots.gd -- --output <dir>
godot --headless --path . --script res://tools/building_collision_probe.gd
python tools/audit_3d.py --output <dir> --render none
```

The architecture checker is part of the engine quality gate. The capsule probe proves the Ashfall
door, adjacent wall and floor, the open workshop entrance, and a ruin breach. Exterior stairs are
visually available but should not be made route-critical without a dedicated stair traversal probe.

