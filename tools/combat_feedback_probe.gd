# Combat feedback probe: the parts of hit feedback and targeting that only exist at runtime or while the
# tree is paused, and that a unit test on the pure maths cannot reach.
#
#   1. HIT-STOP RESTORES THE CLOCK. A freeze dips Engine.TimeScale and puts it back; the comfort scale of
#      0 is no dip at all; a flurry cannot outlast the hard cap; another time effect's clock is never
#      overwritten; and pausing MID-freeze hands the clock back at once (the director runs ProcessMode
#      Always, and a stuck TimeScale of 0 with the world paused is a game you cannot unpause).
#   2. THE PIPELINE. A real swing runs the real events (DamageDealt -> the feedback director -> one
#      HitConfirmed) and reaches hit-stop and the floating number; a real PARRY (which publishes no
#      damage at all) reaches them too, and staggers the attacker.
#   3. LOCK-ON WHILE PAUSED. Acquiring, dropping a killed target and passing the lock to the next enemy
#      are plain calls, so they must work with the tree paused; the kill and the acquire each leave
#      their cue on the overlay.
#   4. TELEGRAPH TIMING. The ring lasts as long as the wind-up the swing really has (a sword's and a
#      maul's differ five-fold), is classed from the action, and the parry cue lights before contact.
#
# ⚠️ Actors are built by hand, as in melee_probe.gd: a C# static method is not reachable through a loaded
# Script object from GDScript, and Entity.GetComponent<T> is generic, so component refs are kept from
# construction. With no PlayerCharacter registered, the player's team (0) stands in for "the player".
#
# ⚠️ Engine.TimeScale = 0 stops PHYSICS frames, so waits here are wall-clock timers that ignore the time
# scale, and polls are on process frames.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/combat_feedback_probe.gd
# Exits 0 when every case holds, 1 otherwise. READ THE WHOLE OUTPUT: a script error prints above PASS.
extends SceneTree

const HEALTH := 0           # StatType.Health
const STAMINA := 1          # StatType.Stamina
const PLAYING := 3          # GameState.Playing
const PAUSED := 4           # GameState.Paused

var _failures: Array[String] = []
# A script error inside a check aborts that check and prints ABOVE the verdict; the probe would still
# say PASS. Every check marks itself when it reaches its own end, and the verdict needs all of them.
var _reached: Array[String] = []
const CHECKS := ["clock", "yields", "pause", "swing", "parry", "lock", "telegraph"]
var _manager: Node


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame
	_manager = root.get_node_or_null("GameManager")
	if _manager == null:
		print("FAIL: no GameManager autoload; nothing here can run")
		quit(1)
		return
	_manager.ChangeState(PLAYING)

	await _check_hit_stop_clock()
	await _check_hit_stop_yields()
	await _check_hit_stop_pause()
	await _check_swing_pipeline()
	await _check_parry_pipeline()
	await _check_lock_on_paused()
	await _check_telegraph_timing()

	Engine.time_scale = 1.0
	paused = false
	_manager.ChangeState(0)
	print("---")
	for name in CHECKS:
		if not _reached.has(name):
			_fail("the '%s' check never reached its end (a script error above?)" % name)
	if _failures.is_empty():
		print("PASS: hit-stop restores the clock (also mid-pause), a swing and a parry reach the feedback, lock-on works paused, telegraphs run off the real wind-up")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _wall(seconds: float) -> void:
	# process_always, process_in_physics, ignore_time_scale
	await create_timer(seconds, true, false, true).timeout


func _fail(text: String) -> void:
	_failures.append(text)


func _spawn_director(path: String) -> Node:
	var node: Node = load(path).new()
	root.add_child(node)
	return node


func _free(nodes: Array) -> void:
	for n in nodes:
		if is_instance_valid(n):
			n.queue_free()


# --- 1. hit-stop and the clock ----------------------------------------------------------------

func _check_hit_stop_clock() -> void:
	var d := _spawn_director("res://src/Combat/HitStopDirector.cs")
	await process_frame
	Engine.time_scale = 1.0

	d.EngageFor(150, 0.0, 1.0)
	if Engine.time_scale > 0.01:
		_fail("a full-comfort hit-stop did not freeze the clock (time scale %.2f)" % Engine.time_scale)
	var t0 := Time.get_ticks_msec()
	while d.IsFreezing and Time.get_ticks_msec() - t0 < 2000:
		await process_frame
	var held := Time.get_ticks_msec() - t0
	print("hit-stop 150 ms:   held %d ms, clock now %.2f" % [held, Engine.time_scale])
	if not is_equal_approx(Engine.time_scale, 1.0):
		_fail("the clock was not restored after a hit-stop (time scale %.2f)" % Engine.time_scale)
	if held < 100 or held > 400:
		_fail("a 150 ms hit-stop held for %d ms" % held)

	# The comfort slider at 0 is no stop at all.
	await _wall(0.9)
	d.EngageFor(150, 0.0, 0.0)
	if Engine.time_scale < 0.99 or d.IsFreezing:
		_fail("hit-stop at comfort 0 still dipped the clock (%.2f)" % Engine.time_scale)

	# Reduced Motion's cap (a quarter): a shallow dip, not a freeze.
	await _wall(0.9)
	d.EngageFor(190, 0.0, 0.25)
	print("hit-stop at 0.25:  clock dips to %.2f" % Engine.time_scale)
	if Engine.time_scale < 0.5 or Engine.time_scale > 0.99:
		_fail("Reduced-Motion hit-stop should dip shallowly, not freeze (time scale %.2f)" % Engine.time_scale)
	while d.IsFreezing:
		await process_frame

	# A flurry cannot stack into a stall: ten heavy requests are still bounded.
	await _wall(0.9)
	t0 = Time.get_ticks_msec()
	for i in 10:
		d.EngageFor(200, 0.0, 1.0)
	while d.IsFreezing and Time.get_ticks_msec() - t0 < 3000:
		await process_frame
	held = Time.get_ticks_msec() - t0
	print("hit-stop flurry:   ten requests held %d ms" % held)
	if held > 450:
		_fail("a flurry of hit-stops held the clock for %d ms; the cap is 240" % held)
	if not is_equal_approx(Engine.time_scale, 1.0):
		_fail("the clock was left at %.2f after a flurry" % Engine.time_scale)

	Engine.time_scale = 1.0
	_free([d])
	await process_frame
	_reached.append("clock")


func _check_hit_stop_yields() -> void:
	var d := _spawn_director("res://src/Combat/HitStopDirector.cs")
	await process_frame
	# Another time effect (the boss-defeat slow-mo) owns the clock: hit-stop must not take it over,
	# and must not hand it back to 1.0 either.
	Engine.time_scale = 0.3
	d.EngageFor(150, 0.0, 1.0)
	await process_frame
	await process_frame
	print("another effect:    clock left at %.2f" % Engine.time_scale)
	if d.IsFreezing or not is_equal_approx(Engine.time_scale, 0.3):
		_fail("hit-stop took over a clock another effect held (time scale %.2f)" % Engine.time_scale)
	Engine.time_scale = 1.0
	_free([d])
	await process_frame
	_reached.append("yields")


func _check_hit_stop_pause() -> void:
	var d := _spawn_director("res://src/Combat/HitStopDirector.cs")
	await process_frame
	Engine.time_scale = 1.0
	await _wall(0.9)

	d.EngageFor(240, 0.0, 1.0)
	if not d.IsFreezing:
		_fail("the pause test could not start a freeze")
	_manager.ChangeState(PAUSED)
	var was_paused := paused
	# Only a ProcessMode.Always node runs now; the director is one, and must hand the clock back.
	for i in 5:
		await process_frame
	print("paused mid-freeze: tree paused %s, clock %.2f, freezing %s" % [was_paused, Engine.time_scale, d.IsFreezing])
	if not was_paused:
		_fail("ChangeState(Paused) did not pause the tree; this check proves nothing")
	if not is_equal_approx(Engine.time_scale, 1.0) or d.IsFreezing:
		_fail("pausing in the middle of a hit-stop left the clock at %.2f" % Engine.time_scale)

	# And it must not start a new freeze while the world is paused.
	d.EngageFor(150, 0.0, 1.0)
	if Engine.time_scale < 0.99 or d.IsFreezing:
		_fail("a hit-stop started while the game was paused")

	_manager.ChangeState(PLAYING)
	Engine.time_scale = 1.0
	_free([d])
	await process_frame


# --- 2. the real pipeline ---------------------------------------------------------------------
	_reached.append("pause")


func _actor(actor_name: String, team: int, at: Vector3, layer: int) -> Array:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = actor_name
	body.position = at
	body.collision_layer = layer
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
	return [body, stats, combat]


# An attacker (team 0, standing in for the player) facing a target (team 1) that faces it back, so a
# raised guard is inside its arc.
func _duel(weapon_path: String, with_telegraph: bool) -> Dictionary:
	var attacker_parts := _actor("Attacker", 0, Vector3(0, 0, 1.2), 1 << 4)
	var target_parts := _actor("Target", 1, Vector3.ZERO, 1 << 5)
	var attacker: CharacterBody3D = attacker_parts[0]
	var target: CharacterBody3D = target_parts[0]
	target.rotation.y = PI

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
	action.Weapon = load(weapon_path)
	action.Hitbox = hitbox
	attacker.add_child(action)

	if with_telegraph:
		var tele = load("res://src/Combat/TelegraphComponent.cs").new()
		tele.name = "Telegraph"
		attacker.add_child(tele)

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
	return {
		"attacker": attacker, "target": target, "action": action,
		"target_stats": target_parts[1], "target_combat": target_parts[2],
		"attacker_stats": attacker_parts[1], "attacker_combat": attacker_parts[2],
	}


func _check_swing_pipeline() -> void:
	Engine.time_scale = 1.0
	var hit_stop := _spawn_director("res://src/Combat/HitStopDirector.cs")
	var feedback := _spawn_director("res://src/Combat/CombatFeedbackDirector.cs")
	var overlay := _spawn_director("res://src/UI/CombatFeedbackOverlay.cs")
	var duel := _duel("res://data/weapons/IronSword.tres", false)
	await process_frame
	await physics_frame
	await physics_frame

	var stats = duel["target_stats"]
	var action = duel["action"]
	var numbers = overlay.get_node("DamageNumbers")
	var before: float = stats.GetCurrent(HEALTH)
	if not action.TryAttack():
		_fail("the sword swing did not start; the pipeline check proves nothing")
	else:
		var min_scale := 1.0
		var most_numbers := 0
		var landed := false
		var t0 := Time.get_ticks_msec()
		while Time.get_ticks_msec() - t0 < 2500:
			await process_frame
			min_scale = minf(min_scale, Engine.time_scale)
			most_numbers = maxi(most_numbers, numbers.LiveCount)
			if stats.GetCurrent(HEALTH) < before:
				landed = true
			if landed and not hit_stop.IsFreezing and numbers.LiveCount == 0 and Time.get_ticks_msec() - t0 > 1200:
				break
		print("sword swing:       landed %s, deepest clock %.2f, most numbers %d, clock now %.2f"
			% [landed, min_scale, most_numbers, Engine.time_scale])
		if not landed:
			_fail("the sword swing never landed")
		if min_scale > 0.99:
			_fail("a landed hit did not reach hit-stop through HitConfirmed (clock never dipped)")
		if most_numbers < 1:
			_fail("a landed hit did not reach the floating damage numbers")
		if not is_equal_approx(Engine.time_scale, 1.0):
			_fail("the clock was left at %.2f after a swing" % Engine.time_scale)

	Engine.time_scale = 1.0
	_free([hit_stop, feedback, overlay, duel["attacker"], duel["target"]])
	await process_frame
	await process_frame
	_reached.append("swing")


func _check_parry_pipeline() -> void:
	Engine.time_scale = 1.0
	await _wall(0.9)   # the hit-stop budget of the previous check must have slid on
	var hit_stop := _spawn_director("res://src/Combat/HitStopDirector.cs")
	var feedback := _spawn_director("res://src/Combat/CombatFeedbackDirector.cs")
	var overlay := _spawn_director("res://src/UI/CombatFeedbackOverlay.cs")
	var duel := _duel("res://data/weapons/IronKingMaul.tres", false)
	await process_frame
	await physics_frame
	await physics_frame

	var stats = duel["target_stats"]
	var target_combat = duel["target_combat"]
	var attacker_combat = duel["attacker_combat"]
	var action = duel["action"]
	var numbers = overlay.get_node("DamageNumbers")
	var before: float = stats.GetCurrent(HEALTH)
	if stats.GetCurrent(STAMINA) < 12.0:
		_fail("the defender has no stamina to parry with; the parry check proves nothing")
	if not action.TryAttack():
		_fail("the maul swing did not start; the parry check proves nothing")
	else:
		var raised := false
		var staggered := false
		var min_scale := 1.0
		var most_numbers := 0
		var t0 := Time.get_ticks_msec()
		while Time.get_ticks_msec() - t0 < 3000:
			await process_frame
			# Guard up the moment the blow goes live: inside the parry window by construction.
			if not raised and action.Phase == 2:
				target_combat.IsBlocking = true
				raised = true
			if attacker_combat.IsStaggered:
				staggered = true
			min_scale = minf(min_scale, Engine.time_scale)
			most_numbers = maxi(most_numbers, numbers.LiveCount)
			if staggered and Time.get_ticks_msec() - t0 > 1500:
				break
		var lost: float = before - stats.GetCurrent(HEALTH)
		print("parry:             guard raised %s, attacker staggered %s, damage taken %.1f, deepest clock %.2f, numbers %d"
			% [raised, staggered, lost, min_scale, most_numbers])
		if not raised:
			_fail("the maul never reached its active window")
		if not staggered:
			_fail("the parry did not stagger the attacker (was it a parry at all?)")
		if lost > 0.5:
			_fail("a parried blow still dealt %.1f damage" % lost)
		# A parry publishes no DamageDealtEvent. It must still reach hit-stop and the numbers.
		if staggered and min_scale > 0.99:
			_fail("a parry, which has no damage event, never reached hit-stop")
		if staggered and most_numbers < 1:
			_fail("a parry, which has no damage event, never reached the floating numbers")

	Engine.time_scale = 1.0
	_free([hit_stop, feedback, overlay, duel["attacker"], duel["target"]])
	await process_frame
	await process_frame


# --- 3. lock-on, paused -----------------------------------------------------------------------
	_reached.append("parry")


func _check_lock_on_paused() -> void:
	Engine.time_scale = 1.0
	var overlay := _spawn_director("res://src/UI/CombatFeedbackOverlay.cs")
	var cues = overlay.get_node("LockCues")

	var player_parts := _actor("Player", 0, Vector3.ZERO, 1 << 4)
	var player: CharacterBody3D = player_parts[0]
	var lock = load("res://src/Combat/LockOnComponent.cs").new()
	lock.name = "LockOn"
	player.add_child(lock)
	var e1_parts := _actor("Enemy1", 1, Vector3(0, 0, -6), 1 << 5)
	var e2_parts := _actor("Enemy2", 1, Vector3(3, 0, -7), 1 << 5)
	root.add_child(player)
	root.add_child(e1_parts[0])
	root.add_child(e2_parts[0])
	await process_frame
	await physics_frame
	await physics_frame

	# One enemy first, under a paused tree: acquire, hold, and drop it dead.
	e2_parts[0].queue_free()
	await process_frame
	lock.ToggleNearest()
	if not lock.IsLocked:
		_fail("the lock did not acquire an enemy six metres ahead (needs a physics frame and line of sight)")
	else:
		var acquired: int = cues.LiveCount
		_manager.ChangeState(PAUSED)
		await process_frame
		var was_paused := paused
		lock.Tick()
		lock.FaceTarget()
		if not lock.IsLocked:
			_fail("a paused Tick dropped a living target")
		e1_parts[1].ModifyCurrent(HEALTH, -9999.0)
		lock.Tick()
		print("lock, paused:      tree paused %s, still locked after the kill %s, cues %d -> %d"
			% [was_paused, lock.IsLocked, acquired, cues.LiveCount])
		if not was_paused:
			_fail("the tree was not paused; the paused lock-on check proves nothing")
		if lock.IsLocked:
			_fail("a killed target kept the lock while the tree was paused")
		if acquired < 1:
			_fail("acquiring a lock left no cue on the overlay")
		if cues.LiveCount <= acquired:
			_fail("a kill left no cue on the overlay (the lock break did not say why)")
		_manager.ChangeState(PLAYING)

	# Two enemies, assist on: killing the locked one passes the lock to the other rather than dropping.
	_free([e1_parts[0]])
	await process_frame
	var e3_parts := _actor("Enemy3", 1, Vector3(-3, 0, -6), 1 << 5)
	var e4_parts := _actor("Enemy4", 1, Vector3(3, 0, -6), 1 << 5)
	root.add_child(e3_parts[0])
	root.add_child(e4_parts[0])
	await process_frame
	await physics_frame
	await physics_frame
	lock.ToggleNearest()
	if not lock.IsLocked:
		_fail("the two-enemy lock did not acquire anything")
	else:
		var first = lock.TargetNode
		first.get_node("Stats").ModifyCurrent(HEALTH, -9999.0)
		lock.Tick()
		var second = lock.TargetNode
		print("lock, pass-on:     first %s -> %s" % [first.name, second.name if second != null else "(dropped)"])
		if second == null:
			_fail("the assist dropped the lock on a kill instead of passing it to the next enemy")
		elif second == first:
			_fail("the lock stayed on a dead target")

	# A toggle off is the player's call, and nothing passes on.
	lock.ToggleNearest()
	if lock.IsLocked:
		_fail("toggling a held lock off left it locked")

	_free([overlay, player, e3_parts[0], e4_parts[0]])
	await process_frame
	await process_frame


# --- 4. telegraph timing ----------------------------------------------------------------------
	_reached.append("lock")


func _check_telegraph_timing() -> void:
	Engine.time_scale = 1.0
	# The sword's wind-up is 0.15 s; the maul's first link is 0.551 / 0.7 = 0.787 s. A ring on a
	# constant would time both the same.
	var cases := [
		{"weapon": "res://data/weapons/IronSword.tres", "label": "iron sword", "windup": 0.15, "class": 1},
		{"weapon": "res://data/weapons/IronKingMaul.tres", "label": "iron king maul", "windup": 0.787, "class": 3},
	]
	for c in cases:
		var duel := _duel(c["weapon"], true)
		await process_frame
		await process_frame
		await physics_frame

		var attacker: Node = duel["attacker"]
		var ring: Node = attacker.get_node_or_null("TelegraphRing")
		var stats = duel["target_stats"]
		var action = duel["action"]
		if ring == null:
			_fail("%s: the telegraph ring never reached the tree (a missed CallDeferred?)" % c["label"])
			_free([duel["attacker"], duel["target"]])
			await process_frame
			continue

		var before: float = stats.GetCurrent(HEALTH)
		if not action.TryAttack():
			_fail("%s: the swing did not start" % c["label"])
			_free([duel["attacker"], duel["target"]])
			await process_frame
			continue

		var t0 := Time.get_ticks_msec()
		var armed_at := -1.0
		var cleared_at := -1.0
		var hit_at := -1.0
		var cls := -1
		var cue_seen := false
		while Time.get_ticks_msec() - t0 < 3000 and (cleared_at < 0.0 or hit_at < 0.0):
			await process_frame
			var now := (Time.get_ticks_msec() - t0) / 1000.0
			if ring.IsActive:
				if armed_at < 0.0:
					armed_at = now
				cls = ring.Class
				if ring.CueLit:
					cue_seen = true
			elif armed_at >= 0.0 and cleared_at < 0.0:
				cleared_at = now
			if hit_at < 0.0 and stats.GetCurrent(HEALTH) < before:
				hit_at = now

		var measured := cleared_at - armed_at
		print("%-16s ring %.3f s (wind-up %.3f), blow landed at %.3f s, class %d, parry cue %s"
			% [c["label"], measured, c["windup"], hit_at, cls, cue_seen])
		if armed_at < 0.0 or cleared_at < 0.0:
			_fail("%s: the ring never armed or never cleared" % c["label"])
		elif absf(measured - c["windup"]) > 0.15:
			_fail("%s: the ring lasted %.3f s but the wind-up is %.3f s" % [c["label"], measured, c["windup"]])
		if hit_at >= 0.0 and cleared_at >= 0.0 and absf(cleared_at - hit_at) > 0.18:
			_fail("%s: the ring cleared at %.3f s and the blow landed at %.3f s" % [c["label"], cleared_at, hit_at])
		if cls != c["class"]:
			_fail("%s: the ring was class %d, expected %d" % [c["label"], cls, c["class"]])
		if c["class"] == 1 and not cue_seen:
			_fail("%s: a parryable blow never lit its parry cue" % c["label"])
		if c["class"] == 3 and cue_seen:
			_fail("%s: a sweep lit a parry cue" % c["label"])

		_free([duel["attacker"], duel["target"]])
		await _wall(0.4)
		await process_frame
	_reached.append("telegraph")

