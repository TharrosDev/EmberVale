extends SceneTree

# Measures the actual Godot-imported scene. It is read-only: instances are never added to the game.
func _initialize() -> void:
	var output := "reports/3d/session-1-foundation/godot-inspection.json"
	var args := OS.get_cmdline_user_args()
	for i in range(args.size() - 1):
		if args[i] == "--output": output = args[i + 1]
	var files: Array[String] = []
	_collect("res://assets/models", files)
	var assets := {}
	for path in files:
		assets[path.trim_prefix("res://")] = _inspect(path)
	var absolute := output if output.is_absolute_path() else ProjectSettings.globalize_path("res://" + output)
	DirAccess.make_dir_recursive_absolute(absolute.get_base_dir())
	var file := FileAccess.open(absolute, FileAccess.WRITE)
	if file == null:
		push_error("Cannot write " + absolute)
		quit(2); return
	file.store_string(JSON.stringify({"schema_version": 1, "godot_version": Engine.get_version_info(), "assets": assets}, "  "))
	print("MODEL_AUDIT_PROBE: %d assets -> %s" % [assets.size(), absolute])
	quit(0)

func _collect(directory: String, files: Array[String]) -> void:
	var dir := DirAccess.open(directory)
	if dir == null: return
	dir.list_dir_begin()
	var name := dir.get_next()
	while name != "":
		var path := directory.path_join(name)
		if dir.current_is_dir(): _collect(path, files)
		elif name.get_extension().to_lower() in ["glb", "gltf"]: files.append(path)
		name = dir.get_next()
	dir.list_dir_end()

func _inspect(path: String) -> Dictionary:
	var packed := load(path) as PackedScene
	if packed == null: return {"errors": ["not a PackedScene"]}
	var root := packed.instantiate()
	var state := {"has_bounds": false, "aabb": AABB(), "mesh_node_count": 0, "collision_node_count": 0,
		"skeleton_count": 0, "max_bones": 0, "animation_clips": [], "mesh_nodes": [], "errors": []}
	_walk(root, Transform3D.IDENTITY, state)
	root.free()
	var aabb: AABB = state.aabb
	var reliable: bool = state.skeleton_count == 0
	return {"aabb_min": [aabb.position.x, aabb.position.y, aabb.position.z] if reliable else null,
		"aabb_max": [aabb.end.x, aabb.end.y, aabb.end.z] if reliable else null,
		"dimensions": [aabb.size.x, aabb.size.y, aabb.size.z] if reliable else null,
		"bounds_reliable": reliable,
		"raw_skinned_aabb_min": [aabb.position.x, aabb.position.y, aabb.position.z] if not reliable else null,
		"raw_skinned_dimensions": [aabb.size.x, aabb.size.y, aabb.size.z] if not reliable else null,
		"mesh_node_count": state.mesh_node_count, "collision_node_count": state.collision_node_count,
		"skeleton_count": state.skeleton_count, "max_bones": state.max_bones,
		"animation_clips": state.animation_clips, "mesh_nodes": state.mesh_nodes, "errors": state.errors}

func _walk(node: Node, parent_transform: Transform3D, state: Dictionary) -> void:
	var node_name := String(node.name)
	if node_name.begins_with("glTF_not_exported") or node_name.begins_with("Icosphere"):
		return
	var world := parent_transform
	if node is Node3D: world = parent_transform * (node as Node3D).transform
	if node is MeshInstance3D and (node as MeshInstance3D).mesh != null and (node as MeshInstance3D).visible:
		state.mesh_node_count += 1
		var box := world * (node as MeshInstance3D).get_aabb()
		state.mesh_nodes.append({"name": node_name, "mesh": (node as MeshInstance3D).mesh.resource_name,
			"aabb_min": [box.position.x, box.position.y, box.position.z], "dimensions": [box.size.x, box.size.y, box.size.z]})
		state.aabb = state.aabb.merge(box) if state.has_bounds else box
		state.has_bounds = true
	if node is CollisionShape3D: state.collision_node_count += 1
	if node is Skeleton3D:
		state.skeleton_count += 1; state.max_bones = maxi(state.max_bones, (node as Skeleton3D).get_bone_count())
	if node is AnimationPlayer:
		for clip in (node as AnimationPlayer).get_animation_list():
			if clip not in state.animation_clips: state.animation_clips.append(clip)
	for child in node.get_children(): _walk(child, world, state)
