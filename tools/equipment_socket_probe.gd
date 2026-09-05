# Socket-contract probe: does every socket resolve to a real bone on every real rig, and does a
# piece hung on one actually move with the body?
#
# ⚠️ THIS IS THE GATE THE OLD ATTACHMENT CODE NEVER HAD, AND ITS ABSENCE IS THE WHOLE POINT. Five
# separate implementations each guessed at bone names, and a miss was completely silent: the player's
# visual sword was QueueFree'd on every spawn for an entire phase because one call site knew only
# "RightHand" while every adopted body says "Wrist.R". Nothing logged. A sword that is not there
# looks exactly like a build that never had one.
#
# EquipmentSocketTests pins the alias table without an engine. This proves the table against the
# THIRTY-THREE ACTUAL RIGS on disk, which is the half a unit test structurally cannot reach.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/equipment_socket_probe.gd
# Exits 0 when every humanoid supports the humanoid sockets and a hung piece tracks its bone.
extends SceneTree

const MANIFEST := "res://assets/models/manifest.json"
const WEAPON_MODEL := "res://assets/models/weapons/wpn_sword_iron.glb"

# What a body that calls itself HUMANOID must be able to carry. Deliberately not the whole enum:
# a shield needs a forearm bone that not every pack rig has, and demanding it would fail bodies that
# are otherwise fine. These six are the ones gameplay actually hangs things on today.
const REQUIRED_HUMANOID := ["HandR", "HandL", "Head", "Chest", "Hips", "ShoulderL"]

var _failures: Array[String] = []
var _checked := 0


func _initialize() -> void:
	var manifest := _load_manifest()
	if manifest.is_empty():
		print("FAIL: could not read %s" % MANIFEST)
		quit(1)
		return

	for entry in manifest:
		if entry.get("type", "") != "HUMANOID":
			continue
		if entry.get("status", "active") != "active":
			continue
		await _check(entry.get("path", ""), entry.get("id", "?"))

	await _check_a_piece_actually_follows()

	print("---")
	print("checked %d humanoid rig(s)" % _checked)
	if _failures.is_empty():
		print("PASS: every humanoid rig carries the socket contract")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _load_manifest() -> Array:
	var text := FileAccess.get_file_as_string(MANIFEST)
	if text.is_empty():
		return []
	var parsed = JSON.parse_string(text)
	# The manifest is either a bare array or an object with a models/entries key; tolerate both
	# rather than pinning a shape this probe does not own.
	if parsed is Array:
		return parsed
	if parsed is Dictionary:
		for key in ["models", "entries", "assets"]:
			if parsed.has(key) and parsed[key] is Array:
				return parsed[key]
	return []


func _check(path: String, id: String) -> void:
	if path.is_empty() or not ResourceLoader.exists(path):
		_failures.append("%s: '%s' does not resolve" % [id, path])
		return

	var scene: PackedScene = load(path)
	if scene == null:
		_failures.append("%s: '%s' failed to load" % [id, path])
		return

	var instance := scene.instantiate()
	root.add_child(instance)
	await process_frame

	var skeleton := _find_skeleton(instance)
	if skeleton == null:
		_failures.append("%s: no Skeleton3D — the manifest calls it HUMANOID" % id)
		instance.queue_free()
		await process_frame
		return

	_checked += 1
	var missing: Array[String] = []
	var resolved: Array[String] = []
	for socket_name in REQUIRED_HUMANOID:
		var bone := _resolve(skeleton, socket_name)
		if bone.is_empty():
			missing.append(socket_name)
		else:
			resolved.append("%s->%s" % [socket_name, bone])

	if missing.is_empty():
		print("  ok   %-24s %s" % [id, ", ".join(resolved)])
	else:
		_failures.append("%s: sockets with no bone on this rig: %s" % [id, ", ".join(missing)])

	instance.queue_free()
	await process_frame


# ⚠️ GDScript cannot call EquipmentSockets.Resolve — it takes a C# enum and lives on a static class,
# neither of which marshals. The candidate order below is therefore a DELIBERATE MIRROR of
# EquipmentSockets.Bindings, and EquipmentSocketTests is what keeps the C# side honest. If the two
# ever disagree this probe passes a contract the game does not use, so keep them together.
const CANDIDATES := {
	"HandR": ["RightHand", "Wrist.R", "Hand.R", "Hand_R", "mixamorig_RightHand"],
	"HandL": ["LeftHand", "Wrist.L", "Hand.L", "Hand_L", "mixamorig_LeftHand"],
	"Head": ["Head", "Skull"],
	"Chest": ["Chest", "UpperChest", "Spine", "Torso"],
	"Hips": ["Hips", "Pelvis", "Torso"],
	"ShoulderL": ["LeftUpperArm", "LeftArm", "UpperArm.L", "Arm.L"],
	"ShoulderR": ["RightUpperArm", "RightArm", "UpperArm.R", "Arm.R"],
}


func _resolve(skeleton: Skeleton3D, socket_name: String) -> String:
	var candidates: Array = CANDIDATES.get(socket_name, [])
	for candidate in candidates:
		if skeleton.find_bone(candidate) >= 0:
			return candidate
	for candidate in candidates:
		var wanted := _normalize(candidate)
		for index in skeleton.get_bone_count():
			if _normalize(skeleton.get_bone_name(index)) == wanted:
				return skeleton.get_bone_name(index)
	return ""


func _normalize(value: String) -> String:
	return value.replace(".", "").replace("_", "").replace(":", "").replace("-", "").to_lower()


# Hanging a weapon on the hand and confirming it is somewhere near the hand rather than at the
# world origin. The failure this catches is a mount that is built but never follows — which renders
# a sword lying on the ground under the character's feet.
func _check_a_piece_actually_follows() -> void:
	if not ResourceLoader.exists(WEAPON_MODEL):
		_failures.append("the weapon model '%s' does not resolve" % WEAPON_MODEL)
		return

	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = "Wielder"
	var visual := Node3D.new()
	visual.name = "BodyMesh"
	visual.add_child((load("res://assets/models/characters/chr_player_base.glb") as PackedScene).instantiate())
	body.add_child(visual)

	var presentation = load("res://src/Animation/EquipmentPresentationComponent.cs").new()
	presentation.name = "EquipmentVisuals"
	body.add_child(presentation)

	root.add_child(body)
	body.global_position = Vector3(7, 0, -3)
	await process_frame
	await process_frame

	if not presentation.HasRig:
		_failures.append("the presentation component found no rig on chr_player_base")
		body.queue_free()
		await process_frame
		return

	var hand_bone: String = presentation.BoneFor(0)   # EquipmentSocket.HandR
	print("  hand socket resolves to '%s'" % hand_bone)
	if hand_bone.is_empty():
		_failures.append("HandR resolved to nothing on chr_player_base")
		body.queue_free()
		await process_frame
		return

	var skeleton := _find_skeleton(visual)
	var attached = presentation.AttachSimple(0, WEAPON_MODEL, "ProbeSword")
	await process_frame
	await process_frame

	if attached == null:
		_failures.append("attaching a weapon to HandR returned nothing")
	else:
		var hand_pos: Vector3 = (skeleton.global_transform * skeleton.get_bone_global_pose(
			skeleton.find_bone(hand_bone))).origin
		var weapon_pos: Vector3 = attached.global_position
		var away: float = hand_pos.distance_to(weapon_pos)
		print("  weapon sits %.3f m from the hand bone (body at %s)" % [away, body.global_position])
		if away > 0.35:
			_failures.append(
				"the attached weapon is %.2f m from the hand bone — the socket is not following it" % away)
		if weapon_pos.distance_to(Vector3.ZERO) < 1.0:
			_failures.append("the attached weapon is at the world origin, not on the body")

	body.queue_free()
	await process_frame


func _find_skeleton(node: Node) -> Skeleton3D:
	if node is Skeleton3D:
		return node
	for child in node.get_children():
		var found := _find_skeleton(child)
		if found != null:
			return found
	return null
