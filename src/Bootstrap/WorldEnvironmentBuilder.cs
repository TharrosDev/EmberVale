using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The world's look before anything is in it: the sun, the sky material, the tonemap and glow, and the
/// ground plane the whole sandbox stands on. This is the reference bar every region is authored
/// against — the dying-world palette of Phase 27F — so it is worth being able to read it without
/// scrolling past a session state machine to find it.
///
/// <b>Why it lives here rather than in <see cref="GameBootstrap"/>.</b> The same reason
/// <see cref="SandboxProps"/> does, and the 2026-08-15 audit picked it as the first extraction for a
/// specific reason: <b>it was the only one with no coupling to unwind.</b> Everything in here is
/// authored constants and four <c>AddChild</c> calls; nothing reads the bootstrap's session state,
/// its region, its player or its panels. The two objects the rest of the build genuinely needs come
/// back in <see cref="Result"/> instead of being left behind in fields.
///
/// ⚠️ <b>Those two used to be fields on the bootstrap and did not need to be.</b> <c>_sun</c> and
/// <c>_environment</c> were written here and read in exactly one other place — the
/// <c>SkyController</c> wiring — <em>inside the same method</em>. They were object state describing a
/// value that never outlived a single call, which is the cheapest kind of god-object weight to carry
/// and the easiest to miss.
///
/// The <c>SkyController</c> animates both off the world clock (day/night, weather haze); this only
/// establishes their starting values.
/// </summary>
internal static class WorldEnvironmentBuilder
{
    /// <summary>The handles the caller must keep: the <c>SkyController</c> drives both.</summary>
    internal readonly record struct Result(DirectionalLight3D Sun, Godot.Environment Environment);

    /// <summary>Builds the lighting, sky and ground under <paramref name="root"/>.</summary>
    internal static Result Build(Node root)
    {
        // No camera here — the player provides the active third-person camera. The sun's
        // orientation/energy/colour are animated by the SkyController off the world clock.
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55f, -40f, 0f),
            ShadowEnabled = true,
            // Softer, less crisp shadows suit the hazy dying-world mood (Phase 27F).
            ShadowBlur = 1.5f,
        };
        root.AddChild(sun);

        // Sky background; with the sky ambient source this also provides soft ambient light, so
        // unlit faces are not pure black. The SkyController dims the sky at night and applies
        // weather fog to this env. The base look here is the dying-world palette (Phase 27F) — an
        // ashen, overcast-leaning sky rather than the bright procedural blue; see SkyController for
        // the day/night + haze tuning that rides on top. This is the reference bar for all regions.
        var sky = new ProceduralSkyMaterial
        {
            // Desaturated grey-blue overhead fading to a dusty warm-grey horizon; dim brown-grey ground.
            SkyTopColor = new Color(0.42f, 0.45f, 0.50f),
            SkyHorizonColor = new Color(0.60f, 0.57f, 0.52f),
            SkyEnergyMultiplier = 0.7f,
            GroundHorizonColor = new Color(0.40f, 0.37f, 0.34f),
            GroundBottomColor = new Color(0.24f, 0.22f, 0.20f),
            SunAngleMax = 18f,
        };

        var worldEnv = new WorldEnvironment();
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            // ACES tonemap with a slightly pulled-back exposure keeps the muted, filmic dying look.
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 0.95f,
            TonemapWhite = 6f,
            // A breath of warm-grey ambient fill so shadowed faces read ashen, not black.
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightColor = new Color(0.46f, 0.44f, 0.42f),
            AmbientLightSkyContribution = 0.85f,
            AmbientLightEnergy = 1.0f,
            // Soft bloom so embers, fires and bright highlights bleed a little.
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.1f,
            GlowStrength = 0.9f,
        };
        environment.Sky = new Sky { SkyMaterial = sky };
        worldEnv.Environment = environment;
        root.AddChild(worldEnv);

        // A generous ground plane so dynamic encounters (spawned ~14–20m out) land on
        // visible terrain; the collider below is an infinite plane regardless. Sits 5 cm below
        // y=0 so authored region greybox floors (top at y=0, Phase 27A) render cleanly on top of
        // it instead of z-fighting; the WorldBoundary collider stays at y=0 so standing is unchanged.
        var floor = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(80f, 80f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.22f, 0.20f) },
            Position = new Vector3(0f, -0.05f, 0f),
        };
        root.AddChild(floor);

        // Physics collider for the ground so the player can stand on it.
        var floorBody = new StaticBody3D { Name = "FloorBody" };
        floorBody.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        root.AddChild(floorBody);

        return new Result(sun, environment);
    }
}
