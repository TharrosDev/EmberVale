# Combat defence probe: does a real Hitbox/Hurtbox -> CombatComponent path give graded parries, guard
# breaks, ripostes, backstabs, poise reactions and hit-zone picks the way the rules say, under real frames?
#
# CombatMath/DefenceRules/Parry are unit-tested for their arithmetic. What only a running engine shows is
# the wiring: that the blow arrives through a real Hurtbox, that the parry window is measured in real
# process time from the guard being raised, that the events a listener sees are the ones published, and
# that a stagger cancels a wind-up and a flinch does not.
#
# ⚠️ Actors are hand-built (CharacterEntity + StatsComponent + CombatComponent + Hurtbox) rather than run
# through a factory: a C# static method is not reachable through a loaded Script object from GDScript.
# A DamagePacket is a C# struct GDScript cannot construct, so src/Debugging/CombatProbeDriver.cs builds
# them and counts the published events; it holds no rules.
#
# ⚠️ READ THE WHOLE OUTPUT. A GDScript error inside an awaited function prints a script error and can
# still fall through to PASS. This probe also fails if it never ran a check.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/combat_defence_probe.gd
# Exits 0 when every case below holds, 1 otherwise.
extends SceneTree

const HEALTH := 0
const STAMINA := 1

# HitKind
const NORMAL := 0
const HEAVY := 1
const CHARGED := 2
const RIPOSTE := 3
const BACKSTAB := 4
const PLUNGE := 5
const RANGED := 6

# ReactionClass
const SMALL := 0
const HUMANOID := 1
const ARMORED := 2
const LARGE := 3
const BOSS := 4

# StaggerResponse
const R_FLINCH := 1
const R_STAGGER := 2
const R_HEAVY := 3
const R_KNOCKDOWN := 4

# ParryGrade
const P_NONE := 0
const P_LATE := 1
const P_GOOD := 2
const P_PERFECT := 3

# OpenCause
const O_POISE := 1
const O_PARRY := 2
const O_GUARD := 3
const O_PERFECT := 4

const STEP := 1.0 / 60.0

var _failures: Array[String] = []
var _checks := 0
var _driver


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame
	_driver = load("res://src/Debugging/CombatProbeDriver.cs").new()
	_driver.Listen()

	await _grades()
	await _guard_break()
	await _riposte_and_latch()
	await _poise()
	await _zones_and_hitbox()
	await _backstab_and_arc()
	await _windup_cancel()

	print("---")
	print("%d checks" % _checks)
	if _checks == 0:
		_failures.append("no check ran")
	if _failures.is_empty():
		print("PASS: graded parry, guard break, riposte, poise, zones and backstab hold through real hurtboxes")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _ok(cond: bool, msg: String) -> void:
	_checks += 1
	if not cond:
		_failures.append(msg)


func _near(a: float, b: float, tol: float, msg: String) -> void:
	_ok(absf(a - b) <= tol, "%s (got %.3f, want %.3f)" % [msg, a, b])


# Builds one actor. Returns {body, stats, combat, hurtbox}. Hands back the components directly because
# Entity.GetComponent<T> is generic and unreachable from GDScript.
func _actor(actor_name: String, team: int, at: Vector3, reaction: int = HUMANOID, poise: float = 50.0) -> Dictionary:
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
	combat.Reaction = reaction
	combat.MaxPoise = poise
	body.add_child(combat)
	var hurtbox := _hurtbox("Hurtbox", 1.0, Vector3(0, 0.9, 0))
	body.add_child(hurtbox)
	root.add_child(body)
	return {"body": body, "stats": stats, "combat": combat, "hurtbox": hurtbox}


func _hurtbox(node_name: String, multiplier: float, at: Vector3) -> Area3D:
	var hurtbox := Area3D.new()
	hurtbox.set_script(load("res://src/Combat/Hurtbox.cs"))
	hurtbox.name = node_name
	hurtbox.DamageMultiplier = multiplier
	var zone := CollisionShape3D.new()
	var sphere := SphereShape3D.new()
	sphere.radius = 0.4
	zone.shape = sphere
	zone.position = at
	hurtbox.add_child(zone)
	return hurtbox


# A defender at the origin facing -Z, and an attacker one and a half metres in front of it.
func _duel(defender_team: int = 2) -> Array:
	var d := _actor("Defender", defender_team, Vector3.ZERO)
	var a := _actor("Attacker", 1, Vector3(0, 0, -1.5))
	return [d, a]


func _free(parts: Array) -> void:
	for p in parts:
		p["body"].queue_free()


func _blow(d: Dictionary, a: Dictionary, kind: int = NORMAL, charge: float = 0.0, amount: float = 10.0, poise: float = 20.0) -> Dictionary:
	return _driver.Deliver(d["hurtbox"], a["body"], amount, kind, charge, poise)


# What an unguarded, non-open defender takes from a plain 10-damage blow: the baseline for "x times".
func _baseline() -> float:
	var d := _actor("Base", 2, Vector3(0, 0, 10))
	var a := _actor("BaseAtk", 1, Vector3(0, 0, 8.5))
	var r := _blow(d, a)
	_free([d, a])
	return r["final"]


func _wait(seconds: float) -> void:
	await create_timer(seconds).timeout


# --- graded parry, in real process time from the guard going up ---------------------------------------

func _grades() -> void:
	var base := _baseline()
	_ok(base > 5.0, "baseline blow did %.2f, the probe would prove nothing" % base)

	# Perfect: first half of the 0.2 s window.
	var duel := _duel()
	var d: Dictionary = duel[0]
	var a: Dictionary = duel[1]
	await process_frame
	d["combat"].IsBlocking = true
	await process_frame
	await _wait(0.03)
	var stamina_before: float = d["stats"].GetCurrent(STAMINA)
	var r := _blow(d, a)
	print("perfect parry: %s" % r)
	_ok(r["parry"] == P_PERFECT, "a guard 30 ms old should be a perfect parry, got %d" % r["parry"])
	_ok(r["final"] == 0.0, "a perfect parry takes no damage")
	_near(d["stats"].GetCurrent(STAMINA), stamina_before, 0.01, "a perfect parry is free of stamina")
	_ok(a["combat"].IsStaggered, "the parried attacker is staggered")
	_ok(a["combat"].IsOpen and a["combat"].CurrentOpenCause == O_PERFECT, "the parried attacker is open to a perfect-parry riposte")
	_ok(a["combat"].StaggerRemaining > 1.2, "a perfect parry staggers longer than a plain one (%.2f)" % a["combat"].StaggerRemaining)
	_ok(_driver.LastParryGrade == P_PERFECT and _driver.GradedParries >= 1, "ParryGradedEvent carried the grade")

	# The latch: one parry per raise. The very next blow is only a block.
	var again := _blow(d, a)
	_ok(again["parry"] == P_NONE and again["blocked"], "a second blow on the same raise is blocked, not parried")
	_free(duel)

	# Good: inside the window, past the perfect half. Costs the parry stamina.
	duel = _duel()
	d = duel[0]
	a = duel[1]
	await process_frame
	d["combat"].IsBlocking = true
	await process_frame
	await _wait(0.15)
	stamina_before = d["stats"].GetCurrent(STAMINA)
	r = _blow(d, a)
	print("good parry: %s" % r)
	_ok(r["parry"] == P_GOOD, "a guard 150 ms old should be a good parry, got %d" % r["parry"])
	_near(stamina_before - d["stats"].GetCurrent(STAMINA), 12.0, 0.5, "a good parry costs ParryStaminaCost")
	_ok(a["combat"].CurrentOpenCause == O_PARRY, "a good parry opens a parry riposte")
	_free(duel)

	# Late: just past the window. A hard deflect, no riposte.
	duel = _duel()
	d = duel[0]
	a = duel[1]
	await process_frame
	d["combat"].IsBlocking = true
	await process_frame
	await _wait(0.24)
	r = _blow(d, a)
	print("late parry: %s (baseline %.2f)" % [r, base])
	_ok(r["parry"] == P_LATE, "a guard 240 ms old should be a late parry, got %d" % r["parry"])
	_ok(r["blocked"] and r["final"] > 0.0 and r["final"] < base * 0.3, "a late parry deflects most of the blow")
	_ok(not a["combat"].IsOpen, "a late parry opens no riposte")
	_free(duel)

	# Held guard: an ordinary block, chip damage, stamina paid by weight.
	duel = _duel()
	d = duel[0]
	a = duel[1]
	await process_frame
	d["combat"].IsBlocking = true
	await process_frame
	await _wait(0.6)
	stamina_before = d["stats"].GetCurrent(STAMINA)
	r = _blow(d, a)
	print("held block: %s" % r)
	_ok(r["parry"] == P_NONE and r["blocked"], "a guard held for 600 ms only blocks")
	_near(r["final"], base * 0.3, base * 0.05, "a block takes the unmitigated 30%")
	_near(stamina_before - d["stats"].GetCurrent(STAMINA), 8.0, 0.5, "a 20-poise blow costs 0.8 of the base guard stamina")
	var maul := _blow(d, a, NORMAL, 0.0, 10.0, 60.0)
	_near(stamina_before - 8.0 - d["stats"].GetCurrent(STAMINA), 20.0, 0.5, "a 60-poise blow costs double")
	_free(duel)

	# Ranged: arrows are blocked, never parried.
	duel = _duel()
	d = duel[0]
	a = duel[1]
	await process_frame
	d["combat"].IsBlocking = true
	await process_frame
	await _wait(0.03)
	r = _blow(d, a, RANGED)
	_ok(r["parry"] == P_NONE and r["blocked"], "an arrow on a fresh guard is blocked, not parried")
	_free(duel)


# --- guard break, then the riposte it opens ---------------------------------------------------------

func _guard_break() -> void:
	var base := _baseline()

	# Stamina-out.
	var duel := _duel()
	var d: Dictionary = duel[0]
	var a: Dictionary = duel[1]
	await process_frame
	d["stats"].ModifyCurrent(STAMINA, -100.0)
	d["combat"].IsBlocking = true
	await process_frame
	await _wait(0.6)
	var broken_before: int = _driver.GuardBroken
	var r := _blow(d, a)
	print("guard break (no stamina): %s" % r)
	_ok(r["guard_broken"], "a guard with no stamina breaks")
	_near(r["final"], base, base * 0.05, "a broken guard takes the full hit")
	_ok(_driver.GuardBroken == broken_before + 1, "GuardBrokenEvent published once")
	_ok(d["combat"].IsStaggered and d["combat"].StaggerRemaining > 1.0, "a broken guard staggers for a long time (%.2f)" % d["combat"].StaggerRemaining)
	_ok(d["combat"].CurrentOpenCause == O_GUARD, "a broken guard opens the body")
	_ok(not d["combat"].GuardUp, "the guard is down while staggered even though block is held")
	_ok(d["combat"].LastResponse == R_HEAVY, "a humanoid's broken guard is a heavy stagger")

	# The riposte: first blow into the window is a critical and closes it.
	var crits: int = _driver.Criticals
	var rip := _blow(d, a)
	print("riposte: %s" % rip)
	_ok(rip["opening"] == RIPOSTE and rip["crit"], "a blow on a broken guard is a riposte critical")
	_near(rip["final"], base * 1.75, base * 0.1, "a guard-break riposte does x1.75")
	_ok(_driver.Criticals == crits + 1 and _driver.LastCriticalKind == RIPOSTE, "CriticalHitEvent published as a riposte")
	var after := _blow(d, a)
	_ok(after["opening"] == NORMAL, "the riposte closed the window; the next blow is ordinary")
	_free(duel)

	# A charged blow at full charge, and a plunge, break a healthy guard outright.
	for case in [[CHARGED, 1.0, 1.75, "full charge"], [PLUNGE, 0.0, 1.0, "plunge"]]:
		duel = _duel()
		d = duel[0]
		a = duel[1]
		await process_frame
		d["combat"].IsBlocking = true
		await process_frame
		await _wait(0.6)
		r = _blow(d, a, case[0], case[1])
		_ok(r["guard_broken"], "%s breaks a guard with full stamina" % case[3])
		_near(r["final"], base * case[2], base * 0.1, "%s damage" % case[3])
		_free(duel)

	# A half charge does not, but presses the guard harder than a plain blow.
	duel = _duel()
	d = duel[0]
	a = duel[1]
	await process_frame
	d["combat"].IsBlocking = true
	await process_frame
	await _wait(0.6)
	var before: float = d["stats"].GetCurrent(STAMINA)
	r = _blow(d, a, CHARGED, 0.5)
	_ok(not r["guard_broken"] and r["blocked"], "a half charge is still blockable")
	_ok(before - d["stats"].GetCurrent(STAMINA) > 8.0 * 1.4, "a half charge costs more than a plain blow")
	_free(duel)

	# A heavy blow costs the guard double: a guard 10 stamina short of it breaks.
	duel = _duel()
	d = duel[0]
	a = duel[1]
	await process_frame
	d["stats"].ModifyCurrent(STAMINA, -90.0)
	d["combat"].IsBlocking = true
	await process_frame
	await _wait(0.6)
	r = _blow(d, a, HEAVY)
	_ok(r["guard_broken"], "a heavy blow breaks a guard a plain blow would have held (10 stamina left)")
	_free(duel)


# --- riposte after a poise break, and never on the player's side --------------------------------------

func _riposte_and_latch() -> void:
	var base := _baseline()

	var duel := _duel()
	var d: Dictionary = duel[0]
	var a: Dictionary = duel[1]
	await process_frame
	_blow(d, a, NORMAL, 0.0, 1.0, 50.0)     # breaks a 50-poise humanoid
	_ok(d["combat"].IsStaggered and d["combat"].CurrentOpenCause == O_POISE, "a poise break opens the body")
	_ok(_driver.LastWindowCause == O_POISE, "PunishWindowOpenedEvent named the cause")
	var r := _blow(d, a)
	_near(r["final"], base * 1.35, base * 0.08, "a poise-break riposte does x1.35")
	_free(duel)

	# The player's side is never critically opened: poise is symmetric, hidden multipliers are not.
	duel = _duel(0)
	d = duel[0]
	a = duel[1]
	await process_frame
	_blow(d, a, NORMAL, 0.0, 1.0, 50.0)
	_ok(d["combat"].IsStaggered, "a player-team body still staggers (poise is symmetric)")
	r = _blow(d, a)
	_ok(r["opening"] == NORMAL, "a player-team body takes no riposte critical")
	_free(duel)

	# Arrows and spells never open anything.
	duel = _duel()
	d = duel[0]
	a = duel[1]
	await process_frame
	_blow(d, a, NORMAL, 0.0, 1.0, 50.0)
	r = _blow(d, a, RANGED)
	_ok(r["opening"] == NORMAL, "an arrow into a staggered body is not a riposte")
	_free(duel)


# --- poise and stagger by reaction class ------------------------------------------------------------

func _poise() -> void:
	# A flinch does not interrupt: an armoured body hit for exactly its poise flinches and is not staggered.
	var d := _actor("Armored", 2, Vector3.ZERO, ARMORED, 50.0)
	var a := _actor("Atk", 1, Vector3(0, 0, -1.5))
	await process_frame
	_blow(d, a, NORMAL, 0.0, 1.0, 50.0)
	_ok(d["combat"].LastResponse == R_FLINCH, "armour flinches at exact break (got %d)" % d["combat"].LastResponse)
	_ok(d["combat"].IsFlinching and not d["combat"].IsStaggered, "a flinch is not an interrupting stagger")
	_ok(not d["combat"].IsOpen, "a flinch opens no riposte")
	_free([d, a])

	# Bosses: never knocked down, and a stagger in progress is not chained.
	d = _actor("Boss", 2, Vector3.ZERO, BOSS, 100.0)
	a = _actor("Atk", 1, Vector3(0, 0, -1.5))
	await process_frame
	_blow(d, a, NORMAL, 0.0, 1.0, 900.0)
	_ok(d["combat"].LastResponse == R_STAGGER, "a boss staggers in place, never knocked down (got %d)" % d["combat"].LastResponse)
	var left: float = d["combat"].StaggerRemaining
	await _wait(0.2)
	_blow(d, a, NORMAL, 0.0, 1.0, 900.0)
	_ok(d["combat"].StaggerRemaining <= left - 0.1, "blows during a stagger do not extend it (%.2f -> %.2f)" % [left, d["combat"].StaggerRemaining])
	_ok(d["combat"].PoiseNormalized >= 0.99, "poise is held full through the punish window")
	_free([d, a])

	# The smallest bodies are knocked down by a real blow; heavy poise damage raises a heavy weight.
	d = _actor("Small", 2, Vector3.ZERO, SMALL, 50.0)
	a = _actor("Atk", 1, Vector3(0, 0, -1.5))
	await process_frame
	_blow(d, a, NORMAL, 0.0, 1.0, 100.0)
	_ok(d["combat"].LastResponse == R_KNOCKDOWN, "a small body is knocked down by a big blow (got %d)" % d["combat"].LastResponse)
	_free([d, a])

	# Heavy blows carry 1.5x poise: 40 poise breaks nothing plain, breaks with the heavy kind.
	d = _actor("H1", 2, Vector3.ZERO, HUMANOID, 50.0)
	a = _actor("Atk", 1, Vector3(0, 0, -1.5))
	await process_frame
	_blow(d, a, NORMAL, 0.0, 1.0, 40.0)
	_ok(not d["combat"].IsStaggered, "40 plain poise does not break 50")
	_blow(d, a, HEAVY, 0.0, 1.0, 40.0)
	_ok(d["combat"].IsStaggered, "a heavy blow's x1.5 poise breaks it")
	_free([d, a])


# --- zones through a real Hitbox, and the poise multiplier ------------------------------------------

func _zones_and_hitbox() -> void:
	var base := _baseline()

	# One body, a weak head zone and a tough tail zone; a real hitbox overlapping both.
	var d := _actor("Beast", 2, Vector3.ZERO)
	d["body"].remove_child(d["hurtbox"])
	d["hurtbox"].queue_free()
	var head := _hurtbox("Hurtbox_head", 2.0, Vector3(0, 1.6, 0))
	var tail := _hurtbox("Hurtbox_tail", 0.5, Vector3(0, 0.4, 0))
	d["body"].add_child(tail)      # added first: the physics query must not decide by order
	d["body"].add_child(head)
	var a := _actor("Atk", 1, Vector3(0, 0, -1.5))
	var hitbox := Area3D.new()
	hitbox.set_script(load("res://src/Combat/Hitbox.cs"))
	hitbox.name = "Hitbox"
	hitbox.position = Vector3(0, 1.0, 1.5)
	var box := CollisionShape3D.new()
	var shape := BoxShape3D.new()
	shape.size = Vector3(1.2, 2.2, 1.2)
	box.shape = shape
	hitbox.add_child(box)
	a["body"].add_child(hitbox)
	await process_frame
	await physics_frame
	var before: float = d["stats"].GetCurrent(HEALTH)
	_driver.Arm(hitbox, a["body"], 10.0, NORMAL, 0.0, 5.0)
	for _i in 6:
		await physics_frame
	hitbox.Deactivate()
	var lost: float = before - d["stats"].GetCurrent(HEALTH)
	print("zones: lost %.2f (baseline %.2f)" % [lost, base])
	_near(lost, base * 2.0, base * 0.1, "a sword clipping head and tail lands once, on the weak head")
	_free([d, a])

	# A zone's poise multiplier: 0 makes it un-unbalanceable while damage still lands.
	d = _actor("Tank", 2, Vector3.ZERO)
	a = _actor("Atk", 1, Vector3(0, 0, -1.5))
	await process_frame
	d["hurtbox"].PoiseMultiplier = 0.0
	var r: Dictionary = _driver.Deliver(d["hurtbox"], a["body"], 10.0, NORMAL, 0.0, 500.0)
	_ok(r["final"] > 0.0 and not d["combat"].IsStaggered, "a zero poise multiplier soaks poise but not damage")
	_free([d, a])


# --- direction: backstab, the flank ring, and the rear being uncovered -------------------------------

func _backstab_and_arc() -> void:
	var base := _baseline()

	var d := _actor("Defender", 2, Vector3.ZERO)
	var behind := _actor("Behind", 1, Vector3(0, 0, 1.5))
	var front := _actor("Front", 1, Vector3(0, 0, -1.5))
	await process_frame
	var r := _blow(d, behind)
	_ok(r["opening"] == BACKSTAB and r["crit"], "a blow from behind is a backstab critical")
	_near(r["final"], base * 1.5, base * 0.08, "a backstab does x1.5")
	r = _blow(d, front)
	_ok(r["opening"] == NORMAL, "a blow from the front is no backstab")
	_free([d, behind, front])

	# The player's side is not backstabbed.
	d = _actor("Player", 0, Vector3.ZERO)
	behind = _actor("Behind", 1, Vector3(0, 0, 1.5))
	await process_frame
	r = _blow(d, behind)
	_ok(r["opening"] == NORMAL, "the player's side is never backstabbed")
	_free([d, behind])

	# A raised guard does not cover the back.
	d = _actor("Defender", 2, Vector3.ZERO)
	behind = _actor("Behind", 1, Vector3(0, 0, 1.5))
	await process_frame
	d["combat"].IsBlocking = true
	await process_frame
	await _wait(0.6)
	r = _blow(d, behind)
	_ok(not r["blocked"] and not r["guard_broken"], "a guard does not cover a blow from behind")
	_free([d, behind])

	# The flank ring blocks, but weaker than the front, and cannot parry.
	var lost := {}
	for label in ["front", "flank"]:
		d = _actor("Defender", 2, Vector3.ZERO)
		var angle := 0.0 if label == "front" else deg_to_rad(85.0)
		var at := Vector3(sin(angle), 0, -cos(angle)) * 1.5
		var atk := _actor("Atk", 1, at)
		await process_frame
		d["combat"].IsBlocking = true
		await process_frame
		if label == "flank":
			# Fresh guard, inside the perfect window: from the flank it must still be no parry.
			await _wait(0.03)
		else:
			await _wait(0.6)
		var res := _blow(d, atk)
		lost[label] = res
		if label == "flank":
			_ok(res["parry"] == P_NONE, "a blow from the flank ring cannot be parried")
		_ok(res["blocked"], "%s blow is blocked" % label)
		_free([d, atk])
	_ok(lost["flank"]["final"] > lost["front"]["final"] + 0.5, "a flank block mitigates less than a front block (%.2f vs %.2f)" % [lost["flank"]["final"], lost["front"]["final"]])


# --- a stagger cancels a wind-up, a flinch does not ----------------------------------------------------

func _windup_cancel() -> void:
	var result := {}
	for label in ["humanoid", "armored"]:
		var reaction := HUMANOID if label == "humanoid" else ARMORED
		var target := _actor("Target", 2, Vector3.ZERO)
		var swinger := _actor("Swinger", 1, Vector3(0, 0, 1.2), reaction, 50.0)
		var hitbox := Area3D.new()
		hitbox.set_script(load("res://src/Combat/Hitbox.cs"))
		hitbox.name = "MeleeHitbox"
		hitbox.position = Vector3(0, 1.0, -1.0)
		var arc := CollisionShape3D.new()
		var box := BoxShape3D.new()
		box.size = Vector3(1.0, 1.4, 1.6)
		arc.shape = box
		hitbox.add_child(arc)
		swinger["body"].add_child(hitbox)
		var action = load("res://src/Combat/Actions/CharacterActionComponent.cs").new()
		action.name = "Action"
		action.Weapon = load("res://data/weapons/IronSword.tres")
		action.Hitbox = hitbox
		swinger["body"].add_child(action)
		await process_frame
		await physics_frame

		var before: float = target["stats"].GetCurrent(HEALTH)
		if not action.TryAttack():
			_failures.append("%s swing did not start" % label)
			_free([target, swinger])
			continue
		await physics_frame
		# The wind-up is 0.15 s; break the swinger's poise a frame into it, from the target's side.
		_blow(swinger, target, NORMAL, 0.0, 1.0, 50.0)
		await _wait(0.6)
		result[label] = before - target["stats"].GetCurrent(HEALTH)
		print("%s: target lost %.2f, swinger staggered=%s" % [label, result[label], swinger["combat"].IsStaggered])
		_free([target, swinger])
	_ok(result.get("humanoid", 1.0) == 0.0, "a stagger during the wind-up cancels the swing (target lost %.2f)" % result.get("humanoid", -1.0))
	_ok(result.get("armored", 0.0) > 0.0, "a flinch during the wind-up does not (target lost %.2f)" % result.get("armored", -1.0))
