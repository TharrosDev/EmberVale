extends SceneTree

## Multi-angle visual QA for the rock, cliff and ice families.
##
## Modelled on tools/architecture_shots.gd, with one addition that matters more here than it does
## for a building.
##
## ⚠️ A ROCK HAS NO INTRINSIC SCALE AND A RENDER OF ONE ON A GREY PLANE PROVES NOTHING. A boulder,
## a pebble and a cliff are the same picture at different camera distances — which is exactly how
## the nature megakit came to ship 1.33 m "short grass" and a 2.49 m "flower" without anyone
## noticing (docs/ASSET_POLICY.md §0.3). Every frame here therefore stands a 1.8 m human reference
## beside the subject, at the production player capsule's height, so "implausible scale" is
## something the eye can catch instead of something a number has to be looked up for.
##
##   godot --path . --resolution 960x720 --script res://tools/environment_shots.gd -- \
##       --output reports/3d/session-06-environment-handoff/visual-qa

const SUBJECTS := [
	# Composed from nature-megakit rocks; these are the new large end of the family.
	"res://assets/models/props/prp_boulder_large.glb",
	"res://assets/models/props/prp_rock_cluster_a.glb",
	"res://assets/models/props/prp_rock_scree.glb",
	"res://assets/models/props/prp_rock_edging.glb",
	"res://assets/models/props/prp_cliff_face.glb",
	"res://assets/models/props/prp_cliff_face_tall.glb",
	# Adopted straight from the pack, unchanged: rendered because their SCALE is the open
	# question, not their geometry.
	"res://assets/models/props/prp_rock_medium.glb",
	"res://assets/models/props/prp_pebble_c.glb",
	"res://assets/models/props/prp_pebble_d.glb",
	# Authored ice.
	"res://assets/models/props/prp_ice_chunk.glb",
	"res://assets/models/props/prp_ice_shard.glb",
	"res://assets/models/props/prp_ice_slab.glb",
	"res://assets/models/props/prp_glacier_wall.glb",
	"res://assets/models/props/prp_glacier_face.glb",
	# The incumbents, in the same frames and the same light, so "is the new one better" is a
	# comparison rather than an assertion.
	"res://assets/models/props/prp_glacier.glb",
	"res://assets/models/props/prp_boulder.glb",
	"res://assets/models/props/prp_rock_cluster.glb",
	# The two hero props this session was asked to judge rather than assume. Both were RETAINED
	# and both had their material response corrected by repair_architecture_materials.py — the
	# relic's gold and the brazier's ironwork sat at 0.4 metallic, which reads as painted plastic
	# on the two objects in the game most dependent on looking like worked metal.
	"res://assets/models/props/prp_relic.glb",
	"res://assets/models/props/prp_brazier.glb",
]

const VIEWS := {
	"front": Vector3(0, 0.12, 1),
	"back": Vector3(0, 0.12, -1),
	"left": Vector3(-1, 0.12, 0),
	"right": Vector3(1, 0.12, 0),
	"front_3q": Vector3(0.72, 0.16, 0.72),
	"rear_3q": Vector3(-0.72, 0.16, -0.72),
	# ⚠️ The eye-level view is the one that catches a buried or floating base, and neither of the
	# three-quarter views does: from above you cannot see the seam where the mesh meets the ground.
	"eye": Vector3(0.55, 0.015, 0.83),
}

var output := "reports/3d/session-06-environment-handoff/visual-qa"
var stage: Node3D
var camera: Camera3D
var reference: Node3D
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
	for path in SUBJECTS:
		await _render_subject(path)
	_write_summary()
	if failures.is_empty():
		print("environment shots: PASS (%d frames)" % [SUBJECTS.size() * VIEWS.size()])
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
	plane.size = Vector2(80, 80)
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
	camera.fov = 48
	camera.near = 0.05
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
	subject.name = "EnvironmentSubject"
	stage.add_child(subject)
	await process_frame
	await process_frame

	var bounds := _visual_bounds(subject)
	if bounds.size.length_squared() <= 0.001:
		failures.append(path + ": no rendered bounds")
		subject.queue_free()
		await process_frame
		return

	# ⚠️ A NON-ZERO BASE IS A DEFECT, NOT A PREFERENCE. The audit's `ground-offset` finding names
	# seven props whose lowest rendered point is not at y=0; placed at a terrain height they
	# float or sink, and every cell that uses one compensates with a per-placement Y nudge.
	# Reported rather than failed, because the incumbents in this list are known to have it.
	var base := bounds.position.y
	var materials := _material_count(subject)

	var slug := path.get_file().get_basename().trim_prefix("prp_")
	var target := bounds.get_center()
	# Stand the reference clear of the subject's footprint rather than inside it.
	reference.position = Vector3(bounds.position.x + bounds.size.x + 1.2, 0, target.z)
	var span: float = max(bounds.size.x + 2.0, bounds.size.z)
	var distance: float = max(6.0, span * 1.42 + bounds.size.y * 0.70)
	for label in VIEWS:
		var direction: Vector3 = VIEWS[label].normalized()
		if label == "eye":
			# A real standing eye height, aimed at the middle of the mass: this is the view the
			# player actually gets, and it is the only one that judges a cliff's presence.
			camera.global_position = target + direction * distance + Vector3.UP * (1.7 - target.y)
			camera.look_at(target, Vector3.UP)
		else:
			camera.global_position = target + direction * distance + Vector3.UP * bounds.size.y * 0.08
			camera.look_at(target + Vector3.UP * bounds.size.y * 0.04, Vector3.UP)
		await _capture(slug + "--" + label)

	summary.append({
		"path": path,
		"slug": slug,
		"bounds": [
			snappedf(bounds.size.x, 0.01), snappedf(bounds.size.y, 0.01), snappedf(bounds.size.z, 0.01)],
		"base_y": snappedf(base, 0.001),
		"grounded": absf(base) < 0.02,
		"surfaces": materials,
		"frames": VIEWS.size(),
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
	var path := ProjectSettings.globalize_path("res://" + output + "/" + label + ".png")
	var error := image.save_png(path)
	if error != OK:
		failures.append("could not save " + path + ": " + str(error))


func _write_summary() -> void:
	var payload := {
		"schema": 1,
		"status": "passed" if failures.is_empty() else "failed",
		"subjects": summary,
		"failures": failures,
		"required_views": VIEWS.keys(),
	}
	var path := ProjectSettings.globalize_path("res://" + output + "/summary.json")
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		failures.append("could not write " + path)
		return
	file.store_string(JSON.stringify(payload, "  ") + "\n")
