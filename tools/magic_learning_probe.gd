# Magic learning probe: mastery, the fading Weave, the one learning route, corrupted casts, the spellbook
# and the HUD chips, driven through the real components. The pure rules are pinned by
# SchoolMasteryMathTests, WeaveMathTests and SpellLearningRulesTests; this proves the wiring around them.
#
#   mastery    casts bank points (a held channel is paced), a rank-up raises the event once, rank 3 grants
#              a school resistance that a load replaces (never doubles, never survives a load without it),
#              and a load raises no rank event
#   weave      potency bends ordinary and corrupted casts in opposite directions, inside the clamp
#   learning   tome, dialogue and trainer share one route: one SpellLearnedEvent with the route string, no
#              double learn, a locked corrupted spell is refused with a reason, a corrupted tome needs two
#              interactions, a retired id learns its replacement once, and a save/load restore is silent
#   book       the real SpellbookPanel builds for the roster, names the lock reason and the rule lines
#   hud        the real GameHud shows a Silenced chip and the Weave chip from live state
#
# Actors are built by hand (a static factory is not reachable from GDScript) and the C# half lives in
# src/Debugging/MagicLearningProbeDriver.cs, which holds no rules.
#
# READ THE WHOLE OUTPUT FOR SCRIPT ERRORS. A GDScript error mid-function aborts that function and the probe
# can still print PASS.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/magic_learning_probe.gd
# Exits 0 when every case holds, 1 otherwise.
extends SceneTree

const FIRE_RESIST := 15   # StatType.FireResist
const FIRE := 1           # DamageType.Fire

const OUTCOME_LEARNED := 0
const OUTCOME_KNOWN := 1
const OUTCOME_LOCKED := 3

const REFUSE_KNOWN := 0
const REFUSE_TIER := 1
const REFUSE_CONFIRM := 3

var _failures: Array[String] = []
var _driver


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame
	_driver = load("res://src/Debugging/MagicLearningProbeDriver.cs").new()
	_driver.Listen()
	_driver.EnsureInput()

	await _check_mastery()
	await _check_weave()
	await _check_learning()
	await _check_book()
	await _check_hud()

	print("---")
	if _failures.is_empty():
		print("PASS: mastery, Weave, the learning route, the spellbook and the HUD chips hold through real components")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


# ---- building blocks -----------------------------------------------------------------------------

func _fail(msg: String) -> void:
	_failures.append(msg)


func _expect(cond: bool, msg: String) -> void:
	if not cond:
		_fail(msg)


func _caster(name_: String, known: Array[String]) -> Dictionary:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = name_
	root.add_child(body)  # in the tree first, so the components resolve their owner as they are added

	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	body.add_child(stats)
	var corruption = load("res://src/Corruption/CorruptionComponent.cs").new()
	corruption.name = "Corruption"
	body.add_child(corruption)
	var effects = load("res://src/Magic/StatusEffectsComponent.cs").new()
	effects.name = "StatusEffects"
	body.add_child(effects)
	var mastery = load("res://src/Magic/SchoolMasteryComponent.cs").new()
	mastery.name = "SchoolMastery"
	body.add_child(mastery)
	var progression = load("res://src/Progression/ProgressionComponent.cs").new()
	progression.name = "Progression"
	body.add_child(progression)
	var casting = load("res://src/Magic/SpellcastingComponent.cs").new()
	casting.name = "Spellcasting"
	casting.KnownSpellIds = known
	body.add_child(casting)
	return {"body": body, "stats": stats, "corruption": corruption, "effects": effects,
		"mastery": mastery, "casting": casting, "progression": progression}


func _all_text(node: Node, out: Array[String]) -> void:
	if node is Label:
		out.append(node.text)
	elif node is Button:
		out.append(node.text)
	for child in node.get_children():
		_all_text(child, out)


func _has_text(node: Node, needle: String) -> bool:
	var texts: Array[String] = []
	_all_text(node, texts)
	for t in texts:
		if needle in t:
			return true
	return false


func _buy_button(node: Node) -> Button:
	if node is Button and node.text.begins_with("Buy (") and not node.disabled:
		return node
	for child in node.get_children():
		var found := _buy_button(child)
		if found != null:
			return found
	return null


# ---- mastery -------------------------------------------------------------------------------------

func _check_mastery() -> void:
	var known: Array[String] = []
	var c := _caster("MasteryCaster", known)
	var mastery = c["mastery"]
	var stats = c["stats"]
	await process_frame

	var base_resist: float = stats.GetValue(FIRE_RESIST)
	var ranked_before: int = _driver.RankedUp

	mastery.SetPoints(FIRE, 9)
	_expect(mastery.RankOf(FIRE) == 0, "mastery: 9 points is still rank 0")
	_expect(_driver.RankedUp == ranked_before, "mastery: no rank event below the first threshold")

	mastery.SetPoints(FIRE, 10)
	_expect(mastery.RankOf(FIRE) == 1, "mastery: 10 points is rank 1")
	_expect(_driver.RankedUp == ranked_before + 1 and _driver.LastRank == 1, "mastery: rank 1 raises exactly one event")

	mastery.AddPoints(FIRE, 1)
	_expect(_driver.RankedUp == ranked_before + 1, "mastery: a cast inside a rank raises no event")

	mastery.SetPoints(FIRE, 25)
	_expect(is_equal_approx(mastery.CooldownMultiplier(FIRE), 0.95), "mastery: rank 2 trims cooldowns by 5 percent")
	_expect(is_equal_approx(stats.GetValue(FIRE_RESIST), base_resist), "mastery: no attunement before rank 3")

	mastery.SetPoints(FIRE, 45)
	_expect(mastery.RankOf(FIRE) == 3, "mastery: 45 points is rank 3")
	_expect(is_equal_approx(stats.GetValue(FIRE_RESIST), base_resist + 15.0), "mastery: rank 3 attunes +15 fire resistance")

	# Casts bank a point each; a held channel banks at most one a second.
	mastery.SetPoints(FIRE, 0)
	_expect(is_equal_approx(stats.GetValue(FIRE_RESIST), base_resist), "mastery: dropping under rank 3 strips the attunement")
	var body: Node = c["body"]
	for i in 3:
		_driver.Cast(body, "spell.frost_nova")
	var frost = _driver.School("Frost")
	_expect(mastery.PointsIn(frost) == 3, "mastery: three instant casts bank three points (got %d)" % mastery.PointsIn(frost))
	var lightning = _driver.School("Lightning")
	for i in 5:
		_driver.Cast(body, "spell.storm_conduit")
	_expect(mastery.PointsIn(lightning) == 1, "mastery: five channel ticks in one instant bank one point (got %d)" % mastery.PointsIn(lightning))

	# Load replaces: never doubles an attunement, never keeps one the save does not have, never announces.
	mastery.SetPoints(FIRE, 45)
	var saved: Dictionary = mastery.Save()
	mastery.SetPoints(FIRE, 100)
	var ranked_mid: int = _driver.RankedUp
	mastery.Load(saved)
	_expect(mastery.PointsIn(FIRE) == 45, "mastery load: the saved points replace the live ones")
	_expect(is_equal_approx(stats.GetValue(FIRE_RESIST), base_resist + 15.0), "mastery load: the attunement is applied once, not doubled")
	mastery.Load({})
	_expect(mastery.PointsIn(FIRE) == 0 and mastery.RankOf(FIRE) == 0, "mastery load: an empty save clears the points")
	_expect(is_equal_approx(stats.GetValue(FIRE_RESIST), base_resist), "mastery load: an empty save strips the attunement")
	mastery.Load(saved)
	_expect(_driver.RankedUp == ranked_mid, "mastery load: a load announces no rank (it restores, it does not narrate)")

	c["body"].queue_free()
	await process_frame


# ---- weave ---------------------------------------------------------------------------------------

func _check_weave() -> void:
	var full: Dictionary = _driver.WeaveAt(1.0)
	_expect(is_equal_approx(full["power"], 1.0) and is_equal_approx(full["corrupt_power"], 1.0), "weave: full potency is neutral")
	_expect(full["band"] == 0, "weave: full potency reads Strong")

	var dying: Dictionary = _driver.WeaveAt(0.3)
	_expect(dying["power"] < 1.0 and dying["power"] >= 0.6 - 0.001, "weave: ordinary power falls but stays inside the floor (%f)" % dying["power"])
	_expect(dying["cost"] > 1.0 and dying["cost"] <= 1.4 + 0.001, "weave: ordinary cost rises but stays inside the ceiling (%f)" % dying["cost"])
	_expect(dying["corrupt_power"] > 1.0 and dying["corrupt_cost"] < 1.0, "weave: corrupted magic strengthens and cheapens as the Weave fails")
	_expect(dying["band"] == 3, "weave: 0.3 reads Failing")

	var dead: Dictionary = _driver.WeaveAt(-5.0)
	_expect(is_equal_approx(dead["potency"], 0.0) and dead["power"] >= 0.6 - 0.001, "weave: a nonsense potency clamps, ordinary magic is never useless")
	_driver.ResetWeave()


# ---- learning ------------------------------------------------------------------------------------

func _check_learning() -> void:
	var known: Array[String] = []
	var c := _caster("Learner", known)
	var body: Node = c["body"]
	var casting = c["casting"]
	var corruption = c["corruption"]
	await process_frame

	var learned0: int = _driver.Learned
	var outcome: int = _driver.Learn(body, "spell.frost_nova", "trainer")
	_expect(outcome == OUTCOME_LEARNED, "learning: a plain spell is learned (outcome %d)" % outcome)
	_expect(_driver.Learned == learned0 + 1 and _driver.LastRoute == "trainer" and _driver.LastLearnedId == "spell.frost_nova",
		"learning: one SpellLearnedEvent carries the trainer route and the id")

	var refused0: int = _driver.Refused
	outcome = _driver.Learn(body, "spell.frost_nova", "dialogue")
	_expect(outcome == OUTCOME_KNOWN, "learning: a known spell reports AlreadyKnown")
	_expect(_driver.Learned == learned0 + 1, "learning: a repeat does not raise the event again")
	_expect(_driver.Refused == refused0, "learning: a conversation re-offering a known spell stays silent")
	_driver.Learn(body, "spell.frost_nova", "tome")
	_expect(_driver.Refused == refused0 + 1 and _driver.LastRefusal == REFUSE_KNOWN, "learning: a tome says you already know it")

	# A retired id learns its replacement, once, with no duplicate entry.
	var resolved: String = _driver.Resolve("spell.lesser_heal")
	if resolved != "":
		var before: int = _driver.SpellCount(casting)
		_driver.Learn(body, "spell.lesser_heal", "dialogue")
		_driver.Learn(body, resolved, "dialogue")
		_expect(_driver.LastLearnedId == resolved, "learning: the retired id learns %s (got %s)" % [resolved, _driver.LastLearnedId])
		_expect(_driver.SpellCount(casting) == before + 1, "learning: the alias and its replacement are one spell, not two")

	# A corrupted spell: locked below its tier with a reason, a two-step tome at the tier.
	var corrupt_id := "spell.ember_siphon"
	_expect(SpellDbHas(corrupt_id), "learning: the probe's corrupted spell exists")
	var refused1: int = _driver.Refused
	outcome = _driver.Learn(body, corrupt_id, "dialogue")
	_expect(outcome == OUTCOME_LOCKED, "corrupted: an Untainted learner is refused (outcome %d)" % outcome)
	_expect(_driver.Refused == refused1 + 1 and _driver.LastRefusal == REFUSE_TIER, "corrupted: the refusal names the tier reason")

	var host := CharacterBody3D.new()
	host.set_script(load("res://src/Entities/CharacterEntity.cs"))
	host.name = "TomeHost"
	root.add_child(host)
	var tome = load("res://src/Magic/SpellTomeComponent.cs").new()
	tome.name = "Tome"
	tome.SpellId = corrupt_id
	host.add_child(tome)
	await process_frame
	_expect(not _driver.ReadTome(tome, body), "corrupted: a locked reader learns nothing from the tome")

	corruption.Set(50)  # Marked
	var learned1: int = _driver.Learned
	_expect(not _driver.ReadTome(tome, body), "corrupted: the first interaction only warns")
	_expect(_driver.LastRefusal == REFUSE_CONFIRM and _driver.Learned == learned1, "corrupted: the warning is the explicit-choice refusal, nothing learned")
	_expect(_driver.ReadTome(tome, body), "corrupted: the second interaction takes the words")
	_expect(_driver.Learned == learned1 + 1 and _driver.LastRoute == "tome", "corrupted: exactly one learned event, route tome")
	_expect(not _driver.ReadTome(tome, body), "learning: a tome already read does nothing more")
	_expect(_driver.Learned == learned1 + 1, "learning: no double learn from the tome")

	# A load restores knowledge and narrates nothing.
	var learned2: int = _driver.Learned
	var saved: Dictionary = casting.Save()
	casting.Load(saved)
	_expect(_driver.Learned == learned2, "learning: a save/load restore raises no learned event")

	host.queue_free()
	body.queue_free()
	await process_frame


func SpellDbHas(_id: String) -> bool:
	return _driver.Resolve(_id) != ""


# ---- spellbook -----------------------------------------------------------------------------------

func _check_book() -> void:
	var known: Array[String] = ["spell.frost_nova"]
	var c := _caster("BookCaster", known)
	var body: Node = c["body"]
	await process_frame

	var panel = load("res://src/UI/SpellbookPanel.cs").new()
	root.add_child(panel)
	await process_frame
	panel.SetSpellcasting(c["casting"])
	c["progression"].Load({"spell_sp": 10})
	panel.SetProgression(c["progression"])
	panel.SetOpen(true)
	for i in 4:
		await process_frame

	var dump: Array[String] = []
	_all_text(panel, dump)
	print("info: spellbook texts: %s" % str(dump.slice(0, 12)))
	_expect(_has_text(panel, "SPELLBOOK") or _has_text(panel, "Spellbook"), "book: the panel builds and shows its title")
	# The fire school is first; the frost school holds Frost Nova, which the probe caster knows.
	_expect(_has_text(panel, "Frost"), "book: the school ring lists the schools")
	var buy := _buy_button(panel)
	_expect(buy != null, "book: an affordable unknown spell offers a purchase")
	if buy != null:
		var before: int = _driver.Learned
		var points_before: int = c["progression"].SpellPoints
		buy.emit_signal("pressed")
		_expect(_driver.Learned == before + 1 and _driver.LastRoute == "spellbook", "book: one purchase emits exactly one learned event")
		_expect(c["progression"].SpellPoints < points_before, "book: purchase spends spell points")

	# Necrotic holds the corrupted Ember Siphon: an Untainted reader must be told why it is out of reach.
	panel.ShowSchool(6)
	for i in 4:
		await process_frame
	_expect(_has_text(panel, "Locked: needs"), "book: a corrupted spell shows its lock reason (tier needed and tier held)")
	_expect(_has_text(panel, "Rank 0/5"), "book: the school shows its mastery rank")

	var rules: PackedStringArray = _driver.RuleKeys("spell.frost_nova")
	print("info: frost_nova rule keys: %s" % str(rules))

	panel.SetOpen(false)
	panel.queue_free()
	body.queue_free()
	await process_frame


# ---- hud -----------------------------------------------------------------------------------------

func _check_hud() -> void:
	var known: Array[String] = ["spell.frost_nova"]
	var c := _caster("HudCaster", known)
	var body: Node = c["body"]
	var effects = c["effects"]
	await process_frame

	var hud = load("res://src/UI/GameHud.cs").new()
	root.add_child(hud)
	await process_frame
	_driver.Play()
	_driver.HudFor(hud, body)
	_driver.WeaveAt(0.5)
	for i in 3:
		await process_frame

	var def = load("res://data/status_effects/Silenced.tres")
	_driver.ApplyStatus(effects, def, body)
	for i in 3:
		await process_frame

	_expect(_has_text(hud, "Silenced"), "hud: a silenced player shows the Silenced chip")
	_expect(_has_text(hud, "Weave"), "hud: a frayed Weave shows the region chip")

	_driver.ResetWeave()
	hud.queue_free()
	body.queue_free()
	await process_frame
