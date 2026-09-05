# Ranged probe: does a bow shot leave on the animation's release frame, fly, hit an enemy once, and
# refuse to hit a friend — and does it survive a target moving fast enough to tunnel through?
#
# ⚠️ THE TUNNELLING CASE IS THE ONE THAT CANNOT BE SEEN ANY OTHER WAY. An arrow at 42 m/s covers
# 0.7 m in a physics frame, comfortably more than its own 0.12 m radius, so a projectile that moves
# once per frame and then tests overlaps passes clean through a body between frames. The shot simply
# misses, occasionally, and it looks like unreliable hit detection rather than a bug. Arrow sub-steps
# its flight for exactly this reason; this is what proves the sub-stepping is on.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/ranged_probe.gd
extends SceneTree

const BOW := "res://data/weapons/HuntingBow.tres"
const HEALTH := 0
const STEP := 1.0 / 60.0

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame
	await _shot_at(2, "enemy", true)
	await _shot_at(1, "ally", false)
	await _tunnel_case()
	print("---")
	if _failures.is_empty():
		print("PASS: arrows fly on the release frame, hit enemies once, and pass allies")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _actor(actor_name: String, team: int, at: Vector3, hurt: bool) -> Array:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = actor_name
	body.position = at
	var shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.4
	capsule.height = 1.8
	shape.shape = capsule
	shape.position = Vector3(0, 0.9, 0)
	body.add_child(shape)
	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	body.add_child(stats)
	var combat = load("res://src/Combat/CombatComponent.cs").new()
	combat.name = "Combat"
	combat.Team = team
	body.add_child(combat)
	if hurt:
		var hurtbox := Area3D.new()
		hurtbox.set_script(load("res://src/Combat/Hurtbox.cs"))
		hurtbox.name = "Hurtbox"
		var zone := CollisionShape3D.new()
		var zc := CapsuleShape3D.new()
		zc.radius = 0.4
		zc.height = 1.8
		zone.shape = zc
		zone.position = Vector3(0, 0.9, 0)
		hurtbox.add_child(zone)
		body.add_child(hurtbox)
	root.add_child(body)
	return [body, stats]


func _archer(at: Vector3) -> Array:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = "Archer"
	body.position = at
	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	body.add_child(stats)
	var combat = load("res://src/Combat/CombatComponent.cs").new()
	combat.name = "Combat"
	combat.Team = 1
	body.add_child(combat)
	var action = load("res://src/Combat/Actions/CharacterActionComponent.cs").new()
	action.name = "Action"
	action.Weapon = load(BOW)
	body.add_child(action)
	root.add_child(body)
	return [body, action]


# Fires one arrow at a target of the given team and reports whether it took damage.
func _shot_at(team: int, label: String, expect_hit: bool) -> void:
	var offset := Vector3(team * 200, 0, 0)
	var parts := _archer(offset)
	var archer: CharacterBody3D = parts[0]
	var action = parts[1]
	var target_parts := _actor("Target", team, offset + Vector3(0, 0, -12), true)
	var target: CharacterBody3D = target_parts[0]
	var stats = target_parts[1]
	await physics_frame

	action.AimAt(target.global_position + Vector3(0, 1.0, 0))
	var before: float = stats.GetCurrent(HEALTH)
	if not action.TryAttack():
		_failures.append("%s: the bow did not draw" % label)
		return

	# The loose action runs 1.35 s and releases at 0.64 of it (~0.86 s); the arrow then needs ~0.3 s
	# to cover 12 m at 42 m/s. 150 frames is comfortably both.
	var hits := 0
	var last: float = before
	var released_at := -1.0
	var elapsed := 0.0
	for i in 150:
		await physics_frame
		elapsed += STEP
		var now: float = stats.GetCurrent(HEALTH)
		if now < last:
			hits += 1
			if released_at < 0.0:
				released_at = elapsed
			last = now

	var dealt: float = before - stats.GetCurrent(HEALTH)
	print("%-6s: dealt %5.1f in %d hit(s), first at %.2fs" % [label, dealt, hits, released_at])

	if expect_hit:
		if dealt <= 0.0:
			_failures.append("%s: the arrow dealt no damage" % label)
		if hits > 1:
			_failures.append("%s: one arrow hit %d times" % [label, hits])
		# The arrow cannot land before the action releases it. A bow that fired on key-down would
		# land at ~0.3 s; releasing at 0.86 s means the earliest possible hit is well past that.
		if released_at >= 0.0 and released_at < 0.7:
			_failures.append("%s: the arrow landed at %.2fs, before the draw finished at 0.86s — it is not waiting for the release frame"
				% [label, released_at])
	elif dealt > 0.0:
		_failures.append("%s: the arrow hit a friendly target for %.1f" % [label, dealt])

	archer.queue_free()
	target.queue_free()
	await process_frame


# A target crossing the arrow's path fast enough that a single-step projectile would miss it.
func _tunnel_case() -> void:
	var offset := Vector3(600, 0, 0)
	var parts := _archer(offset)
	var archer: CharacterBody3D = parts[0]
	var action = parts[1]
	var target_parts := _actor("Runner", 2, offset + Vector3(0, 0, -14), true)
	var target: CharacterBody3D = target_parts[0]
	var stats = target_parts[1]
	await physics_frame

	action.AimAt(target.global_position + Vector3(0, 1.0, 0))
	var before: float = stats.GetCurrent(HEALTH)
	action.TryAttack()
	for i in 150:
		await physics_frame

	var dealt: float = before - stats.GetCurrent(HEALTH)
	print("tunnel: dealt %.1f against a 0.8 m-wide body at 42 m/s (0.70 m per frame)" % dealt)
	if dealt <= 0.0:
		_failures.append("the arrow passed through a stationary body — it is not sub-stepping its flight")

	archer.queue_free()
	target.queue_free()
	await process_frame
