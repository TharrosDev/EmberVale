using Godot;

namespace Embervale.World;

/// <summary>
/// A designer-authored weather state: how long it lasts, how it tints the sky and
/// light, whether it brings fog or rain, and how likely it is to be chosen. Authored
/// as a <c>.tres</c> under <c>data/weather/</c> and indexed by
/// <see cref="WeatherDatabase"/> — a new weather state is a new resource, no code.
///
/// The <see cref="SkyController"/> reads the atmosphere fields (blending between states
/// on a transition); the <see cref="WeatherDirector"/> reads duration + selection weight.
/// </summary>
[GlobalClass]
public partial class WeatherResource : Resource
{
    /// <summary>Stable id, e.g. "weather.rain". The save/database key.</summary>
    [Export] public string Id { get; set; } = "weather.clear";

    [Export] public string DisplayName { get; set; } = "Clear";

    [Export] public WeatherType Type { get; set; } = WeatherType.Clear;

    /// <summary>Relative likelihood of being chosen when the weather rolls over.</summary>
    [Export] public float SelectionWeight { get; set; } = 1f;

    [ExportGroup("Duration (in-game hours)")]
    [Export] public float MinHours { get; set; } = 4f;
    [Export] public float MaxHours { get; set; } = 10f;

    [ExportGroup("Atmosphere")]
    /// <summary>Multiplies the sun's direct light energy (overcast/storm &lt; 1).</summary>
    [Export] public float LightEnergyScale { get; set; } = 1f;

    /// <summary>Multiplies sky/ambient brightness (overcast dims the whole scene).</summary>
    [Export] public float SkyEnergyScale { get; set; } = 1f;

    /// <summary>Distance fog density; 0 disables fog for this state.</summary>
    [Export] public float FogDensity { get; set; } = 0f;

    [Export] public Color FogColor { get; set; } = new(0.72f, 0.74f, 0.78f);

    /// <summary>Rain intensity 0..1 (drives the rain particle effect).</summary>
    [Export] public float Precipitation { get; set; } = 0f;

    /// <summary>A randomised duration in in-game hours for one spell of this weather.</summary>
    public float RollDuration()
    {
        float min = Mathf.Min(MinHours, MaxHours);
        float max = Mathf.Max(MinHours, MaxHours);
        return min + (GD.Randf() * (max - min));
    }
}
