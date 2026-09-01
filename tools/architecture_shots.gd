extends SceneTree

## Six-angle visual QA for authored building scenes.
##
## This complements audit_3d.py: the permanent audit renders individual GLB/glTF assets, while
## buildings are compositions whose missing gables, windows, walls, roofs and colliders only exist
## at the PackedScene level. Run with:
##   godot --path . --resolution 960x720 --script res://tools/architecture_shots.gd -- \
##       --output reports/3d/session-05-architecture-handoff/visual-qa

const BUILDINGS := [
	"res://scenes/props/bld_cottage_shuttered.tscn",
	"res://scenes/props/bld_farmhouse_long.tscn",
	"res://scenes/props/bld_shop_awning.tscn",
	"res://scenes/props/bld_townhouse_balcony.tscn",
	"res://scenes/props/bld_workshop_open.tscn",
	"res://scenes/props/bld_longhouse_stone.tscn",
	"res://scenes/props/bld_townhouse_wide.tscn",
	"res://scenes/props/bld_inn_courtyard.tscn",
	"res://scenes/props/bld_ruin_house.tscn",
	"res://scenes/props/bld_ruin_tower.tscn",
	"res://scenes/props/bld_ashfall_house.tscn",
	"res://assets/models/architecture/bld_cottage.glb",
	"res://assets/models/architecture/bld_house_a.glb",
	"res://assets/models/architecture/bld_inn.glb",
	"res://assets/models/architecture/bld_blacksmith.glb",
]

const VIEWS := {
	"front": Vector3(0, 0.12, 1),
	"back": Vector3(0, 0.12, -1),
	"left": Vector3(-1, 0.12, 0),
	"right": Vector3(1, 0.12, 0),
	"front_3q": Vector3(0.72, 0.16, 0.72),
	"rear_3q": Vector3(-0.72, 0.16, -0.72),
}

var output := "reports/3d/session-05-architecture-handoff/visual-qa"
var stage: Node3D
var camera: Camera3D
var failures: Array[String] = []
var summary: Array[Dictionary] = []


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	for i in range(args.size() - 1):
		if args[i] == "--output":
			output = args[i + 1]
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://" + output))
	call_deferred("_run")


func _run() -> void:
	_build_stage()
	await process_frame
	await process_frame
	for path in BUILDINGS:
		await _render_building(path)
	_write_summary()
	if failures.is_empty():
		print("architecture shots: PASS (", BUILDINGS.size() * VIEWS.size(), " frames)")
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
	stage.add_child(key)
	var fill := DirectionalLight3D.new()
	fill.rotation_degrees = Vector3(-24, 142, 0)
	fill.light_color = Color("98b2d0")
	fill.light_energy = 0.68
	stage.add_child(fill)
	var floor_mesh := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(40, 40)
	floor_mesh.mesh = plane
	var floor_material := StandardMaterial3D.new()
	floor_material.albedo_color = Color("57534b")
	floor_material.roughness = 0.94
	floor_mesh.material_override = floor_material
	stage.add_child(floor_mesh)
	camera = Camera3D.new()
	camera.current = true
	camera.fov = 48
	camera.near = 0.05
	stage.add_child(camera)


func _render_building(path: String) -> void:
	var packed := load(path) as PackedScene
	if packed == null:
		failures.append(path + ": could not load")
		return
	var subject := packed.instantiate() as Node3D
	if subject == null:
		failures.append(path + ": root is not Node3D")
		return
	subject.name = "ArchitectureSubject"
	stage.add_child(subject)
	await process_frame
	await process_frame
	var bounds := _visual_bounds(subject)
	if bounds.size.length_squared() <= 0.001:
		failures.append(path + ": no rendered bounds")
		subject.queue_free()
		await process_frame
		return
	var collisions := subject.find_children("*", "CollisionShape3D", true, false).size()
	if path.ends_with(".tscn") and collisions == 0:
		failures.append(path + ": authored building has no CollisionShape3D")
	var slug := path.get_file().get_basename().trim_prefix("bld_")
	var target := bounds.get_center()
	var span: float = max(bounds.size.x, bounds.size.z)
	var distance: float = max(8.0, span * 1.38 + bounds.size.y * 0.72)
	for label in VIEWS:
		var direction: Vector3 = VIEWS[label].normalized()
		camera.global_position = target + direction * distance + Vector3.UP * bounds.size.y * 0.08
		camera.look_at(target + Vector3.UP * bounds.size.y * 0.04, Vector3.UP)
		await _capture(slug + "--" + label)
	summary.append({
		"path": path,
		"slug": slug,
		"bounds": [bounds.size.x, bounds.size.y, bounds.size.z],
		"collision_shapes": collisions,
		"frames": VIEWS.size(),
	})
	subject.queue_free()
	await process_frame


func _visual_bounds(node: Node3D) -> AABB:
	var found := false
	var result := AABB()
	for child in node.find_children("*", "MeshInstance3D", true, false):
		var mesh := child as MeshInstance3D
		if mesh.mesh == null:
			continue
		var world_box := mesh.global_transform * mesh.get_aabb()
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
	var path := ProjectSettings.globalize_path("res://" + output + "/" + label + ".png")
	var error := image.save_png(path)
	if error != OK:
		failures.append("could not save " + path + ": " + str(error))


func _write_summary() -> void:
	var payload := {
		"schema": 1,
		"status": "passed" if failures.is_empty() else "failed",
		"buildings": summary,
		"failures": failures,
		"required_views": VIEWS.keys(),
	}
	var path := ProjectSettings.globalize_path("res://" + output + "/summary.json")
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		failures.append("could not write " + path)
		return
	file.store_string(JSON.stringify(payload, "  ") + "\n")
