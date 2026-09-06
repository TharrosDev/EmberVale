extends RefCounted
## The drawn-frame barrier used by existing shot harnesses, with portable evidence and diffing.

static func capture(viewport: Viewport, path: String, metadata: Dictionary,
		baseline := "", threshold := 0.05) -> Dictionary:
	if DisplayServer.get_name() == "headless":
		return {"success": false, "message": "Screenshot needs a rendering display; dummy headless has no pixels"}
	await RenderingServer.frame_post_draw
	var image := viewport.get_texture().get_image()
	if image == null or image.is_empty():
		return {"success": false, "message": "Viewport returned an empty image"}
	var darkest := 1.0
	var brightest := 0.0
	for y in range(0, image.get_height(), maxi(1, image.get_height() / 20)):
		for x in range(0, image.get_width(), maxi(1, image.get_width() / 20)):
			var color := image.get_pixel(x, y)
			var luminance := (color.r + color.g + color.b) / 3.0
			darkest = minf(darkest, luminance)
			brightest = maxf(brightest, luminance)
	if brightest - darkest < 0.01:
		return {"success": false, "message": "Flat/blank capture is not useful visual evidence"}
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	if image.save_png(path) != OK:
		return {"success": false, "message": "PNG write failed: " + path}
	metadata["resolution"] = [image.get_width(), image.get_height()]
	var ok := true
	var message := "Captured " + path
	if not baseline.is_empty():
		var expected := Image.new()
		if expected.load(baseline) != OK or expected.get_size() != image.get_size():
			ok = false
			message = "Baseline missing, unreadable or a different resolution"
		else:
			image.convert(Image.FORMAT_RGB8)
			expected.convert(Image.FORMAT_RGB8)
			var actual_data := image.get_data()
			var expected_data := expected.get_data()
			var diff_data := PackedByteArray()
			diff_data.resize(actual_data.size())
			var total := 0.0
			for i in actual_data.size():
				var delta := absi(actual_data[i] - expected_data[i])
				total += delta
				diff_data[i] = mini(255, delta * 4)
			var mean := total / (actual_data.size() * 255.0)
			metadata["diff"] = {"normalized_mean_error": mean, "threshold": threshold, "baseline": baseline}
			ok = mean <= threshold
			if not ok:
				var diff := Image.create_from_data(image.get_width(), image.get_height(), false, Image.FORMAT_RGB8, diff_data)
				diff.save_png(path.get_basename() + ".diff.png")
				message = "Screenshot regression: %.6f > %.6f" % [mean, threshold]
	var file := FileAccess.open(path + ".json", FileAccess.WRITE)
	if file == null:
		return {"success": false, "message": "Capture metadata write failed"}
	file.store_string(JSON.stringify(metadata, "  "))
	return {"success": ok, "message": message}
