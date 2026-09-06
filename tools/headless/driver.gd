extends SceneTree
## Local, finite request protocol. No sockets, expression evaluator, or unrestricted reflection.

const Capture = preload("res://tools/headless/capture.gd")
const Validator = preload("res://tools/headless/validate.gd")
const Author = preload("res://tools/headless/author.gd")
const MUTABLE := ["position", "global_position", "rotation_degrees", "visible", "velocity", "fov"]
const METHODS := ["show", "hide", "make_current", "reset_physics_interpolation"]

var logger = preload("res://tools/headless/logger.gd").new()
var request: Dictionary = {}
var report := {"diagnostics": [], "assertions": [], "metrics": {}}
var frame_times: Array[float] = []
var frame_number := 0
var stopped := false
var failed := false


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var index := args.find("--request")
	if index < 0 or index + 1 >= args.size():
		printerr("ERROR: SDK requires --request JSON")
		quit(2)
		return
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(args[index + 1]))
	if not parsed is Dictionary:
		printerr("ERROR: invalid request JSON")
		quit(2)
		return
	request = parsed
	OS.add_logger(logger)
	seed(int(request.seed))
	Engine.physics_ticks_per_second = 60
	# Stable clear color; GPU shader clocks are renderer-specific (see TOOLING.md).
	RenderingServer.set_default_clear_color(Color(0.04, 0.04, 0.04))
	if request.command == "author":
		var builder = Author.new()
		builder.construct(request.author_plan)
		var authored: Dictionary = builder.save_staged(String(request.artifacts))
		if not authored.success:
			for message in authored.errors:
				_error("author.failed", String(message))
		report.metrics = authored
		_write("author", authored)
		builder.release()
	elif request.command == "validate":
		var validator = Validator.new()
		validator.run(request, report)
	elif request.command == "inspect" and request.has("resource"):
		var resource = ResourceLoader.load(String(request.resource))
		if resource == null:
			_error("inspect.resource", "Resource could not load: " + String(request.resource))
		else:
			var properties := {}
			for property in resource.get_property_list():
				if int(property.usage) & PROPERTY_USAGE_STORAGE:
					properties[String(property.name)] = _safe(resource.get(property.name))
			_write("resource", {"path": request.resource, "class": resource.get_class(), "properties": properties,
				"dependencies": ResourceLoader.get_dependencies(String(request.resource))})
	else:
		await _load_scene(String(request.scene))
		for step in request.steps:
			failed = failed or logger.has_errors()
			if stopped or failed:
				break
			await _step(step)
		if request.command == "perf" and not failed:
			await _frames(60)
			frame_times.clear()
		if not stopped and not failed:
			await _frames(int(request.frames))
		if request.command == "screenshot" and not failed:
			await _capture("capture")
		failed = failed or logger.has_errors()
		_dump("state")
		if failed and DisplayServer.get_name() != "headless":
			await _capture("failure")
		_metrics()
	# Evidence describes the live scene; release its managed resources only after recording it.
	if current_scene != null:
		current_scene.free()
		current_scene = null
	await process_frame
	var collector_script = load("res://src/Bootstrap/ContentDatabaseLoader.cs")
	var collector = collector_script.new()
	collector.call("CollectManagedResources")
	collector.free()
	await process_frame
	report.diagnostics.append_array(logger.snapshot())
	OS.remove_logger(logger)
	_write("result", report)
	quit(1 if failed or not report.diagnostics.filter(func(d): return d.severity == "error").is_empty() else 0)


func _path(label: String, suffix := "json") -> String:
	return String(request.artifacts).path_join("%s.%s.%s" % [request.name, label, suffix])


func _write(label: String, value) -> void:
	var file := FileAccess.open(_path(label), FileAccess.WRITE)
	if file == null:
		_error("artifact.write", "Could not write " + _path(label))
		return
	file.store_string(JSON.stringify(value, "  "))


func _error(code: String, message: String, node = null) -> void:
	failed = true
	report.diagnostics.append({"severity": "error", "code": code, "path": request.scene,
		"node": node, "message": message})


func _load_scene(path: String) -> void:
	if not path.begins_with("res://") or ".." in path.split("/"):
		_error("scene.path", "Scene must be project local")
		return
	if current_scene != null:
		current_scene.free()
	var packed = ResourceLoader.load(path)
	if not packed is PackedScene or not packed.can_instantiate():
		_error("scene.load", "Cannot instantiate " + path)
		return
	var instance: Node = packed.instantiate()
	root.add_child(instance)
	current_scene = instance
	await _frames(2)


func _frames(count: int) -> void:
	for _i in range(clampi(count, 0, 100000)):
		var before := Time.get_ticks_usec()
		await process_frame
		frame_times.append((Time.get_ticks_usec() - before) / 1000.0)
		frame_number += 1
		if frame_number % 30 == 0:
			_write("progress", {"frame": frame_number, "command": request.command, "scene": request.scene,
				"assertions": report.assertions})


func _node(step: Dictionary) -> Node:
	var path := String(step.get("node", ""))
	if not path.begins_with("/root/") or ".." in path.split("/"):
		return null
	return root.get_node_or_null(NodePath(path))


func _has_property(node: Object, property: String) -> bool:
	for info in node.get_property_list():
		if String(info.name) == property:
			return true
	return false


func _safe(value, depth := 0):
	if depth > 3:
		return "<depth limit>"
	if value is Resource:
		return {"resource": value.resource_path, "class": value.get_class()}
	if value is Node:
		return str(value.get_path()) if value.is_inside_tree() else str(value.name)
	if value is Object:
		return {"class": value.get_class()}
	if value is Vector3:
		return [value.x, value.y, value.z]
	if value is Array:
		var values := []
		for item in value.slice(0, 256):
			values.append(_safe(item, depth + 1))
		return values
	if value is Dictionary:
		var values := {}
		for key in value:
			values[str(key)] = _safe(value[key], depth + 1)
		return values
	return value


func _tree(node: Node) -> Dictionary:
	var properties := {}
	for property in node.get_property_list():
		if int(property.usage) & PROPERTY_USAGE_STORAGE:
			properties[String(property.name)] = _safe(node.get(property.name))
	var children := []
	for child in node.get_children():
		children.append(_tree(child))
	return {"path": str(node.get_path()), "class": node.get_class(), "scene": node.scene_file_path,
		"groups": node.get_groups(), "properties": properties, "children": children}


func _dump(label: String) -> void:
	var autoloads := {}
	for property in ProjectSettings.get_property_list():
		if String(property.name).begins_with("autoload/"):
			autoloads[String(property.name)] = ProjectSettings.get_setting(property.name)
	var subject: Node = root
	if request.has("node"):
		subject = root.get_node_or_null(NodePath(request.node))
		if subject == null:
			_error("inspect.node", "Requested node does not exist", request.node)
			return
	_write(label, {"tree": _tree(subject), "autoloads": autoloads, "frame": frame_number,
		"seed": request.seed, "paused": paused, "scene": request.scene,
		"godot_version": Engine.get_version_info(), "user_dir": OS.get_environment("EMBERVALE_USER_DIR")})


func _matches(step: Dictionary) -> Dictionary:
	var node := _node(step)
	var comparator := String(step.get("comparison", "exists"))
	var actual = node != null
	var expected = step.get("value", true)
	if comparator != "exists":
		var property := String(step.get("property", ""))
		if node == null or not _has_property(node, property):
			return {"success": false, "actual": null, "expected": expected}
		actual = _safe(node.get(property))
	var ok := false
	match comparator:
		"exists", "eq": ok = actual == expected
		"ne": ok = actual != expected
		"gt", "ge", "lt", "le":
			if (actual is float or actual is int) and (expected is float or expected is int):
				match comparator:
					"gt": ok = actual > expected
					"ge": ok = actual >= expected
					"lt": ok = actual < expected
					"le": ok = actual <= expected
	return {"success": ok, "actual": actual, "expected": expected}


func _step(step: Dictionary) -> void:
	var op := String(step.get("op", ""))
	match op:
		"build":
			var parent: Node = root.get_node_or_null(NodePath(step.parent))
			if parent == null or current_scene == null or (parent != current_scene and not current_scene.is_ancestor_of(parent)):
				_error("build.parent", "Build parent must belong to the current scene")
				return
			var builder = Author.new()
			var created = builder.construct(step.plan)
			if not created is Node or not builder.errors.is_empty():
				_error("build.failed", str(builder.errors))
				builder.release()
				return
			if parent.has_node(NodePath(created.name)):
				_error("build.name", "Build root name already exists beneath parent")
				builder.release()
				return
			parent.add_child(created)
			_write("build-%d" % frame_number, {"root": str(created.get_path()), "changes": builder.changes})
			# Parent now owns this subtree; drop planning references without freeing the live nodes.
			builder.subject = null
			builder.objects.clear()
		"load_scene": await _load_scene(String(step.scene))
		"wait_frames": await _frames(int(step.get("frames", 1)))
		"new_game":
			if current_scene == null or not current_scene.has_method("AutomationNewGame"):
				_error("scenario.new_game", "New game requires ApplicationRoot and a tooling build")
			else:
				current_scene.call("AutomationNewGame")
		"assert", "wait_until":
			var assertion := _matches(step)
			if op == "wait_until":
				for _i in range(clampi(int(step.get("frames", 600)), 1, 100000)):
					if assertion.success:
						break
					await _frames(1)
					assertion = _matches(step)
			assertion["name"] = step.get("name", "%s %s" % [op, step.node])
			assertion["node"] = step.node
			assertion["frame"] = frame_number
			report.assertions.append(assertion)
			if not assertion.success:
				_error("scenario.assertion", assertion.name, step.node)
		"get_property", "set_property", "call_method":
			var node := _node(step)
			if node == null:
				_error("scenario.node", "Node does not exist", step.node)
				return
			if op == "call_method":
				var method := String(step.get("method", ""))
				if method not in METHODS or not node.has_method(method) or not step.get("args", []).is_empty():
					_error("scenario.method", "Method is not supported on this node", step.node)
					return
				node.call(method)
				return
			var property := String(step.get("property", ""))
			if not _has_property(node, property):
				_error("scenario.property", "Property does not exist: " + property, step.node)
				return
			if op == "get_property":
				_write("property-%d" % frame_number, {"node": step.node, "property": property, "value": _safe(node.get(property))})
			elif property not in MUTABLE:
				_error("scenario.property", "Property is not writable: " + property, step.node)
			else:
				var value = step.get("value")
				if node.get(property) is Vector3:
					if not value is Array or value.size() != 3:
						_error("scenario.type", "Expected a three-number vector", step.node)
						return
					value = Vector3(value[0], value[1], value[2])
				node.set(property, value)
		"send_action":
			var action := String(step.get("action", ""))
			if not InputMap.has_action(action):
				_error("scenario.action", "Unknown InputMap action: " + action)
				return
			var event := InputEventAction.new()
			event.action = action
			event.pressed = true
			event.strength = clampf(float(step.get("strength", 1)), 0, 1)
			Input.parse_input_event(event)
			await _frames(int(step.get("frames", 1)))
			event = InputEventAction.new()
			event.action = action
			event.pressed = false
			Input.parse_input_event(event)
		"scene_tree", "dump_state": _dump("state-%d" % frame_number)
		"collect_metrics": _metrics()
		"collect_logs": _write("logs", {"godot_log": String(request.name) + ".godot.log"})
		"capture_screenshot": await _capture(String(step.get("name", "capture")))
		"stop": stopped = true
		_: _error("scenario.operation", "Unsupported operation: " + op)


func _capture(label: String) -> void:
	var camera: Camera3D = root.get_camera_3d()
	if request.has("camera"):
		camera = root.get_node_or_null(NodePath(request.camera)) as Camera3D
		if camera == null:
			_error("capture.camera", "Requested Camera3D does not exist")
			return
		camera.make_current()
	if request.has("position") or request.has("rotation"):
		if camera == null:
			_error("capture.camera", "No current Camera3D to position")
			return
		if request.has("position"):
			var v: Array = request.position
			camera.global_position = Vector3(v[0], v[1], v[2])
		if request.has("rotation"):
			var v: Array = request.rotation
			camera.rotation_degrees = Vector3(v[0], v[1], v[2])
	await _frames(2)
	var metadata := {"scene": request.scene, "seed": request.seed, "frame": frame_number,
		"camera": str(camera.get_path()) if camera else null,
		"position": _safe(camera.global_position) if camera else null,
		"rotation": _safe(camera.rotation_degrees) if camera else null,
		"godot_version": Engine.get_version_info(), "renderer": RenderingServer.get_current_rendering_method()}
	var result: Dictionary = await Capture.capture(root, _path(label, "png"), metadata,
		String(request.get("baseline", "")) if request.command == "screenshot" else "", float(request.threshold))
	if not result.success:
		_error("capture.failed", result.message)


func _metrics() -> void:
	var sorted := frame_times.duplicate()
	sorted.sort()
	var metrics := {"frames": frame_times.size(), "seed": request.seed,
		"p95_frame_ms": sorted[int((sorted.size() - 1) * 0.95)] if not sorted.is_empty() else 0,
		"max_frame_ms": sorted.back() if not sorted.is_empty() else 0,
		"process_ms": Performance.get_monitor(Performance.TIME_PROCESS) * 1000,
		"physics_ms": Performance.get_monitor(Performance.TIME_PHYSICS_PROCESS) * 1000,
		"nodes": Performance.get_monitor(Performance.OBJECT_NODE_COUNT),
		"orphan_nodes": Performance.get_monitor(Performance.OBJECT_ORPHAN_NODE_COUNT),
		"static_memory_bytes": Performance.get_monitor(Performance.MEMORY_STATIC),
		"draw_calls": Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),
		"primitives": Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME),
		"renderer": RenderingServer.get_current_rendering_method(),
		"samples_ms": frame_times}
	report.metrics = metrics
	_write("metrics", metrics)
