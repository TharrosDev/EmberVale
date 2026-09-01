# Collision analysis

Collision and render geometry are intentionally assessed separately. A direct-usage collision count is a repository heuristic; inspect the listed usage files before changing shared resources.

| Asset | Direct uses | Usage files with collision | Imported collision nodes | Flags |
| --- | --- | --- | --- | --- |
| assets/models/animations/anim_library.glb | 1 | 0 | 0 |  |
| assets/models/architecture/bld_blacksmith.glb | 1 | 1 | 0 |  |
| assets/models/architecture/bld_cottage.glb | 1 | 1 | 0 |  |
| assets/models/architecture/bld_house_a.glb | 3 | 3 | 0 |  |
| assets/models/architecture/bld_house_b.glb | 5 | 5 | 0 |  |
| assets/models/architecture/bld_inn.glb | 1 | 1 | 0 |  |
| assets/models/architecture/mod_chimney.gltf | 7 | 7 | 0 |  |
| assets/models/architecture/mod_corner.gltf | 7 | 7 | 0 |  |
| assets/models/architecture/mod_door.gltf | 7 | 7 | 0 |  |
| assets/models/architecture/mod_floor_wood.gltf | 1 | 1 | 0 |  |
| assets/models/architecture/mod_gable_4.gltf | 4 | 4 | 0 |  |
| assets/models/architecture/mod_gable_6.gltf | 3 | 3 | 0 |  |
| assets/models/architecture/mod_roof_4x4.gltf | 2 | 2 | 0 |  |
| assets/models/architecture/mod_roof_4x6.gltf | 2 | 2 | 0 |  |
| assets/models/architecture/mod_roof_6x8.gltf | 3 | 3 | 0 |  |
| assets/models/architecture/mod_wall_base.gltf | 7 | 7 | 0 |  |
| assets/models/architecture/mod_wall_door.gltf | 7 | 7 | 0 |  |
| assets/models/architecture/mod_wall_plain.gltf | 7 | 7 | 0 |  |
| assets/models/architecture/mod_wall_timber.gltf | 7 | 7 | 0 |  |
| assets/models/architecture/mod_wall_window.gltf | 7 | 7 | 0 |  |
| assets/models/architecture/mod_wall_window_thin.gltf | 7 | 7 | 0 |  |
| assets/models/architecture/mod_window_thin.gltf | 7 | 7 | 0 |  |
| assets/models/architecture/mod_window_wide.gltf | 7 | 7 | 0 |  |
| assets/models/characters/chr_player_base.glb | 4 | 2 | 0 |  |
| assets/models/characters/fp_arm.glb | 0 | 0 | 0 |  |
| assets/models/characters/fp_arm_left.glb | 2 | 0 | 0 |  |
| assets/models/characters/fp_arm_right.glb | 2 | 0 | 0 |  |
| assets/models/characters/npc_adventurer_f.glb | 4 | 3 | 0 |  |
| assets/models/characters/npc_guild_rep.glb | 5 | 5 | 0 |  |
| assets/models/characters/npc_hooded.glb | 5 | 5 | 0 |  |
| assets/models/characters/npc_innkeeper.glb | 3 | 3 | 0 |  |
| assets/models/characters/npc_kael.glb | 2 | 1 | 0 |  |
| assets/models/characters/npc_merchant_f.glb | 0 | 0 | 0 |  |
| assets/models/characters/npc_merchant_m.glb | 2 | 2 | 0 |  |
| assets/models/characters/npc_townsman.glb | 5 | 5 | 0 |  |
| assets/models/characters/npc_townswoman.glb | 3 | 3 | 0 |  |
| assets/models/characters/npc_vendor.glb | 5 | 5 | 0 |  |
| assets/models/characters/npc_woman_dress.glb | 3 | 2 | 0 |  |
| assets/models/creatures/boss_iron_king.glb | 1 | 0 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_ancient_dragon.glb | 1 | 0 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_arcane_echo.glb | 1 | 0 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_ash_dragon.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_ash_maw.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_ashen_acolyte.glb | 2 | 1 | 0 |  |
| assets/models/creatures/enm_ashfall_elk.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_bandit.glb | 1 | 0 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_barrow_wight.glb | 1 | 0 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_bone_knight.glb | 1 | 0 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_cinder_thrall.glb | 1 | 0 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_cinder_wisp.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_clan_beast_tamer.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_clan_raider.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_clan_shaman.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_dire_wolf.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_frost_drake.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_frost_stalker.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_goblin.glb | 1 | 1 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_grave_shade.glb | 1 | 0 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_hollow_husk.glb | 1 | 0 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_hollow_necromancer.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_rime_shard.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_ruin_crawler.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_soldier.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_stone_sentinel.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_storm_mote.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_syndicate_enforcer.glb | 1 | 0 | 0 | collision-render-mismatch |
| assets/models/creatures/enm_thornback_boar.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_ward_golem.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_wild_dragon.glb | 1 | 0 | 0 |  |
| assets/models/creatures/enm_wolf.glb | 1 | 0 | 0 |  |
| assets/models/creatures/mnt_horse.glb | 2 | 0 | 0 |  |
| assets/models/equipment/eqp_pauldron_embervale.glb | 2 | 1 | 0 |  |
| assets/models/equipment/eqp_pouch_embervale.glb | 2 | 1 | 0 |  |
| assets/models/props/prp_anvil.gltf | 0 | 0 | 0 |  |
| assets/models/props/prp_anvil_log.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_arena_wall.glb | 1 | 1 | 0 |  |
| assets/models/props/prp_banner_guild.glb | 4 | 4 | 0 |  |
| assets/models/props/prp_barrel.glb | 5 | 5 | 0 |  |
| assets/models/props/prp_bed.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_bell_tower.glb | 1 | 1 | 0 |  |
| assets/models/props/prp_bench.glb | 5 | 5 | 0 |  |
| assets/models/props/prp_book_stand.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_bookcase.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_boulder.glb | 3 | 3 | 0 |  |
| assets/models/props/prp_brazier.glb | 7 | 7 | 0 |  |
| assets/models/props/prp_bush_flowering.glb | 4 | 2 | 0 |  |
| assets/models/props/prp_cabinet.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_cache_chest.glb | 3 | 2 | 0 |  |
| assets/models/props/prp_cache_chest_open.glb | 1 | 0 | 0 |  |
| assets/models/props/prp_campfire.glb | 5 | 5 | 0 |  |
| assets/models/props/prp_candles.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_cart.glb | 5 | 5 | 0 |  |
| assets/models/props/prp_cauldron.glb | 2 | 2 | 0 |  |
| assets/models/props/prp_chair.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_chandelier.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_chest_wood.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_clover.glb | 2 | 0 | 0 |  |
| assets/models/props/prp_crate.glb | 6 | 6 | 0 |  |
| assets/models/props/prp_crate_wood.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_dock_complex.glb | 2 | 2 | 0 |  |
| assets/models/props/prp_fence.glb | 6 | 6 | 0 |  |
| assets/models/props/prp_fern.glb | 2 | 0 | 0 |  |
| assets/models/props/prp_fishing_hut.glb | 2 | 2 | 0 |  |
| assets/models/props/prp_flowers_a.glb | 0 | 0 | 0 |  |
| assets/models/props/prp_flowers_b.glb | 0 | 0 | 0 |  |
| assets/models/props/prp_gate_palisade.glb | 1 | 1 | 0 |  |
| assets/models/props/prp_gazebo.glb | 2 | 2 | 0 |  |
| assets/models/props/prp_glacier.glb | 3 | 3 | 0 |  |
| assets/models/props/prp_grass_short.glb | 5 | 0 | 0 |  |
| assets/models/props/prp_grass_tall.glb | 3 | 0 | 0 |  |
| assets/models/props/prp_grass_wispy.glb | 0 | 0 | 0 |  |
| assets/models/props/prp_hay.glb | 4 | 4 | 0 |  |
| assets/models/props/prp_jetty.glb | 2 | 2 | 0 |  |
| assets/models/props/prp_lamp_post.glb | 4 | 4 | 0 |  |
| assets/models/props/prp_lantern_wall.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_market_stand.glb | 1 | 1 | 0 |  |
| assets/models/props/prp_market_stand_b.glb | 3 | 3 | 0 |  |
| assets/models/props/prp_mine_head.glb | 1 | 1 | 0 |  |
| assets/models/props/prp_mushrooms.glb | 0 | 0 | 0 |  |
| assets/models/props/prp_nightstand.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_ore_seam.glb | 1 | 1 | 0 |  |
| assets/models/props/prp_pebble_a.glb | 2 | 0 | 0 |  |
| assets/models/props/prp_pebble_b.glb | 0 | 0 | 0 |  |
| assets/models/props/prp_pine_dead.glb | 15 | 11 | 0 |  |
| assets/models/props/prp_pot.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_relic.glb | 1 | 1 | 0 |  |
| assets/models/props/prp_rock_cluster.glb | 17 | 13 | 0 |  |
| assets/models/props/prp_rockpath_small.glb | 1 | 1 | 0 |  |
| assets/models/props/prp_rockpath_wide.glb | 2 | 2 | 0 |  |
| assets/models/props/prp_rope.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_ruin_pillar.glb | 6 | 6 | 0 |  |
| assets/models/props/prp_ruin_wall.glb | 6 | 6 | 0 |  |
| assets/models/props/prp_sacks.glb | 5 | 5 | 0 |  |
| assets/models/props/prp_shelf.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_shelf_arch.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_stall_cart.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_stall_empty.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_station_alchemy.glb | 3 | 3 | 0 |  |
| assets/models/props/prp_station_forge.glb | 3 | 3 | 0 |  |
| assets/models/props/prp_station_workbench.glb | 3 | 3 | 0 |  |
| assets/models/props/prp_stool.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_table_large.gltf | 4 | 4 | 0 |  |
| assets/models/props/prp_tent.glb | 3 | 3 | 0 |  |
| assets/models/props/prp_timber_stack.glb | 3 | 3 | 0 |  |
| assets/models/props/prp_tome_stand.glb | 4 | 4 | 0 |  |
| assets/models/props/prp_training_dummy.glb | 1 | 1 | 0 |  |
| assets/models/props/prp_tree_broadleaf.glb | 5 | 2 | 0 |  |
| assets/models/props/prp_warden_post.glb | 1 | 1 | 0 |  |
| assets/models/props/prp_watch_tower.glb | 2 | 2 | 0 |  |
| assets/models/props/prp_waystone.glb | 10 | 9 | 0 |  |
| assets/models/props/prp_weapon_rack.gltf | 1 | 1 | 0 |  |
| assets/models/props/prp_weapon_stand.gltf | 0 | 0 | 0 |  |
| assets/models/props/prp_well.glb | 3 | 3 | 0 |  |
| assets/models/props/prp_workbench.gltf | 1 | 1 | 0 |  |
| assets/models/weapons/wpn_sword_iron.glb | 2 | 1 | 0 |  |
