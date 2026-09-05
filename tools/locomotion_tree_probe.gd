# AnimationTree probe: does the tree build on the real cast, does speed BLEND rather than snap, and
# does an action still get an honest clock through it?
#
# ⚠️ THE THIRD QUESTION IS THE ONE THAT MATTERS. An active AnimationTree drives the AnimationPlayer,
# so AnimationPlayer.CurrentAnimation and CurrentAnimationPosition stop tracking what is on screen.
# Stage 1 made the animation the clock by reading exactly those; if the tree had been dropped in
# without moving that read onto the tree's own playback, every action would have silently fallen back
# to its timer and the whole overhaul would have quietly undone itself. Nothing would log. This is
# the check that says it did not happen.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/locomotion_tree_probe.gd
extends SceneTree

const BODY := "res://assets/models/characters/chr_player_base.glb"
const WEAPON := "res://data/weapons/IronSword.tres"
const HEALTH := 0
const STEP := 1.0 / 60.0

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame

	var parts := await _build()
	var animation = parts[0]
	var action = parts[1]
	var body: CharacterBody3D = parts[2]

	var tree: AnimationTree = null
	for child in animation.get_children():
		if child is AnimationTree:
			tree = child
	if tree == null:
		_failures.append("no AnimationTree was built on chr_player_base — it fell back to the ladder")
	else:
		print("tree built and active: %s" % tree.active)
		await _check_blend(tree, body)
		await _check_upper_body_layer(tree)
		await _check_action_clock(tree, animation, action)

	print("---")
	if _failures.is_empty():
		print("PASS: the locomotion tree blends, layers, and keeps the action clock honest")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _build() -> Array:
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

	var visual := Node3D.new()
	visual.name = "BodyMesh"
	visual.add_child((load(BODY) as PackedScene).instantiate())
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

	var hitbox := Area3D.new()
	hitbox.set_script(load("res://src/Combat/Hitbox.cs"))
	hitbox.name = "MeleeHitbox"
	var arc := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(1.0, 1.4, 1.6)
	arc.shape = box
	hitbox.add_child(arc)
	body.add_child(hitbox)

	var action = load("res://src/Combat/Actions/CharacterActionComponent.cs").new()
	action.name = "Action"
	action.Weapon = load(WEAPON)
	action.Hitbox = hitbox
	body.add_child(action)

	root.add_child(body)
	await process_frame
	await process_frame
	return [animation, action, body]


# Walking is a BLEND now, not a threshold. Sampling a leg bone across a speed ramp: if the space is
# interpolating, the pose changes continuously; if something is still switching clips at a threshold,
# it jumps once and sits still either side of it.
func _check_blend(tree: AnimationTree, body: CharacterBody3D) -> void:
	var sk: Skeleton3D = body.find_children("*", "Skeleton3D", true, false)[0]
	var bone := sk.find_bone("LeftUpperLeg")
	var poses := []
	for speed in [0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.5]:
		body.velocity = Vector3(0, 0, -speed)
		tree.set("parameters/StateMachine/locomotion/blend_position", speed)
		for i in 6:
			await process_frame
		poses.append(sk.get_bone_pose_rotation(bone))

	var moved := 0
	for i in range(1, poses.size()):
		if poses[i - 1].angle_to(poses[i]) > 0.001:
			moved += 1
	print("leg pose changed at %d of %d speed steps" % [moved, poses.size() - 1])
	if moved < poses.size() - 2:
		_failures.append("the blend space is not interpolating — the pose only changed at %d of %d steps"
			% [moved, poses.size() - 1])

	body.velocity = Vector3.ZERO


# The layer must move the ARMS and leave the LEGS to locomotion. A filter that matched nothing would
# override the whole body and this is the only thing that would say so.
func _check_upper_body_layer(tree: AnimationTree) -> void:
	var sk: Skeleton3D = tree.get_parent().get_parent().find_children("*", "Skeleton3D", true, false)[0]
	var arm := sk.find_bone("RightUpperArm")
	var leg := sk.find_bone("LeftUpperLeg")
	if arm < 0 or leg < 0:
		_failures.append("no arm/leg bone to test the layer against")
		return

	tree.set("parameters/StateMachine/locomotion/blend_position", 1.6)
	tree.set("parameters/Layer/blend_amount", 0.0)
	for i in 8:
		await process_frame
	var arm_off: Quaternion = sk.get_bone_pose_rotation(arm)
	var leg_off: Quaternion = sk.get_bone_pose_rotation(leg)

	tree.set("parameters/Layer/blend_amount", 1.0)
	for i in 8:
		await process_frame
	var arm_on: Quaternion = sk.get_bone_pose_rotation(arm)
	var leg_on: Quaternion = sk.get_bone_pose_rotation(leg)

	var arm_delta := rad_to_deg(arm_off.angle_to(arm_on))
	var leg_delta := rad_to_deg(leg_off.angle_to(leg_on))
	print("upper-body layer moved the arm %.1f deg and the leg %.1f deg" % [arm_delta, leg_delta])

	if arm_delta < 1.0:
		_failures.append("the upper-body layer did not move the arm (%.2f deg) — its filter matched nothing"
			% arm_delta)
	if leg_delta > arm_delta:
		_failures.append("the upper-body layer moved the LEG more than the arm (%.1f vs %.1f deg) — the filter is not limiting it"
			% [leg_delta, arm_delta])


# Stage 1's claim, re-proved through the tree: the action's progress must come from the tree's own
# playback, and must still span the duration the action asked for.
func _check_action_clock(tree: AnimationTree, animation, action) -> void:
	for wanted in [0.8, 0.4]:
		var actual: float = animation.StartAction("attack1", wanted)
		if actual < 0.0:
			_failures.append("StartAction('attack1', %.2f) refused through the tree" % wanted)
			return
		if abs(actual - wanted) > 0.001:
			_failures.append("StartAction returned %.3fs for a %.3fs request" % [actual, wanted])

		# ⚠️ REAL elapsed time, not a frame count. Headless Godot runs its idle loop uncapped, so
		# `await process_frame` is NOT 1/60 s and counting frames measures nothing. The tree ticks on
		# real delta, so the probe has to as well. (melee_probe.gd can count frames because
		# physics_frame is fixed-step; this one cannot.)
		var progressed := []
		var started := Time.get_ticks_usec()
		var elapsed := 0.0
		while elapsed < wanted + 0.25:
			await process_frame
			elapsed = (Time.get_ticks_usec() - started) / 1000000.0
			var p: float = animation.ActionProgress
			if p >= 0.0:
				progressed.append(p)

		if progressed.is_empty():
			_failures.append("ActionProgress never reported a value through the tree — the clock is dead")
			return

		var first: float = progressed[0]
		var last: float = progressed[progressed.size() - 1]
		print("action %.2fs: progress ran %.2f -> %.2f over %.2fs real, %d samples"
			% [wanted, first, last, elapsed, progressed.size()])
		if last <= first:
			_failures.append("ActionProgress did not advance (%.3f -> %.3f)" % [first, last])
		if last < 0.75:
			_failures.append("ActionProgress only reached %.2f by the end of a %.2fs action — the clip is not spanning it"
				% [last, wanted])
		animation.StopAction()
		for i in 10:
			await process_frame
