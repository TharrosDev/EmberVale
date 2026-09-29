# Combat offence probe: the parts of the 2026-09 offence pass that only exist under real physics
# frames. OffenceTests pins the arithmetic (charge maths, tap/hold, the buffer's accept rule, the
# plunge's height scale); this drives the real CharacterActionComponent against real Areas and bodies.
#
#   charge     hold winds a charge (slowed, stamina drained), release swings a Heavy/Charged blow that
#              hits once, the committed tail is a punish window, a stagger/block drops the charge, and a
#              full charge is hyperarmoured while a light heavy is not
#   buffer     a press far from the cancel point is dropped, one near it fires the next link the frame it
#              can, and a stagger clears a queued press
#   direction  a forward/back attack steps the body along its facing during the wind-up
#   warp       a warping slam closes its 1.6 m allowance and turns TOWARD (not away from) a target off
#              to the side; a committed swing cannot be turned faster than its action allows; the flag
#              that switches that off works
#   plunge     a jump-attack dives, lands, hits with HitKind.Plunge carrying the drop, and is refused
#              on the ground or from under the minimum height
#   roll cut   the roll-attack action runs by id
#
# ⚠️ Actors are built by hand (a C# static factory is not reachable from GDScript) and there is no
# AnimationPlayer, so this exercises the FALLBACK clock, the same path an unanimated body takes. There
# is no LocomotionComponent either: the probe plays the motor (gravity + move_and_slide) itself where
# a case needs a body to fall.
#
# ⚠️ READ THE WHOLE OUTPUT FOR SCRIPT ERRORS. A GDScript error mid-function aborts that function and
# the probe can still print PASS. GDScript cannot assign a Vector3? property, hence AimAt elsewhere.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/combat_offence_probe.gd
# Exits 0 when every case holds, 1 otherwise.
extends SceneTree

const HEALTH := 0     # StatType.Health
const STAMINA := 1    # StatType.Stamina
const STEP := 1.0 / 60.0

const KIND_HEAVY := 1    # ActionKind.HeavyAttack / HitKind.Heavy
const HIT_CHARGED := 2   # HitKind.Charged
const HIT_PLUNGE := 5    # HitKind.Plunge

const SWORD := "res://data/weapons/IronSword.tres"
const MAUL := "res://data/weapons/IronKingMaul.tres"

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame

	await _check_charge()
	await _check_hyperarmor()
	await _check_buffer()
	await _check_direction()
	await _check_warp_and_turn()
	await _check_plunge()
	await _check_roll_cut()

	print("---")
	if _failures.is_empty():
		print("PASS: charge, buffer, direction, warp, turn-lock, plunge and roll-cut all hold under real frames")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


# ---- building blocks -----------------------------------------------------------------------------

func _fail(msg: String) -> void:
	_failures.append(msg)


func _frames(n: int) -> void:
	for i in n:
		await physics_frame


# Returns {body, stats, combat, action}. `plunge_volume` adds the ground-pound sphere the player's
# factory builds, which CharacterActionComponent finds by node name.
func _fighter(name_: String, team: int, at: Vector3, weapon: String, plunge_volume := false) -> Dictionary:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = name_
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

	var hitbox := Area3D.new()
	hitbox.set_script(load("res://src/Combat/Hitbox.cs"))
	hitbox.name = "MeleeHitbox"
	hitbox.position = Vector3(0, 1.0, -1.0)
	var arc := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(1.0, 1.4, 1.6)
	arc.shape = box
	hitbox.add_child(arc)
	body.add_child(hitbox)

	if plunge_volume:
		var pv := Area3D.new()
		pv.set_script(load("res://src/Combat/Hitbox.cs"))
		pv.name = "PlungeHitbox"
		pv.position = Vector3(0, 0.6, 0)
		var sphere := CollisionShape3D.new()
		var sp := SphereShape3D.new()
		sp.radius = 2.2
		sphere.shape = sp
		pv.add_child(sphere)
		body.add_child(pv)

	var action = load("res://src/Combat/Actions/CharacterActionComponent.cs").new()
	action.name = "Action"
	action.Weapon = load(weapon)
	action.Hitbox = hitbox
	body.add_child(action)

	root.add_child(body)
	return {"body": body, "stats": stats, "combat": combat, "action": action}


# A target that can be hurt: a body with health and a hurtbox.
func _dummy(at: Vector3) -> Dictionary:
	var parts := _fighter("Dummy", 2, at, SWORD)
	var body: CharacterBody3D = parts["body"]
	var hurt := Area3D.new()
	hurt.set_script(load("res://src/Combat/Hurtbox.cs"))
	hurt.name = "Hurtbox"
	var zone := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.4
	capsule.height = 1.8
	zone.shape = capsule
	zone.position = Vector3(0, 0.9, 0)
	hurt.add_child(zone)
	body.add_child(hurt)
	parts["stats"].ModifyCurrent(HEALTH, 5000.0)
	return parts


func _floor(size: Vector3, at: Vector3) -> StaticBody3D:
	var body := StaticBody3D.new()
	body.collision_layer = 1    # CombatLayers.WorldStatic
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = size
	shape.shape = box
	body.add_child(shape)
	body.position = at
	root.add_child(body)
	return body


func _free(parts: Array) -> void:
	for p in parts:
		if is_instance_valid(p):
			p.queue_free()


# ---- charge --------------------------------------------------------------------------------------

func _check_charge() -> void:
	var a := _fighter("Charger", 1, Vector3(0, 0, 0), SWORD)
	var d := _dummy(Vector3(0, 0, -1.2))
	var action = a["action"]
	var stats = a["stats"]
	var combat = a["combat"]
	var target_stats = d["stats"]
	await _frames(2)

	var before: float = stats.GetCurrent(STAMINA)
	if not action.BeginCharge():
		_fail("charge: BeginCharge refused with a full bar")
		_free([a["body"], d["body"]])
		return
	await _frames(30)

	var charge: float = action.Charge
	var drained: float = before - stats.GetCurrent(STAMINA)
	print("charge: held 0.5s -> charge %.2f, stamina drained %.1f, move scale %.2f"
		% [charge, drained, action.MoveScale])
	if not action.IsCharging:
		_fail("charge: dropped by itself while held")
	if charge < 0.4 or charge > 0.62:
		_fail("charge: 0.5s of holding reached %.2f, expected about 0.5" % charge)
	if drained < 1.0:
		_fail("charge: holding drained only %.2f stamina; a charge is not free" % drained)
	if absf(action.MoveScale - 0.4) > 0.01:
		_fail("charge: the charging body keeps %.2f of its movement, expected 0.4" % action.MoveScale)

	var health_before: float = target_stats.GetCurrent(HEALTH)
	if not action.ReleaseCharge():
		_fail("charge: ReleaseCharge did not swing")
		_free([a["body"], d["body"]])
		return
	if action.Current == null or action.Current.Kind != KIND_HEAVY:
		_fail("charge: the release ran '%s', expected the weapon's heavy"
			% (action.Current.Id if action.Current != null else "nothing"))

	var vulnerable_in_tail := false
	var hits := 0
	var last: float = health_before
	for f in 100:
		await physics_frame
		if action.InCommittedRecovery and combat.InWindup:
			vulnerable_in_tail = true
		var now: float = target_stats.GetCurrent(HEALTH)
		if now < last:
			hits += 1
			last = now

	var dealt: float = health_before - target_stats.GetCurrent(HEALTH)
	print("charge: released -> dealt %.1f in %d hit(s), kind %d, charge %.2f, damage x%.2f"
		% [dealt, hits, action.LastSwingKind, action.LastSwingCharge, action.LastSwingDamageMultiplier])
	if hits != 1:
		_fail("charge: the heavy hit %d times; one swing is one hit" % hits)
	if dealt < 25.0:
		_fail("charge: the charged heavy dealt only %.1f; a 14-damage sword at 2x scale with a charge bonus is far more"
			% dealt)
	if action.LastSwingKind != HIT_CHARGED:
		_fail("charge: the packet was stamped kind %d, expected Charged (%d)"
			% [action.LastSwingKind, HIT_CHARGED])
	if absf(action.LastSwingDamageMultiplier - 1.375) > 0.12:
		_fail("charge: damage multiplier %.3f, expected about 1.375 for a half charge"
			% action.LastSwingDamageMultiplier)
	if not vulnerable_in_tail:
		_fail("charge: the heavy's committed recovery never registered as a punish window")
	if action.Current != null:
		_fail("charge: the heavy had not finished 100 frames after release")

	# A charge drops on a stagger and on a raised guard — and never swings.
	stats.ModifyCurrent(STAMINA, 500.0)
	if not action.BeginCharge():
		_fail("charge: could not begin a second charge")
	else:
		combat.Stagger(0.2)
		await _frames(2)
		if action.IsCharging:
			_fail("charge: a stagger did not break the charge")
	await _frames(25)
	stats.ModifyCurrent(STAMINA, 500.0)
	if action.BeginCharge():
		combat.IsBlocking = true
		await _frames(2)
		if action.IsCharging:
			_fail("charge: raising the guard did not drop the charge")
		combat.IsBlocking = false

	# With no stamina a charge cannot start.
	stats.ModifyCurrent(STAMINA, -5000.0)
	if action.BeginCharge():
		_fail("charge: began a charge with an empty stamina bar")

	_free([a["body"], d["body"]])
	await process_frame


func _check_hyperarmor() -> void:
	# Two releases, one staggered a few frames into its wind-up: the full charge shrugs it off, the
	# short one is cancelled. This is the only place the stagger rule and the charge meet.
	for full in [true, false]:
		var a := _fighter("Armored", 1, Vector3(20, 0, 0), SWORD)
		var action = a["action"]
		var combat = a["combat"]
		await _frames(2)
		action.BeginCharge()
		await _frames(70 if full else 15)
		action.ReleaseCharge()
		await _frames(3)
		combat.Stagger(0.4)
		await _frames(3)
		var still_swinging: bool = action.Current != null
		print("hyperarmor: %s charge, staggered in the wind-up -> %s"
			% ["full" if full else "short", "swing continues" if still_swinging else "swing cancelled"])
		if full and not still_swinging:
			_fail("hyperarmor: a full charge was cancelled by a stagger")
		if not full and still_swinging:
			_fail("hyperarmor: a short charge shrugged off a stagger it should not have")
		_free([a["body"]])
		await process_frame


# ---- buffer --------------------------------------------------------------------------------------

func _check_buffer() -> void:
	# Iron Sword light 1: 0.55 s, cancellable at 0.36 s, buffer lead 0.28 s.
	var a := _fighter("Buffer", 1, Vector3(40, 0, 0), SWORD)
	var action = a["action"]
	var combat = a["combat"]
	await _frames(2)

	# 1. Mashing at the very start is dropped: nothing may fire once the swing is over.
	action.TryAttack()
	await _frames(2)
	action.TryAttack()
	await _frames(70)
	var phantom: bool = action.Current != null
	print("buffer: press 0.03s in (0.33s before cancel) -> %s" % ("phantom swing" if phantom else "dropped"))
	if phantom:
		_fail("buffer: a press made far from the cancel point queued a phantom swing")
	await _frames(20)

	# 2. A press close to the cancel point is kept and fires the next link the moment it can.
	action.TryAttack()
	await _frames(8)
	action.TryAttack()
	await _frames(24)
	var linked: bool = action.Current != null and action.ComboIndex == 1
	print("buffer: press 0.13s in (0.23s before cancel) -> combo index %d" % action.ComboIndex)
	if not linked:
		_fail("buffer: a press inside the lead did not chain to link 2 (combo index %d)" % action.ComboIndex)
	await _frames(70)

	# 3. A stagger clears a queued press: it must not swing when the stagger lifts.
	action.TryAttack()
	await _frames(15)
	action.TryAttack()
	combat.Stagger(0.25)
	await _frames(90)
	var after_stagger: bool = action.Current != null
	print("buffer: press queued then staggered -> %s" % ("swung anyway" if after_stagger else "cleared"))
	if after_stagger:
		_fail("buffer: a press queued before a stagger fired after it")

	_free([a["body"]])
	await process_frame


# ---- direction -----------------------------------------------------------------------------------

func _check_direction() -> void:
	for case in [{"dir": 1, "label": "forward", "z": -0.9, "mult": 1.1},
			{"dir": 2, "label": "back", "z": 0.8, "mult": 0.85}]:
		var a := _fighter("Directed", 1, Vector3(60, 0, 0), SWORD)
		var action = a["action"]
		var body: CharacterBody3D = a["body"]
		await _frames(2)
		var start_z := body.global_position.z
		if not action.TryAttackDirected(case["dir"]):
			_fail("direction: %s attack did not start" % case["label"])
			_free([body])
			continue
		await _frames(50)
		var moved := body.global_position.z - start_z
		print("direction: %s attack stepped %.2f m along z (expected %.2f), damage x%.2f"
			% [case["label"], moved, case["z"], action.LastSwingDamageMultiplier])
		if absf(moved - case["z"]) > 0.3:
			_fail("direction: %s attack moved %.2f m, expected about %.2f" % [case["label"], moved, case["z"]])
		if absf(action.LastSwingDamageMultiplier - case["mult"]) > 0.01:
			_fail("direction: %s attack damage x%.2f, expected x%.2f"
				% [case["label"], action.LastSwingDamageMultiplier, case["mult"]])
		_free([body])
		await process_frame


# ---- warp and turn commitment --------------------------------------------------------------------

func _check_warp_and_turn() -> void:
	# The Iron King's slam: authored WarpToTarget, 1.6 m, 30 degrees, TurnDegreesPerSecond 25.
	var a := _fighter("Slammer", 1, Vector3(80, 0, 0), MAUL)
	var action = a["action"]
	var body: CharacterBody3D = a["body"]
	var target := Node3D.new()
	root.add_child(target)
	# 34 degrees off the facing, to the left (-X is left of -Z forward, and a +yaw turns -Z toward -X).
	target.global_position = Vector3(78.0, 0, -3.0)
	action.WarpTarget = target
	await _frames(2)

	var start := body.global_position
	if not action.TryStartById("ironking.slam"):
		_fail("warp: the slam did not start")
		_free([body, target])
		return
	await _frames(int(ceil(2.1 * 0.62 / 0.7 / STEP)) + 10)
	var closed := start.distance_to(body.global_position)
	var yaw := body.rotation.y
	print("warp: slam closed %.2f m (allowance 1.6), yaw %.2f rad toward a target 0.59 rad to the left"
		% [closed, yaw])
	if closed < 1.2 or closed > 1.9:
		_fail("warp: the slam closed %.2f m on a 1.6 m allowance" % closed)
	if yaw < 0.3:
		_fail("warp: yaw %.2f rad — the swing turned away from (or ignored) a target it was aimed at" % yaw)
	await _frames(200)
	if action.LastSwingKind != KIND_HEAVY:
		_fail("warp: the boss slam was stamped kind %d, expected Heavy (%d)" % [action.LastSwingKind, KIND_HEAVY])
	_free([body, target])
	await process_frame

	# A committed swing cannot be turned faster than its action allows (the sweep: 60 deg/s), however
	# hard something turns the body — which is exactly what an AI facing a circling target does.
	for enforce in [true, false]:
		var b := _fighter("Turned", 1, Vector3(100, 0, 0), MAUL)
		var act = b["action"]
		var tb: CharacterBody3D = b["body"]
		act.EnforceTurnLimit = enforce
		await _frames(2)
		act.TryStartById("ironking.sweep")
		var y0 := tb.rotation.y
		for i in 20:
			tb.rotation.y += 0.1
			await physics_frame
		var turned := tb.rotation.y - y0
		print("turn-lock: %s -> body turned %.2f rad of the 2.0 rad it was pushed" %
			["enforced" if enforce else "not enforced", turned])
		if enforce and turned > 0.55:
			_fail("turn-lock: a committed 60 deg/s swing was turned %.2f rad in a third of a second" % turned)
		if not enforce and turned < 1.8:
			_fail("turn-lock: with the limit off the body turned only %.2f rad; the control proves nothing" % turned)
		_free([tb])
		await process_frame


# ---- plunge --------------------------------------------------------------------------------------

func _check_plunge() -> void:
	_floor(Vector3(40, 1, 40), Vector3(120, -0.5, 0))
	var a := _fighter("Plunger", 1, Vector3(120, 0, 0), SWORD, true)
	var d := _dummy(Vector3(120, 0, 1.5))
	var action = a["action"]
	var body: CharacterBody3D = a["body"]
	var target_stats = d["stats"]
	await _frames(2)

	# Settle on the floor (the probe plays the motor), and check a grounded press is refused.
	for i in 20:
		body.velocity.y -= 9.8 * STEP
		body.move_and_slide()
		await physics_frame
	if not body.is_on_floor():
		_fail("plunge: the attacker never settled on the floor; the probe would prove nothing")
	if action.TryPlunge():
		_fail("plunge: a plunge started from the ground")

	# Too low: airborne, but under the minimum drop.
	body.global_position = Vector3(120, 0.3, 0)
	body.velocity = Vector3.ZERO
	body.move_and_slide()
	if action.TryPlunge():
		_fail("plunge: started from 0.3 m up, under the minimum drop")

	# A real jump-height drop.
	body.global_position = Vector3(120, 3.0, 0)
	body.velocity = Vector3.ZERO
	body.move_and_slide()
	var health_before: float = target_stats.GetCurrent(HEALTH)
	if not action.TryPlunge():
		_fail("plunge: refused from 3 m up")
		_free([body, d["body"]])
		return
	if not action.IsDiving:
		_fail("plunge: started but is not diving")

	var landed_at := -1
	var fastest := 0.0
	for f in 120:
		await physics_frame
		fastest = minf(fastest, body.velocity.y)
		if landed_at < 0 and not action.IsDiving:
			landed_at = f
		body.velocity.y -= 9.8 * STEP
		body.move_and_slide()

	var dealt: float = health_before - target_stats.GetCurrent(HEALTH)
	print("plunge: landed after %d frames, fastest fall %.1f m/s, dealt %.1f, kind %d, damage x%.2f"
		% [landed_at, fastest, dealt, action.LastSwingKind, action.LastSwingDamageMultiplier])
	if landed_at < 0:
		_fail("plunge: never landed")
	if fastest > -15.0:
		_fail("plunge: the dive only reached %.1f m/s; it is not a dive" % fastest)
	if dealt <= 0.0:
		_fail("plunge: the landing hurt nothing 1.5 m away")
	if action.LastSwingKind != HIT_PLUNGE:
		_fail("plunge: stamped kind %d, expected Plunge (%d)" % [action.LastSwingKind, HIT_PLUNGE])
	if action.LastSwingDamageMultiplier < 1.15 or action.LastSwingDamageMultiplier > 1.5:
		_fail("plunge: damage x%.2f for a 3 m drop, expected about 1.3" % action.LastSwingDamageMultiplier)
	if action.Current != null:
		_fail("plunge: the action had not ended 120 frames on")

	_free([body, d["body"]])
	await process_frame


# ---- roll cut and AI compatibility ---------------------------------------------------------------

func _check_roll_cut() -> void:
	var a := _fighter("Roller", 1, Vector3(140, 0, 0), SWORD)
	var action = a["action"]
	await _frames(2)
	if not action.TryRollAttack():
		_fail("roll cut: did not start")
	elif action.Current.Id != "Iron Sword.rollcut":
		_fail("roll cut: ran '%s'" % action.Current.Id)
	else:
		print("roll cut: ran '%s'" % action.Current.Id)
	await _frames(60)

	# The AI's entry points still work, and its repeat-avoidance never strands the only option.
	if not action.TryAttackAt(1.5):
		_fail("ai: TryAttackAt refused an in-range target")
	await _frames(80)
	action.Cancel()
	_free([a["body"]])
	await process_frame
