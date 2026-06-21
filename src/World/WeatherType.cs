namespace Embervale.World;

/// <summary>
/// The kinds of weather the world can be in. The active type is chosen by the
/// <see cref="WeatherDirector"/> from authored <see cref="WeatherResource"/>s; it
/// drives lighting/fog/precipitation via the <see cref="SkyController"/> and biases
/// how often the <see cref="EncounterDirector"/> spawns encounters.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum WeatherType
{
    Clear,
    Cloudy,
    Rain,
    Storm,
    Fog,
}
