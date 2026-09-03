# Prioritized model-quality findings

Automated flags are triage evidence, not authorization to alter an asset. Confirm with visual QA and dependent tracing.

| Severity | Asset | Finding | Evidence | Action |
| --- | --- | --- | --- | --- |
| HIGH | assets/models/props/prp_rock_cluster.glb | metallic-stone | Rocks sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_waystone.glb | metallic-stone | Grey_Floor.001 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/architecture/mod_wall_base.gltf | ground-offset | lowest rendered point is Z=-0.117 m in Blender | IMPROVE |
| HIGH | assets/models/architecture/mod_window_thin.gltf | ground-offset | lowest rendered point is Z=1.016 m in Blender | IMPROVE |
| HIGH | assets/models/architecture/mod_window_wide.gltf | ground-offset | lowest rendered point is Z=1.016 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_crate.glb | metallic-wood | DarkWood sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_fence.glb | metallic-wood | Wood.026 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/architecture/bld_house_b.glb | metallic-wood | Wood.017 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_barrel.glb | metallic-wood | DarkWood.005 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_bench.glb | metallic-wood | Wood.022 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_campfire.glb | metallic-wood | Wood.036 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_cart.glb | metallic-wood | Wood.020 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/architecture/mod_gable_4.gltf | unresolved-provenance | no confident manifest/CREDITS provenance match | KEEP |
| HIGH | assets/models/characters/chr_player_base.glb | ground-offset | lowest rendered point is Z=4.821 m in Blender | IMPROVE |
| HIGH | assets/models/characters/npc_woman_dress.glb | collision-render-mismatch | Blender evaluated render height 4.65 m vs authored capsule height(s) [1.8] | KEEP |
| HIGH | assets/models/creatures/enm_ancient_dragon.glb | collision-render-mismatch | Blender evaluated render height 5.00 m vs authored capsule height(s) [5.0, 4.6, 2.2, 4.0] | IMPROVE |
| HIGH | assets/models/creatures/enm_ancient_dragon.glb | rig-root-translation | rigged root RootNode has translation [0, -0.5372230410575867, 0] | IMPROVE |
| HIGH | assets/models/props/prp_banner_guild.glb | metallic-wood | Wood.032 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_lamp_post.glb | metallic-wood | DarkWood.003 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/architecture/bld_house_a.glb | metallic-wood | Wood.016 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/architecture/mod_gable_6.gltf | unresolved-provenance | no confident manifest/CREDITS provenance match | KEEP |
| HIGH | assets/models/architecture/mod_roof_6x8.gltf | ground-offset | lowest rendered point is Z=-0.782 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_boulder.glb | ground-offset | lowest rendered point is Z=-0.316 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_cache_chest.glb | metallic-wood | DarkWood.001 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_glacier.glb | ground-offset | lowest rendered point is Z=-0.282 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_market_stand_b.glb | metallic-wood | Wood.019 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_station_alchemy.glb | metallic-stone | Stone.024 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_station_workbench.glb | metallic-wood | Wood.043 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_tent.glb | metallic-wood | DarkWood.004 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_timber_stack.glb | metallic-wood | Wood sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_well.glb | metallic-wood | Wood.023 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/architecture/mod_roof_4x4.gltf | ground-offset | lowest rendered point is Z=-0.516 m in Blender | IMPROVE |
| HIGH | assets/models/architecture/mod_roof_4x6.gltf | ground-offset | lowest rendered point is Z=-0.516 m in Blender | IMPROVE |
| HIGH | assets/models/characters/fp_arm_left.glb | ground-offset | lowest rendered point is Z=-0.115 m in Blender | IMPROVE |
| HIGH | assets/models/characters/fp_arm_right.glb | ground-offset | lowest rendered point is Z=-0.115 m in Blender | IMPROVE |
| HIGH | assets/models/equipment/eqp_pouch_embervale.glb | ground-offset | lowest rendered point is Z=-0.110 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_cauldron.glb | metallic-stone | Stone.008 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_dock_complex.glb | ground-offset | lowest rendered point is Z=-0.419 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_dock_complex.glb | metallic-wood | Wood_Light sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_fishing_hut.glb | metallic-wood | Wood sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_gazebo.glb | metallic-wood | Wood.025 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_jetty.glb | ground-offset | lowest rendered point is Z=-0.354 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_jetty.glb | metallic-wood | Wood sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_watch_tower.glb | metallic-wood | Wood sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/weapons/wpn_sword_iron.glb | ground-offset | lowest rendered point is Z=-0.126 m in Blender | IMPROVE |
| HIGH | assets/models/architecture/bld_blacksmith.glb | metallic-stone | Plaster.007 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/architecture/bld_cottage.glb | metallic-stone | Stone.022 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/architecture/bld_inn.glb | metallic-wood | Wood.027 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/creatures/boss_iron_king.glb | collision-render-mismatch | Blender evaluated render height 1.87 m vs authored capsule height(s) [2.6] | KEEP |
| HIGH | assets/models/creatures/enm_arcane_echo.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | KEEP |
| HIGH | assets/models/creatures/enm_ash_maw.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | KEEP |
| HIGH | assets/models/creatures/enm_cinder_wisp.glb | collision-render-mismatch | Blender evaluated render height 0.71 m vs authored capsule height(s) [1.0] | IMPROVE |
| HIGH | assets/models/creatures/enm_cinder_wisp.glb | ground-offset | lowest rendered point is Z=0.330 m in Blender | IMPROVE |
| HIGH | assets/models/creatures/enm_frost_stalker.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | KEEP |
| HIGH | assets/models/creatures/enm_goblin.glb | collision-render-mismatch | Blender evaluated render height 1.03 m vs authored capsule height(s) [1.7] | IMPROVE |
| HIGH | assets/models/creatures/enm_goblin.glb | ground-offset | lowest rendered point is Z=0.556 m in Blender | IMPROVE |
| HIGH | assets/models/creatures/enm_goblin.glb | rig-root-translation | rigged root RootNode has translation [0, 0.6132111437939014, 0] | IMPROVE |
| HIGH | assets/models/creatures/enm_grave_shade.glb | duplicate-payload | byte-identical to assets/models/creatures/enm_barrow_wight.glb | IMPROVE |
| HIGH | assets/models/creatures/enm_grave_shade.glb | rig-root-translation | rigged root RootNode has translation [0, -0.16073162853717804, 0] | IMPROVE |
| HIGH | assets/models/creatures/enm_grave_shade.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | IMPROVE |
| HIGH | assets/models/creatures/enm_rime_shard.glb | collision-render-mismatch | Blender evaluated render height 1.18 m vs authored capsule height(s) [1.6] | IMPROVE |
| HIGH | assets/models/creatures/enm_rime_shard.glb | ground-offset | lowest rendered point is Z=0.101 m in Blender | IMPROVE |
| HIGH | assets/models/creatures/enm_ruin_crawler.glb | collision-render-mismatch | Blender evaluated render height 0.50 m vs authored capsule height(s) [0.8] | IMPROVE |
| HIGH | assets/models/creatures/enm_ruin_crawler.glb | ground-offset | lowest rendered point is Z=0.130 m in Blender | IMPROVE |
| HIGH | assets/models/creatures/enm_stone_sentinel.glb | metallic-stone | ConstructIron sets metallic factor 0.78 without a metallic texture | IMPROVE |
| HIGH | assets/models/creatures/enm_storm_mote.glb | collision-render-mismatch | Blender evaluated render height 0.81 m vs authored capsule height(s) [1.1] | IMPROVE |
| HIGH | assets/models/creatures/enm_storm_mote.glb | ground-offset | lowest rendered point is Z=0.215 m in Blender | IMPROVE |
| HIGH | assets/models/equipment/enemy_identity_kit.glb | ground-offset | lowest rendered point is Z=-0.742 m in Blender | IMPROVE |
| HIGH | assets/models/equipment/npc_kit_embervale.glb | ground-offset | lowest rendered point is Z=-0.670 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_bell_tower.glb | metallic-stone | Stone_Light.005 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_cache_chest_open.glb | metallic-wood | DarkWood.002 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_chandelier.gltf | ground-offset | lowest rendered point is Z=-1.422 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_gate_palisade.glb | metallic-wood | Wood sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_market_stand.glb | metallic-wood | Wood.018 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_mine_head.glb | metallic-stone | Stone sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_ore_seam.glb | metallic-stone | Stone sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_shelf.gltf | ground-offset | lowest rendered point is Z=-0.201 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_shelf_arch.gltf | ground-offset | lowest rendered point is Z=-0.614 m in Blender | IMPROVE |
| HIGH | assets/models/props/prp_training_dummy.glb | metallic-wood | DarkWood.001 sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_warden_post.glb | metallic-wood | Wood sets metallic factor 0.40 without a metallic texture | IMPROVE |
| HIGH | assets/models/props/prp_weapon_rack.gltf | duplicate-geometry | evaluated geometry is identical to assets/models/props/prp_weapon_stand.gltf | IMPROVE |
| HIGH | assets/models/characters/fp_arm.glb | ground-offset | lowest rendered point is Z=-0.115 m in Blender | KEEP |
| HIGH | assets/models/characters/fp_arm.glb | metallic-skin | Skin sets metallic factor 0.40 without a metallic texture | KEEP |
| HIGH | assets/models/creatures/enm_ash_dragon.glb | rig-root-translation | rigged root RootNode has translation [0, -3.5176315307617188, 0] | KEEP |
| HIGH | assets/models/creatures/enm_bandit.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | REPLACE |
| HIGH | assets/models/creatures/enm_barrow_wight.glb | duplicate-payload | byte-identical to assets/models/creatures/enm_grave_shade.glb | IMPROVE |
| HIGH | assets/models/creatures/enm_barrow_wight.glb | rig-root-translation | rigged root RootNode has translation [0, -0.16073162853717804, 0] | IMPROVE |
| HIGH | assets/models/creatures/enm_barrow_wight.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | IMPROVE |
| HIGH | assets/models/creatures/enm_bone_knight.glb | rig-root-translation | rigged root RootNode has translation [0, -0.17639869451522827, 0] | KEEP |
| HIGH | assets/models/creatures/enm_clan_beast_tamer.glb | rig-root-translation | rigged root RootNode has translation [0, -0.14811185002326965, 0] | REPLACE |
| HIGH | assets/models/creatures/enm_clan_beast_tamer.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | REPLACE |
| HIGH | assets/models/creatures/enm_clan_raider.glb | rig-root-translation | rigged root RootNode has translation [0, -0.15634028613567352, 0] | REPLACE |
| HIGH | assets/models/creatures/enm_clan_raider.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | REPLACE |
| HIGH | assets/models/creatures/enm_clan_shaman.glb | duplicate-payload | byte-identical to assets/models/creatures/enm_hollow_necromancer.glb | IMPROVE |
| HIGH | assets/models/creatures/enm_clan_shaman.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | IMPROVE |
| HIGH | assets/models/creatures/enm_frost_drake.glb | rig-root-translation | rigged root RootNode has translation [0, -1.6823455095291138, 0] | REPLACE |
| HIGH | assets/models/creatures/enm_frost_drake.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | REPLACE |
| HIGH | assets/models/creatures/enm_hollow_husk.glb | rig-root-translation | rigged root RootNode has translation [0, -0.17149873077869415, 0] | KEEP |
| HIGH | assets/models/creatures/enm_hollow_necromancer.glb | duplicate-payload | byte-identical to assets/models/creatures/enm_clan_shaman.glb | IMPROVE |
| HIGH | assets/models/creatures/enm_hollow_necromancer.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | IMPROVE |
| HIGH | assets/models/creatures/enm_soldier.glb | rig-root-translation | rigged root RootNode has translation [0, 0.1576627790927887, 0] | KEEP |
| HIGH | assets/models/creatures/enm_syndicate_enforcer.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | REPLACE |
| HIGH | assets/models/creatures/enm_thornback_boar.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | REPLACE |
| HIGH | assets/models/creatures/enm_wild_dragon.glb | rig-root-translation | rigged root RootNode has translation [0, -3.058809995651245, 0] | KEEP |
| HIGH | assets/models/props/prp_mushrooms.glb | unresolved-provenance | no confident manifest/CREDITS provenance match | REPLACE |
| HIGH | assets/models/props/prp_weapon_stand.gltf | duplicate-geometry | evaluated geometry is identical to assets/models/props/prp_weapon_rack.gltf | IMPROVE |
| MEDIUM | assets/models/characters/npc_guild_rep.glb | many-materials | 9 materials | KEEP |
| MEDIUM | assets/models/characters/npc_hooded.glb | many-materials | 11 materials | KEEP |
| MEDIUM | assets/models/architecture/mod_chimney.gltf | metallic-stone-risk | MI_RockTrim uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/architecture/mod_wall_door.gltf | metallic-stone-risk | MI_Plaster uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/architecture/mod_wall_plain.gltf | metallic-stone-risk | MI_Plaster uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/architecture/mod_wall_timber.gltf | metallic-stone-risk | MI_Plaster uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/architecture/mod_wall_window.gltf | metallic-stone-risk | MI_Plaster uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/architecture/mod_wall_window_thin.gltf | metallic-stone-risk | MI_Plaster uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/characters/npc_adventurer_f.glb | many-materials | 9 materials | KEEP |
| MEDIUM | assets/models/characters/npc_townsman.glb | many-materials | 11 materials | KEEP |
| MEDIUM | assets/models/characters/npc_vendor.glb | many-materials | 8 materials | KEEP |
| MEDIUM | assets/models/architecture/mod_gable_4.gltf | metallic-stone-risk | MI_Plaster uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/characters/chr_player_base.glb | many-materials | 11 materials | IMPROVE |
| MEDIUM | assets/models/architecture/bld_house_a.glb | many-materials | 9 materials | IMPROVE |
| MEDIUM | assets/models/architecture/mod_gable_6.gltf | metallic-stone-risk | MI_Plaster uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/characters/npc_innkeeper.glb | many-materials | 9 materials | KEEP |
| MEDIUM | assets/models/characters/npc_kael.glb | many-materials | 11 materials | KEEP |
| MEDIUM | assets/models/characters/npc_merchant_m.glb | many-materials | 11 materials | KEEP |
| MEDIUM | assets/models/creatures/mnt_horse.glb | many-materials | 8 materials | KEEP |
| MEDIUM | assets/models/props/prp_dock_complex.glb | many-materials | 9 materials | IMPROVE |
| MEDIUM | assets/models/architecture/bld_blacksmith.glb | many-materials | 8 materials | IMPROVE |
| MEDIUM | assets/models/architecture/bld_inn.glb | many-materials | 7 materials | IMPROVE |
| MEDIUM | assets/models/creatures/boss_iron_king.glb | many-materials | 9 materials | KEEP |
| MEDIUM | assets/models/creatures/enm_goblin.glb | many-materials | 11 materials | IMPROVE |
| MEDIUM | assets/models/equipment/enemy_identity_kit.glb | many-materials | 11 materials | IMPROVE |
| MEDIUM | assets/models/equipment/npc_kit_embervale.glb | many-materials | 12 materials | IMPROVE |
| MEDIUM | assets/models/props/prp_bed.gltf | metallic-cloth-risk | MI_Trim_Cloth uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/props/prp_bell_tower.glb | many-materials | 10 materials | IMPROVE |
| MEDIUM | assets/models/props/prp_chest_wood.gltf | metallic-wood-risk | MI_Trim_Furniture uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/props/prp_crate_wood.gltf | metallic-wood-risk | MI_Trim_Furniture uses metallic multiplier 1.00 with a metallic/roughness texture; inspect channel values | KEEP |
| MEDIUM | assets/models/characters/npc_merchant_f.glb | many-materials | 7 materials | KEEP |
| MEDIUM | assets/models/creatures/enm_bandit.glb | many-materials | 7 materials | REPLACE |
| MEDIUM | assets/models/creatures/enm_syndicate_enforcer.glb | many-materials | 9 materials | REPLACE |
| MEDIUM | assets/models/creatures/enm_thornback_boar.glb | many-materials | 7 materials | REPLACE |
| INFO | assets/models/props/prp_grass_short.glb | nonunit-import-scale | measured import correction is 0.225; do not normalize blindly | KEEP |
| INFO | assets/models/characters/npc_woman_dress.glb | nonunit-import-scale | measured import correction is 0.384; do not normalize blindly | KEEP |
| INFO | assets/models/props/prp_grass_tall.glb | nonunit-import-scale | measured import correction is 0.375; do not normalize blindly | KEEP |
| INFO | assets/models/props/prp_timber_stack.glb | nonunit-import-scale | measured import correction is 4.88; do not normalize blindly | IMPROVE |
| INFO | assets/models/creatures/mnt_horse.glb | nonunit-import-scale | measured import correction is 0.5; do not normalize blindly | KEEP |
| INFO | assets/models/props/prp_clover.glb | nonunit-import-scale | measured import correction is 0.105; do not normalize blindly | KEEP |
| INFO | assets/models/props/prp_dock_complex.glb | nonunit-import-scale | measured import correction is 5.6; do not normalize blindly | IMPROVE |
| INFO | assets/models/props/prp_fern.glb | nonunit-import-scale | measured import correction is 0.35; do not normalize blindly | KEEP |
| INFO | assets/models/props/prp_fishing_hut.glb | nonunit-import-scale | measured import correction is 5.92; do not normalize blindly | IMPROVE |
| INFO | assets/models/props/prp_jetty.glb | nonunit-import-scale | measured import correction is 2.98; do not normalize blindly | IMPROVE |
| INFO | assets/models/props/prp_pebble_a.glb | nonunit-import-scale | measured import correction is 0.3; do not normalize blindly | KEEP |
| INFO | assets/models/props/prp_watch_tower.glb | nonunit-import-scale | measured import correction is 5.48; do not normalize blindly | IMPROVE |
| INFO | assets/models/creatures/boss_iron_king.glb | nonunit-import-scale | measured import correction is 1.368; do not normalize blindly | KEEP |
| INFO | assets/models/props/prp_gate_palisade.glb | nonunit-import-scale | measured import correction is 8.2; do not normalize blindly | IMPROVE |
| INFO | assets/models/props/prp_mine_head.glb | nonunit-import-scale | measured import correction is 5.13; do not normalize blindly | IMPROVE |
| INFO | assets/models/props/prp_ore_seam.glb | nonunit-import-scale | measured import correction is 3.79; do not normalize blindly | IMPROVE |
| INFO | assets/models/props/prp_warden_post.glb | nonunit-import-scale | measured import correction is 3.74; do not normalize blindly | IMPROVE |
| INFO | assets/models/props/prp_flowers_a.glb | nonunit-import-scale | measured import correction is 0.22; do not normalize blindly | KEEP |
| INFO | assets/models/props/prp_flowers_b.glb | nonunit-import-scale | measured import correction is 0.2; do not normalize blindly | KEEP |
| INFO | assets/models/props/prp_grass_wispy.glb | nonunit-import-scale | measured import correction is 0.33; do not normalize blindly | KEEP |
| INFO | assets/models/props/prp_mushrooms.glb | nonunit-import-scale | measured import correction is 0.3; do not normalize blindly | REPLACE |
| INFO | assets/models/props/prp_pebble_b.glb | nonunit-import-scale | measured import correction is 0.31; do not normalize blindly | KEEP |
