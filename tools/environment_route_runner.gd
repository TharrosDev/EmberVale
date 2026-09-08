extends Node
## Actual-game visual route. Separate isolated save, frozen world clock, real streaming and renderer.
## Run through: python tools/embervale.py tool environment_route --render

var root: Window:
    get: return get_tree().root

func quit(code: int) -> void:
    get_tree().quit(code)

var output: String
var app: Node
var sky: Node
var clock_node: Node
var weather: Node
var streamer: Node
var player: Node3D
var camera: Camera3D
var failures: Array[String] = []
var rows: Array = []

func _ready() -> void:
    _run.call_deferred()

func check(condition: bool, message: String) -> void:
    if not condition:
        failures.append(message)
        printerr("ERROR environment route: " + message)

func frames(count: int) -> void:
    for i in count: await get_tree().process_frame

func _run() -> void:
    if DisplayServer.get_name() == "headless":
        printerr("ERROR environment route needs a rendering display")
        quit(2)
        return
    output = OS.get_environment("EMBERVALE_ARTIFACTS")
    if output.is_empty(): output = ProjectSettings.globalize_path("res://artifacts/environment-route/" + str(Time.get_unix_time_from_system()))
    DirAccess.make_dir_recursive_absolute(output)
    OS.set_environment("EMBERVALE_USER_DIR",output.path_join("user"))
    DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
    DisplayServer.window_set_size(Vector2i(1280,720))
    RenderingServer.viewport_set_measure_render_time(root.get_viewport_rid(),true)
    seed(12345)
    app = load("res://scenes/Main.tscn").instantiate()
    root.add_child(app)
    get_tree().current_scene = app
    await frames(2)
    app.call("AutomationNewGame")
    var count := 0
    while not app.get("AutomationPlaying") and count < 1800:
        await get_tree().process_frame
        count += 1
    check(bool(app.get("AutomationPlaying")),"new game reaches Playing")
    if not failures.is_empty(): quit(1); return
    var skip := InputEventAction.new()
    skip.action = "interact"
    skip.pressed = true
    Input.parse_input_event(skip)
    await frames(2)
    skip = InputEventAction.new()
    skip.action = "interact"
    skip.pressed = false
    Input.parse_input_event(skip)
    await frames(45)
    player = app.find_child("Player",true,false)
    var tutorial: Node = app.find_child("Tutorial",true,false)
    if tutorial != null: tutorial.call("Skip")
    streamer = app.find_child("RegionStreamer",true,false)
    sky = app.find_child("Sky",true,false)
    clock_node = app.find_child("WorldClock",true,false)
    weather = app.find_child("Weather",true,false)
    check(sky != null and clock_node != null and weather != null,"production environment services exist")
    if not failures.is_empty(): quit(1); return
    clock_node.set_process(false)
    weather.set_process(false)
    player.process_mode = Node.PROCESS_MODE_DISABLED
    camera = root.get_camera_3d()
    camera.fov = 70
    camera.far = 900
    camera.global_position = player.global_position + Vector3(0,2.0,3)
    camera.current = true
    await frames(60)
    for entry in [["morning",8,"weather.clear"],["midday",12,"weather.clear"],["sunset",18,"weather.clear"],["night",0,"weather.clear"],["rain",12,"weather.rain"],["fog",9,"weather.fog"]]:
        await capture(entry[0],entry[1],entry[2])
    for tier in 4:
        sky.call("ApplyQuality",tier)
        await capture("quality-"+str(tier),12,"weather.clear")
    sky.call("ApplyQuality",1)
    # Two opposing angles at the same town anchor; never approve a single view.
    camera.rotation_degrees.y = 180
    await capture("settlement-reverse",12,"weather.clear")
    camera.rotation_degrees.y = 0
    var region: Resource = load("res://data/regions/EmberCrown.tres")
    for cell in region.get("Cells"):
        if String(cell.get("Id")).ends_with("wilds_west") or String(cell.get("Id")).ends_with("tarn_landing"):
            await visit(cell)
            await capture(String(cell.get("Id")).replace(".","-"),12,"weather.clear")
        if String(cell.get("Id")).ends_with("ashfall_homestead"):
            await visit(cell)
            var house: Node3D = null
            var expected: Vector3 = cell.get("Center") + Vector3(9,0,-4)
            for candidate in streamer.find_children("House","Node3D",true,false):
                if Vector2(candidate.global_position.x,candidate.global_position.z).distance_to(Vector2(expected.x,expected.z)) < .5:
                    house = candidate
                    break
            check(house != null,"live Ashfall house exists")
            if house != null:
                camera.global_position = house.to_global(Vector3(-1.8,1.7,2.5))
                camera.look_at(house.to_global(Vector3(.4,1.4,-2.5)))
                await capture("ashfall-interior-day",12,"weather.rain")
                check(float(sky.get("Shelter")) > .95,"real Ashfall interior shelters rain")
                await capture("ashfall-interior-night",0,"weather.clear")
                camera.global_position = house.to_global(Vector3(-2,1.7,5.5))
                camera.look_at(house.to_global(Vector3(-2,1.7,2.5)))
                await capture("ashfall-doorway-rain",12,"weather.rain")
                check(float(sky.get("Shelter")) < .05,"leaving Ashfall restores outdoor weather")
                camera.rotation_degrees = Vector3.ZERO
    # The other region exercises the existing climate/terrain/water data through the real streamer.
    region = load("res://data/regions/FrostfangReach.tres")
    streamer.call("Configure",region)
    for cell in region.get("Cells"):
        if String(cell.get("Id")).ends_with("clan_hold") or String(cell.get("Id")).ends_with("glacier") or String(cell.get("Id")).ends_with("ash_roost") or String(cell.get("Id")).ends_with("aerie_ascent"):
            await visit(cell)
            await capture(String(cell.get("Id")).replace(".","-"),12,"weather.rain")
    # Exercise the explicit dungeon contribution without inventing a second environment or altering content.
    var volume: Node3D = load("res://src/World/EnvironmentVolume.cs").new()
    volume.set("Profile",load("res://data/rendering/Dungeon.tres"))
    volume.set("Size",Vector3(20,20,20))
    root.add_child(volume)
    volume.global_position = camera.global_position
    await capture("scoped-dungeon-profile",0,"weather.clear")
    check(float(sky.get("Shelter")) > .95,"scoped profile suppresses precipitation")
    volume.queue_free()
    var file := FileAccess.open(output.path_join("environment-route.json"),FileAccess.WRITE)
    file.store_string(JSON.stringify({"shots":rows,"failures":failures,"engine":Engine.get_version_info(),"gpu":RenderingServer.get_video_adapter_name()},"  "))
    file.close()
    get_tree().current_scene = null
    app.queue_free()
    await frames(4)
    var collector: Node = load("res://src/Bootstrap/ContentDatabaseLoader.cs").new()
    collector.call("CollectManagedResources")
    collector.free()
    await frames(2)
    print("Environment route: %d shots, %d failures. %s" % [rows.size(),failures.size(),output])
    quit(0 if failures.is_empty() else 1)

func visit(cell: Resource) -> void:
    var p: Vector3 = cell.get("Center")
    var view_direction := Vector3.FORWARD
    # Use authored trail anchors instead of placing the camera four metres into a steep hillside.
    match String(cell.get("Id")):
        "ember_crown.wilds_west":
            p += Vector3(4,0,-26)
            view_direction = Vector3(-14,0,18)
        "frostfang_reach.ash_roost":
            p += Vector3(10,0,6)
            view_direction = Vector3(16,0,-14)
        "frostfang_reach.aerie_ascent":
            p += Vector3(-60,0,55)
            view_direction = Vector3(20,0,-25)
    streamer.call("SetStreamingFocus",p)
    var count := 0
    while (not streamer.call("IsPositionReady",p,false) or not streamer.call("IsSettled")) and count < 900:
        await get_tree().process_frame
        count += 1
    check(bool(streamer.call("IsPositionReady",p,false)),"ready at " + String(cell.get("Id")))
    var query := PhysicsRayQueryParameters3D.create(p + Vector3.UP*400,p-Vector3.UP*400,1)
    var hit := camera.get_world_3d().direct_space_state.intersect_ray(query)
    check(not hit.is_empty(),"ground at " + String(cell.get("Id")))
    if not hit.is_empty():
        player.global_position = hit.position + Vector3.UP*.1
        camera.global_position = hit.position + Vector3(0,1.7,0)
        camera.look_at(camera.global_position + view_direction)
    await frames(60)

func capture(label: String,hour: float,weather_id: String) -> void:
    clock_node.call("SetTimeOfDay",hour)
    check(bool(weather.call("Force",weather_id)),"weather exists " + weather_id)
    # Explicitly advance only the presentation blend, then let GPU particles settle in real frames.
    for i in 300: sky.call("_Process",1.0/10.0)
    await frames(80)
    var env: Environment = sky.get("Environment")
    var sun: DirectionalLight3D = sky.get("Sun")
    check(env.fog_density >= 0 and env.fog_density < .15,"bounded fog " + label)
    check(sun.visible == (sun.light_energy > .001),"sun visibility follows energy " + label)
    if hour == 0: check(sun.light_energy < .001,"sun is dark at night")
    if label == "rain": check(float(sky.get("SnowCover")) < .001,"Ember Crown rain is not snow")
    if label.ends_with("glacier"): check(float(sky.get("SnowCover")) > .2,"glacier precipitation accumulates snow")
    var samples: Array[float] = []
    var gpu_samples: Array[float] = []
    var last := Time.get_ticks_usec()
    for i in 90:
        await get_tree().process_frame
        var now := Time.get_ticks_usec()
        samples.append((now-last)/1000.0)
        gpu_samples.append(RenderingServer.viewport_get_measured_render_time_gpu(root.get_viewport_rid()))
        last = now
    samples.sort()
    gpu_samples.sort()
    await RenderingServer.frame_post_draw
    var image := root.get_texture().get_image()
    check(image != null and not image.is_empty(),"nonempty image " + label)
    if image != null: check(image.save_png(output.path_join(label+".png")) == OK,"save image " + label)
    rows.append({"name":label,"hour":hour,"weather":weather_id,"fog":env.fog_density,"exposure":env.tonemap_exposure,"wetness":sky.get("Wetness"),"snow":sky.get("SnowCover"),"shelter":sky.get("Shelter"),"gpu_p50_ms":gpu_samples[45],"gpu_p95_ms":gpu_samples[85],"p50_ms":samples[45],"p95_ms":samples[85],"camera":str(camera.global_position),"quality":sky.get("QualityId")})
