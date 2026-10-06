# Packs the painted item icons into one atlas.
#
#   in    assets/ui/icons/items/src/<key>.png   one picture per archetype key (ItemIconRules.Keys),
#                                               any size, painted on flat black
#   out   assets/ui/icons/items/atlas.png       a grid of 128 px cells
#         assets/ui/icons/items/atlas.json      {"cell": 128, "columns": 10, "keys": {"<key>": [column, row]}}
#
# Each picture is scaled to fit a cell (aspect kept, centred), then its black is keyed out: a pixel
# darker than KEY_FLOOR becomes transparent, one brighter than KEY_CEIL keeps its alpha, and the band
# between ramps, so an edge that was blended into the black background fades instead of leaving a
# dark fringe. A picture that already has transparency keeps it (the key only ever lowers alpha).
#
# The src folder carries a .gdignore, so the sources are never imported or exported; only the atlas
# is. After packing, let the editor (or `--headless --import`) import atlas.png once.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/pack_ui_atlas.gd
# Exits 0 when the atlas was written, 1 otherwise.
extends SceneTree

const SRC := "res://assets/ui/icons/items/src"
const OUT_PNG := "res://assets/ui/icons/items/atlas.png"
const OUT_JSON := "res://assets/ui/icons/items/atlas.json"
const CELL := 128
const COLUMNS := 10
const KEY_FLOOR := 0.05
const KEY_CEIL := 0.16


func _initialize() -> void:
	var src := ProjectSettings.globalize_path(SRC)
	var dir := DirAccess.open(src)
	if dir == null:
		push_error("pack_ui_atlas: %s does not exist" % src)
		quit(1)
		return

	var keys: Array[String] = []
	for file in dir.get_files():
		if file.get_extension().to_lower() == "png":
			keys.append(file.get_basename())
	keys.sort()
	if keys.is_empty():
		push_error("pack_ui_atlas: no .png files in %s" % src)
		quit(1)
		return

	var rows := ceili(float(keys.size()) / COLUMNS)
	var atlas := Image.create(COLUMNS * CELL, rows * CELL, false, Image.FORMAT_RGBA8)
	atlas.fill(Color(0, 0, 0, 0))

	var cells := {}
	var failed := 0
	for i in keys.size():
		var key := keys[i]
		var image := Image.load_from_file(src.path_join(key + ".png"))
		if image == null or image.is_empty():
			push_error("pack_ui_atlas: could not read %s.png" % key)
			failed += 1
			continue

		image.convert(Image.FORMAT_RGBA8)
		var scale := float(CELL) / maxi(image.get_width(), image.get_height())
		var width := maxi(1, roundi(image.get_width() * scale))
		var height := maxi(1, roundi(image.get_height() * scale))
		image.resize(width, height, Image.INTERPOLATE_LANCZOS)
		_key_black(image)

		var column := i % COLUMNS
		var row := i / COLUMNS
		atlas.blit_rect(
			image, Rect2i(0, 0, width, height),
			Vector2i(column * CELL + (CELL - width) / 2, row * CELL + (CELL - height) / 2))
		cells[key] = [column, row]

	if failed > 0:
		quit(1)
		return

	var error := atlas.save_png(ProjectSettings.globalize_path(OUT_PNG))
	if error != OK:
		push_error("pack_ui_atlas: could not write %s (%d)" % [OUT_PNG, error])
		quit(1)
		return

	var index := FileAccess.open(ProjectSettings.globalize_path(OUT_JSON), FileAccess.WRITE)
	if index == null:
		push_error("pack_ui_atlas: could not write %s" % OUT_JSON)
		quit(1)
		return
	index.store_string(JSON.stringify({"cell": CELL, "columns": COLUMNS, "keys": cells}, "  ") + "\n")
	index.close()

	print("pack_ui_atlas: %d icon(s) in %dx%d -> %s" % [keys.size(), atlas.get_width(), atlas.get_height(), OUT_PNG])
	quit(0)


func _key_black(image: Image) -> void:
	for y in image.get_height():
		for x in image.get_width():
			var pixel := image.get_pixel(x, y)
			var value := maxf(pixel.r, maxf(pixel.g, pixel.b))
			pixel.a *= smoothstep(KEY_FLOOR, KEY_CEIL, value)
			image.set_pixel(x, y, pixel)
