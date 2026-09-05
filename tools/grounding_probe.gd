# Grounding probe: do the feet meet real sloped ground, and can a warping attack go through a wall?
#
# Both claims need a physics world and a posed skeleton, so neither is reachable from the unit
# suite — FootPlacementTests and MotionWarpTests pin the arithmetic, and this proves it against
# actual collision.
#
# ⚠️ THE WALL CASE IS THE ONE THAT MATTERS. A warp that assigns the actor's position instead of
# sweeping it puts a lunging enemy inside whatever the player was hiding behind, and the symptom is
# an enemy standing in the rock with its sword through the wall. It is the difference between
# MoveAndCollide and a transform write, and nothing else in the codebase would catch it.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/grounding_probe.gd
extends SceneTree

const BODY := "res://assets/models/characters/chr_player_base.glb"
const WEAPON := "res://data/weapons/IronKingMaul.tres"

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame
	await _check_feet_meet_a_slope()
	# ⚠️ THE CONTROL RUNS FIRST, AND IT HAS TO. Without it, "the attacker did not pass the wall"
	# passes trivially when the warp never fired at all — which is exactly what the first version of
	# this probe reported, because the chain never reached the link that warps.
	await _check_a_warp_actually_closes()
	await _check_a_warp_cannot_pass_a_wall()
	print("---")
	if _failures.is_empty():
		print("PASS: feet meet the ground and a warp respects walls")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _floor(size: Vector3, at: Vector3, tilt := 0.0) -> StaticBody3D:
	var body := StaticBody3D.new()
	body.collision_layer = 1          # CombatLayers.World
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = size
	shape.shape = box
	body.add_child(shape)
	body.position = at
	if tilt != 0.0:
		body.rotation = Vector3(0, 0, tilt)
	root.add_child(body)
	return body


# A character standing on a slope should have its feet at DIFFERENT heights, following the ground,
# rather than both at the height flat-ground animation put them.
func _check_feet_meet_a_slope() -> void:
	_floor(Vector3(20, 1, 20), Vector3(0, -0.5, 0), deg_to_rad(12.0))

	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = "Stander"
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
	var anim = load("res://src/Animation/CharacterAnimationComponent.cs").new()
	anim.name = "Animation"
	body.add_child(anim)
	var ik = load("res://src/Animation/FootIkComponent.cs").new()
	ik.name = "FootIk"
	body.add_child(ik)
	root.add_child(body)
	body.global_position = Vector3(0, 0.3, 0)

	for i in 60:
		await physics_frame
		body.velocity.y -= 0.3
		body.move_and_slide()

	var sk: Skeleton3D = body.find_children("*", "Skeleton3D", true, false)[0]
	var modifiers := sk.find_children("*", "SkeletonModifier3D", true, false)
	if modifiers.is_empty():
		_failures.append("no FootIk modifier was added to the skeleton — it would never run")
		return

	var lf := sk.find_bone("LeftFoot")
	var rf := sk.find_bone("RightFoot")
	var lp: float = (sk.global_transform * sk.get_bone_global_pose(lf)).origin.y
	var rp: float = (sk.global_transform * sk.get_bone_global_pose(rf)).origin.y
	print("on a 12 deg slope: left foot y=%.3f right foot y=%.3f (grounded=%s)"
		% [lp, rp, body.is_on_floor()])

	if absf(lp - rp) < 0.005:
		_failures.append("both feet sit at the same height on a 12 deg slope (%.3f) — the correction is not running"
			% lp)

	body.queue_free()
	await process_frame


# The control: with nothing in the way, a warping attack must actually close the gap.
func _check_a_warp_actually_closes() -> void:
	var parts := await _lunger(Vector3(80, 0, 60), false)
	var attacker: CharacterBody3D = parts[0]
	var moved: float = parts[1]
	print("warp control (no wall): closed %.3f m toward the target" % moved)
	if moved < 0.3:
		_failures.append("the warp closed only %.3f m with nothing in the way — it never fired, so the wall test below would prove nothing"
			% moved)
	# The slam authors MaxWarpDistance = 1.6. Anything much past that means the budget is being
	# spent per frame instead of per action, which turns a lunge into a chase.
	elif moved > 1.9:
		_failures.append("the warp closed %.3f m on a 1.6 m allowance — the distance budget is not per action"
			% moved)
	attacker.queue_free()
	await process_frame


# A warping attack aimed THROUGH a wall must stop at the wall.
func _check_a_warp_cannot_pass_a_wall() -> void:
	var parts := await _lunger(Vector3(0, 0, 0), true)
	var attacker: CharacterBody3D = parts[0]
	var moved: float = parts[1]
	var end_z: float = attacker.global_position.z
	print("warp into a wall: closed %.3f m, ended at z=%.3f (wall face at z=-0.8)" % [moved, end_z])
	if end_z < -0.75:
		_failures.append("the attacker reached z=%.3f, past the wall face at -0.8 — the warp is not swept"
			% end_z)
	attacker.queue_free()
	await process_frame


# Builds an attacker at `at`, optionally behind a wall, chains it into the Iron King's slam (the link
# that authors RootMotion = WarpToTarget) and returns how far it closed.
#
# ⚠️ The second press must land INSIDE the first link's combo window. Pressing after the first link
# finishes restarts the chain at link 1, which does not warp — the mistake that made the first
# version of this probe pass without testing anything.
func _lunger(at: Vector3, wall: bool) -> Array:
	_floor(Vector3(40, 1, 40), at + Vector3(0, -0.5, 0))
	if wall:
		_floor(Vector3(6, 4, 0.4), at + Vector3(0, 2, -1.0))

	var attacker := CharacterBody3D.new()
	attacker.set_script(load("res://src/Entities/CharacterEntity.cs"))
	attacker.name = "Lunger"
	var shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.4
	capsule.height = 1.8
	shape.shape = capsule
	shape.position = Vector3(0, 0.9, 0)
	attacker.add_child(shape)
	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	attacker.add_child(stats)
	var combat = load("res://src/Combat/CombatComponent.cs").new()
	combat.name = "Combat"
	combat.Team = 1
	attacker.add_child(combat)
	var hitbox := Area3D.new()
	hitbox.set_script(load("res://src/Combat/Hitbox.cs"))
	hitbox.name = "MeleeHitbox"
	var arc := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(1, 1.4, 1.6)
	arc.shape = box
	hitbox.add_child(arc)
	attacker.add_child(hitbox)
	var action = load("res://src/Combat/Actions/CharacterActionComponent.cs").new()
	action.name = "Action"
	action.Weapon = load(WEAPON)
	action.Hitbox = hitbox
	attacker.add_child(action)
	root.add_child(attacker)
	attacker.global_position = at

	var target := Node3D.new()
	root.add_child(target)
	target.global_position = at + Vector3(0, 0, -5)
	action.WarpTarget = target

	await physics_frame
	action.TryAttack()
	# The sweep runs 2.071 s and its combo window opens at 0.52 of that (~1.08 s). 70 frames is
	# comfortably inside it.
	for i in 70:
		await physics_frame
	action.TryAttack()
	await physics_frame

	var started: float = attacker.global_position.z
	for i in 220:
		await physics_frame

	var closed: float = started - attacker.global_position.z
	target.queue_free()
	return [attacker, closed]
