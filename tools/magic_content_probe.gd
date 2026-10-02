# Magic content probe: the 2026-09 spell roster and who casts it, proven against the REAL databases.
# The unit suite (SpellRosterTests) reads the same files as text; this loads them the way the game does.
#
#   roster     exactly 25 player-learnable spells, the committed ids, each in its school, unique, and the
#              four retired ids are gone as files
#   contract   every spell status, self status and consumed status resolves; Ground/Barrier/Dash spells
#              carry the numbers their placement needs; control spells carry a control status
#   aliases    each retired id resolves to a live roster spell through SpellDatabase.Get
#   loadouts   no enemy, boss or race still names a retired id, every id resolves, and the caster roster
#              uses the new spells (shamans root, wisps Emberlash, Arcane Echo Null Lance and Gravity Well,
#              bosses carry ground and barrier spells)
#   routes     the player starts with Emberlash and every one of the 25 has a tome, a teacher, a race or
#              the start (the same table --validate enforces), and the validator reports nothing
#
# ⚠️ READ THE WHOLE OUTPUT FOR SCRIPT ERRORS. A GDScript error mid-function aborts that function and the
# probe can still print PASS.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/magic_content_probe.gd
# Exits 0 when every case holds, 1 otherwise.
extends SceneTree

const DELIVERY_GROUND := 4
const DELIVERY_BARRIER := 5
const DELIVERY_DASH := 6
const CONTROL_ROOT := 1
const CONTROL_SILENCE := 2
const CONTROL_STUN := 4

var _failures: Array[String] = []
var _d


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame
	_d = load("res://src/Debugging/MagicContentProbeDriver.cs").new()

	_check_roster()
	_check_contract()
	_check_aliases()
	_check_loadouts()
	_check_routes()
	for issue in _d.NumericGuardIssues():
		_fail(issue)
	await _check_prepared_tomes()

	print("---")
	if _failures.is_empty():
		print("PASS: 25 player spells, statuses, aliases, enemy loadouts and a learn route for each all hold against the real databases")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _fail(msg: String) -> void:
	_failures.append(msg)


func _check_prepared_tomes() -> void:
	var learner := CharacterBody3D.new()
	learner.set_script(load("res://src/Entities/CharacterEntity.cs"))
	learner.name = "PreparedTomeLearner"
	root.add_child(learner)
	for script_name in ["Stats/StatsComponent", "Dialogue/StoryFlagsComponent", "Magic/SpellcastingComponent"]:
		var component = load("res://src/%s.cs" % script_name).new()
		component.name = script_name.get_file()
		learner.add_child(component)
	await process_frame
	for issue in _d.PreparedTomeIssues(learner):
		_fail(issue)
	learner.queue_free()
	await process_frame


func _check_roster() -> void:
	var roster: PackedStringArray = _d.Roster()
	var learnable: PackedStringArray = _d.PlayerLearnableIds()
	if roster.size() != 25:
		_fail("roster has %d ids, expected 25" % roster.size())
	if learnable.size() != 25:
		_fail("%d player-learnable spells, expected 25" % learnable.size())
	var seen := {}
	var sizes := [4, 4, 4, 4, 5, 4]   # Fire, Frost, Lightning, Arcane, Nature, Necrotic
	var index := 0
	for school in sizes.size():
		for n in sizes[school]:
			var id: String = roster[index]
			index += 1
			if seen.has(id):
				_fail("roster lists %s twice" % id)
			seen[id] = true
			var spell = _d.GetSpell(id)
			if spell == null or not _d.HasSpellFile(id):
				_fail("roster spell %s has no file" % id)
				continue
			if not spell.PlayerLearnable:
				_fail("%s is not PlayerLearnable" % id)
			if int(spell.School) != school + 1:
				_fail("%s is school %d, expected %d" % [id, int(spell.School), school + 1])
			if spell.MaxRank != 3:
				_fail("%s MaxRank %d, expected 3" % [id, spell.MaxRank])
			if not (learnable.has(id)):
				_fail("%s missing from the player-learnable set" % id)
	for old in ["spell.firebolt", "spell.fireball", "spell.arcane_lance", "spell.lesser_heal"]:
		if _d.HasSpellFile(old):
			_fail("retired %s still has a file" % old)
	for enemy_only in ["spell.ash_breath", "spell.dragon_breath", "spell.drake_breath", "spell.elder_word", "spell.wither"]:
		var spell = _d.GetSpell(enemy_only)
		if spell == null or spell.PlayerLearnable:
			_fail("%s must exist and stay PlayerLearnable = false" % enemy_only)


func _check_contract() -> void:
	for id in _d.Roster():
		var spell = _d.GetSpell(id)
		if spell == null:
			continue
		for status_id in [spell.StatusEffectId, spell.SelfStatusEffectId, spell.ConsumesStatusId]:
			if status_id != "" and not _d.StatusExists(status_id):
				_fail("%s names unknown status %s" % [id, status_id])
		var windup: float = spell.WindupSeconds
		if windup < 0.15 or windup > 0.9:
			_fail("%s wind-up %.2f is outside 0.15..0.9" % [id, windup])
		var delivery := int(spell.Delivery)
		if delivery == DELIVERY_GROUND and (spell.GroundDelay <= 0.0 or spell.PlaceRange <= 0.0 or spell.ImpactRadius <= 0.0):
			_fail("%s is a ground spell without delay, range and radius" % id)
		if delivery == DELIVERY_BARRIER and (spell.BarrierDuration <= 0.0 or spell.BarrierWidth <= 0.0):
			_fail("%s is a barrier without duration and width" % id)
		if delivery == DELIVERY_DASH and spell.DashDistance <= 0.0:
			_fail("%s is a dash without a distance" % id)
	# The control spells carry a control status (the tactics read control from data, not ids).
	var expect_control := {"spell.thornsnare": CONTROL_ROOT, "spell.null_lance": CONTROL_SILENCE, "spell.thunder_step": CONTROL_STUN}
	for id in expect_control:
		var status = _d.GetStatus(_d.GetSpell(id).StatusEffectId)
		if status == null or (int(status.Controls) & int(expect_control[id])) == 0:
			_fail("%s status does not carry its control flag" % id)
	if _d.GetSpell("spell.soul_tithe").HealthCost <= 0.0:
		_fail("soul_tithe has no health cost")
	if _d.GetSpell("spell.knit_bone").ConsumesStatusId != "status.decay":
		_fail("knit_bone does not consume decay")


func _check_aliases() -> void:
	var expected := {
		"spell.firebolt": "spell.emberlash", "spell.fireball": "spell.sunfall",
		"spell.arcane_lance": "spell.null_lance", "spell.lesser_heal": "spell.mending_bloom",
	}
	for old in expected:
		var spell = _d.GetSpell(old)
		if spell == null or spell.Id != expected[old]:
			_fail("%s does not resolve to %s" % [old, expected[old]])
		if _d.Resolve(old) != expected[old]:
			_fail("alias table maps %s to %s" % [old, _d.Resolve(old)])


func _loadout(owner_id: String) -> Array:
	var ids := []
	for row in _d.LoadoutRows():
		var parts: PackedStringArray = row.split("|")
		if parts[0] == owner_id:
			ids.append(parts[1])
	return ids


func _check_loadouts() -> void:
	var retired := ["spell.firebolt", "spell.fireball", "spell.arcane_lance", "spell.lesser_heal"]
	var rows: PackedStringArray = _d.LoadoutRows()
	if rows.size() < 30:
		_fail("only %d loadout rows read; the driver is not seeing the databases" % rows.size())
	for row in rows:
		var parts: PackedStringArray = row.split("|")
		if retired.has(parts[1]):
			_fail("%s still names retired %s" % [parts[0], parts[1]])
		if _d.GetSpell(parts[1]) == null:
			_fail("%s names unknown spell %s" % [parts[0], parts[1]])
	var wants := {
		"enemy.clan_shaman": ["spell.thornsnare", "spell.mending_bloom"],
		"enemy.cinder_wisp": ["spell.emberlash"],
		"enemy.arcane_echo": ["spell.null_lance", "spell.gravity_well"],
		"enemy.hollow_necromancer": ["spell.grave_mark", "spell.knit_bone"],
		"boss.morthul": ["spell.sunfall", "spell.pyre_wall"],
		"boss.hollow_queen": ["spell.gravity_well"],
		"boss.crimson_prophet": ["spell.sunfall"],
		"race.valari": ["spell.null_lance"],
		"race.draekyn": ["spell.flame_lance"],
	}
	for owner_id in wants:
		var have := _loadout(owner_id)
		for spell_id in wants[owner_id]:
			if not have.has(spell_id):
				_fail("%s should carry %s, has %s" % [owner_id, spell_id, str(have)])


func _check_routes() -> void:
	var starting: PackedStringArray = _d.Starting()
	if starting.size() != 1 or starting[0] != "spell.emberlash":
		_fail("the player should start with exactly Emberlash, got %s" % str(starting))
	var table := []
	for id in _d.Roster():
		var routes: PackedStringArray = _d.RoutesOf(id)
		if routes.is_empty():
			_fail("%s has no learn route" % id)
		table.append("%s: %s" % [id, ", ".join(routes)])
	for line in table:
		print(line)
	for issue in _d.RuleIssues():
		_fail("validator: %s" % issue)
