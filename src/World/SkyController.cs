using Embervale.Core.Services;
using Embervale.Player;
using Godot;

namespace Embervale.World;

/// <summary>
/// Renders the world's atmosphere by driving a directional "sun" light and the scene
/// <see cref="Godot.Environment"/> from the <see cref="WorldClock"/>'s continuous time
/// of day, blended with the active <see cref="WeatherResource"/> (from the
/// <see cref="WeatherDirector"/>). It sweeps the sun across the sky, warms and dims it
/// at dawn/dusk, darkens the sky/ambient at night, and applies weather fog + rain.
///
/// The sun light and environment are injected by the bootstrap (they already exist in
/// the sandbox scene), so this node only animates them — it owns no scene structure
/// beyond a rain particle emitter it parents for precipitation.
/// </summary>
[GlobalClass]
public partial class SkyController : Node3D
{
    /// <summary>The directional light treated as the sun (injected by the bootstrap).</summary>
    public DirectionalLight3D? Sun { get; set; }

    /// <summary>The scene environment whose sky/fog/ambient this drives (injected).</summary>
    public Godot.Environment? Environment { get; set; }

    /// <summary>How fast weather atmosphere blends in/out (units per second).</summary>
    [Export] public float WeatherBlendRate { get; set; } = 0.5f;

    private static readonly Color WarmLight = new(1.0f, 0.62f, 0.36f);
    private static readonly Color NoonLight = new(1.0f, 0.96f, 0.88f);

    private WorldClock? _clock;
    private WeatherDirector? _weather;
    private GpuParticles3D _rain = null!;

    // Blended (current → target) weather atmosphere values.
    private float _lightScale = 1f;
    private float _skyScale = 1f;
    private float _fogDensity;
    private float _precipitation;
    private Color _fogColor = new(0.72f, 0.74f, 0.78f);

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Pausable;
        _rain = BuildRain();
        AddChild(_rain);

        // Start already matching the active weather so there's no first-frame fade-in.
        SyncWeatherTargets(snap: true);
    }

    public override void _Process(double delta)
    {
        ResolveServices();
        BlendWeather((float)delta);

        float time = _clock?.TimeOfDay ?? 12f;
        UpdateSun(time);
        UpdateEnvironment(time);
        UpdateRain();
    }

    // --- Sun & sky ----------------------------------------------------------

    private void UpdateSun(float time)
    {
        if (Sun == null)
        {
            return;
        }

        // Elevation peaks at noon (t=12), zero at 6/18, negative at night.
        float elevation = Mathf.Sin((time - 6f) / 12f * Mathf.Pi);
        float dayFactor = Mathf.Clamp(elevation, 0f, 1f);

        // Sweep east→west across the day; pitch dips toward the horizon at dawn/dusk.
        float azimuth = -110f + (Mathf.Clamp((time - 6f) / 12f, 0f, 1f) * 220f);
        float pitch = -Mathf.Lerp(2f, 78f, dayFactor);
        Sun.RotationDegrees = new Vector3(pitch, azimuth, 0f);

        Sun.Visible = dayFactor > 0.01f;
        Sun.LightEnergy = Mathf.Lerp(0f, 1.15f, dayFactor) * _lightScale;
        Sun.LightColor = WarmLight.Lerp(NoonLight, dayFactor);
    }

    private void UpdateEnvironment(float time)
    {
        if (Environment == null)
        {
            return;
        }

        float elevation = Mathf.Sin((time - 6f) / 12f * Mathf.Pi);
        float dayFactor = Mathf.Clamp(elevation, 0f, 1f);

        // Sky + sky-derived ambient: dark at night, full by day, dimmed by overcast.
        float skyEnergy = Mathf.Lerp(0.12f, 1f, dayFactor) * _skyScale;
        Environment.BackgroundEnergyMultiplier = skyEnergy;

        bool fog = _fogDensity > 0.0005f;
        Environment.FogEnabled = fog;
        if (fog)
        {
            Environment.FogDensity = _fogDensity;
            Environment.FogLightColor = _fogColor;
        }
    }

    // --- Weather blending ---------------------------------------------------

    private void BlendWeather(float delta)
    {
        WeatherResource? target = _weather?.Current;
        if (target == null)
        {
            return;
        }

        float step = WeatherBlendRate * delta;
        _lightScale = Mathf.MoveToward(_lightScale, target.LightEnergyScale, step);
        _skyScale = Mathf.MoveToward(_skyScale, target.SkyEnergyScale, step);
        _fogDensity = Mathf.MoveToward(_fogDensity, target.FogDensity, step * 0.2f);
        _precipitation = Mathf.MoveToward(_precipitation, target.Precipitation, step);
        _fogColor = _fogColor.Lerp(target.FogColor, Mathf.Clamp(step, 0f, 1f));
    }

    private void SyncWeatherTargets(bool snap)
    {
        ResolveServices();
        WeatherResource? target = _weather?.Current;
        if (target == null || !snap)
        {
            return;
        }

        _lightScale = target.LightEnergyScale;
        _skyScale = target.SkyEnergyScale;
        _fogDensity = target.FogDensity;
        _precipitation = target.Precipitation;
        _fogColor = target.FogColor;
    }

    // --- Rain ---------------------------------------------------------------

    private void UpdateRain()
    {
        _rain.Emitting = _precipitation > 0.03f;
        _rain.AmountRatio = Mathf.Clamp(_precipitation, 0f, 1f);

        // Keep the emitter overhead and centred on the player so rain falls around them.
        if (ServiceLocator.Instance != null &&
            ServiceLocator.Instance.TryGet(out PlayerCharacter player) &&
            IsInstanceValid(player))
        {
            _rain.GlobalPosition = player.GlobalPosition + (Vector3.Up * 12f);
        }
    }

    private static GpuParticles3D BuildRain()
    {
        var process = new ParticleProcessMaterial
        {
            Direction = new Vector3(0f, -1f, 0f),
            Spread = 0f,
            InitialVelocityMin = 18f,
            InitialVelocityMax = 22f,
            Gravity = new Vector3(0f, -12f, 0f),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(14f, 0.5f, 14f),
            ScaleMin = 1f,
            ScaleMax = 1f,
            Color = new Color(0.6f, 0.7f, 0.9f, 0.5f),
        };

        var streak = new QuadMesh { Size = new Vector2(0.03f, 0.55f) };
        streak.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.8f, 1f, 0.45f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };

        return new GpuParticles3D
        {
            Name = "Rain",
            Amount = 1200,
            Lifetime = 1.3,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = streak,
        };
    }

    private void ResolveServices()
    {
        ServiceLocator? locator = ServiceLocator.Instance;
        if (locator == null)
        {
            return;
        }

        if (_clock == null && locator.TryGet(out WorldClock clock))
        {
            _clock = clock;
        }

        if (_weather == null && locator.TryGet(out WeatherDirector weather))
        {
            _weather = weather;
        }
    }
}
