# Focused production-enemy review through the real new-game session and EnemyShots factory.
# Run with the SDK (rendered; no --enemy-shots flag, which would create a second harness):
#   EMBERVALE_ENEMY_SHOT_ID=enemy.storm_tyrant python tools/embervale.py tool enemy_audit_shots --render --timeout 300 -- --capture
# SDK supplies isolated EMBERVALE_USER_DIR and EMBERVALE_ARTIFACTS. Images and metadata land at
#   <SDK run directory>/enemy-shots/<enemy stem>--<view>.png[.json]
extends SceneTree

const STARTUP_FRAMES := 1800
const STARTUP_SECONDS := 120
const CAPTURE_SECONDS := 150
const OPENING_SCRIPT := "res://src/UI/OpeningSequence.cs"


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	if DisplayServer.get_name() == "headless":
		_fail("a rendering display is required; run with SDK --render")
		return
	if OS.get_environment("EMBERVALE_USER_DIR").is_empty():
		_fail("an isolated EMBERVALE_USER_DIR is required; run through the SDK")
		return
	seed(int(OS.get_environment("EMBERVALE_SEED")) if OS.has_environment("EMBERVALE_SEED") else 12345)

	var packed: PackedScene = load("res://scenes/Main.tscn")
	if packed == null:
		_fail("Main.tscn could not load")
		return
	var main := packed.instantiate()
	root.add_child(main)
	current_scene = main
	# Main must finish _Ready before the real shell's new-game adapter is called.
	await process_frame
	if not main.has_method("AutomationNewGame"):
		_fail("the tooling build is required (AutomationNewGame is unavailable)")
		return
	main.call("AutomationNewGame")
	var startup_deadline := Time.get_ticks_msec() + STARTUP_SECONDS * 1000
	var playing := false
	for _frame in range(STARTUP_FRAMES):
		if bool(main.get("AutomationPlaying")):
			playing = true
			break
		if Time.get_ticks_msec() >= startup_deadline:
			break
		await process_frame
	if not playing:
		_fail("new game did not reach AutomationPlaying within the startup deadline")
		return

	# Skip through the same interact action used by the SDK gameplay-capture scenario. Hiding the
	# overlay directly would leave its cinematic input lock active and would not exercise the player flow.
	var opening: CanvasLayer = null
	for candidate in main.find_children("*", "CanvasLayer", true, false):
		var script = candidate.get_script()
		if script != null and script.resource_path == OPENING_SCRIPT:
			opening = candidate
			break
	if opening != null and opening.visible:
		var event := InputEventAction.new()
		event.action = "interact"
		event.pressed = true
		event.strength = 1.0
		Input.parse_input_event(event)
		await process_frame
		await process_frame
		event = InputEventAction.new()
		event.action = "interact"
		event.pressed = false
		Input.parse_input_event(event)
		for _frame in range(10):
			await process_frame
		if opening.visible:
			_fail("opening sequence did not dismiss after interact")
			return

	# This pass reviews the body and its authored attachments. Keep UI state and gameplay services
	# intact while clearing HUD/tutorial layers from the review pixels.
	for layer in main.find_children("*", "CanvasLayer", true, false):
		layer.visible = false

	var harness_script = load("res://src/Debugging/EnemyShots.cs")
	if harness_script == null or not harness_script.can_instantiate():
		_fail("EnemyShots requires a compiled tooling build")
		return
	var harness: Node = harness_script.new()
	harness.name = "EnemyAuditShots"
	main.add_child(harness)
	# EnemyShots owns capture, state verification and the successful quit. Bound a stalled harness
	# locally as well as the SDK's outer process deadline.
	var capture_deadline := Time.get_ticks_msec() + CAPTURE_SECONDS * 1000
	while Time.get_ticks_msec() < capture_deadline:
		await process_frame
	_fail("EnemyShots did not complete within the capture deadline")


func _fail(message: String) -> void:
	printerr("ERROR: enemy_audit_shots: " + message)
	quit(1)
