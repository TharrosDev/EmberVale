extends Logger
## Godot-native diagnostics preserve origin file/line instead of guessing them from console text.
var entries: Array[Dictionary] = []
var mutex := Mutex.new()

func _log_error(function: String, file: String, line: int, code: String, rationale: String,
		_editor_notify: bool, error_type: int, _script_backtraces: Array[ScriptBacktrace]) -> void:
	mutex.lock()
	entries.append({"severity": "warning" if error_type == 1 else "error",
		"code": "godot.%s" % ["error", "warning", "script", "shader"][clampi(error_type, 0, 3)],
		"path": file, "line": line, "node": null, "function": function,
		"message": rationale if not rationale.is_empty() else code})
	mutex.unlock()

func snapshot() -> Array[Dictionary]:
	mutex.lock()
	var copy := entries.duplicate(true)
	mutex.unlock()
	return copy

func has_errors() -> bool:
	for entry in snapshot():
		if entry.severity == "error":
			return true
	return false
