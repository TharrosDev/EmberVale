namespace Embervale.World;

/// <summary>
/// The fixed top-level world divisions from the LORE (the four realms left after The
/// Shattering, plus the ruined Celestial Realm of the endgame). A <see cref="RegionResource"/>
/// belongs to one realm; this is a finite, lore-pinned taxonomy, so it is an enum rather than
/// authored data (like <see cref="DayPhase"/>/<see cref="WeatherType"/>).
/// </summary>
public enum Realm
{
    EmberCrown,
    FrostfangReach,
    AshenWilds,
    SunspireDominion,
    CelestialRealm,
}
