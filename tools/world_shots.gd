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

# ⚠️ THE CELL TABLE IS READ OUT OF THE REGION RESOURCE, NOT COPIED HERE (the 2026-08-29 geography
# overhaul). It used to be sixteen hand-written centres and envelopes in this file, which is the
# second copy of a number NOW.md invariant 12 spends a paragraph forbidding — and the copy that
# would have silently framed every shot at the OLD lattice while the baseline "passed".
const REGION_PATHS := [
	"res://data/regions/EmberCrown.tres",
	"res://data/regions/FrostfangReach.tres",
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

	for region_path in REGION_PATHS:
		var region: Resource = load(region_path)
		streamer.call("Configure", region)
		var settle_frames := 0
		while not streamer.call("IsSettled") and settle_frames < 600:
			await process_frame
			settle_frames += 1
		if not streamer.call("IsSettled"):
			printerr("world shots: region failed to settle: %s" % region_path)
			quit(2)
			return
		for authored_cell in region.get("Cells"):
			if authored_cell == null or authored_cell.get("Presentation") == null:
				continue
			var presentation: Resource = authored_cell.get("Presentation")
			await _render_cell([
				String(authored_cell.get("Id")),
				authored_cell.get("Center"),
				Vector2(presentation.get("Width"), presentation.get("Depth")),
			], region)
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


## The ground under a world X/Z, from the real terrain collider the streamer just built. Every
## camera in this harness used to assume y = 0 and would now be underground on half the realm.
func _ground_at(x: float, z: float) -> float:
	var space := root.world_3d.direct_space_state
	var query := PhysicsRayQueryParameters3D.create(Vector3(x, 400.0, z), Vector3(x, -200.0, z))
	query.collide_with_areas = false
	var hit := space.intersect_ray(query)
	return hit.position.y if hit.has("position") else 0.0


func _on_ground(point: Vector3, clearance: float) -> Vector3:
	return Vector3(point.x, _ground_at(point.x, point.z) + clearance, point.z)


func _render_cell(cell: Array, region: Resource) -> void:
	var cell_id: String = cell[0]
	var centre: Vector3 = cell[1]
	var size: Vector2 = cell[2]
	var radius: float = max(size.x, size.y) * 0.5
	var route_views := _route_views(cell_id, centre, size, region)
	var landmark_view := _landmark_view(centre, radius)
	var overview := centre + Vector3(-radius * 0.75, 0.0, radius * 0.80)
	overview.y = _ground_at(overview.x, overview.z) + radius * 0.72
	var shots := [
		["01_entry", route_views[0], route_views[1]],
		["02_centre", route_views[2], route_views[3]],
		["03_landmark", landmark_view, _on_ground(centre, 2.5)],
		["04_exit", route_views[4], route_views[5]],
		["05_overview", overview, _on_ground(centre, 1.0)],
	]
	var folder := "res://tools/shots/world/%s" % cell_id.replace(".", "_")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(folder))

	for pass_name in ["day", "dusk"]:
		_set_pass(pass_name, region.get("EnvironmentProfile"))
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
	var entry_at := _on_ground(centre + Vector3(entry[0].x + entry[1].x * 2.0, 0.0, entry[0].y + entry[1].y * 2.0), 1.75)
	var exit_at := _on_ground(centre + Vector3(exit[0].x + exit[1].x * 2.0, 0.0, exit[0].y + exit[1].y * 2.0), 1.75)
	var entry_look := _on_ground(entry_at + Vector3(entry[1].x * 12.0, 0.0, entry[1].y * 12.0), 1.6)
	var exit_look := _on_ground(exit_at + Vector3(exit[1].x * 12.0, 0.0, exit[1].y * 12.0), 1.6)
	var middle := _on_ground((entry_at + exit_at) * 0.5, 1.75)
	var middle_look := _on_ground(middle + Vector3(entry[1].x * 10.0, 0.0, entry[1].y * 10.0), 1.7)
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
		var candidate := _on_ground(centre + Vector3(offset.x * radius, 0.0, offset.y * radius), 1.75)
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


## Apply one lighting pass, COLOURED BY THE REGION.
##
## ⚠️ THE HARNESS USED TO LIGHT EVERY REGION THE SAME WAY, AND THAT MADE IT LIE ABOUT THE ONE THING
## it exists for. Two fixed passes with a warm sun and an ash haze meant Frostfang Reach was
## reviewed and signed off under the Ember Crown's golden hour: its cold bedrock rendered as warm
## tan sand, its thick air was not in the frame at all, and no amount of palette work in the region
## spec could show up here. A screenshot gate blind to a region's own atmosphere approves the
## wrong world.
func _set_pass(pass_name: String, profile: Resource = null) -> void:
	if pass_name == "day":
		_sun.light_energy = 1.35
		_sun.light_color = Color(1, 0.9, 0.76)
		_sky.sky_top_color = Color(0.42, 0.45, 0.52)
		_sky.sky_horizon_color = Color(0.72, 0.63, 0.50)
		_environment.ambient_light_energy = 0.65
		_environment.fog_density = 0.004
		# ⚠️ Reset per pass. The region blend below is a lerp, so without a fresh base it
		# compounds every time _set_pass runs and the realm drifts to the haze colour.
		_environment.fog_light_color = Color(0.52, 0.55, 0.61)
	else:
		_sun.light_energy = 0.16
		_sun.light_color = Color(0.48, 0.52, 0.78)
		_sky.sky_top_color = Color(0.05, 0.06, 0.11)
		_sky.sky_horizon_color = Color(0.22, 0.14, 0.13)
		_environment.ambient_light_energy = 0.16
		_environment.fog_density = 0.008
		_environment.fog_light_color = Color(0.20, 0.21, 0.28)

	if profile == null:
		return
	var tint: Color = profile.get("SunTint")
	_sun.light_color = Color(
		_sun.light_color.r * tint.r, _sun.light_color.g * tint.g, _sun.light_color.b * tint.b)
	_sun.light_energy *= float(profile.get("SunEnergyScale"))
	var haze: Color = profile.get("HazeColor")
	# Blend toward the region's haze rather than replacing the pass colour, exactly as
	# SkyController does. Replacing it outright put the Ember Crown's warm ash at full
	# strength on every frame and turned the whole realm sepia.
	_environment.fog_light_color = _environment.fog_light_color.lerp(haze, 0.5)
	_environment.fog_density *= float(profile.get("HazeScale"))
	# The sky is part of a region's air too: a cold realm under a warm horizon reads as a repaint.
	_sky.sky_horizon_color = _sky.sky_horizon_color.lerp(haze, 0.4)
