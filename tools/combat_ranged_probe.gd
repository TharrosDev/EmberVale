# Combat ranged probe: a drawn shot vs a snap shot, headshot / hit-zone routing, arrow drop, world
# collision, a thin hurtbox at speed, and aim assist — all through real physics frames.
#
# ⚠️ EVERY CASE IS AN ASSERTION ON A NUMBER THE PLAYER FEELS. A drawn shot must hit harder AND land
# sooner than a snap shot fired on the same release frame; a head must hurt more than a chest; a
# bent arrow must still land where the reticle was (drop is solved for, not suffered); a wall must
# stop an arrow that a person does not; a hurtbox thinner than one frame of travel must still be
# hit; and aim assist must pull only when it is on, only near the target, and never through cover.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/combat_ranged_probe.gd
# READ THE WHOLE OUTPUT: a script error mid-function can still end in a PASS line.
extends SceneTree

const BOW := "res://data/weapons/HuntingBow.tres"
const HEALTH := 0
const STEP := 1.0 / 60.0
const FRAMES := 170

var _failures: Array[String] = []
var _slot := 0


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame
	await _draw_vs_snap()
	await _head_routing()
	await _drop_and_walls()
	await _thin_hurtbox()
	await _aim_assist()
	print("---")
	if _failures.is_empty():
		print("PASS: drawn beats snap, heads beat chests, drop is solved, walls stop arrows, thin bodies are hit, aim assist is honest")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _next_origin() -> Vector3:
	_slot += 1
	return Vector3(_slot * 250, 0, 0)


func _target(team: int, at: Vector3, zones: bool = false, height: float = 1.8) -> Array:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = "Target"
	body.position = at
	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	body.add_child(stats)
	var combat = load("res://src/Combat/CombatComponent.cs").new()
	combat.name = "Combat"
	combat.Team = team
	body.add_child(combat)
	body.add_child(_hurtbox("Hurtbox", "", 1.0, Vector3(0, height * 0.5, 0), height, 0.4, false))
	if zones:
		body.add_child(_hurtbox("Hurtbox_head", "head", 2.0, Vector3(0, 1.7, 0), 0.0, 0.25, true))
	root.add_child(body)
	return [body, stats]


func _hurtbox(node_name: String, zone: String, mult: float, at: Vector3, height: float, radius: float, sphere: bool) -> Area3D:
	var hurtbox := Area3D.new()
	hurtbox.set_script(load("res://src/Combat/Hurtbox.cs"))
	hurtbox.name = node_name
	hurtbox.ZoneId = zone
	hurtbox.DamageMultiplier = mult
	var shape := CollisionShape3D.new()
	if sphere:
		var s := SphereShape3D.new()
		s.radius = radius
		shape.shape = s
	else:
		var c := CapsuleShape3D.new()
		c.radius = radius
		c.height = height
		shape.shape = c
	shape.position = at
	hurtbox.add_child(shape)
	return hurtbox


func _archer(at: Vector3, with_draw: bool, with_aim: bool) -> Array:
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
	var draw = null
	if with_draw:
		draw = load("res://src/Player/BowDrawComponent.cs").new()
		draw.name = "BowDraw"
		body.add_child(draw)
	var aim = null
	if with_aim:
		aim = load("res://src/Player/AimController.cs").new()
		aim.name = "Aim"
		body.add_child(aim)
	root.add_child(body)
	return [body, action, draw, aim]


# Runs one shot to completion. hold_frames < 0 holds the button throughout; otherwise it lets go
# after that many frames. Returns [damage dealt, seconds to first damage].
func _shoot(action, draw, stats, hold_frames: int) -> Array:
	var before: float = stats.GetCurrent(HEALTH)
	if not action.TryAttack():
		_failures.append("the bow did not draw")
		return [0.0, -1.0]
	var first := -1.0
	var t := 0.0
	for i in FRAMES:
		await physics_frame
		t += STEP
		if draw != null:
			draw.Tick(STEP, hold_frames < 0 or i < hold_frames)
		if first < 0.0 and stats.GetCurrent(HEALTH) < before:
			first = t
	return [before - stats.GetCurrent(HEALTH), first]


func _draw_vs_snap() -> void:
	var results := []
	for hold in [-1, 4]:
		var origin := _next_origin()
		var parts := _archer(origin, true, false)
		var tparts := _target(2, origin + Vector3(0, 0, -8))
		await physics_frame
		parts[1].AimAt(origin + Vector3(0, 1.0, -8))
		var r: Array = await _shoot(parts[1], parts[2], tparts[1], hold)
		results.append(r)
		parts[0].queue_free()
		tparts[0].queue_free()
		await process_frame
	var drawn: Array = results[0]
	var snap: Array = results[1]
	print("draw  : drawn %.1f at %.2fs | snap %.1f at %.2fs" % [drawn[0], drawn[1], snap[0], snap[1]])
	if drawn[0] <= 0.0 or snap[0] <= 0.0:
		_failures.append("a shot dealt nothing (drawn %.1f, snap %.1f)" % [drawn[0], snap[0]])
		return
	# 1.3 rather than the 2.2 the curve gives: a crit on the snap shot must not flake the gate.
	if drawn[0] < snap[0] * 1.3:
		_failures.append("a full draw (%.1f) should hit much harder than a snap shot (%.1f)" % [drawn[0], snap[0]])
	if snap[1] - drawn[1] < 0.06:
		_failures.append("a snap arrow should fly slower: drawn landed at %.2fs, snap at %.2fs" % [drawn[1], snap[1]])
	if drawn[1] < 0.86:
		_failures.append("the drawn arrow landed at %.2fs, before the release frame at 0.86s" % drawn[1])


func _head_routing() -> void:
	# Same full-power AI-style archer, three aim heights: chest, the head of a multi-zone body, and the
	# top slice of a plain tall capsule.
	var chest := await _hit_at(false, 1.0)
	var zoned := await _hit_at(true, 1.7)
	var plain_chest := await _hit_at(false, 1.0)
	var plain_head := await _hit_at(false, 1.7)
	print("zones : chest %.1f, head zone %.1f | plain chest %.1f, plain head %.1f" % [chest, zoned, plain_chest, plain_head])
	if chest <= 0.0 or zoned < chest * 1.7:
		_failures.append("a head-zone arrow (%.1f) should land near double a chest arrow (%.1f)" % [zoned, chest])
	if plain_chest <= 0.0 or plain_head < plain_chest * 1.35 or plain_head > plain_chest * 1.7:
		_failures.append("the top slice of a tall body (%.1f) should hit about 1.5x its chest (%.1f)" % [plain_head, plain_chest])


func _hit_at(zones: bool, y: float) -> float:
	var origin := _next_origin()
	var parts := _archer(origin, false, false)
	var tparts := _target(2, origin + Vector3(0, 0, -10), zones)
	await physics_frame
	parts[1].AimAt(origin + Vector3(0, y, -10))
	var r: Array = await _shoot(parts[1], null, tparts[1], -1)
	parts[0].queue_free()
	tparts[0].queue_free()
	await process_frame
	return r[0]


func _drop_and_walls() -> void:
	# A 35 m shot: the arrow falls ~2.4 m over that flight, so it only lands if the launch was solved.
	var origin := _next_origin()
	var parts := _archer(origin, false, false)
	var tparts := _target(2, origin + Vector3(0, 0, -35))
	await physics_frame
	parts[1].AimAt(origin + Vector3(0, 1.0, -35))
	var far: Array = await _shoot(parts[1], null, tparts[1], -1)
	print("drop  : dealt %.1f at 35 m" % far[0])
	if far[0] <= 0.0:
		_failures.append("a 35 m shot fell short: the launch does not compensate for drop")
	parts[0].queue_free()
	tparts[0].queue_free()
	await process_frame

	# A wall between archer and target stops the arrow; the same shot with no wall lands.
	origin = _next_origin()
	parts = _archer(origin, false, false)
	tparts = _target(2, origin + Vector3(0, 0, -16))
	var wall := StaticBody3D.new()
	wall.collision_layer = 1
	var ws := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(8, 5, 0.5)
	ws.shape = box
	wall.add_child(ws)
	wall.position = origin + Vector3(0, 2, -9)
	root.add_child(wall)
	await physics_frame
	parts[1].AimAt(origin + Vector3(0, 1.0, -16))
	var blocked: Array = await _shoot(parts[1], null, tparts[1], -1)
	print("wall  : dealt %.1f through a wall" % blocked[0])
	if blocked[0] > 0.0:
		_failures.append("an arrow passed through a wall and dealt %.1f" % blocked[0])
	parts[0].queue_free()
	tparts[0].queue_free()
	wall.queue_free()
	await process_frame


func _thin_hurtbox() -> void:
	# A hurtbox 2 cm thick against 0.7 m of travel a frame: only a per-step query can hit it.
	var origin := _next_origin()
	var parts := _archer(origin, false, false)
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.position = origin + Vector3(0, 0, -14)
	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	body.add_child(stats)
	var combat = load("res://src/Combat/CombatComponent.cs").new()
	combat.name = "Combat"
	combat.Team = 2
	body.add_child(combat)
	var hurtbox := Area3D.new()
	hurtbox.set_script(load("res://src/Combat/Hurtbox.cs"))
	hurtbox.name = "Hurtbox"
	var shape := CollisionShape3D.new()
	var slab := BoxShape3D.new()
	slab.size = Vector3(2.0, 2.0, 0.02)
	shape.shape = slab
	shape.position = Vector3(0, 1.0, 0)
	hurtbox.add_child(shape)
	body.add_child(hurtbox)
	root.add_child(body)
	await physics_frame
	parts[1].AimAt(origin + Vector3(0, 1.0, -14))
	var r: Array = await _shoot(parts[1], null, stats, -1)
	print("thin  : dealt %.1f against a 2 cm slab at 42 m/s" % r[0])
	if r[0] <= 0.0:
		_failures.append("the arrow tunnelled through a 2 cm hurtbox")
	parts[0].queue_free()
	body.queue_free()
	await process_frame


func _aim_assist() -> void:
	var origin := _next_origin()
	var parts := _archer(origin, false, true)
	var aim = parts[3]
	var tparts := _target(2, origin + Vector3(0, 0, -20))
	await physics_frame
	var focus := origin + Vector3(1.0, 1.0, -20)   # 1 m off the target: ~2.9 degrees
	var from := origin + Vector3(0, 1.4, 0)

	aim.StrengthOverride = 0.0
	var off: Vector3 = aim.AssistedFocus(from, focus, 1.0)
	if not off.is_equal_approx(focus):
		_failures.append("aim assist at strength 0 moved the aim")

	aim.StrengthOverride = 1.0
	var on: Vector3 = aim.AssistedFocus(from, focus, 1.0)
	var snap: Vector3 = aim.AssistedFocus(from, focus, 0.0)
	print("assist: off %s | full %s | snap %s" % [off, on, snap])
	if absf(on.x - origin.x) >= absf(focus.x - origin.x) - 0.3:
		_failures.append("aim assist did not pull a near miss toward the target (%s)" % on)
	if absf(snap.x - origin.x) <= absf(on.x - origin.x):
		_failures.append("a snap shot should be pulled less than a drawn one")

	# Far outside the cone: left alone.
	var wide := origin + Vector3(6.0, 1.0, -20)
	if not aim.AssistedFocus(from, wide, 1.0).is_equal_approx(wide):
		_failures.append("aim assist pulled a shot that was clearly aimed elsewhere")

	# Through the assisted arrow itself: with assist on, the near miss lands; with it off it does not.
	parts[1].AimAt(focus)
	aim.SetFocus(focus)
	var landed: Array = await _shoot(parts[1], null, tparts[1], -1)
	print("assist: near-miss shot dealt %.1f with assist on" % landed[0])
	if landed[0] <= 0.0:
		_failures.append("the assisted near miss did not land")
	parts[0].queue_free()
	tparts[0].queue_free()
	await process_frame

	# Cover: a wall between archer and target cancels the pull entirely.
	origin = _next_origin()
	parts = _archer(origin, false, true)
	aim = parts[3]
	aim.StrengthOverride = 1.0
	tparts = _target(2, origin + Vector3(0, 0, -20))
	var wall := StaticBody3D.new()
	wall.collision_layer = 1
	var ws := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(10, 6, 0.5)
	ws.shape = box
	wall.add_child(ws)
	wall.position = origin + Vector3(0, 3, -10)
	root.add_child(wall)
	await physics_frame
	focus = origin + Vector3(1.0, 1.0, -20)
	from = origin + Vector3(0, 1.4, 0)
	if not aim.AssistedFocus(from, focus, 1.0).is_equal_approx(focus):
		_failures.append("aim assist pulled toward a target behind a wall")
	print("assist: no pull through cover")
	parts[0].queue_free()
	tparts[0].queue_free()
	wall.queue_free()
	await process_frame
