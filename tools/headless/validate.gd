extends RefCounted
## ResourceLoader is authoritative; no .tscn parsing or mutation. Scene instances stay off-tree.

var paths: Array[String] = []
var dependencies: Dictionary = {}
var report: Dictionary


func run(request: Dictionary, result: Dictionary) -> void:
	report = result
	_walk("res://")
	paths.sort()
	for path in paths:
		var deps: Array[String] = []
		for dep in ResourceLoader.get_dependencies(path):
			var pieces := String(dep).split("::")
			var target := String(pieces[pieces.size() - 1])
			if target.begins_with("uid://"):
				var id := ResourceUID.text_to_id(target)
				if ResourceUID.has_id(id):
					target = ResourceUID.get_id_path(id)
			deps.append(target)
		dependencies[path] = deps
	var selected: Dictionary = {}
	if request.has("changed") and not request.get("full", false):
		for path in request.changed:
			selected["res://" + String(path).trim_suffix(".import")] = true
		# Reverse closure catches deleted and renamed dependencies too.
		var expanded := true
		while expanded:
			expanded = false
			for path in paths:
				if selected.has(path):
					continue
				for dep in dependencies[path]:
					if selected.has(dep):
						selected[path] = true
						expanded = true
						break
	else:
		for path in paths:
			selected[path] = true
	var checked := 0
	for path in paths:
		if not selected.has(path):
			continue
		checked += 1
		for dep in dependencies[path]:
			if not ResourceLoader.exists(dep):
				_issue("error", "resource.dependency", path, "", "Missing dependency: " + dep)
		var resource = ResourceLoader.load(path)
		if resource == null:
			_issue("error", "resource.load", path, "", "ResourceLoader could not load resource")
		elif resource is PackedScene:
			if not resource.can_instantiate():
				_issue("error", "scene.instantiate", path, "", "PackedScene cannot instantiate")
				continue
			var instance: Node = resource.instantiate()
			if instance == null:
				_issue("error", "scene.instantiate", path, "", "Instantiation returned null")
				continue
			_nodes(instance, instance, path)
			instance.free()
	report.metrics = {"resources_discovered": paths.size(), "resources_checked": checked,
		"selected_paths": selected.keys(), "dependency_graph": dependencies}


func _walk(folder: String) -> void:
	var dir := DirAccess.open(folder)
	if dir == null or FileAccess.file_exists(folder.path_join(".gdignore")):
		return
	dir.list_dir_begin()
	var name := dir.get_next()
	while not name.is_empty():
		if not name.begins_with("."):
			var path := folder.path_join(name)
			if dir.current_is_dir():
				if name not in ["addons", "bin", "obj", "reports", "artifacts", "tests"]:
					_walk(path)
			elif name.get_extension() in ["tscn", "scn", "tres", "res", "gd", "glb", "gltf"]:
				paths.append(path)
		name = dir.get_next()
	dir.list_dir_end()


func _issue(severity: String, code: String, path: String, node: String, message: String) -> void:
	report.diagnostics.append({"severity": severity, "code": code, "path": path, "node": node, "message": message})


func _nodes(node: Node, scene: Node, path: String) -> void:
	var node_path := str(scene.get_path_to(node))
	for property in node.get_property_list():
		if int(property.type) != TYPE_NODE_PATH or not int(property.usage) & PROPERTY_USAGE_STORAGE:
			continue
		var target: NodePath = node.get(property.name)
		if not target.is_empty() and not target.is_absolute() and node.get_node_or_null(target) == null:
			_issue("warning", "node.path", path, node_path, "Unresolved authored NodePath %s: %s (may be runtime-created)" % [property.name, target])
	if node is Node3D:
		var scale: Vector3 = node.scale
		if not scale.is_finite() or not node.position.is_finite() or absf(scale.x * scale.y * scale.z) < 0.000001:
			_issue("error", "transform.invalid", path, node_path, "Non-finite transform or effectively zero scale")
		elif scale.x < 0 or scale.y < 0 or scale.z < 0:
			_issue("warning", "transform.mirrored", path, node_path, "Negative scale; verify normals and collision")
	if node is CollisionShape3D and node.shape == null and not node.disabled:
		_issue("warning", "physics.shape", path, node_path, "Enabled CollisionShape3D has no authored shape (may be assigned at runtime)")
	if node is CollisionObject3D and node.collision_layer == 0 and node.collision_mask == 0:
		_issue("warning", "physics.layers", path, node_path, "Collision object neither detects nor is detected")
	if node is NavigationRegion3D and node.navigation_mesh == null:
		_issue("warning", "navigation.mesh", path, node_path, "Navigation region has no authored mesh (may be generated)")
	for child in node.get_children():
		_nodes(child, scene, path)
