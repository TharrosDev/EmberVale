# Full exterior-cell visual QA harness for the world-authoring pipeline.
#
# Run:
#   Godot_..._console.exe --path . --script res://tools/world_shots.gd
#
# Do not add --headless: Windows' dummy renderer has no viewport texture to capture. The harness
# fails explicitly in that mode instead of emitting 150 null-texture errors.
#
# It uses RegionStreamer rather than instancing scenes directly, so the same region profile,
# surface skin and silhouette path exercised in play is what reaches the screenshots. Output is
# disposable under tools/shots/world/ and intentionally ignored by Godot/import control.
extends SceneTree

const BASELINE_PATH := "res://tests/visual_baselines/world_signatures.json"
const SIGNATURE_WIDTH := 12
const SIGNATURE_HEIGHT := 8
const DEFAULT_MEAN_CHANNEL_DELTA := 18.0

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
var _content_loader: Node
var _signatures: Dictionary = {}


func _initialize() -> void:
	if DisplayServer.get_name() == "headless":
		printerr("world shots: a rendering-capable display is required; run without --headless")
		quit(4)
		return

	# Isolated tools do not construct GameBootstrap. Use the same centralized content initializer
	# before the production RegionStreamer so lairs and other registry-backed actors preview honestly.
	var content_loader_script: Script = load("res://src/Bootstrap/ContentDatabaseLoader.cs")
	_content_loader = content_loader_script.new()
	root.add_child(_content_loader)

	_build_light()
	_camera = Camera3D.new()
	_camera.fov = 70
	_camera.current = true
	root.add_child(_camera)

	var streamer_script: Script = load("res://src/World/RegionStreamer.cs")
	var streamer: Node3D = streamer_script.new()
	root.add_child(streamer)
	# Synchronous PNG writes are deliberately frame-blocking and are not performance samples.
	streamer.call("SetPerformanceSamplingEnabled", false)

	for entry in REGIONS:
		var region: Resource = load(entry.path)
		streamer.call("Configure", region)
		var settle_frames := 0
		while not streamer.call("IsSettled") and settle_frames < 600:
			await process_frame
			settle_frames += 1
		if not streamer.call("IsSettled"):
			printerr("world shots: region failed to settle: %s" % entry.path)
			quit(2)
			return
		for cell in entry.cells:
			await _render_cell(cell, region)
		streamer.call("UnloadAll")
		streamer.call("Configure", null)
		await process_frame
		await process_frame
		region = null

	var regression_ok := _finish_visual_regression()
	_content_loader.call("CollectManagedResources")
	await process_frame
	print("world shots: complete" if regression_ok else "world shots: visual regression failed")
	quit(0 if regression_ok else 3)


func _render_cell(cell: Array, region: Resource) -> void:
	var cell_id: String = cell[0]
	var centre: Vector3 = cell[1]
	var size: Vector2 = cell[2]
	var radius: float = max(size.x, size.y) * 0.5
	var route_views := _route_views(cell_id, centre, size, region)
	var landmark_view := _landmark_view(centre, radius)
	var shots := [
		["01_entry", route_views[0], route_views[1]],
		["02_centre", route_views[2], route_views[3]],
		["03_landmark", landmark_view, centre + Vector3(0, 2.5, 0)],
		["04_exit", route_views[4], route_views[5]],
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
			var image := root.get_texture().get_image()
			var error := image.save_png(path)
			var key := "%s/%s_%s" % [cell_id, pass_name, shot[0]]
			_signatures[key] = _image_signature(image)
			print("%s -> %s" % [path, "ok" if error == OK else str(error)])


## First-person shots follow the authored route network, not a generic north/south axis. The old
## fixed cameras looked into walls in bent layouts and could completely miss a real seam opening.
func _route_views(cell_id: String, centre: Vector3, size: Vector2, region: Resource) -> Array:
	var endpoints: Array = []
	var first_route: Resource = null
	for authored_cell in region.get("Cells"):
		if String(authored_cell.get("Id")) != cell_id:
			continue
		var presentation: Resource = authored_cell.get("Presentation")
		if presentation == null:
			break
		for route in presentation.get("Paths"):
			if route == null:
				continue
			if first_route == null:
				first_route = route
			for pair in [[route.get("Start"), route.get("End")], [route.get("End"), route.get("Start")]]:
				var local: Vector2 = pair[0]
				var inward: Vector2 = (pair[1] - pair[0]).normalized()
				var edge_distance := minf(
					absf(absf(local.x) - size.x * 0.5),
					absf(absf(local.y) - size.y * 0.5))
				if edge_distance <= 1.0:
					endpoints.append([local, inward])
		break

	if endpoints.is_empty() and first_route != null:
		var start: Vector2 = first_route.get("Start")
		var finish: Vector2 = first_route.get("End")
		endpoints.append([start, (finish - start).normalized()])
		endpoints.append([finish, (start - finish).normalized()])
	if endpoints.is_empty():
		endpoints.append([Vector2(0, size.y * 0.4), Vector2(0, -1)])
		endpoints.append([Vector2(0, -size.y * 0.4), Vector2(0, 1)])

	var entry: Array = endpoints[0]
	var exit: Array = endpoints[endpoints.size() - 1]
	var entry_at := centre + Vector3(entry[0].x + entry[1].x * 2.0, 1.75, entry[0].y + entry[1].y * 2.0)
	var entry_look := entry_at + Vector3(entry[1].x * 12.0, -0.15, entry[1].y * 12.0)
	var exit_at := centre + Vector3(exit[0].x + exit[1].x * 2.0, 1.75, exit[0].y + exit[1].y * 2.0)
	var exit_look := exit_at + Vector3(exit[1].x * 12.0, -0.15, exit[1].y * 12.0)
	var middle := (entry_at + exit_at) * 0.5
	middle.y = 1.75
	var middle_look := middle + Vector3(entry[1].x * 10.0, -0.05, entry[1].y * 10.0)
	return [entry_at, entry_look, middle, middle_look, exit_at, exit_look]


## Pick a first-person landmark camera that is not embedded in authored collision. A fixed diagonal
## landed inside the Clan Hold longhouse and produced a full-frame wall that could never review the
## landmark. The candidates remain deterministic so visual signatures stay stable.
func _landmark_view(centre: Vector3, radius: float) -> Vector3:
	var candidates := [
		Vector2(-0.42, 0.28), Vector2(0.42, 0.28),
		Vector2(0.42, -0.28), Vector2(-0.42, -0.28),
		Vector2(0.0, 0.46), Vector2(0.0, -0.46),
	]
	var sphere := SphereShape3D.new()
	sphere.radius = 0.75
	for offset in candidates:
		var candidate := centre + Vector3(offset.x * radius, 1.75, offset.y * radius)
		var query := PhysicsShapeQueryParameters3D.new()
		query.shape = sphere
		query.transform = Transform3D(Basis.IDENTITY, candidate)
		query.collide_with_areas = false
		query.collide_with_bodies = true
		if root.world_3d.direct_space_state.intersect_shape(query, 1).is_empty():
			return candidate
	return centre + Vector3(0, 6.0, radius * 0.35)


func _image_signature(image: Image) -> Array:
	var thumbnail := image.duplicate()
	thumbnail.resize(SIGNATURE_WIDTH, SIGNATURE_HEIGHT, Image.INTERPOLATE_LANCZOS)
	var values: Array = []
	for y in range(SIGNATURE_HEIGHT):
		for x in range(SIGNATURE_WIDTH):
			var colour: Color = thumbnail.get_pixel(x, y)
			values.append(roundi(colour.r * 255.0))
			values.append(roundi(colour.g * 255.0))
			values.append(roundi(colour.b * 255.0))
	return values


func _finish_visual_regression() -> bool:
	if OS.get_cmdline_user_args().has("--update-world-baseline"):
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://tests/visual_baselines"))
		var file := FileAccess.open(BASELINE_PATH, FileAccess.WRITE)
		if file == null:
			printerr("world shots: could not write baseline: %s" % FileAccess.get_open_error())
			return false
		file.store_string(JSON.stringify({
			"version": 1,
			"signature_width": SIGNATURE_WIDTH,
			"signature_height": SIGNATURE_HEIGHT,
			"mean_channel_delta": DEFAULT_MEAN_CHANNEL_DELTA,
			"signatures": _signatures,
		}, "  "))
		print("world shots: updated visual baseline (%d frames)" % _signatures.size())
		return true

	if not FileAccess.file_exists(BASELINE_PATH):
		printerr("world shots: missing baseline %s (run with -- --update-world-baseline)" % BASELINE_PATH)
		return false
	var baseline_file := FileAccess.open(BASELINE_PATH, FileAccess.READ)
	var baseline = JSON.parse_string(baseline_file.get_as_text())
	if not baseline is Dictionary or not baseline.has("signatures"):
		printerr("world shots: malformed visual baseline")
		return false

	var expected: Dictionary = baseline.signatures
	var threshold: float = float(baseline.get("mean_channel_delta", DEFAULT_MEAN_CHANNEL_DELTA))
	var failures := 0
	for key in _signatures:
		if not expected.has(key):
			printerr("world shots: baseline missing frame %s" % key)
			failures += 1
			continue
		var actual_values: Array = _signatures[key]
		var expected_values: Array = expected[key]
		if actual_values.size() != expected_values.size():
			printerr("world shots: signature size changed for %s" % key)
			failures += 1
			continue
		var delta := 0.0
		for index in range(actual_values.size()):
			delta += absf(float(actual_values[index]) - float(expected_values[index]))
		delta /= actual_values.size()
		if delta > threshold:
			printerr("world shots: %s mean channel delta %.2f > %.2f" % [key, delta, threshold])
			failures += 1
	for key in expected:
		if not _signatures.has(key):
			printerr("world shots: capture missing baseline frame %s" % key)
			failures += 1
	print("world shots: visual regression %s (%d frames, threshold %.2f)" % [
		"PASS" if failures == 0 else "FAIL", _signatures.size(), threshold])
	return failures == 0


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
