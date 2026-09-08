using System;
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
    private Color _regionTint = Colors.White, _haze = new(0.70f, 0.66f, 0.60f), _weatherFog = Colors.White;
    private double _sampleClock;
    private Vector3 _windTarget = new(1.5f, 0f, 0.5f);
    private bool _initialized;

    public override void _Ready()
    {
        Cycle ??= GD.Load<EnvironmentCycleResource>("res://data/rendering/DayCycle.tres");
        _interior = GD.Load<EnvironmentSpaceProfileResource>("res://data/rendering/Interior.tres");
        _underwater = GD.Load<EnvironmentSpaceProfileResource>("res://data/rendering/Underwater.tres");
        ServiceScope.RegisterOwned(this, this);
        _moon = new DirectionalLight3D { Name = "Moon", ShadowEnabled = false };
        AddChild(_moon);
        _rain = BuildPrecipitation(false);
        _snow = BuildPrecipitation(true);
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

    public void ApplyQuality(int tier)
    {
        string name = new[] { "Low", "Medium", "High", "Ultra" }[Math.Clamp(tier, 0, 3)];
        _quality = GD.Load<RenderQualityResource>($"res://data/rendering/{name}.tres");
        if (_quality == null || Environment == null) return;
        RenderingServer.DirectionalShadowAtlasSetSize(_quality.ShadowAtlasSize, true);
        GetViewport().Scaling3DScale = _quality.RenderScale;
        GetViewport().MeshLodThreshold = _quality.MeshLodThreshold;
        Environment.SsaoEnabled = _quality.AmbientOcclusion;
        Environment.SsaoRadius = 0.65f;
        Environment.SsaoIntensity = 1.1f;
        Environment.SsaoPower = 1.2f;
        Environment.SsilEnabled = _quality.IndirectLighting;
        Environment.SsilIntensity = 0.35f;
        Environment.SsrEnabled = _quality.Reflections;
        // Streaming terrain never rebuilds voxel GI. Sky radiance is the outdoor indirect source.
        Environment.SdfgiEnabled = false;
        Environment.VolumetricFogEnabled = _quality.VolumetricFog;
        Environment.VolumetricFogLength = 96f;
        if (Sun != null)
        {
            Sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
            Sun.DirectionalShadowMaxDistance = _quality.ShadowDistance;
            Sun.DirectionalShadowSplit1 = 0.10f;
            Sun.DirectionalShadowSplit2 = 0.25f;
            Sun.DirectionalShadowSplit3 = 0.55f;
            Sun.DirectionalShadowBlendSplits = true;
            Sun.ShadowBias = 0.025f;
            Sun.ShadowNormalBias = 0.7f;
        }
    }

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
        float hour = _clock?.TimeOfDay ?? 12f;
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
        if (Sun != null)
        {
            float elevation = Mathf.Sin((hour - 6f) / 12f * Mathf.Pi);
            Sun.RotationDegrees = new Vector3(-Mathf.Max(0f, elevation) * 72f, -110f + (hour - 6f) / 12f * 220f, 0f);
            Sun.LightEnergy = L(a.SunEnergy, b.SunEnergy) * _light * _regionEnergy;
            Sun.LightColor = a.SunColor.Lerp(b.SunColor, t) * _regionTint;
            Sun.Visible = Sun.LightEnergy > .001f;
        }
        _moon.RotationDegrees = new Vector3(-38f, hour * 15f + 80f, 0f);
        _moon.LightColor = Cycle.MoonColor;
        _moon.LightEnergy = L(a.MoonEnergy, b.MoonEnergy) * _sky;
        _moon.Visible = _moon.LightEnergy > .001f;
        Environment.BackgroundEnergyMultiplier = L(a.SkyEnergy, b.SkyEnergy) * _sky;
        Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        Environment.AmbientLightColor = a.HorizonColor.Lerp(b.HorizonColor, t);
        Environment.AmbientLightEnergy = L(a.AmbientEnergy, b.AmbientEnergy) * Mathf.Lerp(.72f, 1f, _sky) * _ambientScale;
        Environment.AmbientLightSkyContribution = .65f;
        Environment.FogEnabled = true;
        Environment.FogDensity = (Cycle.ClearFogDensity + _fog * .65f) * _regionHaze * (1f + _humidity * .25f) * outside;
        Environment.FogLightColor = a.HorizonColor.Lerp(b.HorizonColor, t).Lerp(_haze, .18f).Lerp(_weatherFog, _precipitation * .2f) * _spaceFog;
        Environment.FogLightEnergy = L(a.FogEnergy, b.FogEnergy);
        Environment.FogSkyAffect = .22f;
        Environment.VolumetricFogDensity = _quality?.VolumetricFog == true ? Mathf.Min(.008f, _fog * .18f) * outside : 0f;
        Environment.VolumetricFogAlbedo = Environment.FogLightColor;
        Environment.TonemapExposure = L(a.Exposure, b.Exposure) * _exposureScale;
        Environment.AdjustmentEnabled = true;
        Environment.AdjustmentContrast = Cycle.Contrast;
        Environment.AdjustmentSaturation = Cycle.Saturation;
        Environment.GlowIntensity = Cycle.GlowIntensity;
        Environment.GlowBloom = 0f;
        Environment.GlowHdrThreshold = 1.2f;
        if (Environment.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
        {
            sky.SkyTopColor = a.SkyColor.Lerp(b.SkyColor, t).Lerp(_weatherFog, _precipitation * .35f);
            sky.SkyHorizonColor = a.HorizonColor.Lerp(b.HorizonColor, t);
            sky.GroundHorizonColor = sky.SkyHorizonColor * .55f;
            sky.GroundBottomColor = sky.SkyTopColor * .30f;
            sky.SkyEnergyMultiplier = 1f;
            sky.SunAngleMax = 2f;
        }
        UpdatePrecipitation(_rain, rain);
        UpdatePrecipitation(_snow, snow);
        _weatherCollision.Visible = _rain.Emitting || _snow.Emitting;
        if (_weatherCollision.Visible && GetViewport().GetCamera3D() is { } focus)
        {
            Vector3 p = focus.GlobalPosition;
            _weatherCollision.GlobalPosition = new Vector3(Mathf.Round(p.X / 4f) * 4f, Mathf.Round(p.Y / 2f) * 2f - 2f, Mathf.Round(p.Z / 4f) * 4f);
        }
        RenderingServer.GlobalShaderParameterSet("world_wetness", Wetness);
        RenderingServer.GlobalShaderParameterSet("world_snow", SnowCover);
        RenderingServer.GlobalShaderParameterSet("world_wind", Wind);
        RenderingServer.GlobalShaderParameterSet("world_rain", rain);
        // Use the saved world clock, not shader TIME, so regression captures can freeze all motion.
        RenderingServer.GlobalShaderParameterSet("world_visual_time", (((_clock?.Day ?? 0) * 24f) + hour) * (_clock?.DayLengthSeconds ?? 180f) / 24f);
        _initialized = true;
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
            _emitters.Update(p, Wind, Mathf.Clamp(Mathf.Sin(((_clock?.TimeOfDay ?? 12f) - 6f) / 12f * Mathf.Pi), 0f, 1f), _quality);
        EventBus.Instance?.Publish(new EnvironmentVisualStateEvent(_clock?.TimeOfDay ?? 12f,
            _precipitation * (1f - _cold), _precipitation * _cold, Wetness, Wind, Shelter));
    }

    private void UpdatePrecipitation(GpuParticles3D particles, float intensity)
    {
        float amount = intensity * (1f - Shelter) * (_quality?.ParticleScale ?? .5f);
        particles.Emitting = amount > .015f;
        particles.AmountRatio = Mathf.Clamp(amount, 0f, 1f);
        if (GetViewport().GetCamera3D() is { } camera)
            particles.GlobalPosition = camera.GlobalPosition + Vector3.Up * 10f;
        if (particles.ProcessMaterial is ParticleProcessMaterial process)
            process.Gravity = new Vector3(Wind.X, particles == _snow ? -.3f : -12f, Wind.Z);
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
