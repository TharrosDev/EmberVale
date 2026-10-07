# Facing probe: does the body face the way it is going, and stay inside its own capsule, at every
# gait?
#
# ⚠️ THE DEFECT THIS EXISTS FOR WAS REPORTED AS A CAMERA BUG. "In third person the character faces the
# camera when idle or moving and only faces away when running." Nothing was wrong with the camera. The
# shared library's idle is a look-around whose hips rest about 40 degrees off forward, its loops carry
# the hips metres away from the capsule and snap them back, and the blend points sat at speeds nobody
# moves at, so a walking body was mostly that idle. Each of those is invisible in a clip list and in
# every other probe: the clips resolve, the legs swing, the tree blends.
#
# So this measures the two things a person looking at the screen would: with the body built the way
# PlayerFactory builds it and driven through the real animation component, the CHEST points where the
# body is facing (within 20 degrees, on average over a stride), and the HIPS stay over the capsule
# (within 0.3 m). At idle, a walk, a jog, a sprint, both strafes and a backpedal.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/facing_probe.gd
extends SceneTree

const BODY := "res://assets/models/characters/chr_player_base.glb"
const GAIT := "parameters/StateMachine/locomotion/blend_position"

# The actor built here has no LocomotionComponent, so its gaits are measured against
# LocomotionBlend.FallbackRunSpeed. Mirrors that constant.
const RUN_SPEED := 3.6

# How far the chest's average facing may be from the body's (degrees), and how far the hips may sit
# from the capsule's axis (metres).
const MAX_CHEST_OFF := 20.0
const MAX_HIPS_OFF := 0.3

# Seconds each gait is watched for. Longer than any loop in the library, so a whole stride is seen.
const WATCH_SECONDS := 1.5

# [label, strafe gait, forward gait]: the body's sideways and forward speed over its run speed.
const GAITS := [
	["idle", 0.0, 0.0],
	["walk", 0.0, 0.45],
	["jog", 0.0, 1.0],
	["sprint", 0.0, 1.6],
	["strafe left", -1.0, 0.0],
	["strafe right", 1.0, 0.0],
	["backpedal", 0.0, -0.45],
]

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame

	var body := await _build()
	var skeletons := body.find_children("*", "Skeleton3D", true, false)
	var tree: AnimationTree = null
	for child in body.get_node("Animation").get_children():
		if child is AnimationTree:
			tree = child

	if skeletons.is_empty():
		_failures.append("chr_player_base has no Skeleton3D")
	elif tree == null:
		_failures.append("no AnimationTree was built on chr_player_base — it fell back to the ladder")
	else:
		var sk: Skeleton3D = skeletons[0]
		var chest := _first_bone(sk, ["Chest", "UpperChest", "Spine"])
		var hips := sk.find_bone("Hips")
		if chest < 0 or hips < 0:
			_failures.append("chr_player_base has no chest/hips bone to measure")
		else:
			print("measuring %s and Hips on %s" % [sk.get_bone_name(chest), BODY.get_file()])
			for gait in GAITS:
				await _check_gait(body, tree, sk, chest, hips, gait[0], gait[1], gait[2])

	print("---")
	if _failures.is_empty():
		print("PASS: the body faces the way it is going and stays in its capsule at every gait")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


# The player's body as PlayerFactory makes it: the model turned half a turn under the body, because
# glTF's forward is +Z and Godot's is -Z. That turn is half of what is being tested, so it is not
# left out the way the tree probe leaves it out.
func _build() -> CharacterBody3D:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = "Walker"
	var shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.4
	capsule.height = 1.8
	shape.shape = capsule
	shape.position = Vector3(0, 0.9, 0)
	body.add_child(shape)

	var visual: Node3D = (load(BODY) as PackedScene).instantiate()
	visual.name = "BodyMesh"
	visual.rotate_y(PI)
	body.add_child(visual)

	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	body.add_child(stats)
	var combat = load("res://src/Combat/CombatComponent.cs").new()
	combat.name = "Combat"
	combat.Team = 1
	body.add_child(combat)
	var animation = load("res://src/Animation/CharacterAnimationComponent.cs").new()
	animation.name = "Animation"
	body.add_child(animation)

	root.add_child(body)
	await process_frame
	await process_frame
	return body


func _first_bone(sk: Skeleton3D, names: Array) -> int:
	for bone_name in names:
		var bone := sk.find_bone(bone_name)
		if bone >= 0:
			return bone
	return -1


# One gait: set the body's velocity (the animation component turns it into the blend position), let
# the blend settle, then watch a stride in real time.
#
# ⚠️ REAL elapsed time, not a frame count. Headless Godot runs its idle loop uncapped, so a frame is
# not 1/60 s; the tree ticks on real delta and so does this.
func _check_gait(body: CharacterBody3D, tree: AnimationTree, sk: Skeleton3D, chest: int, hips: int,
		label: String, strafe: float, forward: float) -> void:
	# The body's own axes: -Z is forward, +X is right.
	var basis := body.global_basis
	body.velocity = (basis.x * strafe - basis.z * forward) * RUN_SPEED

	var started := Time.get_ticks_usec()
	while (Time.get_ticks_usec() - started) / 1000000.0 < 0.3:
		await process_frame

	var sent: Vector2 = tree.get(GAIT)
	if absf(sent.x - strafe) > 0.03 or absf(sent.y - forward) > 0.03:
		_failures.append("%s: the blend sits at (%.2f, %.2f), not (%.2f, %.2f) — the gait is not reaching its clip"
			% [label, sent.x, sent.y, strafe, forward])

	var body_forward := -body.global_basis.z
	var body_yaw := atan2(body_forward.x, body_forward.z)
	var chest_rest_inverse: Basis = sk.get_bone_global_rest(chest).basis.orthonormalized().inverse()
	var here := Vector2(body.global_position.x, body.global_position.z)

	var sum_sin := 0.0
	var sum_cos := 0.0
	var worst_chest := 0.0
	var worst_hips := 0.0
	var samples := 0
	started = Time.get_ticks_usec()
	while (Time.get_ticks_usec() - started) / 1000000.0 < WATCH_SECONDS:
		await process_frame
		# The chest's facing: the turn its pose has taken from rest, carried through the skeleton's
		# own placement in the world. At rest the rig faces its +Z.
		var turned: Basis = sk.global_transform.basis.orthonormalized() \
			* sk.get_bone_global_pose(chest).basis.orthonormalized() * chest_rest_inverse
		var facing: Vector3 = turned * Vector3(0.0, 0.0, 1.0)
		var off := wrapf(atan2(facing.x, facing.z) - body_yaw, -PI, PI)
		sum_sin += sin(off)
		sum_cos += cos(off)
		worst_chest = maxf(worst_chest, absf(off))

		var at: Vector3 = sk.global_transform * sk.get_bone_global_pose(hips).origin
		worst_hips = maxf(worst_hips, Vector2(at.x, at.z).distance_to(here))
		samples += 1

	if samples == 0:
		_failures.append("%s: no frames were sampled" % label)
		return

	var mean_off := rad_to_deg(atan2(sum_sin, sum_cos))
	print("  %-13s chest %+6.1f deg off the body's facing on average (%.1f at most); hips at most %.3f m from the capsule; %d samples"
		% [label, mean_off, rad_to_deg(worst_chest), worst_hips, samples])

	if absf(mean_off) > MAX_CHEST_OFF:
		_failures.append("%s: the chest faces %+.1f deg away from the body's facing (limit %.0f deg) — the character is not facing where it is going"
			% [label, mean_off, MAX_CHEST_OFF])
	if worst_hips > MAX_HIPS_OFF:
		_failures.append("%s: the hips reach %.3f m from the capsule (limit %.2f m) — the clip is carrying the body off its own collider"
			% [label, worst_hips, MAX_HIPS_OFF])
