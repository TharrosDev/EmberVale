using System;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Embervale.Settings;
using Godot;

namespace Embervale.World;

/// <summary>Read-only presentation state for audio and environmental effects. Gameplay keeps its own authority.</summary>
public readonly record struct EnvironmentVisualStateEvent(float Hour, float Rain, float Snow, float Wetness,
    Vector3 Wind, float Shelter) : IGameEvent;

/// <summary>The single world visual authority. Composes the day cycle, region, climate, weather and shelter.</summary>
[GlobalClass]
public partial class SkyController : Node3D
{
    public DirectionalLight3D? Sun { get; set; }
    public Godot.Environment? Environment { get; set; }
    // Existing streamer input retained; this is data, never a second renderer.
    public static WorldEnvironmentProfileResource? RegionAtmosphere { get; set; }
    [Export] public EnvironmentCycleResource? Cycle { get; set; }
    public float Wetness { get; private set; }
    public float SnowCover { get; private set; }
    public float Shelter { get; private set; }
    public Vector3 Wind { get; private set; }
    public string QualityId => _quality?.Id ?? "medium";
    public string Diagnostics => $"{QualityId} | fog {Environment?.FogDensity:0.0000} | wet {Wetness:0.00} snow {SnowCover:0.00} | shelter {Shelter:0.00}";

    private readonly EnvironmentEmitters _emitters = new();
    private EnvironmentSpaceProfileResource? _interior, _underwater, _space;
    private float _spaceWeight, _ambientScale = 1f, _exposureScale = 1f, _fogScale = 1f;
    private Color _spaceFog = Colors.White;
    private WorldClock? _clock;
    private WeatherDirector? _weather;
    private DirectionalLight3D _moon = null!;
    private GpuParticles3D _rain = null!;
    private GpuParticles3D _snow = null!;
    private GpuParticlesCollisionHeightField3D _weatherCollision = null!;
    private RenderQualityResource? _quality;
    private float _light = 1f, _sky = 1f, _fog, _precipitation, _cold;
    private float _regionEnergy = 1f, _regionHaze = 1f, _humidity, _shelterTarget;
    private Color _regionTint = Colors.White, _haze = new(0.70f, 0.66f, 0.60f), _weatherFog = Colors.White, _weatherTint = Colors.White;
    private double _sampleClock;
    private Vector3 _windTarget = new(1.5f, 0f, 0.5f);
    private bool _initialized;
    private (int Tier, GraphicsOverrides Overrides)? _applied;
    private bool _volumetricFog, _weatherCollisionActive;
    private int _localShadowLights;
    private ProceduralSkyMaterial? _skyMaterial;
    private ParticleProcessMaterial _rainProcess = null!, _snowProcess = null!;
    // Seconds since each precipitation system last emitted; past its particles' lifetime it is idle.
    private float _rainIdle = PrecipitationTail, _snowIdle = PrecipitationTail;
    private float _sentWetness = float.NaN, _sentSnow = float.NaN, _sentRain = float.NaN;
    private const float PrecipitationTail = 5f;
    // Built once: a string literal at the call site allocates a fresh StringName per call, per frame.
    private static readonly StringName WetnessGlobal = "world_wetness", SnowGlobal = "world_snow",
        RainGlobal = "world_rain", WindGlobal = "world_wind", VisualTimeGlobal = "world_visual_time";

    /// <summary>The active preset's world-scale inputs, for the streamer. Set only by <see cref="ApplyQuality"/>.</summary>
    public float DrawDistanceScale { get; private set; } = 1f;
    public float ScatterDensityScale { get; private set; } = 1f;
    /// <summary>Metres beyond which characters cast no shadow; 0 when shadows are switched off.</summary>
    public float ActorShadowDistance { get; private set; } = 10000f;

    public override void _Ready()
    {
        Cycle ??= ResidentResources.Load<EnvironmentCycleResource>("res://data/rendering/DayCycle.tres");
        _interior = ResidentResources.Load<EnvironmentSpaceProfileResource>("res://data/rendering/Interior.tres");
        _underwater = ResidentResources.Load<EnvironmentSpaceProfileResource>("res://data/rendering/Underwater.tres");
        ServiceScope.RegisterOwned(this, this);
        _moon = new DirectionalLight3D { Name = "Moon", ShadowEnabled = false };
        AddChild(_moon);
        _rain = BuildPrecipitation(false);
        _snow = BuildPrecipitation(true);
        _rainProcess = (ParticleProcessMaterial)_rain.ProcessMaterial;
        _snowProcess = (ParticleProcessMaterial)_snow.ProcessMaterial;
        AddChild(_rain);
        AddChild(_snow);
        _weatherCollision = new GpuParticlesCollisionHeightField3D { Size = new Vector3(32, 32, 32), Visible = false };
        AddChild(_weatherCollision);
        EventBus.Instance?.Subscribe<SettingsAppliedEvent>(OnSettings);
        int tier = 1;
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings))
            tier = settings.Current.RenderQuality;
        ApplyQuality(tier);
        GetTree().NodeAdded += OnNodeAdded;
        ResolveServices();
        SampleContext();
        _Process(0);
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= OnNodeAdded;
        _emitters.Clear();
        EventBus.Instance?.Unsubscribe<SettingsAppliedEvent>(OnSettings);
        RenderingServer.GlobalShaderParameterSet("world_wetness", 0f);
        RenderingServer.GlobalShaderParameterSet("world_snow", 0f);
        RenderingServer.GlobalShaderParameterSet("world_wind", Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet("world_rain", 0f);
        RenderingServer.GlobalShaderParameterSet("world_visual_time", 0f);
    }

    private void OnNodeAdded(Node node)
    {
        if (GetParent().IsAncestorOf(node)) _emitters.Observe(node);
    }

    private void OnSettings(SettingsAppliedEvent e) => ApplyQuality(e.Current.RenderQuality);

    /// <summary>
    /// The one place a quality preset, and the player's departures from it, reach the renderer.
    /// <paramref name="tier"/> is a saved <c>Settings.RenderQuality</c> value (see <see cref="GraphicsMath"/>).
    /// </summary>
    public void ApplyQuality(int tier)
    {
        tier = GraphicsMath.ClampTier(tier);
        GraphicsOverrides overrides = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings.Current.Overrides()
            : GraphicsOverrides.None;

        // Every settings apply lands here, each tick of a volume or FOV drag included. The tier and
        // the overrides are everything below reads, so an unchanged pair has nothing to push.
        if (_applied is { } done && done.Tier == tier && done.Overrides == overrides) return;

        _quality = RenderQualityResource.ForTier(tier);
        if (_quality == null || Environment == null) return;
        bool shadows = GraphicsMath.ShadowsEnabled(overrides.ShadowQuality);
        RenderQualityResource shadow =
            RenderQualityResource.ForTier(GraphicsMath.ShadowSourceTier(overrides.ShadowQuality, tier)) ?? _quality;
        _localShadowLights = shadows ? shadow.LocalShadowLights : 0;

        Viewport viewport = GetViewport();
        int scaling = GraphicsMath.Resolve(overrides.ScalingMode, _quality.ScalingMode);
        viewport.Scaling3DMode = scaling switch
        {
            1 => Viewport.Scaling3DModeEnum.Fsr,
            2 => Viewport.Scaling3DModeEnum.Fsr2,
            _ => Viewport.Scaling3DModeEnum.Bilinear,
        };
        viewport.Scaling3DScale = GraphicsMath.ResolveScale(overrides.RenderScale, _quality.RenderScale);
        viewport.MeshLodThreshold = _quality.MeshLodThreshold;
        int antiAliasing = GraphicsMath.Resolve(overrides.AntiAliasing, _quality.AntiAliasing);
        viewport.Msaa3D = antiAliasing switch
        {
            2 => Viewport.Msaa.Msaa2X,
            3 => Viewport.Msaa.Msaa4X,
            _ => Viewport.Msaa.Disabled,
        };
        viewport.ScreenSpaceAA = antiAliasing == 1 ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        // FSR 2.2 is itself temporal; it replaces TAA rather than stacking on it.
        viewport.UseTaa = antiAliasing == 4 && scaling != 2;

        RenderingServer.DirectionalShadowAtlasSetSize(shadow.ShadowAtlasSize, true);
        int filter = shadow.ShadowFilterQuality;
        RenderingServer.DirectionalSoftShadowFilterSetQuality((RenderingServer.ShadowQuality)(filter >= 0
            ? filter : ProjectInt("rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality", 2)));
        RenderingServer.PositionalSoftShadowFilterSetQuality((RenderingServer.ShadowQuality)(filter >= 0
            ? filter : ProjectInt("rendering/lights_and_shadows/positional_shadow/soft_shadow_filter_quality", 2)));
        ApplyPositionalAtlas(viewport, shadow.PositionalShadowAtlasSize);

        Environment.SsaoEnabled = GraphicsMath.Resolve(overrides.AmbientOcclusion, _quality.AmbientOcclusion);
        Environment.SsaoRadius = 0.65f;
        Environment.SsaoIntensity = 1.1f;
        Environment.SsaoPower = 1.2f;
        Environment.SsilEnabled = _quality.IndirectLighting;
        Environment.SsilIntensity = 0.35f;
        Environment.SsrEnabled = _quality.Reflections;
        // Streaming terrain never rebuilds voxel GI. Sky radiance is the outdoor indirect source.
        Environment.SdfgiEnabled = false;
        _volumetricFog = GraphicsMath.Resolve(overrides.VolumetricFog, _quality.VolumetricFog);
        Environment.VolumetricFogEnabled = _volumetricFog;
        Environment.VolumetricFogLength = 96f;
        Environment.GlowEnabled = GraphicsMath.Resolve(overrides.Glow, _quality.Glow);
        RenderingServer.EnvironmentGlowSetUseBicubicUpscale(_quality.GlowBicubicUpscale &&
            ProjectInt("rendering/environment/glow/upscale_mode", 1) > 0);
        if (Environment.Sky is { } sky)
        {
            Sky.RadianceSizeEnum radiance = _quality.SkyRadianceSize switch
            {
                <= 32 => Sky.RadianceSizeEnum.Size32,
                <= 64 => Sky.RadianceSizeEnum.Size64,
                <= 128 => Sky.RadianceSizeEnum.Size128,
                _ => Sky.RadianceSizeEnum.Size256,
            };
            // Assigning it reallocates the cubemap, so only on a real change.
            if (sky.RadianceSize != radiance) sky.RadianceSize = radiance;
        }
        if (Sun != null)
        {
            Sun.ShadowEnabled = shadows;
            Sun.DirectionalShadowMaxDistance = shadow.ShadowDistance;
            if (shadow.ShadowSplits >= 4)
            {
                Sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
                Sun.DirectionalShadowSplit1 = 0.10f;
                Sun.DirectionalShadowSplit2 = 0.25f;
                Sun.DirectionalShadowSplit3 = 0.55f;
            }
            else if (shadow.ShadowSplits >= 2)
            {
                // Two cascades draw the casters twice instead of four times. The near one keeps the
                // first quarter of the range, where the player and the ground at their feet are.
                Sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
                Sun.DirectionalShadowSplit1 = 0.25f;
            }
            else
            {
                Sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal;
            }
            Sun.DirectionalShadowBlendSplits = shadow.ShadowBlendSplits;
            Sun.ShadowBias = 0.025f;
            Sun.ShadowNormalBias = 0.7f;
        }

        // ── WORLD SCALE SEAM ─────────────────────────────────────────────────────────────────────
        // These three are the streamer's inputs. Published here, and nowhere else, so they change
        // exactly when a preset or a shadow override does. Medium, High and Ultra author 1 / 1, at
        // which every consumer does nothing; Set raises Changed only when a value moved.
        DrawDistanceScale = _quality.DrawDistanceScale;
        ScatterDensityScale = _quality.ScatterDensityScale;
        ActorShadowDistance = shadows ? shadow.ActorShadowDistance : 0f;
        WorldQualityScale.Set(DrawDistanceScale, ScatterDensityScale, ActorShadowDistance);

        _applied = (tier, overrides);
    }

    /// <summary>
    /// Sizes the omni/spot shadow atlas. 0 restores the project's own size and quadrant layout, which
    /// is what every tier from Medium up uses. A smaller atlas is split into fewer, larger slots, so
    /// the few lights that still cast keep their resolution while the texture itself shrinks.
    /// </summary>
    private static void ApplyPositionalAtlas(Viewport viewport, int size)
    {
        const string root = "rendering/lights_and_shadows/positional_shadow/";
        Span<int> quadrants = stackalloc int[4];
        if (size <= 0)
        {
            size = ProjectInt(root + "atlas_size", 4096);
            quadrants[0] = ProjectInt(root + "atlas_quadrant_0_subdiv", 2);
            quadrants[1] = ProjectInt(root + "atlas_quadrant_1_subdiv", 2);
            quadrants[2] = ProjectInt(root + "atlas_quadrant_2_subdiv", 3);
            quadrants[3] = ProjectInt(root + "atlas_quadrant_3_subdiv", 4);
        }
        else
        {
            bool small = size <= 1024;
            quadrants[0] = (int)Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv1;
            quadrants[1] = (int)(small ? Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv1 : Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv4);
            quadrants[2] = (int)Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv4;
            quadrants[3] = (int)(small ? Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv4 : Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv16);
        }
        viewport.PositionalShadowAtlasSize = size;
        for (int i = 0; i < 4; i++)
            viewport.SetPositionalShadowAtlasQuadrantSubdiv(i, (Viewport.PositionalShadowAtlasQuadrantSubdiv)quadrants[i]);
    }

    private static int ProjectInt(string setting, int fallback) =>
        ProjectSettings.HasSetting(setting) ? ProjectSettings.GetSetting(setting).AsInt32() : fallback;

    public override void _Process(double delta)
    {
        if (Cycle == null || Cycle.Keys.Count < 2 || Environment == null) return;
        ResolveServices();
        _sampleClock -= delta;
        if (_sampleClock <= 0) { SampleContext(); _sampleClock = 0.25; }
        float dt = (float)delta;
        float blend = _initialized ? EnvironmentMath.BlendWeight(dt, Cycle.TransitionSeconds) : 1f;
        WeatherResource? weather = _weather?.Current;
        _light = Mathf.Lerp(_light, weather?.LightEnergyScale ?? 1f, blend);
        _sky = Mathf.Lerp(_sky, weather?.SkyEnergyScale ?? 1f, blend);
        _fog = Mathf.Lerp(_fog, weather?.FogDensity ?? 0f, blend);
        _precipitation = Mathf.Lerp(_precipitation, weather?.Precipitation ?? 0f, blend);
        _weatherFog = _weatherFog.Lerp(weather?.FogColor ?? Colors.White, blend);
        _weatherTint = _weatherTint.Lerp(weather?.SkyTint ?? Colors.White, blend);
        _regionEnergy = Mathf.Lerp(_regionEnergy, RegionAtmosphere?.SunEnergyScale ?? 1f, blend);
        _regionHaze = Mathf.Lerp(_regionHaze, RegionAtmosphere?.HazeScale ?? 1f, blend);
        _regionTint = _regionTint.Lerp(RegionAtmosphere?.SunTint ?? Colors.White, blend);
        _haze = _haze.Lerp(RegionAtmosphere?.HazeColor ?? new Color(.7f, .66f, .6f), blend);
        Shelter = Mathf.Lerp(Shelter, _shelterTarget, blend);
        Wind = Wind.Lerp(_windTarget * (weather?.WindStrength ?? 1f), blend);
        float rain = _precipitation * (1f - _cold);
        float snow = _precipitation * _cold;
        Wetness = Mathf.Lerp(Wetness, rain, EnvironmentMath.BlendWeight(dt, rain > Wetness ? 12f : 90f));
        SnowCover = Mathf.Lerp(SnowCover, snow, EnvironmentMath.BlendWeight(dt, snow > SnowCover ? 45f : 150f));
        float hour = RegionAtmosphere is { FixedSkyHour: >= 0f } pinned
            ? pinned.FixedSkyHour
            : _clock?.TimeOfDay ?? 12f;
        EnvironmentKeyframeResource a = Cycle.Keys[^1], b = Cycle.Keys[0];
        for (int i = 0; i < Cycle.Keys.Count; i++)
        {
            var next = Cycle.Keys[(i + 1) % Cycle.Keys.Count];
            if (hour >= Cycle.Keys[i].Hour && (next.Hour <= Cycle.Keys[i].Hour || hour < next.Hour))
            { a = Cycle.Keys[i]; b = next; break; }
        }
        float t = EnvironmentMath.KeyWeight(hour, a.Hour, b.Hour);
        float L(float x, float y) => Mathf.Lerp(x, y, t);
        _ambientScale = Mathf.Lerp(_ambientScale, Mathf.Lerp(1f, _space?.AmbientScale ?? 1f, _spaceWeight), blend);
        _exposureScale = Mathf.Lerp(_exposureScale, Mathf.Lerp(1f, _space?.ExposureScale ?? 1f, _spaceWeight), blend);
        _fogScale = Mathf.Lerp(_fogScale, Mathf.Lerp(1f, _space?.FogScale ?? 1f, _spaceWeight), blend);
        _spaceFog = _spaceFog.Lerp(Colors.White.Lerp(_space?.FogTint ?? Colors.White, _spaceWeight), blend);
        float outside = _fogScale;
        if (!_initialized) ApplyConstants();
        if (Sun != null)
        {
            float elevation = Mathf.Sin((hour - 6f) / 12f * Mathf.Pi);
            Sun.RotationDegrees = new Vector3(-Mathf.Max(0f, elevation) * 72f, -110f + (hour - 6f) / 12f * 220f, 0f);
            Sun.LightEnergy = L(a.SunEnergy, b.SunEnergy) * _light * _regionEnergy;
            Sun.LightColor = a.SunColor.Lerp(b.SunColor, t) * _regionTint;
            Sun.Visible = Sun.LightEnergy > .001f;
        }
        _moon.RotationDegrees = new Vector3(-38f, hour * 15f + 80f, 0f);
        _moon.LightEnergy = L(a.MoonEnergy, b.MoonEnergy) * _sky;
        _moon.Visible = _moon.LightEnergy > .001f;
        Environment.BackgroundEnergyMultiplier = L(a.SkyEnergy, b.SkyEnergy) * _sky;
        Environment.AmbientLightColor = a.HorizonColor.Lerp(b.HorizonColor, t) * _weatherTint;
        Environment.AmbientLightEnergy = L(a.AmbientEnergy, b.AmbientEnergy) * Mathf.Lerp(.72f, 1f, _sky) * _ambientScale;
        Environment.FogDensity = (Cycle.ClearFogDensity + _fog * .65f) * _regionHaze * (1f + _humidity * .25f) * outside;
        Environment.FogLightColor = a.HorizonColor.Lerp(b.HorizonColor, t).Lerp(_haze, .18f).Lerp(_weatherFog, _precipitation * .2f) * _spaceFog * _weatherTint;
        Environment.FogLightEnergy = L(a.FogEnergy, b.FogEnergy);
        if (_volumetricFog)
        {
            // Inert while the effect is off, and each setter re-sends the whole fog-volume block.
            Environment.VolumetricFogDensity = Mathf.Min(.008f, _fog * .18f) * outside;
            Environment.VolumetricFogAlbedo = Environment.FogLightColor;
        }
        Environment.TonemapExposure = L(a.Exposure, b.Exposure) * _exposureScale;
        if (_skyMaterial is { } sky)
        {
            Color top = a.SkyColor.Lerp(b.SkyColor, t).Lerp(_weatherFog, _precipitation * .35f) * _weatherTint;
            Color horizon = a.HorizonColor.Lerp(b.HorizonColor, t) * _weatherTint;
            sky.SkyTopColor = top;
            sky.SkyHorizonColor = horizon;
            sky.GroundHorizonColor = horizon * .55f;
            sky.GroundBottomColor = top * .30f;
        }
        bool raining = UpdatePrecipitation(_rain, _rainProcess, rain, -12f, ref _rainIdle, dt);
        bool snowing = UpdatePrecipitation(_snow, _snowProcess, snow, -.3f, ref _snowIdle, dt);
        if ((raining || snowing) != _weatherCollisionActive)
        {
            _weatherCollisionActive = raining || snowing;
            _weatherCollision.Visible = _weatherCollisionActive;
        }
        if (_weatherCollisionActive && GetViewport().GetCamera3D() is { } focus)
        {
            Vector3 p = focus.GlobalPosition;
            _weatherCollision.GlobalPosition = new Vector3(Mathf.Round(p.X / 4f) * 4f, Mathf.Round(p.Y / 2f) * 2f - 2f, Mathf.Round(p.Z / 4f) * 4f);
        }
        // In clear weather these three sit at one value for minutes; send a change, not a frame.
        if (Wetness != _sentWetness) RenderingServer.GlobalShaderParameterSet(WetnessGlobal, _sentWetness = Wetness);
        if (SnowCover != _sentSnow) RenderingServer.GlobalShaderParameterSet(SnowGlobal, _sentSnow = SnowCover);
        if (rain != _sentRain) RenderingServer.GlobalShaderParameterSet(RainGlobal, _sentRain = rain);
        RenderingServer.GlobalShaderParameterSet(WindGlobal, Wind);
        // Use the saved world clock, not shader TIME, so regression captures can freeze all motion.
        RenderingServer.GlobalShaderParameterSet(VisualTimeGlobal, (((_clock?.Day ?? 0) * 24f) + (_clock?.TimeOfDay ?? 12f)) * (_clock?.DayLengthSeconds ?? 180f) / 24f);
        _initialized = true;
    }

    /// <summary>
    /// The values that never change over a world's life. They were re-sent every frame beside the
    /// ones that do, and each Environment setter re-sends its whole parameter block to the renderer.
    /// </summary>
    private void ApplyConstants()
    {
        if (Cycle == null || Environment == null) return;
        _moon.LightColor = Cycle.MoonColor;
        Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        Environment.AmbientLightSkyContribution = .65f;
        Environment.FogEnabled = true;
        Environment.FogSkyAffect = .22f;
        Environment.AdjustmentEnabled = true;
        Environment.AdjustmentContrast = Cycle.Contrast;
        Environment.AdjustmentSaturation = Cycle.Saturation;
        Environment.GlowIntensity = Cycle.GlowIntensity;
        Environment.GlowBloom = 0f;
        Environment.GlowHdrThreshold = 1.2f;
        _skyMaterial = Environment.Sky?.SkyMaterial as ProceduralSkyMaterial;
        if (_skyMaterial != null)
        {
            _skyMaterial.SkyEnergyMultiplier = 1f;
            _skyMaterial.SunAngleMax = 2f;
        }
    }

    private void SampleContext()
    {
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera == null) return;
        Vector3 p = camera.GlobalPosition;
        WorldSample? sample = WorldGround.Field?.Sample(p.X, p.Z);
        _cold = EnvironmentMath.SnowFraction(sample?.Temperature ?? .6f);
        _humidity = sample?.Moisture ?? .4f;
        var query = PhysicsRayQueryParameters3D.Create(p, p + Vector3.Up * 25f, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        _shelterTarget = hit.Count > 0 ? 1f : 0f;
        _space = _interior;
        _spaceWeight = _shelterTarget;
        int priority = int.MinValue;
        foreach (Node node in GetTree().GetNodesInGroup("environment_volumes"))
        {
            if (node is not EnvironmentVolume volume || volume.Profile == null) continue;
            float weight = volume.WeightAt(p);
            if (weight <= 0 || volume.Priority < priority) continue;
            priority = volume.Priority; _space = volume.Profile; _spaceWeight = weight;
            _shelterTarget = Mathf.Max(_shelterTarget, volume.Profile.Shelter * weight);
        }
        foreach (var body in WorldWater.Bodies)
        {
            if (Mathf.Abs(p.X - body.X) <= body.ExtentX && Mathf.Abs(p.Z - body.Z) <= body.ExtentZ && p.Y < body.SurfaceY)
            { _space = _underwater; _spaceWeight = Mathf.Clamp((body.SurfaceY - p.Y) * 3f, 0f, 1f); _shelterTarget = 1f; break; }
        }
        if (_quality != null)
            _emitters.Update(p, Wind, Mathf.Clamp(Mathf.Sin(((_clock?.TimeOfDay ?? 12f) - 6f) / 12f * Mathf.Pi), 0f, 1f),
                _quality.ParticleScale, _localShadowLights, _quality.LocalLightDistance);
        EventBus.Instance?.Publish(new EnvironmentVisualStateEvent(_clock?.TimeOfDay ?? 12f,
            _precipitation * (1f - _cold), _precipitation * _cold, Wetness, Wind, Shelter));
    }

    /// <summary>
    /// Drives one precipitation system and reports whether it is emitting. Once it has been off for
    /// longer than any of its particles can live there is nothing left to steer, so the follow, the
    /// wind and the amount are not sent again until it next falls. That is every clear-weather frame.
    /// </summary>
    private bool UpdatePrecipitation(GpuParticles3D particles, ParticleProcessMaterial process, float intensity,
        float fall, ref float idle, float dt)
    {
        float amount = intensity * (1f - Shelter) * (_quality?.ParticleScale ?? .5f);
        bool emit = amount > .015f;
        idle = emit ? 0f : idle + dt;
        if (idle >= PrecipitationTail) return false;
        particles.Emitting = emit;
        particles.AmountRatio = Mathf.Clamp(amount, 0f, 1f);
        if (GetViewport().GetCamera3D() is { } camera)
            particles.GlobalPosition = camera.GlobalPosition + Vector3.Up * 10f;
        process.Gravity = new Vector3(Wind.X, fall, Wind.Z);
        return emit;
    }

    private static GpuParticles3D BuildPrecipitation(bool snow)
    {
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Down,
            Spread = snow ? 12f : 0f,
            InitialVelocityMin = snow ? 1f : 18f,
            InitialVelocityMax = snow ? 2f : 22f,
            Gravity = new Vector3(0, -12, 0),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(12, .5f, 12),
            CollisionMode = ParticleProcessMaterial.CollisionModeEnum.HideOnContact,
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(.78f, .83f, .88f, snow ? .75f : .3f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        var particles = new GpuParticles3D
        {
            Name = snow ? "Snow" : "Rain",
            Amount = snow ? 500 : 1200,
            Lifetime = snow ? 4 : 1.3,
            Emitting = false,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = snow ? new Vector2(.055f, .055f) : new Vector2(.018f, .40f), Material = material },
            VisibilityAabb = new Aabb(new Vector3(-18, -35, -18), new Vector3(36, 38, 36)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        return particles;
    }

    private void ResolveServices()
    {
        if (ServiceLocator.Instance is not { } locator) return;
        if (_clock == null && locator.TryGet(out WorldClock clock)) _clock = clock;
        if (_weather == null && locator.TryGet(out WeatherDirector weather)) _weather = weather;
    }
}
