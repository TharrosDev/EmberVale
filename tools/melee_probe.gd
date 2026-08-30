# Melee window probe: does a swing still open its hitbox, land damage, and re-arm for the next swing
# now that Hitbox and MeleeWeaponComponent park their physics callbacks while idle (the 2026-08-30
# debugging pass)?
#
# It exists because that gating is the one change in that pass a unit test cannot reach: the suite is
# pure logic, and neither an Area3D overlap nor SetPhysicsProcess exists outside a running engine.
#
# ⚠️ Actors are built by hand rather than through EnemyFactory: a C# static method is not reachable
# through a loaded Script object from GDScript, and calling one silently strands _initialize.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/melee_probe.gd
# Exits 0 when two consecutive swings each land damage, 1 otherwise.
extends SceneTree

const WEAPON := "res://data/weapons/IronSword.tres"
const SWING_FRAMES := 120   # 2 s at 60 Hz — longer than any authored windup + active + recovery
const HEALTH := 0           # StatType.Health

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame

	# ⚠️ Entity.GetComponent<T> is generic and therefore unreachable from GDScript, so the stats
	# component is kept from construction rather than looked up.
	var attacker_parts := _actor("Attacker", 1, Vector3(0, 0, 1.2))
	var target_parts := _actor("Target", 2, Vector3.ZERO)
	var attacker: CharacterBody3D = attacker_parts[0]
	var target: CharacterBody3D = target_parts[0]
	var stats = target_parts[1]

	var hitbox := Area3D.new()
	hitbox.set_script(load("res://src/Combat/Hitbox.cs"))
	hitbox.name = "MeleeHitbox"
	hitbox.position = Vector3(0, 1.0, -1.0)
	var arc := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(1.0, 1.4, 1.6)
	arc.shape = box
	hitbox.add_child(arc)
	attacker.add_child(hitbox)

	var weapon = load("res://src/Combat/MeleeWeaponComponent.cs").new()
	weapon.name = "Weapon"
	weapon.Weapon = load(WEAPON)
	weapon.Hitbox = hitbox
	attacker.add_child(weapon)

	var hurtbox := Area3D.new()
	hurtbox.set_script(load("res://src/Combat/Hurtbox.cs"))
	hurtbox.name = "Hurtbox"
	var zone := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.4
	capsule.height = 1.8
	zone.shape = capsule
	zone.position = Vector3(0, 0.9, 0)
	hurtbox.add_child(zone)
	target.add_child(hurtbox)

	root.add_child(attacker)
	root.add_child(target)
	await process_frame
	await physics_frame

	if stats == null or stats.GetCurrent(HEALTH) <= 0.0:
		_failures.append("target has no health; the probe would prove nothing")
	else:
		await _swing(weapon, stats, "first")
		await _swing(weapon, stats, "second")

	if _failures.is_empty():
		print("PASS: two swings, both landing, with the hitbox parked in between")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _actor(actor_name: String, team: int, at: Vector3) -> Array:
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
	return [body, stats]


func _swing(weapon, stats, label: String) -> void:
	var before: float = stats.GetCurrent(HEALTH)
	if not weapon.TryAttack():
		_failures.append("%s swing did not start" % label)
		return
	for _f in SWING_FRAMES:
		await physics_frame
	var after: float = stats.GetCurrent(HEALTH)
	print("%-7s swing: health %.1f -> %.1f (dealt %.1f)" % [label, before, after, before - after])
	if before - after <= 0.0:
		_failures.append("%s swing dealt no damage — the hitbox never opened" % label)
