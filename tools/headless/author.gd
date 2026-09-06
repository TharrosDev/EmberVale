extends RefCounted
## Constructs detached scenes/resources and live subtrees with the same finite operations.
## Property typing and scene serialization are Godot-native; no text scene mutation or eval.

const NODES := ["Node", "Node3D", "Marker3D", "MeshInstance3D", "Camera3D", "DirectionalLight3D",
	"OmniLight3D", "SpotLight3D", "WorldEnvironment", "StaticBody3D", "Area3D", "CollisionShape3D",
	"NavigationRegion3D", "NavigationLink3D"]
const RESOURCES := ["StandardMaterial3D", "BoxMesh", "SphereMesh", "CylinderMesh", "PlaneMesh", "CapsuleMesh",
	"BoxShape3D", "SphereShape3D", "CapsuleShape3D", "CylinderShape3D", "Environment", "NavigationMesh"]
const FORBIDDEN := ["script", "owner", "name", "resource_path", "scene_file_path", "process_mode"]

var subject: Object
var objects: Dictionary = {}
var errors: Array[String] = []
var changes: Array[Dictionary] = []


func construct(plan: Dictionary) -> Object:
	if plan.has("source"):
		var source = ResourceLoader.load(String(plan.source))
		if source is PackedScene:
			subject = source.instantiate()
		elif source is Resource:
			subject = source.duplicate(true)
		else:
			errors.append("Cannot load source: " + String(plan.source))
			return null
	else:
		subject = Node3D.new()
	if subject is Node:
		subject.name = String(plan.get("name", "SdkBuild"))
	objects["root"] = subject
	for operation in plan.operations:
		if not errors.is_empty():
			break
		_operation(operation)
	return subject


func _target(id: String) -> Object:
	if objects.has(id) and is_instance_valid(objects[id]):
		return objects[id]
	if subject is Node and not id.begins_with("/") and ".." not in id.split("/"):
		return subject.get_node_or_null(NodePath(id))
	return null


func _operation(operation: Dictionary) -> void:
	var op := String(operation.op)
	var object: Object
	if op in ["create_node", "instantiate_scene", "create_resource"]:
		var id := String(operation.id)
		if objects.has(id):
			errors.append("Duplicate construction id: " + id)
			return
		if op == "instantiate_scene":
			var packed = ResourceLoader.load(String(operation.scene))
			if packed is PackedScene:
				object = packed.instantiate()
		elif op == "create_resource" and operation.has("source"):
			var resource = ResourceLoader.load(String(operation.source))
			if resource is Resource:
				object = resource.duplicate(true)
		else:
			var type := String(operation.get("type", "Node3D"))
			if (op == "create_node" and type in NODES) or (op == "create_resource" and type in RESOURCES):
				object = ClassDB.instantiate(type)
		if object == null:
			errors.append("Cannot construct allowed type/source for " + id)
			return
		objects[id] = object
		if object is Node:
			var parent := _target(String(operation.get("parent", "root"))) as Node
			if parent == null or not subject is Node:
				object.free()
				errors.append("Parent must be a node in this construction")
				return
			if parent.has_node(NodePath(id)):
				object.free()
				errors.append("Node name already exists under parent: " + id)
				return
			object.name = id
			parent.add_child(object)
			object.owner = subject
	elif op in ["customize", "add_group", "remove_node"]:
		object = _target(String(operation.target))
		if object == null:
			errors.append("Target does not exist: " + String(operation.target))
			return
		if op == "add_group":
			if not object is Node:
				errors.append("Groups require a node")
				return
			object.add_to_group(String(operation.group), true)
		elif op == "remove_node":
			if not object is Node or object == subject:
				errors.append("Only child nodes can be removed")
				return
			object.free()
			changes.append(operation)
			return
	else:
		errors.append("Unsupported construction operation: " + op)
		return
	_customize(object, operation.get("properties", {}))
	changes.append(operation)


func _customize(object: Object, properties: Dictionary) -> void:
	for key in properties:
		if key in FORBIDDEN:
			errors.append("Forbidden property: " + key)
			return
		var info := {}
		for candidate in object.get_property_list():
			if String(candidate.name) == String(key) and (int(candidate.usage) & PROPERTY_USAGE_STORAGE or key in ["position", "rotation_degrees", "scale"]):
				info = candidate
				break
		if info.is_empty():
			errors.append("No stored/exported property %s on %s" % [key, object.get_class()])
			return
		var value = properties[key]
		if value is Dictionary:
			if value.has("resource"):
				value = objects.get(String(value.resource))
			elif value.has("path"):
				value = ResourceLoader.load(String(value.path))
			else:
				value = null
			if not value is Resource:
				errors.append("Resource reference does not resolve: " + key)
				return
		var type := int(info.type)
		if type in [TYPE_VECTOR2, TYPE_VECTOR2I, TYPE_VECTOR3, TYPE_VECTOR3I, TYPE_COLOR]:
			var size := 4 if type == TYPE_COLOR else (2 if type in [TYPE_VECTOR2, TYPE_VECTOR2I] else 3)
			if not value is Array or value.size() != size:
				errors.append("Incorrect vector/color size for " + key)
				return
			for component in value:
				if not (component is float or component is int) or not is_finite(float(component)):
					errors.append("Vector/color components must be finite numbers")
					return
			match type:
				TYPE_VECTOR2: value = Vector2(value[0], value[1])
				TYPE_VECTOR2I: value = Vector2i(value[0], value[1])
				TYPE_VECTOR3: value = Vector3(value[0], value[1], value[2])
				TYPE_VECTOR3I: value = Vector3i(value[0], value[1], value[2])
				TYPE_COLOR: value = Color(value[0], value[1], value[2], value[3])
		elif type == TYPE_OBJECT:
			if not value is Resource:
				errors.append("Object properties require an explicit resource reference")
				return
		elif type == TYPE_FLOAT:
			if not (value is int or value is float):
				errors.append("Expected number for " + key)
				return
		elif type == TYPE_INT:
			if not (value is int or (value is float and value == floor(value))):
				errors.append("Expected integer for " + key)
				return
		elif type == TYPE_BOOL and not value is bool:
			errors.append("Expected boolean for " + key)
			return
		elif type in [TYPE_STRING, TYPE_STRING_NAME, TYPE_NODE_PATH] and not value is String:
			errors.append("Expected string for " + key)
			return
		elif type not in [TYPE_NIL, TYPE_BOOL, TYPE_INT, TYPE_FLOAT, TYPE_STRING, TYPE_STRING_NAME, TYPE_NODE_PATH, TYPE_OBJECT]:
			errors.append("Property type is not supported: " + key)
			return
		object.set(key, value)


func save_staged(directory: String) -> Dictionary:
	if not errors.is_empty() or subject == null:
		return {"success": false, "errors": errors}
	var resource: Resource
	var suffix := "tres"
	if subject is Node:
		var packed := PackedScene.new()
		if packed.pack(subject) != OK:
			return {"success": false, "errors": ["PackedScene.pack failed"]}
		resource = packed
		suffix = "tscn"
	else:
		resource = subject
	var path := directory.path_join("authored." + suffix)
	if ResourceSaver.save(resource, path) != OK:
		return {"success": false, "errors": ["ResourceSaver.save failed"]}
	var restored = ResourceLoader.load(path, "", ResourceLoader.CACHE_MODE_IGNORE)
	if restored == null:
		return {"success": false, "errors": ["Saved resource failed round-trip load"]}
	if restored is PackedScene:
		var instance: Node = restored.instantiate()
		if instance == null:
			return {"success": false, "errors": ["Saved scene failed round-trip instantiation"]}
		instance.free()
	return {"success": true, "staged_path": path, "operations": changes.size(),
		"changes": changes, "resource_type": resource.get_class()}


func release() -> void:
	if is_instance_valid(subject) and subject is Node:
		subject.free()
	objects.clear()
	subject = null
