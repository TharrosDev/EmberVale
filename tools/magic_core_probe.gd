# Magic core probe: the casting core of the 2026-09 magic upgrade under real physics frames and real
# Areas. SpellRulesTests pins the arithmetic (wind-up shape, interrupt table, refund, health cost,
# consume multiplier, pierce, blink price, barrier plane maths); this drives the real
# SpellcastingComponent, action timeline, hurtboxes, projectiles, ground, barrier, dash and totem.
#
#   windup      a cast is committed: mana spent at once, the spell leaves after the wind-up (not before),
#               the wind-up event reports it, a second cast is refused, the hit is stamped as a Spell
#               and carries the spell's ImpactWeight to the feedback event
#   interrupt   a stagger, a silence and a stun in the wind-up each cancel the cast and refund half the
#               mana; an uninterruptible spell and a Barkskin caster ride a stagger out; a silenced or
#               stunned caster cannot start; a channel is slowed and a stagger drops it
#   blows       a spell is blockable (chip damage) and never parryable, an unblockable spell ignores
#               the guard, PoiseDamage breaks poise, a full charge crushes a guard and a tap does not
#   ground      the aim point is clamped to PlaceRange, the ring is armed for exactly GroundDelay and the
#               blast lands then, a pull draws a foe to the centre
#   barrier     a non-solid wall eats an enemy bolt and an arrow (control: without it both land), burns
#               what stands in it, and expires; a solid wall blocks a body, breaks after BarrierHealth
#               and says so
#   dash        travels the distance, strikes the foes on the line once each, stuns the last, stops
#               short of a wall; blink stops short of a wall and costs less for the shorter jump
#   costs       a health cost is paid, and refused rather than killing the caster; a self status lands
#               on release; a consumed status is stripped and adds damage per stack
#   pierce      a fuller charge strikes more foes; homing prefers a Stormbranded foe
#   totem       heals, and can be destroyed
#   save        a load replaces live cooldowns, dedupes and keeps a known spell
#
# ⚠️ Actors are built by hand (a C# static factory is not reachable from GDScript), there is no
# AnimationPlayer (so the FALLBACK clock runs, as it does for any unanimated body) and no locomotion:
# the probe does not simulate gravity. A DamagePacket is a C# struct, so the events and the arrow are
# driven through src/Debugging/MagicCoreProbeDriver.cs.
#
# ⚠️ READ THE WHOLE OUTPUT FOR SCRIPT ERRORS. A GDScript error mid-function aborts that function and
# the probe can still print PASS.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/magic_core_probe.gd
# Exits 0 when every case holds, 1 otherwise.
extends SceneTree

const HEALTH := 0     # StatType.Health
const MANA := 2       # StatType.Mana
const MOVE_SPEED := 11  # StatType.MoveSpeed

const DELIVERY_PROJECTILE := 0
const DELIVERY_SELF := 2
const DELIVERY_GROUND := 4
const DELIVERY_BARRIER := 5
const DELIVERY_DASH := 6
const MODE_INSTANT := 0
const MODE_CHARGED := 1
const MODE_CHANNELED := 2
const FIRE := 1
const KIND_SPELL := 7  # HitKind.Spell

const SWORD := "res://data/weapons/IronSword.tres"

var _failures: Array[String] = []
var _driver
var _databases
var _floor: StaticBody3D
var _world: Node3D
# Loaded C# resources are held here for the whole run: a wrapper nobody holds can be collected while
# the native object is cached, and the next load of that path is a gchandle fatal (CLAUDE.md section 7).
var _held := {}


func _initialize() -> void:
	_databases = load("res://src/Bootstrap/ContentDatabaseLoader.cs").new()
	root.add_child(_databases)
	await process_frame
	root.add_child(load("res://src/Combat/CombatFeedbackDirector.cs").new())
	# Spells add their projectiles and effects to the current scene; a --script run has none.
	_world = Node3D.new()
	_world.name = "World"
	root.add_child(_world)
	current_scene = _world
	_driver = load("res://src/Debugging/MagicCoreProbeDriver.cs").new()
	_driver.Listen()
	_floor = _make_floor()
	await process_frame

	await _check_windup()
	await _check_interrupts()
	await _check_armour()
	await _check_channel()
	await _check_blows()
	await _check_ground()
	await _check_barrier()
	await _check_dash_and_blink()
	await _check_costs()
	await _check_roster_identities()
	await _check_pierce_and_homing()
	await _check_actor_geometry()
	await _check_worldray_crowd()
	await _check_status_respawn()
	await _check_totem()
	await _check_save()
	await _check_lifecycle_interrupts()
	await _clear_transients()
	_world.queue_free()
	await process_frame
	await process_frame
	_databases.CollectManagedResources()
	await process_frame

	print("---")
	if _failures.is_empty():
		print("PASS: committed casts, interrupts, blows, ground, barrier, dash, blink, costs, pierce, homing, totem and save all hold under real frames")
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


func _make_floor() -> StaticBody3D:
	var body := StaticBody3D.new()
	body.collision_layer = 1
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(200, 1, 200)
	shape.shape = box
	body.add_child(shape)
	body.position = Vector3(0, -0.5, 0)
	_world.add_child(body)
	return body


# A spell built in code. `props` are exported SpellResource properties.
func _spell(id: String, props: Dictionary) -> Resource:
	var s = load("res://src/Magic/SpellResource.cs").new()
	s.Id = id
	s.DisplayName = id
	s.School = FIRE
	s.Delivery = DELIVERY_PROJECTILE
	s.CastMode = MODE_INSTANT
	s.ManaCost = 10.0
	s.Cooldown = 0.2
	s.BaseDamage = 20.0
	s.Range = 30.0
	s.ProjectileSpeed = 40.0
	s.WindupSeconds = 0.4
	s.RecoverySeconds = 0.3
	for k in props:
		s.set(k, props[k])
	_held[id + str(s.get_instance_id())] = s
	return s


# A body with health, combat, statuses and (for a caster) a spellbook. Faces -Z by default; yaw_deg
# turns it. Crits are switched off so damage compares exactly.
func _actor(name_: String, team: int, at: Vector3, spells: Array = [], yaw_deg := 0.0, max_poise := 50.0) -> Dictionary:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = name_
	body.collision_layer = 2  # CombatLayers.Body: an actor is not world cover for spell bursts.
	body.collision_mask = 1
	body.position = at
	body.rotation_degrees.y = yaw_deg
	var shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.4
	capsule.height = 1.8
	shape.shape = capsule
	shape.position = Vector3(0, 0.9, 0)
	body.add_child(shape)

	var attrs = load("res://src/Stats/AttributeSet.cs").new()
	attrs.CritChance = 0.0
	attrs.SpellPower = 0.0
	attrs.Intelligence = 0.0
	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	stats.Attributes = attrs
	stats.ManaRegen = 0.0
	body.add_child(stats)
	var combat = load("res://src/Combat/CombatComponent.cs").new()
	combat.name = "Combat"
	combat.Team = team
	combat.MaxPoise = max_poise
	body.add_child(combat)
	var status = load("res://src/Magic/StatusEffectsComponent.cs").new()
	status.name = "Status"
	body.add_child(status)

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

	var action = load("res://src/Combat/Actions/CharacterActionComponent.cs").new()
	action.name = "Action"
	action.Weapon = _res(SWORD)
	action.Hitbox = hitbox
	body.add_child(action)

	var hurt := Area3D.new()
	hurt.set_script(load("res://src/Combat/Hurtbox.cs"))
	hurt.name = "Hurtbox"
	var zone := CollisionShape3D.new()
	var hcap := CapsuleShape3D.new()
	hcap.radius = 0.4
	hcap.height = 1.8
	zone.shape = hcap
	zone.position = Vector3(0, 0.9, 0)
	hurt.add_child(zone)
	body.add_child(hurt)

	var aim := Node3D.new()
	aim.name = "Aim"
	aim.position = Vector3(0, 1.0, 0)
	body.add_child(aim)

	var casting = load("res://src/Magic/SpellcastingComponent.cs").new()
	casting.name = "Spellcasting"
	casting.AimNode = aim
	body.add_child(casting)

	_world.add_child(body)
	for s in spells:
		casting.Teach(s)
	return {"body": body, "stats": stats, "combat": combat, "status": status, "action": action,
		"casting": casting}


func _health(a: Dictionary) -> float:
	return a["stats"].GetCurrent(HEALTH)


func _mana(a: Dictionary) -> float:
	return a["stats"].GetCurrent(MANA)


func _free(list: Array) -> void:
	for a in list:
		if a is Dictionary:
			a = a["body"]
		if is_instance_valid(a):
			a.queue_free()
	await _frames(2)


func _clear_transients() -> void:
	for n in _world.get_children():
		if is_instance_valid(n) and n != _floor and not (n is CharacterBody3D) and (n is Node3D):
			n.queue_free()
	await _frames(2)


func _res(path: String) -> Resource:
	if not _held.has(path):
		_held[path] = load(path)
	return _held[path]


func _apply(a: Dictionary, status_path: String) -> void:
	_driver.ApplyStatus(a["status"], _res(status_path))


func _has_status(a: Dictionary, id: String) -> bool:
	return a["status"].Has(id)


func _find(name_: String):
	for n in _world.get_children():
		if n.name == name_ and is_instance_valid(n) and not n.is_queued_for_deletion():
			return n
	return null


# ---- windup --------------------------------------------------------------------------------------

func _check_windup() -> void:
	var bolt := _spell("spell.probe_bolt", {"ImpactWeight": 0.9})
	var other := _spell("spell.probe_other", {})
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [bolt, other])
	var dummy := _actor("Dummy", 2, Vector3(0, 0, -6))
	var sc = caster["casting"]
	await _frames(3)
	_driver.Reset()

	var mana0: float = _mana(caster)
	var hp0: float = _health(dummy)
	if not sc.TryCastById("spell.probe_bolt"):
		_fail("windup: a ready cast was refused")
		await _free([caster, dummy])
		return
	var spent: float = mana0 - _mana(caster)
	print("windup: mana spent at once %.1f, wind-up reported %.2fs, move scale %.2f" % [spent, _driver.LastWindup, caster["action"].MoveScale])
	if absf(spent - 10.0) > 0.8:
		_fail("windup: mana spent at the start was %.2f, expected 10" % spent)
	if _driver.WindupStarted != 1 or _driver.LastWindupSpell != "spell.probe_bolt":
		_fail("windup: CastWindupStartedEvent count %d spell '%s'" % [_driver.WindupStarted, _driver.LastWindupSpell])
	if absf(_driver.LastWindup - 0.4) > 0.06:
		_fail("windup: reported wind-up %.3f, expected 0.4 (from WindupSeconds)" % _driver.LastWindup)
	if sc.PendingSpell == null:
		_fail("windup: nothing pending after a committed cast began")
	if absf(caster["action"].MoveScale - 0.5) > 0.01:
		_fail("windup: the caster keeps %.2f of its movement, expected 0.5 so mages can kite" % caster["action"].MoveScale)
	if sc.TryCastById("spell.probe_other"):
		_fail("windup: a second cast started while the first was committed")
	if absf((mana0 - _mana(caster)) - 10.0) > 0.8:
		_fail("windup: the refused second cast still spent mana")

	await _frames(6)
	if _health(dummy) < hp0:
		_fail("windup: the spell hurt the target inside its wind-up")
	await _frames(70)
	var dmg: float = hp0 - _health(dummy)
	print("windup: target lost %.1f after the release; hits %d, confirmed kind %d weight %.2f"
		% [dmg, _driver.Hits, _driver.LastConfirmedKind, _driver.LastConfirmedWeight])
	if dmg <= 0.0:
		_fail("windup: the spell never landed")
	if sc.PendingSpell != null:
		_fail("windup: still pending after it landed")
	if _driver.Hits != 1 or _driver.LastHitSpell != "spell.probe_bolt":
		_fail("windup: SpellHitEvent count %d spell '%s'" % [_driver.Hits, _driver.LastHitSpell])
	if _driver.Confirmed < 1 or _driver.LastConfirmedKind != KIND_SPELL:
		_fail("windup: the confirmed hit was kind %d, expected Spell (7)" % _driver.LastConfirmedKind)
	if absf(_driver.LastConfirmedWeight - 0.9) > 0.01:
		_fail("windup: the feedback event carried weight %.2f, expected the spell's ImpactWeight 0.9" % _driver.LastConfirmedWeight)
	await _free([caster, dummy])


# ---- interrupts ----------------------------------------------------------------------------------

func _cast_and_wait_for_windup(caster: Dictionary, id: String, frames: int) -> void:
	caster["casting"].TryCastById(id)
	await _frames(frames)


func _check_interrupts() -> void:
	var bolt := _spell("spell.probe_bolt", {})
	# stagger
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [bolt])
	var dummy := _actor("Dummy", 2, Vector3(0, 0, -6))
	await _frames(3)
	_driver.Reset()
	var mana0: float = _mana(caster)
	var hp0: float = _health(dummy)
	await _cast_and_wait_for_windup(caster, "spell.probe_bolt", 6)
	caster["combat"].Stagger(0.6, 0, 2)
	await _frames(6)
	var back: float = _mana(caster) - (mana0 - 10.0)
	await _frames(84)
	print("interrupt (stagger): interrupted events %d, mana handed back %.1f, target lost %.1f" % [_driver.Interrupted, back, hp0 - _health(dummy)])
	if _driver.Interrupted != 1 or _driver.LastInterruptedSpell != "spell.probe_bolt":
		_fail("interrupt: SpellInterruptedEvent count %d spell '%s' after a stagger" % [_driver.Interrupted, _driver.LastInterruptedSpell])
	if back < 4.0 or back > 6.5:
		_fail("interrupt: a stagger refunded %.2f mana, expected half of 10" % back)
	if _health(dummy) < hp0:
		_fail("interrupt: a staggered cast still went off")
	if caster["casting"].PendingSpell != null:
		_fail("interrupt: the cancelled cast is still pending")
	await _free([caster, dummy])

	# silence in the wind-up, and cannot start while silenced
	caster = _actor("Caster", 1, Vector3(0, 0, 0), [bolt])
	dummy = _actor("Dummy", 2, Vector3(0, 0, -6))
	await _frames(3)
	_driver.Reset()
	hp0 = _health(dummy)
	await _cast_and_wait_for_windup(caster, "spell.probe_bolt", 6)
	_apply(caster, "res://data/status_effects/Silenced.tres")
	await _frames(90)
	print("interrupt (silence): interrupted events %d, target lost %.1f" % [_driver.Interrupted, hp0 - _health(dummy)])
	if _driver.Interrupted != 1 or _health(dummy) < hp0:
		_fail("interrupt: a silence in the wind-up did not cancel the cast")
	var m: float = _mana(caster)
	if caster["casting"].TryCastById("spell.probe_bolt") or caster["casting"].CanCast(bolt):
		_fail("interrupt: a silenced caster could start a cast")
	if absf(_mana(caster) - m) > 0.5:
		_fail("interrupt: a refused silenced cast spent mana")
	await _free([caster, dummy])

	# stun in the wind-up
	caster = _actor("Caster", 1, Vector3(0, 0, 0), [bolt])
	dummy = _actor("Dummy", 2, Vector3(0, 0, -6))
	await _frames(3)
	_driver.Reset()
	hp0 = _health(dummy)
	await _cast_and_wait_for_windup(caster, "spell.probe_bolt", 6)
	_apply(caster, "res://data/status_effects/Stunned.tres")
	await _frames(6)
	if _driver.Interrupted != 1:
		_fail("interrupt: a stun in the wind-up did not cancel the cast")
	if caster["casting"].CanCast(bolt):
		_fail("interrupt: a stunned caster could start a cast")
	await _frames(60)
	if _health(dummy) < hp0:
		_fail("interrupt: a stunned caster's cancelled cast still landed")
	await _free([caster, dummy])


func _check_armour() -> void:
	# spell.Interruptible = false rides a stagger out
	var firm := _spell("spell.probe_firm", {"Interruptible": false})
	var bolt := _spell("spell.probe_bolt", {})
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [firm, bolt])
	var dummy := _actor("Dummy", 2, Vector3(0, 0, -6))
	await _frames(3)
	_driver.Reset()
	var hp0: float = _health(dummy)
	await _cast_and_wait_for_windup(caster, "spell.probe_firm", 6)
	caster["combat"].Stagger(0.6, 0, 2)
	await _frames(90)
	print("armour (Interruptible=false): interrupted %d, target lost %.1f" % [_driver.Interrupted, hp0 - _health(dummy)])
	if _driver.Interrupted != 0 or _health(dummy) >= hp0:
		_fail("armour: an uninterruptible spell was cancelled by a stagger")
	await _free([caster, dummy])

	# Barkskin lets an ordinary cast finish through a stagger
	caster = _actor("Caster", 1, Vector3(0, 0, 0), [bolt])
	dummy = _actor("Dummy", 2, Vector3(0, 0, -6))
	await _frames(3)
	_driver.Reset()
	hp0 = _health(dummy)
	_apply(caster, "res://data/status_effects/Barkskin.tres")
	await _cast_and_wait_for_windup(caster, "spell.probe_bolt", 6)
	caster["combat"].Stagger(0.6, 0, 2)
	await _frames(90)
	print("armour (Barkskin): interrupted %d, target lost %.1f" % [_driver.Interrupted, hp0 - _health(dummy)])
	if _driver.Interrupted != 0 or _health(dummy) >= hp0:
		_fail("armour: a Barkskin caster's cast was cancelled by a stagger")
	await _free([caster, dummy])


func _check_channel() -> void:
	var beam := _res("res://data/spells/StormConduit.tres")
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [beam])
	var dummy := _actor("Dummy", 2, Vector3(0, 0, -6))
	var sc = caster["casting"]
	await _frames(3)
	_driver.Reset()
	var speed0: float = caster["stats"].GetValue(MOVE_SPEED)
	var hp0: float = _health(dummy)
	var mana0: float = _mana(caster)
	if not sc.BeginCastById("spell.storm_conduit"):
		_fail("channel: BeginCastById refused")
		await _free([caster, dummy])
		return
	if sc.PendingSpell == null or absf(_driver.LastWindup - 0.25) > 0.04:
		_fail("channel: actual Storm Conduit skipped its authored .25-second wind-up")
	for i in 8:
		sc.UpdateCast(1.0 / 60.0)
		await physics_frame
	if _health(dummy) < hp0 or _driver.Casts > 1 or _mana(caster) < mana0 - 0.5:
		_fail("channel: damage or tick mana was delivered during the wind-up")
	if caster["action"].TryAttack():
		_fail("channel: melee started inside channel wind-up commitment")
	for i in 72:
		sc.UpdateCast(1.0 / 60.0)
		await physics_frame
	var slowed: float = caster["stats"].GetValue(MOVE_SPEED)
	print("channel: speed %.2f -> %.2f while held, %d casts, channelling %s" % [speed0, slowed, _driver.Casts, sc.IsChanneling])
	if not sc.IsChanneling or _driver.Casts < 2:
		_fail("channel: the beam did not sustain (%d ticks)" % _driver.Casts)
	if slowed > speed0 * 0.7:
		_fail("channel: a channel did not slow the caster (%.2f of %.2f)" % [slowed, speed0])
	if not caster["action"].IsCommitted:
		_fail("channel: a sustained beam stopped owning the action after its initial timeline")
	sc.EndCast()
	if sc.IsChanneling or not caster["action"].IsCommitted:
		_fail("channel: release skipped authored recovery")
	await _frames(6)
	if not caster["action"].IsCommitted:
		_fail("channel: .2-second recovery ended before .1 seconds")
	await _frames(10)
	if caster["action"].Current != null:
		_fail("channel: action did not finish its .2-second recovery")
	if absf(caster["stats"].GetValue(MOVE_SPEED) - speed0) > 0.01:
		_fail("channel: slow survived an ordinary channel release")
	await _frames(45)
	caster["stats"].SetCurrent(MANA, 50.0)
	sc.BeginCastById("spell.storm_conduit")
	for i in 24:
		sc.UpdateCast(1.0 / 60.0)
		await physics_frame
	caster["combat"].Stagger(0.5, 0, 2)
	await _frames(4)
	if sc.IsChanneling:
		_fail("channel: a stagger did not drop the channel")
	if _driver.Interrupted < 1:
		_fail("channel: no SpellInterruptedEvent for a dropped channel")
	if absf(caster["stats"].GetValue(MOVE_SPEED) - speed0) > 0.01:
		_fail("channel: the slow outlived the channel (%.2f)" % caster["stats"].GetValue(MOVE_SPEED))
	await _frames(60)
	caster["stats"].SetCurrent(MANA, 50.0)
	caster["action"].TryAttack()
	if sc.BeginCastById("spell.storm_conduit") or sc.IsChanneling:
		_fail("channel: BeginCastById reported success while another action was committed")
	await _free([caster, dummy])
	await _clear_transients()


# ---- blows ---------------------------------------------------------------------------------------

# Casts `id`, raises the target's guard the moment the spell is released (so a Normal blow would land
# inside the parry window), and returns the final amount of the hit.
func _cast_at_guard(caster: Dictionary, target: Dictionary, id: String) -> float:
	_driver.Reset()
	caster["casting"].TryCastById(id)
	var guard = target["combat"]
	for i in 120:
		if caster["casting"].PendingSpell == null and not guard.IsBlocking:
			guard.IsBlocking = true
		await physics_frame
		if _driver.Hits + _driver.Blocked > 0:
			break
	await _frames(3)
	return _driver.LastHitAmount


func _check_blows() -> void:
	var bolt := _spell("spell.probe_bolt", {})
	var unblockable := _spell("spell.probe_unblockable", {"Blockable": false})
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [bolt, unblockable])

	# open target: the baseline
	var open := _actor("Open", 2, Vector3(0, 0, -6), [], 180.0)
	await _frames(3)
	_driver.Reset()
	caster["casting"].TryCastById("spell.probe_bolt")
	await _frames(80)
	var base: float = _driver.LastHitAmount
	await _free([open])

	# guarded target facing the caster: blocked, chip damage, never parried
	var guarded := _actor("Guarded", 2, Vector3(0, 0, -6), [], 180.0)
	await _frames(3)
	caster["stats"].SetCurrent(MANA, 50.0)
	await _frames(40)
	var chip: float = await _cast_at_guard(caster, guarded, "spell.probe_bolt")
	var chip_lost: float = guarded["stats"].GetMax(HEALTH) - _health(guarded)
	print("blows: unguarded %.1f, guarded chip %.1f, blocked events %d" % [base, chip_lost, _driver.Blocked])
	if base <= 0.0:
		_fail("blows: the baseline spell did no damage")
	if _driver.Blocked < 1:
		_fail("blows: a guarded spell raised no SpellBlockedEvent (blockable)")
	if _health(guarded) >= guarded["stats"].GetMax(HEALTH):
		_fail("blows: a spell was parried outright (spells are blockable, never parryable): target took nothing")
	var lost: float = guarded["stats"].GetMax(HEALTH) - _health(guarded)
	if lost >= base * 0.6:
		_fail("blows: the guard did not soften the spell (%.1f of %.1f)" % [lost, base])

	# unblockable ignores the guard
	guarded["stats"].SetCurrent(HEALTH, guarded["stats"].GetMax(HEALTH))
	guarded["combat"].IsBlocking = false
	await _frames(60)
	caster["stats"].SetCurrent(MANA, 50.0)
	await _cast_at_guard(caster, guarded, "spell.probe_unblockable")
	var full: float = guarded["stats"].GetMax(HEALTH) - _health(guarded)
	print("blows: unblockable through a guard did %.1f, blocked events %d" % [full, _driver.Blocked])
	if _driver.Blocked != 0:
		_fail("blows: an unblockable spell was blocked")
	if absf(full - base) > base * 0.1:
		_fail("blows: an unblockable spell did %.1f through a guard, expected about %.1f" % [full, base])
	await _free([guarded])

	# poise damage
	var tough := _actor("Tough", 2, Vector3(0, 0, -6), [], 180.0, 20.0)
	await _frames(3)
	caster["stats"].SetCurrent(MANA, 50.0)
	caster["casting"].TryCastById("spell.probe_bolt")
	await _frames(80)
	var soft: bool = tough["combat"].IsStaggered
	await _free([tough])
	var breaker := _spell("spell.probe_breaker", {"PoiseDamage": 200.0})
	caster["casting"].Teach(breaker)
	tough = _actor("Tough", 2, Vector3(0, 0, -6), [], 180.0, 20.0)
	await _frames(40)
	caster["stats"].SetCurrent(MANA, 50.0)
	caster["casting"].TryCastById("spell.probe_breaker")
	await _frames(80)
	var broken: bool = tough["combat"].IsStaggered
	print("blows: default poise damage staggers %s, PoiseDamage 200 staggers %s" % [soft, broken])
	if soft:
		_fail("blows: the default spell poise damage staggered a 20-poise body")
	if not broken:
		_fail("blows: spell.PoiseDamage was not honoured")
	await _free([tough])

	# charge: a tap is blocked, a full hold crushes the guard
	var lance := _spell("spell.probe_lance", {"CastMode": MODE_CHARGED, "ChargeTime": 0.5, "MaxChargeMultiplier": 2.0,
		"WindupSeconds": 0.2})
	caster["casting"].Teach(lance)
	var target := _actor("Guarded", 2, Vector3(0, 0, -6), [], 180.0)
	await _frames(40)
	caster["stats"].SetCurrent(MANA, 50.0)
	caster["casting"].BeginCastById("spell.probe_lance")
	caster["casting"].UpdateCast(1.0 / 60.0)
	_driver.Reset()
	caster["casting"].EndCast()
	var tap_staggered := false
	for i in 90:
		if caster["casting"].PendingSpell == null and not target["combat"].IsBlocking:
			target["combat"].IsBlocking = true
		await physics_frame
		tap_staggered = tap_staggered or target["combat"].IsStaggered
	print("blows: a tapped lance staggered the guard %s, blocked events %d" % [tap_staggered, _driver.Blocked])
	if _driver.Blocked < 1 or tap_staggered:
		_fail("blows: a tapped charged spell should be blocked, not crush the guard")
	await _free([target])

	target = _actor("Guarded", 2, Vector3(0, 0, -6), [], 180.0)
	await _frames(40)
	caster["stats"].SetCurrent(MANA, 50.0)
	caster["casting"].BeginCastById("spell.probe_lance")
	for i in 40:
		caster["casting"].UpdateCast(1.0 / 60.0)
		await physics_frame
	var progress: float = caster["casting"].ChargeProgress
	_driver.Reset()
	caster["casting"].EndCast()
	var max_hp: float = target["stats"].GetMax(HEALTH)
	var crushed := false
	for i in 100:
		if caster["casting"].PendingSpell == null and not target["combat"].IsBlocking:
			target["combat"].IsBlocking = true
		await physics_frame
		crushed = crushed or target["combat"].IsStaggered
	var took: float = max_hp - _health(target)
	print("blows: full charge %.2f, guard crushed %s, target took %.1f (baseline %.1f)" % [progress, crushed, took, base])
	if progress < 0.95:
		_fail("blows: the held charge reached only %.2f" % progress)
	if not crushed:
		_fail("blows: a full charge did not crush the guard")
	if took < base * 1.6:
		_fail("blows: a full charge did %.1f, expected about twice the %.1f baseline" % [took, base])
	await _free([caster, target])


# ---- ground --------------------------------------------------------------------------------------

func _check_ground() -> void:
	var meteor := _spell("spell.probe_meteor", {"Delivery": DELIVERY_GROUND, "ImpactRadius": 3.0, "GroundDelay": 0.8,
		"PlaceRange": 6.0, "BaseDamage": 25.0, "WindupSeconds": 0.2, "RecoverySeconds": 0.2})
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [meteor])
	var near := _actor("Near", 2, Vector3(0, 0, -6))
	var far := _actor("Far", 2, Vector3(0, 0, -14))
	await _frames(3)
	_driver.Reset()
	var near0: float = _health(near)
	var far0: float = _health(far)
	caster["casting"].TryCastById("spell.probe_meteor")
	var spawned := -1
	var landed := -1
	var ground = null
	for i in 240:
		await physics_frame
		if spawned < 0:
			ground = _find("SpellGround")
			if ground != null:
				spawned = i
		if landed < 0 and _health(near) < near0:
			landed = i
	if spawned < 0:
		_fail("ground: no ground spell appeared after the cast released")
		await _free([caster, near, far])
		return
	var delay: float = float(landed - spawned) / 60.0
	print("ground: spawned frame %d, landed frame %d -> %.2fs after the ring appeared (GroundDelay 0.8)" % [spawned, landed, delay])
	if landed < 0:
		_fail("ground: the blast never landed")
	elif absf(delay - 0.8) > 0.12:
		_fail("ground: the blast landed %.2fs after its ring appeared, expected 0.8" % delay)
	if _health(far) < far0:
		_fail("ground: a foe beyond the placed point's radius was hit")
	await _frames(2)

	# placement clamp: aim far beyond PlaceRange, the ring lands at 6 m
	caster["stats"].SetCurrent(MANA, 50.0)
	await _frames(70)
	caster["casting"].TryCastById("spell.probe_meteor")
	var placed = null
	var ring_active := false
	var ring_seconds := 0.0
	for i in 60:
		await physics_frame
		placed = _find("SpellGround")
		if placed != null:
			break
	if placed == null:
		_fail("ground: second cast produced no ground spell")
	else:
		var pos: Vector3 = placed.global_position
		ring_active = placed.Ring.IsActive
		print("ground: placed at %s, ring armed %s" % [pos, ring_active])
		if absf(pos.z + 6.0) > 0.3 or absf(pos.x) > 0.3:
			_fail("ground: aimed past PlaceRange 6, the spell was placed at %s" % pos)
		if absf(pos.y) > 0.2:
			_fail("ground: the spell was not dropped onto the floor (y %.2f)" % pos.y)
		if not ring_active:
			_fail("ground: the telegraph ring was not armed")
	await _free([caster, near, far])
	await _clear_transients()

	# pull
	var well := _spell("spell.probe_well", {"Delivery": DELIVERY_GROUND, "ImpactRadius": 5.0, "GroundDelay": 0.3,
		"PlaceRange": 8.0, "BaseDamage": 1.0, "PullStrength": 6.0, "PullSeconds": 1.0, "WindupSeconds": 0.2})
	caster = _actor("Caster", 1, Vector3(0, 0, 0), [well])
	var pulled := _actor("Pulled", 2, Vector3(0, 0, -11))
	await _frames(3)
	var z0: float = pulled["body"].global_position.z
	caster["casting"].TryCastById("spell.probe_well")
	await _frames(150)
	var z1: float = pulled["body"].global_position.z
	print("ground: pulled foe moved from z %.2f to %.2f" % [z0, z1])
	if z1 - z0 < 1.0:
		_fail("ground: PullStrength did not draw the foe toward the centre (%.2f m)" % (z1 - z0))
	await _free([caster, pulled])
	await _clear_transients()


# ---- barrier -------------------------------------------------------------------------------------

func _check_barrier() -> void:
	var bolt := _spell("spell.probe_bolt", {"Cooldown": 0.3, "WindupSeconds": 0.15, "RecoverySeconds": 0.1})
	var wall := _spell("spell.probe_pyre", {"Delivery": DELIVERY_BARRIER, "BarrierDuration": 6.0, "BarrierWidth": 6.0,
		"BarrierBlocksProjectiles": true, "BarrierBlocksBodies": false, "PlaceRange": 5.0, "BaseDamage": 3.0,
		"StatusEffectId": "status.burning", "WindupSeconds": 0.15, "RecoverySeconds": 0.1, "ZoneTickInterval": 0.5,
		"GroundDelay": 0.4})
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [wall])
	var enemy := _actor("Enemy", 2, Vector3(0, 0, -10), [bolt], 180.0)
	var stander := _actor("Stander", 2, Vector3(0, 0, -5))
	await _frames(3)

	# control: no wall, the enemy bolt lands on the caster and an arrow hurts it
	_driver.Reset()
	var hp0: float = _health(caster)
	stander["body"].global_position = Vector3(20, 0, -5)
	enemy["casting"].TryCastById("spell.probe_bolt")
	await _frames(80)
	var control: float = hp0 - _health(caster)
	_driver.FireArrow(_world, enemy["body"], Vector3(0, 1.0, -10), Vector3(0, 0, 1), 8.0, 2)
	var hp1: float = _health(caster)
	await _frames(60)
	var arrow_control: float = hp1 - _health(caster)
	print("barrier: control bolt %.1f, control arrow %.1f" % [control, arrow_control])
	if control <= 0.0:
		_fail("barrier: the control bolt never reached the caster")
	if arrow_control <= 0.0:
		_fail("barrier: the control arrow never reached the caster")

	# the wall: cast it 5 m ahead, then shoot through it
	stander["body"].global_position = Vector3(0, 0, -5)
	caster["stats"].SetCurrent(MANA, 50.0)
	caster["stats"].SetCurrent(HEALTH, caster["stats"].GetMax(HEALTH))
	_driver.Reset()
	caster["casting"].TryCastById("spell.probe_pyre")
	var barrier = null
	for i in 60:
		await physics_frame
		barrier = _find("SpellBarrier")
		if barrier != null:
			break
	if barrier == null:
		_fail("barrier: no wall appeared")
		await _free([caster, enemy, stander])
		return
	print("barrier: wall at %s, width %.1f, standing at once %s, ring armed %s" % [barrier.global_position, barrier.Width, barrier.Active, barrier.Ring.IsActive])
	if barrier.Active or not barrier.Ring.IsActive:
		_fail("barrier: a wall with a GroundDelay must telegraph before it stands (active %s, ring %s)" % [barrier.Active, barrier.Ring.IsActive])
	if absf(barrier.global_position.z + 5.0) > 0.4:
		_fail("barrier: the wall stands at z %.2f, expected about -5" % barrier.global_position.z)
	await _frames(45)
	var burned: bool = _has_status(stander, "status.burning")
	print("barrier: a foe standing in the fire is burning %s (health %.1f of %.1f)"
		% [burned, _health(stander), stander["stats"].GetMax(HEALTH)])
	if not burned or _health(stander) >= stander["stats"].GetMax(HEALTH):
		_fail("barrier: a non-solid wall did not burn what stood in it")
	stander["body"].global_position = Vector3(20, 0, -5)

	_driver.Reset()
	var hp2: float = _health(caster)
	enemy["casting"].TryCastById("spell.probe_bolt")
	await _frames(80)
	_driver.FireArrow(_world, enemy["body"], Vector3(0, 1.0, -10), Vector3(0, 0, 1), 8.0, 2)
	await _frames(60)
	var through: float = hp2 - _health(caster)
	print("barrier: through the wall the caster lost %.1f; blocked events %d" % [through, _driver.Blocked])
	if through > 0.01:
		_fail("barrier: a wall that blocks projectiles let %.1f damage through" % through)
	if _driver.Blocked < 1:
		_fail("barrier: a bolt eaten by a wall raised no SpellBlockedEvent")
	if not is_instance_valid(barrier) or barrier.Ended:
		_fail("barrier: an unbreakable wall ended early")
	await _frames(420)
	if _driver.BarrierEnded != 1 or _driver.BarrierBroken != 0:
		_fail("barrier: expiry raised %d ended / %d broken events, expected 1 / 0" % [_driver.BarrierEnded, _driver.BarrierBroken])
	await _free([caster, enemy, stander])
	await _clear_transients()

	# a solid, breakable wall
	var ice := _spell("spell.probe_ice", {"Delivery": DELIVERY_BARRIER, "BarrierDuration": 8.0, "BarrierWidth": 6.0,
		"BarrierBlocksProjectiles": true, "BarrierBlocksBodies": true, "BarrierHealth": 30.0, "PlaceRange": 5.0,
		"BaseDamage": 0.0, "WindupSeconds": 0.15, "RecoverySeconds": 0.1})
	var hard := _spell("spell.probe_hard", {"BaseDamage": 20.0, "Cooldown": 0.3, "WindupSeconds": 0.15,
		"RecoverySeconds": 0.1, "ManaCost": 5.0})
	caster = _actor("Caster", 1, Vector3(0, 0, 0), [ice])
	enemy = _actor("Enemy", 2, Vector3(0, 0, -10), [hard], 180.0)
	var walker := _actor("Walker", 2, Vector3(0, 0, -8))
	await _frames(3)
	_driver.Reset()
	caster["casting"].TryCastById("spell.probe_ice")
	barrier = null
	for i in 60:
		await physics_frame
		barrier = _find("SpellBarrier")
		if barrier != null:
			break
	if barrier == null:
		_fail("barrier: the solid wall never appeared")
		await _free([caster, enemy, walker])
		return
	await _frames(3)
	var solid = barrier.get_node_or_null("Solid")
	if solid == null or solid.collision_layer != 1:
		_fail("barrier: a solid wall has no world-layer body")
	var moved = walker["body"].move_and_collide(Vector3(0, 0, 6.0))
	var wz: float = walker["body"].global_position.z
	print("barrier: a body pushed 6 m at the solid wall stopped at z %.2f (collided %s)" % [wz, moved != null])
	if wz > -5.0:
		_fail("barrier: a body walked through a solid wall (z %.2f)" % wz)

	# two 20-damage bolts break a 30-health wall; the third reaches the caster
	walker["body"].global_position = Vector3(20, 0, -8)
	var hp3: float = _health(caster)
	_driver.FireArrow(_world, enemy["body"], Vector3(0, 1.0, -10), Vector3(0, 0, 1), 8.0, 2)
	await _frames(30)
	if not is_instance_valid(barrier) or absf(barrier.Health - 22.0) > 0.01:
		_fail("barrier: arrow near-face collision did not take 8 health from the solid wall")
	enemy["casting"].TryCastById("spell.probe_hard")
	await _frames(70)
	if not is_instance_valid(barrier) or barrier.Ended:
		_fail("barrier: a wall with 22 health broke after one 20-damage bolt")
	elif absf(barrier.Health - 2.0) > 0.1:
		_fail("barrier: bolt volume near-face collision did not take 20 health from the solid wall")
	enemy["stats"].SetCurrent(MANA, 50.0)
	enemy["casting"].TryCastById("spell.probe_hard")
	await _frames(70)
	print("barrier: broken events %d, blocked %d, caster health lost so far %.1f" % [_driver.BarrierBroken, _driver.Blocked, hp3 - _health(caster)])
	if _driver.BarrierBroken != 1:
		_fail("barrier: the wall did not break after %s damage (broken events %d)" % [40, _driver.BarrierBroken])
	if _health(caster) < hp3:
		_fail("barrier: damage reached the caster while the wall stood")
	enemy["stats"].SetCurrent(MANA, 50.0)
	enemy["casting"].TryCastById("spell.probe_hard")
	await _frames(70)
	if _health(caster) >= hp3:
		_fail("barrier: once broken the wall still stopped bolts")
	await _free([caster, enemy, walker])
	await _clear_transients()


# ---- dash and blink ------------------------------------------------------------------------------

func _dash_line(status_id: String, dodge_final := false) -> void:
	var step := _spell("spell.probe_step", {"Delivery": DELIVERY_DASH, "DashDistance": 8.0, "DashHitRadius": 1.2,
		"BaseDamage": 10.0, "WindupSeconds": 0.15, "RecoverySeconds": 0.1, "StatusEffectId": status_id})
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [step])
	var f1 := _actor("F1", 2, Vector3(0, 0, -3))
	var f2 := _actor("F2", 2, Vector3(0.3, 0, -6))
	var f3 := _actor("F3", 2, Vector3(0, 0, -12))
	await _frames(3)
	f2["combat"].IsInvulnerable = dodge_final
	_driver.Reset()
	var hp := [_health(f1), _health(f2), _health(f3)]
	caster["casting"].TryCastById("spell.probe_step")
	var stunned_f1 := false
	var stunned_f2 := false
	for i in 60:
		await physics_frame
		stunned_f1 = stunned_f1 or _has_status(f1, "status.stunned")
		stunned_f2 = stunned_f2 or _has_status(f2, "status.stunned")
	var z: float = caster["body"].global_position.z
	print("dash (status '%s'): caster ended at z %.2f, hits %d, F1 stunned %s, F2 stunned %s"
		% [status_id, z, _driver.Hits, stunned_f1, stunned_f2])
	if absf(z + 8.0) > 0.4:
		_fail("dash: travelled to z %.2f, expected -8" % z)
	if _health(f1) >= hp[0] or (not dodge_final and _health(f2) >= hp[1]):
		_fail("dash: a foe on the line was not struck")
	if dodge_final and _health(f2) < hp[1]:
		_fail("dash: a dodging final candidate took damage")
	if _health(f3) < hp[2]:
		_fail("dash: a foe beyond the dash was struck")
	var expected_hits := 1 if dodge_final else 2
	if _driver.Hits != expected_hits:
		_fail("dash: %d hits for two foes on the line (once each)" % _driver.Hits)
	if not dodge_final and not stunned_f2:
		_fail("dash: the last foe struck was not stunned")
	if not dodge_final and stunned_f1:
		_fail("dash: a foe struck earlier on the line was stunned")
	if dodge_final and (not stunned_f1 or stunned_f2):
		_fail("dash: the last landed foe did not receive the stun when the final candidate dodged")
	await _free([caster, f1, f2, f3])
	await _clear_transients()


func _check_dash_and_blink() -> void:
	var step := _spell("spell.probe_step", {"Delivery": DELIVERY_DASH, "DashDistance": 8.0, "DashHitRadius": 1.2,
		"BaseDamage": 10.0, "WindupSeconds": 0.15, "RecoverySeconds": 0.1})
	await _dash_line("")
	await _dash_line("status.stunned")
	await _dash_line("status.stunned", true)

	# a wall stops the dash short
	var caster
	var z: float
	var wall := StaticBody3D.new()
	wall.collision_layer = 1
	var ws := CollisionShape3D.new()
	var wb := BoxShape3D.new()
	wb.size = Vector3(10, 4, 0.5)
	ws.shape = wb
	wall.add_child(ws)
	wall.position = Vector3(0, 2, -5)
	_world.add_child(wall)
	caster = _actor("Caster", 1, Vector3(0, 0, 0), [step])
	await _frames(3)
	caster["casting"].TryCastById("spell.probe_step")
	await _frames(60)
	z = caster["body"].global_position.z
	print("dash: with a wall at z -4.75 the caster stopped at z %.2f" % z)
	if z < -4.75 or z > -3.6:
		_fail("dash: with a wall ahead the caster ended at z %.2f, expected short of -4.75" % z)
	await _free([caster])

	# blink: clear vs against the wall
	var blink := _spell("spell.probe_blink", {"Delivery": DELIVERY_SELF, "BlinkDistance": 8.0, "ManaCost": 20.0,
		"BaseDamage": 0.0, "WindupSeconds": 0.15, "RecoverySeconds": 0.1})
	caster = _actor("Caster", 1, Vector3(20, 0, 0), [blink])
	await _frames(3)
	var m0: float = _mana(caster)
	caster["casting"].TryCastById("spell.probe_blink")
	var full_cost: float = m0 - _mana(caster)
	await _frames(40)
	var full_move: float = absf(caster["body"].global_position.z)
	await _free([caster])
	caster = _actor("Caster", 1, Vector3(0, 0, 0), [blink])
	await _frames(3)
	m0 = _mana(caster)
	caster["casting"].TryCastById("spell.probe_blink")
	var short_cost: float = m0 - _mana(caster)
	await _frames(40)
	var short_move: float = absf(caster["body"].global_position.z)
	print("blink: clear jump %.2f m costs %.1f; against a wall %.2f m costs %.1f" % [full_move, full_cost, short_move, short_cost])
	if absf(full_move - 8.0) > 0.4 or absf(full_cost - 20.0) > 1.2:
		_fail("blink: a clear jump moved %.2f m for %.1f mana, expected 8 m for 20" % [full_move, full_cost])
	if short_move > 4.6 or short_move < 3.8:
		_fail("blink: a jump at a wall moved %.2f m, expected about 4.25 (stopped short)" % short_move)
	if short_cost >= full_cost - 2.0:
		_fail("blink: the shorter jump cost %.1f, not less than the full %.1f" % [short_cost, full_cost])
	await _free([caster])
	# Turning away during wind-up cannot turn the discounted wall jump into a long jump.
	caster = _actor("TurnedBlink", 1, Vector3(0, 0, 0), [blink])
	await _frames(3)
	caster["casting"].TryCastById("spell.probe_blink")
	caster["body"].get_node("Aim").rotation_degrees.y = 180.0
	await _frames(40)
	var turned: Vector3 = caster["body"].global_position
	if turned.z > -3.8 or turned.z < -4.6 or absf(turned.x) > 0.1:
		_fail("blink: turning during wind-up changed the committed jump (%s)" % turned)
	await _free([caster])
	# A new obstruction shortens the committed path and refunds its unused distance cost.
	caster = _actor("ObstructedBlink", 1, Vector3(20, 0, 0), [blink])
	await _frames(3)
	m0 = _mana(caster)
	caster["casting"].TryCastById("spell.probe_blink")
	wall.global_position.x = 20.0
	await _frames(40)
	var stopped: Vector3 = caster["body"].global_position
	var final_cost: float = m0 - _mana(caster)
	if stopped.z > -3.8 or stopped.z < -4.6 or final_cost > short_cost + 0.2:
		_fail("blink: a new obstacle was not reflected in placement and refunded cost (%s, %.2f)" % [stopped, final_cost])
	await _free([caster, wall])


# ---- costs and identity --------------------------------------------------------------------------

func _check_costs() -> void:
	var tithe := _spell("spell.probe_tithe", {"HealthCost": 30.0, "ManaCost": 5.0,
		"SelfStatusEffectId": "status.barkskin", "WindupSeconds": 0.15, "RecoverySeconds": 0.1})
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [tithe])
	var dummy := _actor("Dummy", 2, Vector3(0, 0, -6))
	await _frames(3)
	var hp0: float = _health(caster)
	caster["casting"].TryCastById("spell.probe_tithe")
	await _frames(40)
	var paid: float = hp0 - _health(caster)
	print("costs: health paid %.1f, barkskin on release %s" % [paid, _has_status(caster, "status.barkskin")])
	if absf(paid - 30.0) > 1.0:
		_fail("costs: the health cost paid %.1f, expected 30" % paid)
	if not _has_status(caster, "status.barkskin"):
		_fail("costs: SelfStatusEffectId was not applied on release")
	caster["stats"].SetCurrent(HEALTH, 20.0)
	await _frames(70)
	if caster["casting"].CanCast(tithe) or caster["casting"].TryCastById("spell.probe_tithe"):
		_fail("costs: a health cost above the caster's health was not refused")
	if _health(caster) < 19.5:
		_fail("costs: the refused cast still cost health (%.1f)" % _health(caster))
	await _free([caster, dummy])

	# consume-and-bonus
	var eater := _spell("spell.probe_eater", {"ConsumesStatusId": "status.burning", "BonusPerConsumedStack": 1.0,
		"ManaCost": 5.0, "Cooldown": 0.1, "WindupSeconds": 0.15, "RecoverySeconds": 0.1})
	caster = _actor("Caster", 1, Vector3(0, 0, 0), [eater])
	dummy = _actor("Dummy", 2, Vector3(0, 0, -6))
	await _frames(3)
	_driver.Reset()
	caster["casting"].TryCastById("spell.probe_eater")
	await _frames(70)
	var plain: float = _driver.LastHitAmount
	_apply(dummy, "res://data/status_effects/Burning.tres")
	_apply(dummy, "res://data/status_effects/Burning.tres")
	var stacks: int = dummy["status"].StacksOf("status.burning")
	await _frames(20)
	caster["stats"].SetCurrent(MANA, 50.0)
	caster["casting"].TryCastById("spell.probe_eater")
	await _frames(70)
	var boosted: float = _driver.LastHitAmount
	print("costs: consume %d stack(s): %.1f -> %.1f, still burning %s" % [stacks, plain, boosted, _has_status(dummy, "status.burning")])
	if stacks < 1:
		_fail("costs: the probe could not stack a status to consume")
	elif absf(boosted / plain - (1.0 + stacks)) > 0.15:
		_fail("costs: consuming %d stacks changed damage by x%.2f, expected x%d" % [stacks, boosted / plain, 1 + stacks])
	if _has_status(dummy, "status.burning"):
		_fail("costs: the consumed status was not stripped")
	await _free([caster, dummy])


# ---- pierce and homing ---------------------------------------------------------------------------

func _check_roster_identities() -> void:
	# Knit Bone consumes the actual Decay resource on the Self path and scales its heal per stack.
	var knit := _res("res://data/spells/KnitBone.tres")
	var plain := _actor("PlainKnit", 1, Vector3(0, 0, 0), [knit])
	await _frames(3)
	plain["stats"].SetCurrent(HEALTH, 10.0)
	plain["casting"].TryCastById("spell.knit_bone")
	await _frames(50)
	var plain_heal: float = _health(plain) - 10.0
	await _free([plain])
	var afflicted := _actor("AfflictedKnit", 1, Vector3(0, 0, 0), [knit])
	await _frames(3)
	afflicted["stats"].SetCurrent(HEALTH, 10.0)
	for i in 3:
		_apply(afflicted, "res://data/status_effects/Decay.tres")
	var stacks: int = afflicted["status"].StacksOf("status.decay")
	afflicted["casting"].TryCastById("spell.knit_bone")
	await _frames(50)
	var healed: float = _health(afflicted) - 10.0
	if _has_status(afflicted, "status.decay") or absf(healed - plain_heal * (1.0 + stacks * knit.BonusPerConsumedStack)) > 0.1:
		_fail("roster: Knit Bone retained Decay or lost stack healing (plain %.1f, %d stacks %.1f)" % [plain_heal, stacks, healed])
	await _free([afflicted])

	# Soul Tithe refunds precisely its own killing projectile. A later kill under Soul Echo does not.
	var tithe := _res("res://data/spells/SoulTithe.tres")
	var emberlash := _res("res://data/spells/Emberlash.tres")
	var caster := _actor("TitheCaster", 1, Vector3(0, 0, 0), [tithe, emberlash])
	var foe := _actor("TitheVictim", 2, Vector3(0, 0, -6))
	await _frames(3)
	foe["stats"].SetCurrent(HEALTH, 1.0)
	var mana0: float = _mana(caster)
	caster["casting"].TryCastById("spell.soul_tithe")
	await _frames(75)
	if _health(foe) > 0.0 or absf(_mana(caster) - mana0) > 0.1:
		_fail("roster: Soul Tithe killing hit did not refund its 12 mana exactly once")
	await _free([foe])
	foe = _actor("UnrelatedVictim", 2, Vector3(0, 0, -6))
	await _frames(3)
	foe["stats"].SetCurrent(HEALTH, 1.0)
	mana0 = _mana(caster)
	caster["casting"].TryCastById("spell.emberlash")
	await _frames(60)
	if _health(foe) > 0.0 or absf(_mana(caster) - (mana0 - emberlash.ManaCost)) > 0.1:
		_fail("roster: Soul Echo refunded mana for an unrelated spell kill")
	await _free([caster, foe])

	# The real Sunfall resource crushes at its ground centre; a full-charge edge is still blockable.
	var sunfall := _res("res://data/spells/Sunfall.tres")
	caster = _actor("MeteorCaster", 1, Vector3(0, 0, 0))
	var centre := _actor("CentreGuard", 2, Vector3(0, 0, -6), [], 180.0)
	var edge := _actor("EdgeGuard", 2, Vector3(2.5, 0, -6), [], 180.0)
	await _frames(3)
	centre["combat"].IsBlocking = true
	edge["combat"].IsBlocking = true
	_driver.Reset()
	_driver.GroundImpact(_world, sunfall, caster["body"], Vector3(0, 0.3, -6), 10.0, 1.0, 1)
	if not centre["combat"].IsStaggered or edge["combat"].IsStaggered or _driver.Blocked != 1:
		_fail("roster: Sunfall direct guard crush or blockable full-charge edge failed")
	await _free([caster, centre, edge])
	await _clear_transients()


func _line_of_four() -> Array:
	var foes := []
	for i in 4:
		foes.append(_actor("Foe%d" % i, 2, Vector3(0, 0, -4.0 - 3.0 * i)))
	return foes


func _struck_count(foes: Array, hp: Array) -> int:
	var n := 0
	for i in foes.size():
		if _health(foes[i]) < hp[i]:
			n += 1
	return n


func _check_pierce_and_homing() -> void:
	var lance := _res("res://data/spells/FlameLance.tres")
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [lance])
	var foes := _line_of_four()
	await _frames(3)
	var hp := []
	for f in foes:
		hp.append(_health(f))
	caster["casting"].BeginCastById("spell.flame_lance")
	caster["casting"].UpdateCast(1.0 / 60.0)
	caster["casting"].EndCast()
	await _frames(90)
	var tap := _struck_count(foes, hp)
	var tap_burn: float = _driver.StatusRemaining(foes[0]["status"], "status.burning")
	await _free(foes)

	foes = _line_of_four()
	await _frames(70)
	hp = []
	for f in foes:
		hp.append(_health(f))
	caster["stats"].SetCurrent(MANA, 50.0)
	caster["casting"].BeginCastById("spell.flame_lance")
	for i in 65:
		caster["casting"].UpdateCast(1.0 / 60.0)
		await physics_frame
	caster["casting"].EndCast()
	await _frames(90)
	var full := _struck_count(foes, hp)
	var full_burn: float = _driver.StatusRemaining(foes[0]["status"], "status.burning")
	print("pierce: a tapped lance struck %d foes, a full charge struck %d" % [tap, full])
	if tap != 2:
		_fail("pierce: a tapped lance (PierceCount 1) struck %d foes, expected 2" % tap)
	if full != 4:
		_fail("pierce: a full charge (1 + 2) struck %d of 4 foes, expected 4" % full)
	if tap_burn <= 0.0 or full_burn < tap_burn + 3.5:
		_fail("pierce: actual Flame Lance full charge did not extend Burning (tap %.2f, full %.2f)" % [tap_burn, full_burn])
	await _free(foes)
	await _free([caster])

	# homing prefers a Stormbranded foe
	var orb := _spell("spell.probe_orb", {"HomingRange": 12.0, "ProjectileSpeed": 8.0, "BaseDamage": 12.0,
		"WindupSeconds": 0.15, "RecoverySeconds": 0.1, "Cooldown": 0.1})
	caster = _actor("Caster", 1, Vector3(0, 0, 0), [orb])
	var plain_foe := _actor("Plain", 2, Vector3(-2.5, 0, -8))
	var branded := _actor("Branded", 2, Vector3(3.5, 0, -9))
	await _frames(3)
	var plain_hp: float = _health(plain_foe)
	var brand_hp: float = _health(branded)
	caster["casting"].TryCastById("spell.probe_orb")
	await _frames(150)
	var plain_hit: bool = _health(plain_foe) < plain_hp
	var brand_hit: bool = _health(branded) < brand_hp
	print("homing (no brand): plain foe hit %s, other foe hit %s" % [plain_hit, brand_hit])
	if not plain_hit or brand_hit:
		_fail("homing: with no brand the orb should hunt the nearer foe (plain %s, other %s)" % [plain_hit, brand_hit])
	plain_foe["stats"].SetCurrent(HEALTH, plain_hp)
	_apply(branded, "res://data/status_effects/Stormbrand.tres")
	caster["stats"].SetCurrent(MANA, 50.0)
	await _frames(15)
	plain_hp = _health(plain_foe)
	brand_hp = _health(branded)
	caster["casting"].TryCastById("spell.probe_orb")
	await _frames(170)
	plain_hit = _health(plain_foe) < plain_hp
	brand_hit = _health(branded) < brand_hp
	print("homing (branded): plain foe hit %s, branded foe hit %s" % [plain_hit, brand_hit])
	if not brand_hit or plain_hit:
		_fail("homing: the orb did not prefer the Stormbranded foe (plain %s, branded %s)" % [plain_hit, brand_hit])
	await _free([caster, plain_foe, branded])


# ---- totem ---------------------------------------------------------------------------------------

func _check_actor_geometry() -> void:
	var bolt := _spell("spell.probe_actor_collision", {"WindupSeconds": 0.05, "RecoverySeconds": 0.1,
		"ManaCost": 0.0, "ImpactRadius": 0.0})
	var caster := _actor("GeometryCaster", 1, Vector3.ZERO, [bolt])
	var ally := _actor("GeometryAlly", 1, Vector3(0, 0, -3))
	var target := _actor("GeometryTarget", 2, Vector3(0, 0, -6))
	# Production CharacterEntity bodies occupy the legacy world layer. Earlier magic probes
	# used layer 2 exclusively, hiding actor capsules mistaken for spell-blocking walls.
	for actor in [caster, ally, target]:
		actor["body"].collision_layer = 1
	await _frames(3)
	var target_hp: float = _health(target)
	var ally_hp: float = _health(ally)
	if not caster["casting"].TryCastById(bolt.Id):
		_fail("geometry: control projectile was refused")
	await _frames(70)
	if _health(target) >= target_hp or _health(ally) != ally_hp:
		_fail("geometry: a projectile must pass an allied legacy-world capsule and hit the hostile hurtbox")
	target_hp = _health(target)
	bolt.ImpactRadius = 10.0
	_driver.GroundImpact(_world, bolt, caster["body"], Vector3(0, 1, -1), 10.0, 0.0, 1)
	if _health(target) >= target_hp or _health(ally) != ally_hp:
		_fail("geometry: actor capsules must not occlude a burst against a hostile target")
	# A physical world body on layer 2 is cover, even though actors on that layer are skipped.
	var wall := StaticBody3D.new()
	wall.collision_layer = 2
	var holder := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(4, 3, 0.2)
	holder.shape = box
	wall.add_child(holder)
	wall.position = Vector3(0, 1.5, -4.5)
	_world.add_child(wall)
	await _frames(3)
	target_hp = _health(target)
	_driver.GroundImpact(_world, bolt, caster["body"], Vector3(0, 1, -1), 10.0, 0.0, 1)
	if _health(target) != target_hp:
		_fail("geometry: a burst passed through physical world geometry on layer 2")
	bolt.ImpactRadius = 0.0
	if not caster["casting"].TryCastById(bolt.Id):
		_fail("geometry: covered projectile was refused")
	await _frames(70)
	if _health(target) != target_hp:
		_fail("geometry: a projectile passed through physical world geometry on layer 2")
	print("geometry: spells pass actor capsules and respect physical world cover")
	await _free([caster, ally, target, wall])
	await _clear_transients()


func _check_worldray_crowd() -> void:
	var crowd := []
	for i in 6:
		var actor := _actor("Crowd%d" % i, 1, Vector3(20, 0, -2 - i))
		actor["body"].collision_layer = 1
		crowd.append(actor)
	await _frames(3)
	var ray_from := Vector3(20, 1, 0)
	var ray_to := Vector3(20, 1, -10)
	if not _driver.WorldSegmentClear(_world, ray_from, ray_to):
		_fail("crowd: a clear ray through six actor capsules reported world cover")
	var wall := StaticBody3D.new()
	wall.collision_layer = 1
	var holder := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(4, 3, 0.2)
	holder.shape = box
	wall.add_child(holder)
	wall.position = Vector3(20, 1.5, -8.5)
	_world.add_child(wall)
	await _frames(3)
	if _driver.WorldSegmentClear(_world, ray_from, ray_to):
		_fail("crowd: a wall behind six actor capsules disappeared when WorldRay exhausted its old four-actor skip budget")
	print("crowd: clear actor crowds pass rays; walls behind them remain cover")
	crowd.append(wall)
	await _free(crowd)
	# More than the bounded query budget is an unproven segment, so fail closed. These
	# fixtures need only an entity capsule, keeping the exhaustion case inexpensive.
	crowd.clear()
	for i in 65:
		var actor := CharacterBody3D.new()
		actor.set_script(load("res://src/Entities/CharacterEntity.cs"))
		actor.collision_layer = 1
		actor.position = Vector3(20, 0, -2 - i)
		var capsule_holder := CollisionShape3D.new()
		var capsule := CapsuleShape3D.new()
		capsule.radius = 0.2
		capsule.height = 1.8
		capsule_holder.shape = capsule
		capsule_holder.position.y = 0.9
		actor.add_child(capsule_holder)
		_world.add_child(actor)
		crowd.append(actor)
	await _frames(3)
	if _driver.WorldSegmentClear(_world, ray_from, Vector3(20, 1, -70)):
		_fail("crowd: exhausting the bounded actor query budget fabricated a clear segment")
	await _free(crowd)


func _check_status_respawn() -> void:
	_driver.BeginImmediateRespawn("RespawningDot")
	var bearer := _actor("RespawningDot", 2, Vector3.ZERO)
	await _frames(3)
	bearer["status"].process_mode = Node.PROCESS_MODE_DISABLED
	bearer["stats"].process_mode = Node.PROCESS_MODE_DISABLED
	bearer["stats"].SetCurrent(HEALTH, 1.0)
	_apply(bearer, "res://data/status_effects/Burning.tres")
	_driver.StepStatuses(bearer["status"], 3.1)
	if _health(bearer) != bearer["stats"].GetMax(HEALTH) or _has_status(bearer, "status.burning"):
		_fail("respawn: removed pre-death Burning continued its catch-up ticks on the revived actor")
	_driver.EndImmediateRespawn()
	await _free([bearer])

	_driver.BeginImmediateRespawn("RespawningKindle")
	bearer = _actor("RespawningKindle", 2, Vector3.ZERO)
	var caster := _actor("KindleCaster", 1, Vector3(0, 0, 8))
	await _frames(3)
	bearer["status"].process_mode = Node.PROCESS_MODE_DISABLED
	bearer["stats"].SetCurrent(HEALTH, 1.0)
	var kindled := _res("res://data/status_effects/Kindled.tres")
	for i in 3:
		_driver.ApplyStatusFrom(bearer["status"], kindled, caster["body"])
	if _health(bearer) != bearer["stats"].GetMax(HEALTH) or _has_status(bearer, "status.burning"):
		_fail("respawn: lethal Kindle detonation applied Burning to the revived actor")
	_driver.EndImmediateRespawn()
	await _free([bearer, caster])

	# The same lethal-outcome rule must protect a neighbour killed by the detonation splash.
	_driver.BeginImmediateRespawn("RespawningKindleNeighbour")
	var neighbour := _actor("RespawningKindleNeighbour", 2, Vector3(2, 0, 0))
	bearer = _actor("KindleBearer", 2, Vector3.ZERO)
	caster = _actor("KindleCaster", 1, Vector3(0, 0, 8))
	await _frames(3)
	for actor in [bearer, neighbour]:
		actor["status"].process_mode = Node.PROCESS_MODE_DISABLED
	neighbour["stats"].SetCurrent(HEALTH, 1.0)
	for i in 3:
		_driver.ApplyStatusFrom(bearer["status"], kindled, caster["body"])
	if not _has_status(bearer, "status.burning") or _has_status(neighbour, "status.burning"):
		_fail("respawn: Kindle splash must ignite a surviving bearer and withhold Burning from its revived neighbour")
	_driver.EndImmediateRespawn()
	print("respawn: removed DoTs and lethal Kindle hits cannot afflict the revived actor")
	await _free([bearer, caster, neighbour])


# ---- totem ---------------------------------------------------------------------------------------

func _check_totem() -> void:
	var bloom := _spell("spell.probe_totem", {"Delivery": DELIVERY_SELF, "SummonDuration": 8.0, "SummonTickInterval": 1.0,
		"Healing": 5.0, "SummonHealth": 20.0, "BaseDamage": 0.0, "WindupSeconds": 0.15, "RecoverySeconds": 0.1})
	var hard := _spell("spell.probe_hard", {"BaseDamage": 30.0, "Cooldown": 0.3, "WindupSeconds": 0.15,
		"RecoverySeconds": 0.1, "ManaCost": 5.0})
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [bloom])
	var enemy := _actor("Enemy", 2, Vector3(0, 0, -10), [hard], 180.0)
	await _frames(3)
	_driver.Reset()
	caster["casting"].TryCastById("spell.probe_totem")
	await _frames(40)
	var totem = _find("SpellTotem")
	if totem == null:
		_fail("totem: no totem appeared")
		await _free([caster, enemy])
		return
	caster["body"].global_position = Vector3(-8, 0, 0)
	caster["stats"].SetCurrent(HEALTH, 50.0)
	await _frames(90)
	print("totem: caster health after the totem pulsed %.1f" % _health(caster))
	if _health(caster) <= 50.5:
		_fail("totem: it did not heal its owner (%.1f)" % _health(caster))
	enemy["casting"].TryCastById("spell.probe_hard")
	await _frames(80)
	var gone: bool = not is_instance_valid(totem) or totem.is_queued_for_deletion() or totem.Ended
	print("totem: destroyed by a bolt %s, broken events %d" % [gone, _driver.BarrierBroken])
	if not gone or _driver.BarrierBroken != 1:
		_fail("totem: it was not destroyed by a hostile bolt (gone %s, broken events %d)" % [gone, _driver.BarrierBroken])
	await _free([caster, enemy])


# ---- save ----------------------------------------------------------------------------------------

func _check_lifecycle_interrupts() -> void:
	var bolt := _spell("spell.probe_release_control", {"WindupSeconds": 0.4})
	var channel := _res("res://data/spells/StormConduit.tres")
	var lance := _res("res://data/spells/FlameLance.tres")
	# Respawn runs before the actor's own subscriptions, as it does in PlayerHost.
	_driver.BeginImmediateRespawn("RespawningCaster")
	var caster := _actor("RespawningCaster", 1, Vector3.ZERO, [lance, channel, bolt])
	var target := _actor("LifecycleTarget", 2, Vector3(0, 0, -6))
	await _frames(3)
	caster["casting"].BeginCastById("spell.flame_lance")
	caster["casting"].UpdateCast(1.0)
	_driver.Kill(caster["body"])
	caster["casting"].EndCast()
	if caster["casting"].IsCharging or caster["casting"].PendingSpell != null:
		_fail("lifecycle: a held charge survived death followed by immediate respawn")
	caster["casting"].BeginCastById("spell.storm_conduit")
	_driver.Kill(caster["body"])
	if caster["casting"].IsChanneling or caster["action"].Current != null:
		_fail("lifecycle: channel/action survived immediate respawn")
	_driver.EndImmediateRespawn()
	# Silence arrives and the release event follows synchronously, before _Process.
	caster["casting"].TryCastById("spell.probe_release_control")
	var before := _health(target)
	var mana_before := _mana(caster)
	_apply(caster, "res://data/status_effects/Silenced.tres")
	_driver.Reset()
	_driver.ReleaseCast(caster["body"])
	if caster["casting"].PendingSpell != null or _driver.Interrupted != 1 or _mana(caster) <= mana_before:
		_fail("lifecycle: release did not cancel/refund newly silenced wind-up before idle poll")
	await _frames(50)
	if _health(target) < before:
		_fail("lifecycle: a silenced wind-up released damage")
	_driver.PreLoad()
	caster["casting"].BeginCastById("spell.storm_conduit")
	await _frames(20)
	_apply(caster, "res://data/status_effects/Silenced.tres")
	_driver.Reset()
	caster["casting"].UpdateCast(1.0)
	if caster["casting"].IsChanneling or _driver.Casts != 0:
		_fail("lifecycle: newly silenced channel emitted another tick before idle poll")
	await _free([caster, target])
	# Lethal spell results must retain the actual health loss and not re-afflict a revived target.
	_driver.BeginImmediateRespawn("RespawningTarget")
	caster = _actor("NecroticCaster", 1, Vector3.ZERO)
	target = _actor("RespawningTarget", 2, Vector3(0, 0, -6))
	await _frames(3)
	caster["stats"].SetCurrent(HEALTH, 50.0)
	target["stats"].SetCurrent(HEALTH, 10.0)
	_apply(target, "res://data/status_effects/GraveMark.tres")
	var ash := _res("res://data/spells/AshBreath.tres")
	_driver.Hit(target["body"], ash, caster["body"], 100.0)
	var expected: float = 50.0 + _driver.ExpectedLifesteal(10.0, 0.0, true)
	if absf(_health(caster) - expected) > 0.1 or _health(target) < 99.0:
		_fail("lifecycle: lethal hit lost its damage/health snapshot during immediate respawn")
	if _has_status(target, "status.decay") or target["combat"].IsStaggered:
		_fail("lifecycle: lethal spell re-afflicted or staggered the revived target")
	_driver.EndImmediateRespawn()
	await _free([caster, target])

func _check_save() -> void:
	var bolt := _res("res://data/spells/Emberlash.tres")
	var caster := _actor("Caster", 1, Vector3(0, 0, 0), [bolt])
	var dummy := _actor("Dummy", 2, Vector3(0, 0, -6))
	var sc = caster["casting"]
	await _frames(3)
	sc.TryCastById("spell.emberlash")
	await _frames(10)
	if sc.CooldownOf(bolt) <= 0.0:
		_fail("save: the probe cast left no cooldown to replace")
	var saved: Dictionary = sc.Save()
	if not saved.has("cooldowns") or not saved["cooldowns"].has("spell.emberlash"):
		_fail("save: cooldowns are not saved")

	# a save that predates cooldowns replaces the live ones
	sc.Load({"spells": ["spell.emberlash"], "selected": 0, "ranks": {}})
	if sc.CooldownOf(bolt) > 0.0:
		_fail("save: Load merged over live cooldowns instead of replacing them")
	if sc.SpellCount != 1:
		_fail("save: the spellbook has %d spells after a one-spell save" % sc.SpellCount)
	if sc.PendingSpell != null or caster["action"].Current != null:
		_fail("save: load left the abandoned cast action running")
	sc.Load({})
	if sc.SpellCount != 0 or sc.PendingSpell != null:
		_fail("save: an empty restore retained learned spells or transient casts")

	# a retired id resolves through the database, a duplicate collapses, an unknown id is dropped
	sc.Load({"spells": ["spell.fireball", "spell.fireball", "spell.no_such_spell"], "selected": 0,
		"ranks": {"spell.fireball": 2}, "cooldowns": {"spell.fireball": 4.0}})
	var count: int = sc.SpellCount
	print("save: after a load naming a duplicate and an unknown id the spellbook holds %d spell(s)" % count)
	if count != 1:
		_fail("save: expected one known spell after dedupe and dropping an unknown id, found %d" % count)
	elif sc.SpellAt(0) != null:
		var resolved = sc.SpellAt(0)
		if sc.RankOf(resolved) != 2:
			_fail("save: the rank saved under a retired id was lost (rank %d)" % sc.RankOf(resolved))
		if sc.CooldownOf(resolved) < 3.0:
			_fail("save: the cooldown saved under a retired id was lost (%.1f)" % sc.CooldownOf(resolved))
	await _free([caster, dummy])
