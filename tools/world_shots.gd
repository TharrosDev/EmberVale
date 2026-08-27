# Full exterior-cell visual QA harness for the world-authoring pipeline.
#
# Run:
#   Godot_..._console.exe --path . --script res://tools/world_shots.gd
#
# It uses RegionStreamer rather than instancing scenes directly, so the same region profile,
# surface skin and silhouette path exercised in play is what reaches the screenshots. Output is
# disposable under tools/shots/world/ and intentionally ignored by Godot/import control.
extends SceneTree

const REGIONS := [
	{
		"path": "res://data/regions/EmberCrown.tres",
		"cells": [
			["ember_crown.town_hub", Vector3(0, 0, -10), Vector2(60, 60)],
			["ember_crown.embermarket", Vector3(0, 0, 46), Vector2(52, 52)],
			["ember_crown.crossway_post", Vector3(0, 0, -66), Vector2(52, 52)],
			["ember_crown.emberdeep_mine", Vector3(56, 0, -10), Vector2(52, 52)],
			["ember_crown.wilds_north", Vector3(0, 0, -117), Vector2(50, 50)],
			["ember_crown.tarn_landing", Vector3(-56, 0, -6), Vector2(52, 52)],
			["ember_crown.hollowreach", Vector3(-52, 0, 46), Vector2(52, 52)],
			["ember_crown.wilds_west", Vector3(-107, 0, -6), Vector2(50, 40)],
			["ember_crown.ashfall_homestead", Vector3(52, 0, 46), Vector2(52, 52)],
			["ember_crown.arena", Vector3(0, 0, -160), Vector2(36, 36)],
		],
	},
	{
		"path": "res://data/regions/FrostfangReach.tres",
		"cells": [
			["frostfang_reach.clan_hold", Vector3(100, 0, -20), Vector2(60, 60)],
			["frostfang_reach.glacier", Vector3(100, 0, -60), Vector2(60, 20)],
			["frostfang_reach.dragon_roost", Vector3(25, 0, -20), Vector2(90, 90)],
			["frostfang_reach.ash_roost", Vector3(180, 0, -20), Vector2(100, 100)],
			["frostfang_reach.ancient_aerie", Vector3(25, 0, -110), Vector2(90, 90)],
		],
	},
]

var _sun: DirectionalLight3D
var _sky: ProceduralSkyMaterial
var _environment: Environment
var _camera: Camera3D


func _initialize() -> void:
	_build_light()
	_camera = Camera3D.new()
	_camera.fov = 70
	_camera.current = true
	root.add_child(_camera)

	var streamer_script: Script = load("res://src/World/RegionStreamer.cs")
	var streamer: Node3D = streamer_script.new()
	root.add_child(streamer)

	for entry in REGIONS:
		var region: Resource = load(entry.path)
		streamer.call("Configure", region)
		for _frame in range(entry.cells.size() + 8):
			await process_frame
		for cell in entry.cells:
			await _render_cell(cell)
		streamer.call("UnloadAll")
		streamer.call("Configure", null)
		await process_frame
		await process_frame

	print("world shots: complete")
	quit(0)


func _render_cell(cell: Array) -> void:
	var cell_id: String = cell[0]
	var centre: Vector3 = cell[1]
	var size: Vector2 = cell[2]
	var radius: float = max(size.x, size.y) * 0.5
	var shots := [
		["01_entry", centre + Vector3(0, 1.75, radius * 0.78), centre + Vector3(0, 1.4, 0)],
		["02_centre", centre + Vector3(0, 1.75, radius * 0.18), centre + Vector3(0, 1.7, -radius * 0.42)],
		["03_landmark", centre + Vector3(-radius * 0.42, 2.2, radius * 0.28), centre + Vector3(0, 2.5, 0)],
		["04_exit", centre + Vector3(0, 1.75, -radius * 0.70), centre + Vector3(0, 1.5, 0)],
		["05_overview", centre + Vector3(-radius * 0.75, radius * 0.72, radius * 0.80), centre],
	]
	var folder := "res://tools/shots/world/%s" % cell_id.replace(".", "_")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(folder))

	for pass_name in ["day", "dusk"]:
		_set_pass(pass_name)
		for shot in shots:
			_camera.global_position = shot[1]
			_camera.look_at(shot[2], Vector3.UP)
			for _frame in range(5):
				await process_frame
			var path := "%s/%s_%s.png" % [folder, pass_name, shot[0]]
			var error := root.get_texture().get_image().save_png(path)
			print("%s -> %s" % [path, "ok" if error == OK else str(error)])


func _build_light() -> void:
	var world_env := WorldEnvironment.new()
	_environment = Environment.new()
	var sky := Sky.new()
	_sky = ProceduralSkyMaterial.new()
	sky.sky_material = _sky
	_environment.background_mode = Environment.BG_SKY
	_environment.sky = sky
	_environment.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	_environment.glow_enabled = true
	_environment.fog_enabled = true
	world_env.environment = _environment
	root.add_child(world_env)

	_sun = DirectionalLight3D.new()
	_sun.rotation_degrees = Vector3(-42, 38, 0)
	root.add_child(_sun)


func _set_pass(pass_name: String) -> void:
	if pass_name == "day":
		_sun.light_energy = 1.35
		_sun.light_color = Color(1, 0.9, 0.76)
		_sky.sky_top_color = Color(0.42, 0.45, 0.52)
		_sky.sky_horizon_color = Color(0.72, 0.63, 0.50)
		_environment.ambient_light_energy = 0.65
		_environment.fog_density = 0.004
	else:
		_sun.light_energy = 0.16
		_sun.light_color = Color(0.48, 0.52, 0.78)
		_sky.sky_top_color = Color(0.05, 0.06, 0.11)
		_sky.sky_horizon_color = Color(0.22, 0.14, 0.13)
		_environment.ambient_light_energy = 0.16
		_environment.fog_density = 0.008
