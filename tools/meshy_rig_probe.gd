extends SceneTree

# Verifies a Meshy-adopted model imported the way the animation spine needs.
#
# CharacterAnimationComponent.AddSharedLibrary only attaches the 46-clip anim_library.res when the
# Skeleton3D is literally named "GeneralSkeleton" -- i.e. the retarget ran. When it does not, the
# actor T-poses with no log and no error, which is why this check exists as a gate rather than a
# spot check. Also reports the rest-pose AABB, because a model swap does not inherit its
# predecessor's capsule (CLAUDE.md 12).
#
#   godot --headless --path . --script res://tools/meshy_rig_probe.gd -- --asset res://path.glb ...

const REQUIRED_BONES := [
	"Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
	"LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
	"RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
	"LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
	"RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
]

func _initialize() -> void:
	var assets: Array[String] = []
	var args := OS.get_cmdline_user_args()
	for i in range(args.size() - 1):
		if args[i] == "--asset": assets.append(args[i + 1])
	if assets.is_empty():
		push_error("no --asset given"); quit(2); return

	var failed := 0
	for path in assets:
		var packed := ResourceLoader.load(path) as PackedScene
		if packed == null:
			print("FAIL  %s  cannot load" % path); failed += 1; continue
		var root := packed.instantiate()
		var skeleton := _find_skeleton(root)
		if skeleton == null:
			print("FAIL  %s  no Skeleton3D" % path); failed += 1; root.free(); continue

		var bones := {}
		for i in skeleton.get_bone_count(): bones[skeleton.get_bone_name(i)] = true
		var missing := REQUIRED_BONES.filter(func(b): return not bones.has(b))
		var clips: Array = []
		var player := _find_player(root)
		if player != null: clips = player.get_animation_list()

		var aabb := _rest_aabb(root)
		var head_y := _bone_rest_y(skeleton, "Head")
		var hips_y := _bone_rest_y(skeleton, "Hips")
		var toes_y := _bone_rest_y(skeleton, "LeftToes")
		var retargeted: bool = str(skeleton.name) == "GeneralSkeleton"
		var ok: bool = retargeted and missing.is_empty()
		if not ok: failed += 1
		print("%s  %s" % ["PASS " if ok else "FAIL ", path.get_file()])
		print("      skeleton=%s  bones=%d  retargeted=%s" % [skeleton.name, skeleton.get_bone_count(), retargeted])
		print("      skinned_aabb_h=%.3fm (inflated, advisory)  bone_head_y=%.3f  hips_y=%.3f  toes_y=%.3f" % [aabb.size.y, head_y, hips_y, toes_y])
		print("      clips=%s" % [clips])
		if not missing.is_empty(): print("      MISSING PROFILE BONES: %s" % [missing])
		root.free()

	quit(1 if failed > 0 else 0)

func _find_skeleton(node: Node) -> Skeleton3D:
	if node is Skeleton3D: return node
	for child in node.get_children():
		var found := _find_skeleton(child)
		if found != null: return found
	return null

func _find_player(node: Node) -> AnimationPlayer:
	if node is AnimationPlayer: return node
	for child in node.get_children():
		var found := _find_player(child)
		if found != null: return found
	return null

func _rest_aabb(node: Node) -> AABB:
	var total := AABB()
	var first := true
	for mesh in _meshes(node):
		var box: AABB = mesh.global_transform * mesh.get_aabb()
		if first: total = box; first = false
		else: total = total.merge(box)
	return total

func _meshes(node: Node) -> Array:
	var found := []
	if node is MeshInstance3D: found.append(node)
	for child in node.get_children(): found += _meshes(child)
	return found

# Bone rest positions are exact. A skinned MeshInstance3D's get_aabb() is inflated by Godot and is
# marked bounds_reliable:false by the 3D audit, so capsule and eye-height numbers come from here.
func _bone_rest_y(skeleton: Skeleton3D, bone: String) -> float:
	var index := skeleton.find_bone(bone)
	if index < 0: return -1.0
	return (skeleton.get_bone_global_rest(index)).origin.y
