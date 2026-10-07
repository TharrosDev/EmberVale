# Shared base for the headless GDScript probes under tools/.
#
#     extends "res://tools/probe_base.gd"
#
#     func _initialize() -> void:
#         boot()                              # the content databases, as the game loads them
#         await process_frame
#         for region_path in regions():       # every data/regions/*.tres, narrowed by --region
#             if wants("streams"):            # narrowed by --only
#                 check(await _streams(region_path), "%s did not stream" % region_path)
#         metric("cells", 145)
#         finish("every region streams")      # prints, writes the result file, quits 0 or 1
#
# Run a probe as before; arguments go after `--`:
#     godot --headless --path . --script res://tools/<probe>.gd -- --region ember_crown --only a,b
#
# What finish() prints, in order:
#     FAIL: <message>          one line per failure (nothing when there are none)
#     PASS: <pass text>        only when there are no failures
#     PROBE {"probe":"<name>","ok":true,"checks":12,"failures":[],"metrics":{},"seconds":3.2}
# and it writes that same object to <probe>.result.json under $EMBERVALE_ARTIFACTS when the variable
# is set (the SDK sets it), or to the path given with `-- --result-file PATH`.
#
# A probe that ran no check() at all fails with "no checks ran": a probe that cannot fail is not a
# gate. Pass finish(text, false) for a census that measures and asserts nothing.
#
# What this deliberately does not do: build actors. The probes that hand-build a CharacterEntity
# each need a different set of components kept from construction (generic C# lookups do not marshal
# to GDScript), so one shared factory would change their timing.
extends SceneTree

const REGIONS_DIR := "res://data/regions"

## Name used in the PROBE line and the result file. Defaults to the script's file name.
var probe_name := ""

var _probe_failures: Array[String] = []
var _probe_checks := 0
var _probe_metrics := {}
var _probe_started := Time.get_ticks_msec()
var _probe_args := {}
var _probe_args_parsed := false


## Adds the content databases the way the game does. The caller awaits a frame afterwards.
func boot() -> Node:
	var databases: Node = load("res://src/Bootstrap/ContentDatabaseLoader.cs").new()
	root.add_child(databases)
	return databases


## The value of a user argument given as `--name value` or `--name=value`; `fallback` when absent.
## A bare `--name` reads as "true".
func arg(name: String, fallback: String = "") -> String:
	_parse_args()
	return str(_probe_args.get(name, fallback))


func has_arg(name: String) -> bool:
	_parse_args()
	return _probe_args.has(name)


## A comma separated argument as a list, entries trimmed and empties dropped.
func arg_list(name: String) -> PackedStringArray:
	var out := PackedStringArray()
	for part in arg(name).split(",", false):
		var trimmed: String = part.strip_edges()
		if not trimmed.is_empty():
			out.append(trimmed)
	return out


## True when `--only` is absent or names this case.
func wants(case_name: String) -> bool:
	var only := arg_list("--only")
	return only.is_empty() or only.has(case_name)


## Every region resource path, sorted, narrowed by `--region` (comma list; `ember_crown`,
## `EmberCrown` and `region.ember_crown` all name data/regions/EmberCrown.tres).
func regions() -> Array[String]:
	var wanted := {}
	for wanted_name in arg_list("--region"):
		wanted[_region_key(wanted_name)] = true
	var out: Array[String] = []
	var dir := DirAccess.open(REGIONS_DIR)
	if dir == null:
		fail("cannot open %s" % REGIONS_DIR)
		return out
	for file in dir.get_files():
		# An exported build lists resources as <name>.tres.remap.
		var file_name: String = file.trim_suffix(".remap")
		if not file_name.ends_with(".tres"):
			continue
		if wanted.is_empty() or wanted.has(_region_key(file_name)):
			out.append("%s/%s" % [REGIONS_DIR, file_name])
	out.sort()
	if out.is_empty():
		fail("no region matches --region %s" % arg("--region"))
	return out


func wait_frames(count: int = 1) -> void:
	for _i in count:
		await process_frame


func wait_physics(count: int = 1) -> void:
	for _i in count:
		await physics_frame


## Counts an assertion; records `message` when it does not hold. Returns `ok` for chaining.
func check(ok: bool, message: String) -> bool:
	_probe_checks += 1
	if not ok:
		_probe_failures.append(message)
	return ok


## Records a failure outright (it also counts as a check that ran).
func fail(message: String) -> void:
	_probe_checks += 1
	_probe_failures.append(message)


func failure_count() -> int:
	return _probe_failures.size()


## A measurement for the result object: a number, string, bool, array or dictionary.
func metric(key: String, value: Variant) -> void:
	_probe_metrics[key] = value


## Prints the verdict, writes the result file and quits with 0 (no failures) or 1.
func finish(pass_text: String = "", require_checks: bool = true) -> void:
	if require_checks and _probe_checks == 0:
		_probe_failures.append("no checks ran")
	var ok := _probe_failures.is_empty()
	for failure in _probe_failures:
		print("FAIL: %s" % failure)
	if ok and not pass_text.is_empty():
		print("PASS: %s" % pass_text)
	var result := {
		"probe": _name(),
		"ok": ok,
		"checks": _probe_checks,
		"failures": _probe_failures,
		"metrics": _probe_metrics,
		"seconds": snappedf((Time.get_ticks_msec() - _probe_started) / 1000.0, 0.1),
	}
	var text := JSON.stringify(result)
	print("PROBE %s" % text)
	var path := _result_path()
	if not path.is_empty():
		write_text(path, text + "\n")
	quit(0 if ok else 1)


## Writes a text file, creating its folder. Returns false (and prints why) when it cannot.
func write_text(path: String, text: String) -> bool:
	var target := resolve_path(path)
	DirAccess.make_dir_recursive_absolute(target.get_base_dir())
	var file := FileAccess.open(target, FileAccess.WRITE)
	if file == null:
		printerr("probe: cannot write %s (error %d)" % [target, FileAccess.get_open_error()])
		return false
	file.store_string(text)
	file.close()
	return true


## A path argument as given on the command line: res://, user:// and absolute paths are kept, a
## bare relative path is taken from the project root.
func resolve_path(path: String) -> String:
	if path.contains("://"):
		return ProjectSettings.globalize_path(path)
	if path.is_absolute_path():
		return path
	return ProjectSettings.globalize_path("res://" + path)


func _name() -> String:
	if not probe_name.is_empty():
		return probe_name
	var script: Script = get_script()
	return script.resource_path.get_file().get_basename() if script != null else "probe"


func _result_path() -> String:
	if has_arg("--result-file"):
		return arg("--result-file")
	var artifacts := OS.get_environment("EMBERVALE_ARTIFACTS")
	return "" if artifacts.is_empty() else artifacts.path_join("%s.result.json" % _name())


func _region_key(name: String) -> String:
	return name.trim_suffix(".tres").trim_prefix("region.").replace("_", "").to_lower()


func _parse_args() -> void:
	if _probe_args_parsed:
		return
	_probe_args_parsed = true
	var args := OS.get_cmdline_user_args()
	var index := 0
	while index < args.size():
		var token: String = args[index]
		index += 1
		if not token.begins_with("--"):
			continue
		var split := token.find("=")
		if split > 0:
			_probe_args[token.substr(0, split)] = token.substr(split + 1)
		elif index < args.size() and not args[index].begins_with("--"):
			_probe_args[token] = args[index]
			index += 1
		else:
			_probe_args[token] = "true"
