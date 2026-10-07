extends SceneTree

## One asset on a flat lit stage, seen the way the player meets it: standing eye height, at
## 3, 10, 30 and 100 m, from the front and from the back, with a 1.8 m human reference in frame.
##
## Modelled on tools/environment_shots.gd (same stage, light and capsule). That harness orbits a
## prop at a distance fitted to its size, which answers "is the model sound". This one answers the
## two questions a 12-36 m generated landmark raises and an orbit cannot: does the surface hold up
## with the camera 3 m from it (one atlas over a giant is 20-40 texels per metre, which is what
## landmark_detail.gdshader is for), and does it read as BIG from where the player first sees it.
## The camera uses the game's default 75 degree field of view, not a flattering long lens.
##
## Subjects are the user arguments: res:// or project-relative paths of .glb or .tscn files. Give a
## scenes/props/lm_*.tscn wrapper to judge the detail material and tint, the bare .glb to see the
## imported model without them. `--scale N` renders every subject at a uniform scale.
##
##   godot --path . --resolution 1280x720 --script res://tools/asset_stage_shots.gd -- \
##       --output artifacts/meshy-overhaul/stage \
##       res://scenes/props/lm_monolith_a.tscn res://assets/models/world/prp_lm_monolith_a.glb
##
## Frames land at <output>/<file stem>--<front|back>-<distance>m.png beside a summary.json with the
## measured bounds. Needs a rendering display (not --headless) and an imported project.

const DISTANCES: Array[float] = [3.0, 10.0, 30.0, 100.0]
const SIDES := {"front": 1.0, "back": -1.0}
const EYE_HEIGHT := 1.7

var output := "artifacts/meshy-overhaul/stage"
var subject_scale := 1.0
var subjects: Array[String] = []
var stage: Node3D
var camera: Camera3D
var reference: Node3D
var failures: Array[String] = []
var summary: Array[Dictionary] = []


func _initialize() -> void:
	if DisplayServer.get_name() == "headless":
		printerr("ERROR: asset_stage_shots requires a rendering display")
		quit(2)
		return
	if OS.has_environment("EMBERVALE_ARTIFACTS"):
		output = OS.get_environment("EMBERVALE_ARTIFACTS").path_join("asset_stage_shots")
	seed(int(OS.get_environment("EMBERVALE_SEED")) if OS.has_environment("EMBERVALE_SEED") else 12345)
	var args := OS.get_cmdline_user_args()
	var i := 0
	while i < args.size():
		if args[i] == "--output" and i + 1 < args.size():
			output = args[i + 1]
			i += 2
		elif args[i] == "--scale" and i + 1 < args.size():
			subject_scale = float(args[i + 1])
			i += 2
		else:
			subjects.append(args[i] if args[i].begins_with("res://") else "res://" + args[i].replace("\\", "/"))
			i += 1
	if subjects.is_empty():
		printerr("ERROR: asset_stage_shots: name at least one .glb or .tscn after --")
		quit(2)
		return
	DirAccess.make_dir_recursive_absolute(_output_path(""))
	call_deferred("_run")


func _output_path(file: String) -> String:
	var folder := output if output.is_absolute_path() else "res://" + output
	return ProjectSettings.globalize_path(folder.path_join(file) if file != "" else folder)


func _run() -> void:
	_build_stage()
	await process_frame
	await process_frame
	for path: String in subjects:
		await _render_subject(path)
	_write_summary()
	if failures.is_empty():
		print("asset stage shots: PASS (%d frames)" % [summary.size() * DISTANCES.size() * SIDES.size()])
		quit(0)
	else:
		for failure in failures:
			push_error(failure)
		quit(1)


func _build_stage() -> void:
	stage = Node3D.new()
	root.add_child(stage)
	var world_environment := WorldEnvironment.new()
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color("25282d")
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color("8995a6")
	environment.ambient_light_energy = 0.72
	environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	world_environment.environment = environment
	stage.add_child(world_environment)
	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-48, -34, 0)
	key.light_color = Color("ffd3a0")
	key.light_energy = 1.65
	key.shadow_enabled = true
	# The default 100 m shadow distance would leave a giant unshadowed in the 100 m frame.
	key.directional_shadow_max_distance = 260.0
	stage.add_child(key)
	var fill := DirectionalLight3D.new()
	fill.rotation_degrees = Vector3(-24, 142, 0)
	fill.light_color = Color("98b2d0")
	fill.light_energy = 0.68
	stage.add_child(fill)
	var floor_mesh := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(800, 800)
	floor_mesh.mesh = plane
	var floor_material := StandardMaterial3D.new()
	floor_material.albedo_color = Color("57534b")
	floor_material.roughness = 0.94
	floor_mesh.material_override = floor_material
	stage.add_child(floor_mesh)
	reference = _build_reference()
	stage.add_child(reference)
	camera = Camera3D.new()
	camera.current = true
	camera.fov = 75
	camera.near = 0.05
	camera.far = 600.0
	stage.add_child(camera)


func _build_reference() -> Node3D:
	# The production player capsule: 1.8 m tall, 0.4 m radius, origin at the feet.
	var holder := Node3D.new()
	holder.name = "HumanReference"
	var mesh_instance := MeshInstance3D.new()
	var capsule := CapsuleMesh.new()
	capsule.radius = 0.4
	capsule.height = 1.8
	mesh_instance.mesh = capsule
	mesh_instance.position = Vector3(0, 0.9, 0)
	var material := StandardMaterial3D.new()
	material.albedo_color = Color("c8543f")
	material.roughness = 0.9
	mesh_instance.material_override = material
	holder.add_child(mesh_instance)
	return holder


func _render_subject(path: String) -> void:
	var packed := load(path) as PackedScene
	if packed == null:
		failures.append(path + ": could not load")
		return
	var subject := packed.instantiate() as Node3D
	if subject == null:
		failures.append(path + ": root is not Node3D")
		return
	subject.name = "StageSubject"
	subject.scale = Vector3.ONE * subject_scale
	stage.add_child(subject)
	# Two frames: the wrapper's script swaps materials in _ready and the shader compiles on first draw.
	await process_frame
	await process_frame

	var bounds := _visual_bounds(subject)
	if bounds.size.length_squared() <= 0.001:
		failures.append(path + ": no rendered bounds")
		subject.queue_free()
		await process_frame
		return

	var slug := path.get_file().get_basename()
	if path.get_extension() != "tscn":
		slug += "-" + path.get_extension()
	var centre := bounds.get_center()
	for side: String in SIDES:
		var facing: float = SIDES[side]
		# The face the camera looks at: the bounds' +Z side from the front, -Z from the back.
		var face_z: float = centre.z + facing * bounds.size.z * 0.5
		for distance: float in DISTANCES:
			var eye := Vector3(centre.x, EYE_HEIGHT, face_z + facing * distance)
			# Look where a player looks: level when close, tilting up to take in a tall piece, never
			# more than about 20 degrees so the ground and the reference stay in frame.
			var aim_height: float = clampf(centre.y, EYE_HEIGHT, EYE_HEIGHT + distance * 0.36)
			camera.global_position = eye
			camera.look_at(Vector3(centre.x, aim_height, face_z), Vector3.UP)
			# The reference stands just clear of the face, to the camera's right, and close enough
			# to the axis to be in frame at 3 m however wide the subject is.
			var aside: float = minf(bounds.size.x * 0.5 + 1.2, distance * 0.45)
			reference.position = Vector3(centre.x + facing * aside, 0, face_z + facing * 0.5)
			await _capture("%s--%s-%03dm" % [slug, side, int(distance)])

	summary.append({
		"path": path,
		"slug": slug,
		"scale": subject_scale,
		"bounds": [
			snappedf(bounds.size.x, 0.01), snappedf(bounds.size.y, 0.01), snappedf(bounds.size.z, 0.01)],
		"base_y": snappedf(bounds.position.y, 0.001),
		"grounded": absf(bounds.position.y) < 0.02,
		"surfaces": _material_count(subject),
		"detail_material": _uses_shader(subject),
		"colliders": subject.find_children("*", "CollisionShape3D", true, false).size(),
		"frames": DISTANCES.size() * SIDES.size(),
	})
	subject.queue_free()
	await process_frame


func _material_count(node: Node3D) -> int:
	var total := 0
	for child in node.find_children("*", "MeshInstance3D", true, false):
		var mesh_instance := child as MeshInstance3D
		if mesh_instance.mesh != null:
			total += mesh_instance.mesh.get_surface_count()
	return total


func _uses_shader(node: Node3D) -> bool:
	# True when a surface draws with a ShaderMaterial: the wrapper's detail script took effect.
	for child in node.find_children("*", "MeshInstance3D", true, false):
		var mesh_instance := child as MeshInstance3D
		if mesh_instance.mesh == null:
			continue
		for surface in mesh_instance.mesh.get_surface_count():
			if mesh_instance.get_active_material(surface) is ShaderMaterial:
				return true
	return false


func _visual_bounds(node: Node3D) -> AABB:
	var found := false
	var result := AABB()
	for child in node.find_children("*", "MeshInstance3D", true, false):
		var mesh_instance := child as MeshInstance3D
		if mesh_instance.mesh == null:
			continue
		var world_box := mesh_instance.global_transform * mesh_instance.get_aabb()
		if not found:
			result = world_box
			found = true
		else:
			result = result.merge(world_box)
	return result


func _capture(label: String) -> void:
	await process_frame
	await RenderingServer.frame_post_draw
	var image := root.get_texture().get_image()
	var path := _output_path(label + ".png")
	var error := image.save_png(path)
	if error != OK:
		failures.append("could not save " + path + ": " + str(error))


func _write_summary() -> void:
	var payload := {
		"schema": 1,
		"status": "passed" if failures.is_empty() else "failed",
		"subjects": summary,
		"failures": failures,
		"distances_m": DISTANCES,
		"sides": SIDES.keys(),
	}
	var file := FileAccess.open(_output_path("summary.json"), FileAccess.WRITE)
	if file == null:
		failures.append("could not write " + _output_path("summary.json"))
		return
	file.store_string(JSON.stringify(payload, "  ") + "\n")
