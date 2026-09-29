# Magic status probe: the status rules of the 2026-09 Magic upgrade through real components, real
# hurtboxes and the real SpellResolver seam. MagicStatusRulesTests pins the arithmetic; this proves the
# component applies it.
#
#   content    all 15 status .tres load, every one has a name, a school and Loc text; the reserved ids carry
#              the fields the rules read
#   kindle     three Kindle hits detonate: consumed, damage to the bearer and a neighbour, both ignited, a far
#              foe untouched, StatusDetonatedEvent raised
#   swarm      a Swarmed bearer's death jumps the status to the nearest living hostile, and stops after 3 jumps
#   mark       Grave Mark amplifies incoming damage
#   ward       a fixed-amount absorb: spends down, breaks with an event, passes the overflow; an unspent ward
#              returns mana on expiry, a broken one does not
#   dispel     Null Lance's dispel strips one buff (never a harm), Knit Bone's cleanse reports stacks; events
#   control    root: live control not refreshed, re-apply inside immunity refused with an event, allowed after;
#              a stun holds the body like a stagger; a frost hit on a chilled foe freezes (Stun+Root), then is
#              refused inside the immunity window
#   locomotion a rooted body does not move on foot and an unrooted one does
#   lightning  an arc prefers a Stormbrand over a nearer foe and carries more; a brand spell does not arc
#   necrotic   lifesteal heals more off a nearly dead target and doubles off a Grave Mark; Soul Echo refunds
#              mana on a kill
#   combos     every combo fires with its stable id and spends its status
#   vfx        each shaped status builds its mark and removes it with the status
#
# ⚠️ Actors are built by hand and the C# statics are reached through MagicStatusProbeSeam (a GDScript call
# cannot pass an IEntity or DamagePacket). READ THE WHOLE OUTPUT FOR SCRIPT ERRORS: a GDScript error
# mid-function aborts that function and the probe can still print PASS.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/magic_status_probe.gd
# Exits 0 when every case holds, 1 otherwise.
extends SceneTree

const HEALTH := 0     # StatType.Health
const MANA := 2       # StatType.Mana

const FIRE := 1
const FROST := 2
const LIGHTNING := 3
const NECROTIC := 6

const CTRL_ROOT := 1
const CTRL_STUN := 4

var _failures: Array[String] = []
var _seam


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	_seam = load("res://src/Magic/MagicStatusProbeSeam.cs").new()
	root.add_child(_seam)
	await process_frame

	if not _seam.BusAvailable:
		_fail("the EventBus autoload is not present, so no event can be observed")
	_floor()

	_check_content()
	await _check_kindle()
	await _check_swarm()
	await _check_mark_and_ward()
	await _check_dispel_and_cleanse()
	await _check_controls()
	await _check_locomotion()
	await _check_lightning()
	await _check_necrotic()
	await _check_combos()
	await _check_vfx()

	print("---")
	if _failures.is_empty():
		print("PASS: content, kindle, swarm, mark, ward, dispel, controls, locomotion, lightning, necrotic, combos and vfx all hold")
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


func _near(a: float, b: float, tol: float) -> bool:
	return absf(a - b) <= tol


func _floor() -> void:
	var body := StaticBody3D.new()
	body.collision_layer = 1
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(800, 1, 800)
	shape.shape = box
	body.add_child(shape)
	body.position = Vector3(0, -0.5, 0)
	root.add_child(body)


# A hand-built actor: {body, stats, combat, status, hurt}. `hp` is its health pool.
func _actor(name_: String, team: int, at: Vector3, hp := 5000.0, with_vfx := false, with_loco := false) -> Dictionary:
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

	var attrs = load("res://src/Stats/AttributeSet.cs").new()
	attrs.Health = hp
	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	stats.Attributes = attrs
	body.add_child(stats)
	var combat = load("res://src/Combat/CombatComponent.cs").new()
	combat.name = "Combat"
	combat.Team = team
	body.add_child(combat)
	var status = load("res://src/Magic/StatusEffectsComponent.cs").new()
	status.name = "StatusEffects"
	body.add_child(status)
	if with_vfx:
		var vfx = load("res://src/Magic/StatusEffectVfxComponent.cs").new()
		vfx.name = "StatusVfx"
		body.add_child(vfx)
	if with_loco:
		var loco = load("res://src/Movement/LocomotionComponent.cs").new()
		loco.name = "Locomotion"
		body.add_child(loco)

	var hurt := Area3D.new()
	hurt.set_script(load("res://src/Combat/Hurtbox.cs"))
	hurt.name = "Hurtbox"
	var zone := CollisionShape3D.new()
	var zshape := CapsuleShape3D.new()
	zshape.radius = 0.4
	zshape.height = 1.8
	zone.shape = zshape
	zone.position = Vector3(0, 0.9, 0)
	hurt.add_child(zone)
	body.add_child(hurt)

	root.add_child(body)
	return {"body": body, "stats": stats, "combat": combat, "status": status, "hurt": hurt}


func _spell(school: int, status_id := "") -> Resource:
	var s = load("res://src/Magic/SpellResource.cs").new()
	s.Id = "spell.probe_%d_%s" % [school, status_id]
	s.School = school
	s.Delivery = 0
	s.StatusEffectId = status_id
	return s


func _hp(a: Dictionary) -> float:
	return a["stats"].GetCurrent(HEALTH)


func _free(list: Array) -> void:
	for a in list:
		if a is Dictionary:
			a["body"].queue_free()


# ---- content -------------------------------------------------------------------------------------

func _check_content() -> void:
	var ids := {}
	var dir := DirAccess.open("res://data/status_effects")
	var files := 0
	for f in dir.get_files():
		if not f.ends_with(".tres"):
			continue
		files += 1
		var r = load("res://data/status_effects/" + f)
		if r == null:
			_fail("content: %s did not load" % f)
			continue
		ids[r.Id] = r
		if str(r.DisplayName).is_empty() or r.DisplayName == "Unknown Effect":
			_fail("content: %s has no DisplayName" % r.Id)
		if not _seam.LocHas(r.LocKey + ".name"):
			_fail("content: %s has no locale name (%s.name)" % [r.Id, r.LocKey])
		if str(r.LocalDescription).is_empty():
			_fail("content: %s has no locale description (%s.desc)" % [r.Id, r.LocKey])
		if r.Duration <= 0.0:
			_fail("content: %s has no duration" % r.Id)
	print("content: %d status effect files" % files)
	for id in ["status.kindled", "status.rooted", "status.silenced", "status.stormbrand", "status.barkskin",
			"status.grave_mark", "status.swarmed", "status.soul_echo", "status.stunned", "status.burning",
			"status.chill", "status.frozen", "status.decay", "status.regrowth", "status.arcane_ward"]:
		if not ids.has(id):
			_fail("content: reserved status %s is missing" % id)
	if ids.size() < 15:
		return
	var k = ids["status.kindled"]
	if k.DetonateAtStacks != 3 or k.MaxStacks < 3 or k.DetonateDamagePerStack <= 0.0 or k.DetonateRadius <= 0.0:
		_fail("content: kindled does not detonate at 3 stacks with radius and damage")
	if ids["status.rooted"].Controls != CTRL_ROOT or ids["status.rooted"].ControlImmunitySeconds <= 0.0:
		_fail("content: rooted is not a Root with immunity")
	if ids["status.silenced"].Controls != 2:
		_fail("content: silenced is not a Silence")
	if ids["status.stunned"].Controls != CTRL_STUN:
		_fail("content: stunned is not a Stun")
	if ids["status.frozen"].Controls != (CTRL_STUN | CTRL_ROOT) or ids["status.frozen"].ControlImmunitySeconds <= 0.0:
		_fail("content: frozen is not Stun|Root with immunity")
	if ids["status.stormbrand"].Controls != 8 or ids["status.grave_mark"].Controls != 8 or k.Controls != 8:
		_fail("content: a mark status lost its Mark control")
	if not ids["status.barkskin"].IsBeneficial or ids["status.barkskin"].ModStat != 8 or ids["status.barkskin"].ModValue <= 0.0:
		_fail("content: barkskin is not a beneficial armour buff")
	if ids["status.grave_mark"].DamageTakenModifier <= 0.0:
		_fail("content: grave mark does not amplify")
	if ids["status.swarmed"].SpreadOnDeathRadius <= 0.0 or ids["status.swarmed"].DamagePerTick <= 0.0:
		_fail("content: swarmed neither hurts nor spreads")
	if ids["status.soul_echo"].ManaOnKill <= 0.0 or not ids["status.soul_echo"].IsBeneficial:
		_fail("content: soul echo does not refund mana")
	var w = ids["status.arcane_ward"]
	if w.AbsorbAmount <= 0.0 or w.DamageTakenModifier >= 0.0 or w.ExpiryManaReturn <= 0.0:
		_fail("content: arcane ward is not a fixed absorb with a mana return")
	if ids["status.decay"].MaxStacks < 2:
		_fail("content: decay does not stack (Knit Bone heals per stack)")


# ---- kindle --------------------------------------------------------------------------------------

func _check_kindle() -> void:
	var c := _actor("KindleCaster", 1, Vector3(300, 0, 0))
	var t := _actor("KindleT", 2, Vector3(300, 0, -6))
	var n := _actor("KindleN", 2, Vector3(302, 0, -6))
	var far := _actor("KindleF", 2, Vector3(312, 0, -6))
	await _frames(3)
	var spell = _spell(FIRE, "status.kindled")
	_seam.ResetCounts()
	var hp_t := _hp(t)
	var hp_n := _hp(n)
	var hp_f := _hp(far)

	_seam.Hit(t["hurt"], spell, c["body"], 5.0)
	_seam.Hit(t["hurt"], spell, c["body"], 5.0)
	var mid: int = t["status"].StacksOf("status.kindled")
	print("kindle: after two hits stacks=%d, detonations=%d" % [mid, _seam.Count("detonated:status.kindled")])
	if mid != 2:
		_fail("kindle: two hits left %d stacks, expected 2" % mid)
	if _seam.Count("detonated:status.kindled") != 0:
		_fail("kindle: detonated before the third stack")

	_seam.Hit(t["hurt"], spell, c["body"], 5.0)
	var bearer_loss := hp_t - _hp(t)
	var neighbour_loss := hp_n - _hp(n)
	print("kindle: third hit -> detonations=%d dmg=%.1f, bearer lost %.1f, neighbour lost %.1f, far lost %.1f"
		% [_seam.Count("detonated:status.kindled"), _seam.LastDetonateDamage, bearer_loss, neighbour_loss, hp_f - _hp(far)])
	if _seam.Count("detonated:status.kindled") != 1:
		_fail("kindle: the third stack raised %d detonations" % _seam.Count("detonated:status.kindled"))
	if not _near(_seam.LastDetonateDamage, 27.0, 0.01):
		_fail("kindle: detonation damage %.1f, expected 3 stacks x 9" % _seam.LastDetonateDamage)
	if t["status"].StacksOf("status.kindled") != 0:
		_fail("kindle: the detonated Kindle was not consumed")
	if bearer_loss < 27.0 + 15.0 - 1.0:
		_fail("kindle: the bearer lost only %.1f (three hits of 15 plus a 27 burst expected)" % bearer_loss)
	if neighbour_loss < 26.0:
		_fail("kindle: a neighbour 2 m away lost only %.1f of the burst" % neighbour_loss)
	if not t["status"].Has("status.burning") or not n["status"].Has("status.burning"):
		_fail("kindle: the detonation did not ignite the bearer and its neighbour")
	if _hp(far) != hp_f or far["status"].Has("status.burning"):
		_fail("kindle: a foe 12 m away was caught in a 3 m burst")
	_free([c, t, n, far])


# ---- swarm ---------------------------------------------------------------------------------------

func _check_swarm() -> void:
	var c := _actor("SwarmCaster", 1, Vector3(500, 0, 0))
	var chain: Array = []
	for i in 6:
		chain.append(_actor("Swarm%d" % i, 2, Vector3(500 + i * 6, 0, -10)))
	await _frames(3)

	_seam.Apply(chain[0]["body"], "status.swarmed", c["body"])
	_seam.Kill(chain[0]["body"], c["body"])
	var jumped: bool = chain[1]["status"].Has("status.swarmed")
	print("swarm: bearer 0 died -> next carries it: %s" % jumped)
	if not jumped:
		_fail("swarm: the status did not jump to the nearest living hostile")
	if chain[2]["status"].Has("status.swarmed"):
		_fail("swarm: it jumped past the nearest foe")

	# Follow it down the line: each death is one jump, and the third jump's bearer cannot pass it on.
	_seam.Kill(chain[1]["body"], c["body"])
	var second: bool = chain[2]["status"].Has("status.swarmed")
	_seam.Kill(chain[2]["body"], c["body"])
	var third: bool = chain[3]["status"].Has("status.swarmed")
	_seam.Kill(chain[3]["body"], c["body"])
	var fourth: bool = chain[4]["status"].Has("status.swarmed")
	print("swarm: jump 2 landed %s, jump 3 landed %s, jump 4 landed %s" % [second, third, fourth])
	if not second or not third:
		_fail("swarm: the second or third jump did not land")
	if fourth:
		_fail("swarm: a fourth jump landed; spread must be bounded to three")

	# A caster with nobody hostile around: the swarm just dies with its bearer.
	var lone := _actor("SwarmLone", 2, Vector3(700, 0, 0))
	await _frames(2)
	_seam.Apply(lone["body"], "status.swarmed", c["body"])
	_seam.Kill(lone["body"], c["body"])
	if lone["status"].Has("status.swarmed") and _hp(lone) > 0.0:
		_fail("swarm: a killed bearer kept its status")
	_free([c, lone])
	_free(chain)


# ---- mark and ward -------------------------------------------------------------------------------

func _check_mark_and_ward() -> void:
	var c := _actor("WardCaster", 1, Vector3(900, 0, 0))
	var marked := _actor("Marked", 2, Vector3(900, 0, -6))
	var plain := _actor("Plain", 2, Vector3(910, 0, -6))
	var ward := _actor("Warded", 2, Vector3(920, 0, -6))
	await _frames(3)
	var hit = _spell(0)

	_seam.Apply(marked["body"], "status.grave_mark", c["body"])
	var m0 := _hp(marked)
	var p0 := _hp(plain)
	_seam.Hit(marked["hurt"], hit, c["body"], 40.0)
	_seam.Hit(plain["hurt"], hit, c["body"], 40.0)
	var ratio := (m0 - _hp(marked)) / (p0 - _hp(plain))
	print("mark: marked took x%.3f of an unmarked hit" % ratio)
	if not _near(ratio, 1.25, 0.02):
		_fail("mark: a Grave Marked foe took x%.3f, expected x1.25" % ratio)

	# A fixed-amount ward: SpellPower 10 x 2 + 30 = 50.
	_seam.Apply(ward["body"], "status.arcane_ward", c["body"])
	var cap: float = _seam.WardRemaining(ward["body"])
	print("ward: capacity %.1f" % cap)
	if not _near(cap, 50.0, 0.01):
		_fail("ward: capacity %.2f, expected 30 + 10 SpellPower x 2 = 50" % cap)
	var w0 := _hp(ward)
	_seam.Hit(ward["hurt"], hit, c["body"], 30.0)
	var left: float = _seam.WardRemaining(ward["body"])
	print("ward: after a 30 hit health lost %.2f, ward left %.1f" % [w0 - _hp(ward), left])
	if _hp(ward) != w0:
		_fail("ward: a hit inside the ward's capacity still cost %.2f health" % (w0 - _hp(ward)))
	if left <= 0.0 or left >= cap:
		_fail("ward: the pool did not spend down (left %.1f)" % left)
	_seam.Hit(ward["hurt"], hit, c["body"], 100.0)
	print("ward: after a 100 hit health lost %.2f, ward present: %s, broke x%d"
		% [w0 - _hp(ward), ward["status"].Has("status.arcane_ward"), _seam.Count("wardbroken:status.arcane_ward")])
	if _hp(ward) >= w0:
		_fail("ward: the overflow past a broken ward did not land")
	if w0 - _hp(ward) > 100.0 - left + 1.0:
		_fail("ward: the remainder absorbed too little (lost %.1f)" % (w0 - _hp(ward)))
	if ward["status"].Has("status.arcane_ward"):
		_fail("ward: a spent ward stayed on the bearer")
	if _seam.Count("wardbroken:status.arcane_ward") != 1:
		_fail("ward: breaking raised %d WardBrokenEvents" % _seam.Count("wardbroken:status.arcane_ward"))

	# Unspent expiry returns mana; a broken ward does not.
	var self_c := _actor("SelfWard", 1, Vector3(940, 0, 0))
	await _frames(2)
	self_c["stats"].SetCurrent(MANA, 0.0)
	_seam.Apply(self_c["body"], "status.arcane_ward", self_c["body"])
	_seam.Step(self_c["body"], 13.0)
	var returned: float = self_c["stats"].GetCurrent(MANA)
	print("ward: unspent expiry returned %.2f mana" % returned)
	if returned < 7.5 or returned > 10.0:
		_fail("ward: an unspent ward returned %.2f mana, expected about 8" % returned)
	if self_c["status"].Has("status.arcane_ward"):
		_fail("ward: it did not expire")
	self_c["stats"].SetCurrent(MANA, 0.0)
	_seam.Apply(self_c["body"], "status.arcane_ward", self_c["body"])
	_seam.ModifyIncoming(self_c["body"], 500.0, null)
	_seam.Step(self_c["body"], 13.0)
	var after_break: float = self_c["stats"].GetCurrent(MANA)
	if after_break > 1.0:
		_fail("ward: a broken ward returned %.2f mana" % after_break)
	_free([c, marked, plain, ward, self_c])


# ---- dispel and cleanse --------------------------------------------------------------------------

func _check_dispel_and_cleanse() -> void:
	var c := _actor("DispelCaster", 1, Vector3(1100, 0, 0))
	var t := _actor("DispelT", 2, Vector3(1100, 0, -6))
	await _frames(2)
	_seam.ResetCounts()
	_seam.Apply(t["body"], "status.regrowth", null)
	_seam.Apply(t["body"], "status.arcane_ward", null)
	_seam.Apply(t["body"], "status.burning", c["body"])
	var stripped: String = _seam.Dispel(t["body"], c["body"])
	print("dispel: stripped '%s'" % stripped)
	if stripped != "status.arcane_ward":
		_fail("dispel: stripped '%s', expected the longest-lasting buff (the ward)" % stripped)
	if _seam.Count("dispelled:status.arcane_ward") != 1:
		_fail("dispel: no StatusDispelledEvent for the ward")
	if not t["status"].Has("status.burning"):
		_fail("dispel: a harmful status was stripped")
	if _seam.Dispel(t["body"], c["body"]) != "status.regrowth":
		_fail("dispel: the second dispel did not take the remaining buff")
	if _seam.Dispel(t["body"], c["body"]) != "":
		_fail("dispel: it stripped something with no buff left (burning must stay)")
	if not t["status"].Has("status.burning"):
		_fail("dispel: burning was dispelled")

	# Knit Bone: cleanse Decay on the caster, one event, stacks reported.
	_seam.Apply(c["body"], "status.decay", t["body"])
	_seam.Apply(c["body"], "status.decay", t["body"])
	_seam.Apply(c["body"], "status.decay", t["body"])
	var stacks: int = _seam.Cleanse(c["body"], "status.decay", c["body"])
	print("cleanse: removed %d decay stacks" % stacks)
	if stacks != 3:
		_fail("cleanse: reported %d stacks, expected 3" % stacks)
	if c["status"].Has("status.decay") or _seam.Count("dispelled:status.decay") != 1:
		_fail("cleanse: decay stayed or no StatusDispelledEvent")
	if _seam.Cleanse(c["body"], "status.decay", c["body"]) != 0:
		_fail("cleanse: an absent status reported stacks")
	_free([c, t])


# ---- controls ------------------------------------------------------------------------------------

func _check_controls() -> void:
	var c := _actor("CtlCaster", 1, Vector3(1300, 0, 0))
	var t := _actor("CtlT", 2, Vector3(1300, 0, -6))
	await _frames(3)
	_seam.ResetCounts()

	# Root: a live root is not extended, a fresh one inside the immunity window is refused.
	if not _seam.Apply(t["body"], "status.rooted", c["body"]) or not t["status"].IsRooted:
		_fail("control: root did not apply")
	_seam.Step(t["body"], 2.0)
	_seam.Apply(t["body"], "status.rooted", c["body"])
	_seam.Step(t["body"], 0.7)
	if t["status"].IsRooted:
		_fail("control: re-applying a live root extended it (chain-lock)")
	if _seam.Apply(t["body"], "status.rooted", c["body"]):
		_fail("control: a root inside the immunity window was accepted")
	if _seam.Count("resisted:status.rooted") != 1:
		_fail("control: the refused root raised %d StatusResistedEvents" % _seam.Count("resisted:status.rooted"))
	if t["status"].IsRooted:
		_fail("control: a refused root still rooted the bearer")
	if _seam.ImmunityRemaining(t["body"], CTRL_ROOT) <= 0.0:
		_fail("control: no immunity was recorded after the root ended")
	if not t["status"].IsRooted and _seam.Apply(t["body"], "status.stunned", c["body"]) == false:
		_fail("control: root immunity wrongly refused a stun")
	_seam.Step(t["body"], 4.5)
	if not _seam.Apply(t["body"], "status.rooted", c["body"]):
		_fail("control: root was still refused after the immunity ran out")

	# Stun holds the body like a stagger, with no punish window.
	var s := _actor("CtlStun", 2, Vector3(1320, 0, -6))
	await _frames(2)
	_seam.Apply(s["body"], "status.stunned", c["body"])
	if not s["status"].IsStunned or not s["combat"].IsStaggered:
		_fail("control: a stun did not hold the body (stunned=%s staggered=%s)"
			% [s["status"].IsStunned, s["combat"].IsStaggered])
	if s["combat"].IsOpen:
		_fail("control: a stun opened a riposte window")

	# Frost identity: chill, then a frost hit freezes; then the freeze is refused inside its immunity.
	var f := _actor("CtlFrost", 2, Vector3(1340, 0, -6))
	await _frames(2)
	var frost = _spell(FROST, "status.chill")
	_seam.Hit(f["hurt"], frost, c["body"], 10.0)
	if not f["status"].Has("status.chill") or f["status"].Has("status.frozen"):
		_fail("frost: the first hit should chill and not freeze")
	_seam.Hit(f["hurt"], frost, c["body"], 10.0)
	print("frost: second hit -> frozen=%s stunned=%s rooted=%s staggered=%s"
		% [f["status"].Has("status.frozen"), f["status"].IsStunned, f["status"].IsRooted, f["combat"].IsStaggered])
	if not f["status"].Has("status.frozen") or not f["status"].IsStunned or not f["status"].IsRooted:
		_fail("frost: a frost hit on a chilled foe did not freeze it (Stun and Root)")
	if not f["combat"].IsStaggered:
		_fail("frost: the freeze did not hold the body")
	_seam.Hit(f["hurt"], frost, c["body"], 10.0)
	if _seam.Count("resisted:status.frozen") != 0:
		_fail("frost: hitting a frozen foe was 'resisted'; a live freeze is just left alone")
	_seam.Step(f["body"], 2.0)
	if f["status"].Has("status.frozen"):
		_fail("frost: the freeze outlived its duration")
	_seam.Hit(f["hurt"], frost, c["body"], 10.0)
	_seam.Hit(f["hurt"], frost, c["body"], 10.0)
	print("frost: refrozen inside immunity -> frozen=%s resisted=%d"
		% [f["status"].Has("status.frozen"), _seam.Count("resisted:status.frozen")])
	if f["status"].Has("status.frozen") or _seam.Count("resisted:status.frozen") < 1:
		_fail("frost: a second freeze inside the immunity window was not refused with an event")
	_seam.Step(f["body"], 6.0)
	_seam.Hit(f["hurt"], frost, c["body"], 10.0)
	_seam.Hit(f["hurt"], frost, c["body"], 10.0)
	if not f["status"].Has("status.frozen"):
		_fail("frost: the freeze never came back after the immunity ended")
	_free([c, t, s, f])


# ---- locomotion ----------------------------------------------------------------------------------

func _flat(a: Vector3, b: Vector3) -> float:
	return Vector2(a.x - b.x, a.z - b.z).length()


func _walk(a: Dictionary, loco, frames: int) -> float:
	var start: Vector3 = a["body"].global_position
	for i in frames:
		loco.Move(1.0 / 60.0, Vector3(0, 0, -1), false, false)
		await physics_frame
	return _flat(a["body"].global_position, start)


func _check_locomotion() -> void:
	var a := _actor("Walker", 2, Vector3(0, 0.05, 300), 100.0, false, true)
	var loco = a["body"].get_node("Locomotion")
	await _frames(5)

	var free_moved: float = await _walk(a, loco, 40)
	_seam.Apply(a["body"], "status.rooted", null)
	var rooted_moved: float = await _walk(a, loco, 40)
	_seam.Consume(a["body"], "status.rooted")
	_seam.Apply(a["body"], "status.stunned", null)
	var stunned_moved: float = await _walk(a, loco, 40)
	_seam.Consume(a["body"], "status.stunned")
	var again: float = await _walk(a, loco, 40)
	print("locomotion: free moved %.2f m, rooted %.2f m, stunned %.2f m, freed again %.2f m (y=%.2f)"
		% [free_moved, rooted_moved, stunned_moved, again, a["body"].global_position.y])
	if free_moved < 1.0:
		_fail("locomotion: an unrooted body moved only %.2f m in 40 frames" % free_moved)
	if rooted_moved > 0.05:
		_fail("locomotion: a rooted body still moved %.2f m" % rooted_moved)
	if stunned_moved > 0.05:
		_fail("locomotion: a stunned body still moved %.2f m" % stunned_moved)
	if again < 1.0:
		_fail("locomotion: the body did not move again once the control ended")
	_free([a])


# ---- lightning -----------------------------------------------------------------------------------

func _check_lightning() -> void:
	var c := _actor("LtCaster", 1, Vector3(1700, 0, 0))
	var p := _actor("LtPrimary", 2, Vector3(1700, 0, -6))
	var near := _actor("LtNear", 2, Vector3(1702, 0, -6))
	var brand := _actor("LtBrand", 2, Vector3(1700, 0, -16))
	await _frames(3)
	var bolt = _spell(LIGHTNING)

	var p0 := _hp(p)
	var n0 := _hp(near)
	_seam.Hit(p["hurt"], bolt, c["body"], 40.0)
	var primary_loss := p0 - _hp(p)
	var plain_arc := n0 - _hp(near)
	print("lightning: no brand -> primary lost %.1f, nearest foe %.1f, far foe %.1f"
		% [primary_loss, plain_arc, 5000.0 - _hp(brand)])
	if plain_arc <= 0.0 or _hp(brand) != 5000.0:
		_fail("lightning: without a brand the arc should reach the nearest foe only")
	var plain_ratio := plain_arc / primary_loss

	_seam.Apply(brand["body"], "status.stormbrand", c["body"])
	p0 = _hp(p)
	n0 = _hp(near)
	var b0 := _hp(brand)
	_seam.Hit(p["hurt"], bolt, c["body"], 40.0)
	var primary2 := p0 - _hp(p)
	var brand_arc := b0 - _hp(brand)
	print("lightning: brand 10 m away -> primary lost %.1f, brand %.1f, nearer foe %.1f"
		% [primary2, brand_arc, n0 - _hp(near)])
	if brand_arc <= 0.0:
		_fail("lightning: a Stormbrand 10 m away did not draw the arc")
	if _hp(near) != n0:
		_fail("lightning: the arc went to the nearer foe instead of the brand")
	var brand_ratio := brand_arc / primary2
	if brand_ratio <= plain_ratio + 0.15:
		_fail("lightning: an arc to a brand (x%.2f) should carry clearly more than a plain arc (x%.2f)"
			% [brand_ratio, plain_ratio])

	# The brand spell itself is a mark, not a bolt.
	var brand_spell = _spell(LIGHTNING, "status.stormbrand")
	p0 = _hp(p)
	b0 = _hp(brand)
	n0 = _hp(near)
	_seam.Hit(p["hurt"], brand_spell, c["body"], 10.0)
	if _hp(brand) != b0 or _hp(near) != n0:
		_fail("lightning: casting Stormbrand itself arced to another foe")
	_free([c, p, near, brand])


# ---- necrotic ------------------------------------------------------------------------------------

func _check_necrotic() -> void:
	var c := _actor("NecCaster", 1, Vector3(1900, 0, 0), 100.0)
	var full := _actor("NecFull", 2, Vector3(1900, 0, -6))
	var marked := _actor("NecMarked", 2, Vector3(1920, 0, -6))
	var low := _actor("NecLow", 2, Vector3(1940, 0, -6))
	await _frames(3)
	var drain = _spell(NECROTIC)
	_seam.Apply(marked["body"], "status.grave_mark", c["body"])
	low["stats"].SetCurrent(HEALTH, 120.0)

	c["stats"].SetCurrent(HEALTH, 1.0)
	_seam.Hit(full["hurt"], drain, c["body"], 40.0)
	var heal_full: float = c["stats"].GetCurrent(HEALTH) - 1.0
	c["stats"].SetCurrent(HEALTH, 1.0)
	_seam.Hit(marked["hurt"], drain, c["body"], 40.0)
	var heal_marked: float = c["stats"].GetCurrent(HEALTH) - 1.0
	c["stats"].SetCurrent(HEALTH, 1.0)
	_seam.Hit(low["hurt"], drain, c["body"], 40.0)
	var heal_low: float = c["stats"].GetCurrent(HEALTH) - 1.0
	print("necrotic: lifesteal full %.2f, marked %.2f, near dead %.2f" % [heal_full, heal_marked, heal_low])
	if not _near(heal_full, 14.1, 0.5):
		_fail("necrotic: a hit on a healthy foe healed %.2f, expected about 14" % heal_full)
	if not _near(heal_marked / heal_full, 2.0, 0.05):
		_fail("necrotic: off a Grave Mark the heal was x%.2f, expected doubled" % (heal_marked / heal_full))
	if heal_low < heal_full * 1.5:
		_fail("necrotic: a nearly dead target healed %.2f, expected clearly more than %.2f" % [heal_low, heal_full])

	# Soul Echo refunds mana on a kill, and only while it is on.
	var prey := _actor("NecPrey", 2, Vector3(1960, 0, -6))
	var prey2 := _actor("NecPrey2", 2, Vector3(1980, 0, -6))
	await _frames(2)
	c["stats"].SetCurrent(MANA, 0.0)
	_seam.Kill(prey["body"], c["body"])
	var without: float = c["stats"].GetCurrent(MANA)
	_seam.Apply(c["body"], "status.soul_echo", c["body"])
	_seam.Kill(prey2["body"], c["body"])
	var with_echo: float = c["stats"].GetCurrent(MANA)
	print("necrotic: mana after a kill without echo %.2f, with echo %.2f" % [without, with_echo])
	if without > 1.0:
		_fail("necrotic: a kill refunded %.2f mana with no Soul Echo" % without)
	if with_echo < 11.0:
		_fail("necrotic: a kill under Soul Echo refunded only %.2f mana" % with_echo)
	_free([c, full, marked, low, prey, prey2])


# ---- combos --------------------------------------------------------------------------------------

func _check_combos() -> void:
	var c := _actor("ComboCaster", 1, Vector3(2100, 0, 0))
	# [trigger school, status on the target, combo id]
	var cases := [
		[LIGHTNING, "status.chill", "combo.shatter"],
		[FIRE, "status.chill", "combo.thermal_shock"],
		[FIRE, "status.frozen", "combo.meltdown"],
		[FROST, "status.kindled", "combo.steam_burst"],
		[FROST, "status.stormbrand", "combo.superconduct"],
		[FIRE, "status.swarmed", "combo.smoke_out"],
	]
	var i := 0
	for k in cases:
		var t := _actor("Combo%d" % i, 2, Vector3(2100 + i * 40, 0, -6))
		await _frames(2)
		_seam.ResetCounts()
		_seam.Apply(t["body"], k[1], c["body"])
		var before := _hp(t)
		_seam.Hit(t["hurt"], _spell(k[0]), c["body"], 10.0)
		var fired: int = _seam.Count("combo:" + k[2])
		print("combo: %s into %s -> %s x%d, extra damage %.1f, status left: %s"
			% [k[0], k[1], k[2], fired, before - _hp(t) - 10.0, t["status"].Has(k[1])])
		if fired != 1:
			_fail("combo: %s did not fire exactly once (fired %d, last '%s')" % [k[2], fired, _seam.LastComboId])
		if t["status"].Has(k[1]):
			_fail("combo: %s did not spend %s" % [k[2], k[1]])
		if before - _hp(t) < 20.0:
			_fail("combo: %s dealt no burst (%.1f)" % [k[2], before - _hp(t)])
		t["body"].queue_free()
		i += 1
	# No combo without the status.
	var bare := _actor("ComboBare", 2, Vector3(2600, 0, -6))
	await _frames(2)
	_seam.ResetCounts()
	_seam.Hit(bare["hurt"], _spell(FIRE), c["body"], 10.0)
	if _seam.LastComboId != "":
		_fail("combo: '%s' fired on a foe with no status" % _seam.LastComboId)
	_free([c, bare])


# ---- vfx -----------------------------------------------------------------------------------------

func _check_vfx() -> void:
	var shapes := {
		"status.kindled": "StatusVfxMarkRing",
		"status.rooted": "StatusVfxThorns",
		"status.silenced": "StatusVfxBrokenGlyph",
		"status.stunned": "StatusVfxStars",
		"status.frozen": "StatusVfxIceShell",
		"status.arcane_ward": "StatusVfxWardShell",
	}
	var i := 0
	# One bearer per status: applying a second control to the same body would (rightly) be refused
	# by the diminishing-returns rule once the first one ended.
	for id in shapes:
		var a := _actor("VfxActor%d" % i, 2, Vector3(2800 + i * 10, 0, 0), 5000.0, true)
		await _frames(3)
		_seam.Apply(a["body"], id, null)
		await _frames(2)
		var node = a["body"].get_node_or_null(shapes[id])
		if node == null:
			_fail("vfx: %s built no %s" % [id, shapes[id]])
		else:
			if node.get_child_count() == 0:
				_fail("vfx: %s's %s is empty" % [id, shapes[id]])
			_seam.Consume(a["body"], id)
			await _frames(3)
			if a["body"].get_node_or_null(shapes[id]) != null:
				_fail("vfx: %s's %s outlived the status" % [id, shapes[id]])
		_free([a])
		i += 1
	print("vfx: %d shaped marks checked" % shapes.size())
	# A burning status still gets the swirl.
	var b := _actor("VfxBurn", 2, Vector3(2900, 0, 0), 5000.0, true)
	await _frames(3)
	_seam.Apply(b["body"], "status.burning", null)
	await _frames(2)
	var swirl := false
	for ch in b["body"].get_children():
		if ch is GPUParticles3D:
			swirl = true
	if not swirl:
		_fail("vfx: burning lost its particle swirl")
	_free([b])
