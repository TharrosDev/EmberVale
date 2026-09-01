# Weapon art conventions

This is the canonical contract for Embervale weapon models and their presentation. It applies to
new weapon art, replacements, and first-person variants. Gameplay data remains authoritative for
damage and timing; visual sockets must never become hit detection.

## Coordinate and scale contract

- Author at **1 Blender unit = 1 metre** and export at scale `1.0`.
- A weapon's functional long axis is local **+Y**. For a sword, +Y runs from grip to point.
- Local **+Z** is the front/face of the weapon and +X is the wielder's right when viewed in its
  authoring rest orientation.
- Put the origin on the centreline of the grip, at the point the hand should own. Do not use the
  mesh centroid or point as the pivot.
- Apply object rotation and scale before export. The imported Godot root must remain identity scale.
- The current iron sword is the size reference: `0.223 x 0.960 x 0.051 m`, with the centre of its
  wrapped grip at local `Y = 0.03 m`. One-handed grips should remain approximately 28–36 mm in
  diameter unless the design requires a different class of weapon.

## Sockets and alignment

- Runtime socket names are semantic and stable: `WeaponSocket`, `SpellSocket`, and
  `InteractionSocket` on the first-person arms; `WeaponSocket` on the third-person right hand.
- First-person placement is derived from a measured fist point and a desired blade direction in
  `FirstPersonArmsComponent.GripTransform()`. Do not add a second compensating transform inside a
  weapon GLB.
- A future weapon may provide a different visual grip height, but it must keep +Y forward and the
  grip-centred origin contract. Store class-specific offsets in equipment data or a socket profile,
  not by rotating the mesh differently for first and third person.
- The third-person visual is a rigid child of the normalized humanoid hand. The melee hitbox remains
  owned by `MeleeWeaponComponent`; it is not inferred from render geometry.
- Scabbards, sheaths, quivers, and other stowed visuals are separate `eqp_*` assets on named body
  sockets. A drawn weapon and its empty scabbard can therefore coexist without duplicating weapon
  gameplay state.

## Naming and file layout

| Kind | Pattern | Example |
| --- | --- | --- |
| weapon model | `assets/models/weapons/wpn_<type>_<tier>.glb` | `wpn_sword_iron.glb` |
| equipment/stowed visual | `assets/models/equipment/eqp_<item>_<variant>.glb` | `eqp_pouch_embervale.glb` |
| first-person anatomy | `assets/models/characters/fp_arm_<side>.glb` | `fp_arm_left.glb` |
| reusable Blender builder | `tools/build_<scope>_assets.py` | `build_player_weapon_assets.py` |

Use descriptive material names by physical role (`LightSteel`, `WornLeather`, `GripWrap`) rather
than numbered Blender defaults. Keep one material for surfaces with the same physical response;
do not merge metal, leather, wood, cloth, or skin merely to lower a material count.

## Material contract

- Iron/steel: metallic `0.8–1.0`, roughness normally `0.28–0.55`.
- Leather/wood/grip wrap: metallic `0.0`, roughness normally `0.58–0.82`.
- Cloth: metallic `0.0`, roughness normally `0.78–0.95`.
- Skin: metallic `0.0`, roughness normally `0.58–0.78`.
- Wear is restrained and readable at gameplay distance. Prefer bevel highlights, slightly varied
  roughness, and localized edge wear over noisy full-surface damage.
- Normal maps use the Godot/glTF tangent convention. Inspect both lit sides after import; never fix
  inverted normals with a double-sided material.

## Geometry, collision, and performance

- Give blades real thickness, taper, and bevels. A single plane is not a production blade.
- Keep the silhouette budget where it reads: point, guard, pommel, grip wrap, and large bevels.
- First-person and third-person visuals normally share the same weapon GLB. Create a separate hero
  version only when the world mesh demonstrably fails in gameplay framing.
- Cosmetic viewmodel arms and body equipment have no collision. World pickups use simple authored
  collision; equipped melee collision remains the gameplay hitbox and must not be a generated
  trimesh.
- Preserve source files. Rebuild derived GLBs with repository-local scripts and record provenance in
  the project manifest/reporting policy.

## Export and validation

1. Apply transforms, recalculate outward normals, and inspect UV seams in Blender.
2. Export GLB with `export_yup=True`, embedded materials, and only the intended selected objects.
3. Let Godot regenerate the `.import`; instantiate the imported `PackedScene` rather than reading
   raw glTF accessor bounds as the final authority.
4. Run `python tools/audit_3d.py` and check material, scale/origin, collision, and duplicate reports.
5. Render front, rear, left, right, front 3/4, and rear 3/4. For held weapons also render idle,
   attack, block, draw/equip, and both first- and third-person framing.
6. Reject any pass with a floating grip, wrist intersection, inverted face, implausible scale, or
   material category violation even if export/import succeeds.

Rebuild the Session 2 derived assets with:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background `
  --python tools/build_player_weapon_assets.py -- (Get-Location).Path
```
