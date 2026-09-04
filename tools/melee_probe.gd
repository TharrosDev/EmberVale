# Action-timeline probe: does a swing open its hitbox *inside its own active window*, land damage
# once, park itself, and re-arm — and does the window MOVE when the authored action shape moves?
#
# It exists because that is the one claim of the 2026-09-04 combat/animation overhaul a unit test
# cannot reach. ActionTimelineTests pins the arithmetic; only a running engine has an Area3D overlap,
# a physics frame and SetPhysicsProcess gating, and only here can the two be shown to agree.
#
# ⚠️ There is no AnimationPlayer on these hand-built actors, so this exercises the FALLBACK clock —
# which is exactly the path an unanimated body takes, and the one that must run the identical
# fractions. The clip-driven path is proved in the editor, not headless.
#
# ⚠️ Actors are built by hand rather than through EnemyFactory: a C# static method is not reachable
# through a loaded Script object from GDScript, and calling one silently strands _initialize.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/melee_probe.gd
# Exits 0 when every case below holds, 1 otherwise.
extends SceneTree

const HEALTH := 0           # StatType.Health
const STEP := 1.0 / 60.0

# Weapon, and the action shape its legacy timings synthesise. windup/active/recovery/attack_speed.
const CASES := [
	{
		"weapon": "res://data/weapons/IronSword.tres",
		"label": "iron sword",
		"windup": 0.15, "active": 0.12, "recovery": 0.28, "speed": 1.0,
	},
	{
		# Four times the wind-up and a 0.7 attack speed. Under the old stopwatch this and the sword
		# above played the SAME Sword_Slash at the SAME speed; the whole point of the rebuild is
		# that the hit window now sits where the authored shape says it does.
		"weapon": "res://data/weapons/IronKingMaul.tres",
		"label": "iron king maul",
		# The AUTHORED chain, not the synthesised one: link 1 is the lateral sweep, whose fractions
		# were deliberately authored to the shape the legacy timings produced so the fight did not
		# change feel the day it stopped being synthesised.
		"windup": 0.551, "active": 0.203, "recovery": 0.696, "speed": 0.7,
		# Link 2, reached only by chaining inside the combo window: the uninterruptible overhead
		# slam. 1.75x damage is the claim the chain test checks.
		"chain_damage_ratio": 1.75,
	},
]

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame

	for case in CASES:
		await _run_case(case)

	if _failures.is_empty():
		print("PASS: every swing opened inside its own active window and hit once")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _run_case(case: Dictionary) -> void:
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

	var action = load("res://src/Combat/Actions/CharacterActionComponent.cs").new()
	action.name = "Action"
	action.Weapon = load(case["weapon"])
	action.Hitbox = hitbox
	attacker.add_child(action)

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
		_failures.append("%s: target has no health; the probe would prove nothing" % case["label"])
	else:
		await _swing(action, stats, case, "first")
		await _swing(action, stats, case, "second")
		if case.has("chain_damage_ratio"):
			await _chain(action, stats, case)

	attacker.queue_free()
	target.queue_free()
	await process_frame


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


# Swings once, sampling health every physics frame, and checks WHEN the damage landed rather than
# only that it did. A hit outside [windup, windup+active] is the exact defect the overhaul exists to
# make impossible, and a second hit from one swing is the dedupe rule failing.
func _swing(action, stats, case: Dictionary, label: String) -> void:
	var speed: float = case["speed"]
	var total: float = (case["windup"] + case["active"] + case["recovery"]) / speed
	var open_at: float = case["windup"] / speed
	var close_at: float = (case["windup"] + case["active"]) / speed

	# One frame of slack at each edge: the hitbox polls overlaps on the physics step after Monitoring
	# is enabled (the documented Area3D timing gotcha), so the landing frame is allowed to be late.
	var earliest := open_at - STEP
	var latest := close_at + (2.0 * STEP)

	var before: float = stats.GetCurrent(HEALTH)
	if not action.TryAttack():
		_failures.append("%s %s swing did not start" % [case["label"], label])
		return

	var elapsed := 0.0
	var hits := 0
	var first_hit := -1.0
	var last: float = before
	var frames := int(ceil((total + 0.5) / STEP))
	for _f in frames:
		await physics_frame
		elapsed += STEP
		var now: float = stats.GetCurrent(HEALTH)
		if now < last:
			hits += 1
			if first_hit < 0.0:
				first_hit = elapsed
			last = now

	var dealt: float = before - stats.GetCurrent(HEALTH)
	print("%-16s %-6s: dealt %5.1f, landed at %.3fs (window %.3f..%.3f, action %.3fs)"
		% [case["label"], label, dealt, first_hit, open_at, close_at, total])

	if dealt <= 0.0:
		_failures.append("%s %s swing dealt no damage — the hitbox never opened"
			% [case["label"], label])
		return

	if hits != 1:
		_failures.append("%s %s swing hit the same target %d times; one swing is one hit"
			% [case["label"], label, hits])

	if first_hit < earliest or first_hit > latest:
		_failures.append("%s %s swing landed at %.3fs, outside its own active window %.3f..%.3f"
			% [case["label"], label, first_hit, earliest, latest])


# Presses a second attack INSIDE the combo window and checks the authored second link actually ran —
# a different, harder blow rather than the same one again. This is the only exercise of
# WeaponResource.Attacks (the authored chain); every other weapon still synthesises its chain from
# the legacy timings.
func _chain(action, stats, case: Dictionary) -> void:
	var speed: float = case["speed"]
	var link1_total: float = (case["windup"] + case["active"] + case["recovery"]) / speed
	# ⚠️ TOP THE TARGET UP FIRST. The two swings above have already taken most of its health, and a
	# dying target absorbs only what is left — which reads exactly like the chain failing to advance.
	# That is what this line is for and it cost a debugging round to find.
	stats.ModifyCurrent(HEALTH, 500.0)

	var link1_damage: float = 0.0
	var before: float = stats.GetCurrent(HEALTH)

	if not action.TryAttack():
		_failures.append("%s: chain link 1 did not start" % case["label"])
		return

	# Run to just past the end of the active window, then press again — that is inside the combo
	# window and out of commitment, which is exactly when a chain is supposed to be reachable.
	var until: float = ((case["windup"] + case["active"]) / speed) + (3.0 * STEP)
	var elapsed := 0.0
	while elapsed < until:
		await physics_frame
		elapsed += STEP
	link1_damage = before - stats.GetCurrent(HEALTH)

	if not action.TryAttack():
		_failures.append("%s: the combo window refused a chained press at %.3fs"
			% [case["label"], elapsed])
		return

	var mid: float = stats.GetCurrent(HEALTH)
	var frames := int(ceil((link1_total + 3.0) / STEP))
	for _f in frames:
		await physics_frame
	var link2_damage: float = mid - stats.GetCurrent(HEALTH)

	var ratio: float = link2_damage / link1_damage if link1_damage > 0.0 else 0.0
	print("%-16s chain : link1 %.1f, link2 %.1f (ratio %.2f, expected ~%.2f)"
		% [case["label"], link1_damage, link2_damage, ratio, case["chain_damage_ratio"]])

	if link1_damage <= 0.0:
		_failures.append("%s: chain link 1 dealt no damage" % case["label"])
	elif abs(ratio - case["chain_damage_ratio"]) > 0.35:
		# Wide tolerance: CombatMath.RollAttack rolls a crit, so this asserts the SECOND LINK RAN
		# rather than pinning a damage number.
		_failures.append("%s: chained link dealt %.2fx link 1, expected ~%.2fx — the authored chain did not advance"
			% [case["label"], ratio, case["chain_damage_ratio"]])
