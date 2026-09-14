# Journey review harness: renders named world-space viewpoints (approach, reverse, vista, boss
# approach...) through the production RegionStreamer, so a layout is judged the way a player meets
# it rather than cell by cell. Not a regression gate — world_shots.gd owns the baselines.
#
# Run (no --headless; the dummy renderer has no viewport texture):
#   EMBERVALE_VIEWS=<views.json> Godot_..._console.exe --path . --script res://tools/journey_shots.gd
#
# views.json: {"region": "res://data/regions/EmberCrown.tres", "out": "<dir>",
#   "views": [["name", [eye_x, eye_z, eye_clearance], [look_x, look_z, look_clearance]], ...]}
# Both clearances are metres above the terrain at that X/Z.
extends SceneTree

var _camera: Camera3D
var _streamer: Node3D


func _initialize() -> void:
	if DisplayServer.get_name() == "headless":
		printerr("journey shots: run without --headless")
		quit(4)
		return
	var spec: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(OS.get_environment("EMBERVALE_VIEWS")))
	DisplayServer.window_set_size(Vector2i(1600, 900))
	var loader: Node = load("res://src/Bootstrap/ContentDatabaseLoader.cs").new()
	root.add_child(loader)
	var world_env := WorldEnvironment.new()
	var env := Environment.new()
	var sky := Sky.new()
	var sky_mat := ProceduralSkyMaterial.new()
	sky_mat.sky_top_color = Color(0.42, 0.45, 0.52)
	sky_mat.sky_horizon_color = Color(0.72, 0.63, 0.50)
	sky.sky_material = sky_mat
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_energy = 0.65
	env.fog_enabled = true
	env.fog_density = 0.0012
	env.fog_light_color = Color(0.62, 0.62, 0.66)
	world_env.environment = env
	root.add_child(world_env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-38, 38, 0)
	sun.light_energy = 1.35
	sun.light_color = Color(1, 0.9, 0.76)
	sun.shadow_enabled = true
	root.add_child(sun)
	_camera = Camera3D.new()
	_camera.fov = 70
	_camera.far = 4000
	_camera.current = true
	root.add_child(_camera)
	_streamer = load("res://src/World/RegionStreamer.cs").new()
	root.add_child(_streamer)
	_streamer.call("SetPerformanceSamplingEnabled", false)
	_streamer.call("Configure", load(spec.region))

	var out: String = spec.out
	DirAccess.make_dir_recursive_absolute(out)
	var failures := 0
	for view in spec.views:
		var look := await _grounded(view[2])
		var eye := await _grounded(view[1])
		if eye == Vector3.INF or look == Vector3.INF:
			printerr("journey shots: no ground for %s" % view[0])
			failures += 1
			continue
		_streamer.call("SetStreamingFocus", eye)
		for _i in range(90):
			await process_frame
		_camera.global_position = eye
		_camera.look_at(look, Vector3.UP)
		for _i in range(20):
			await process_frame
		await RenderingServer.frame_post_draw
		var path := "%s/%s.png" % [out, view[0]]
		root.get_texture().get_image().save_png(path)
		var scatter := 0
		for mmi in _streamer.find_children("*", "MultiMeshInstance3D", true, false):
			if mmi.is_visible_in_tree() and mmi.multimesh != null and mmi.global_position.distance_to(eye) < 200.0:
				scatter += mmi.multimesh.visible_instance_count if mmi.multimesh.visible_instance_count >= 0 else mmi.multimesh.instance_count
		print("journey shots: %s eye=%s look=%s scatter_within_200m=%d" % [path, eye, look, scatter])
	loader.call("CollectManagedResources")
	quit(0 if failures == 0 else 3)


## Focus the streamer on an X/Z until its collision exists, then return the point lifted by clearance.
func _grounded(p: Array) -> Vector3:
	var at := Vector3(p[0], 0, p[1])
	_streamer.call("SetStreamingFocus", at)
	for _i in range(900):
		if _streamer.call("IsPositionReady", at, false) and _streamer.call("IsSettled"):
			break
		await process_frame
	await physics_frame
	await physics_frame
	var query := PhysicsRayQueryParameters3D.create(Vector3(p[0], 600, p[1]), Vector3(p[0], -300, p[1]))
	query.collide_with_areas = false
	var hit := root.world_3d.direct_space_state.intersect_ray(query)
	return Vector3(p[0], hit.position.y + p[2], p[1]) if hit.has("position") else Vector3.INF
