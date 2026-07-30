namespace Embervale.World;

/// <summary>
/// The fixed top-level world divisions from the LORE (the four realms left after The
/// Shattering, the ruined Celestial Realm of the endgame, and the Pale Concord — the fifth
/// realm the world believes was never there). A <see cref="RegionResource"/> belongs to one
/// realm; this is a finite, lore-pinned taxonomy, so it is an enum rather than authored data
/// (like <see cref="DayPhase"/>/<see cref="WeatherType"/>).
/// </summary>
// APPEND ONLY: ordinals are authored into region .tres files — never reorder/insert/remove
// (EnumStabilityTests). PaleConcord is a mortal realm but sits after CelestialRealm because
// ordinal stability outranks conceptual grouping.
public enum Realm
{
    EmberCrown,
    FrostfangReach,
    AshenWilds,
    SunspireDominion,
    CelestialRealm,
    PaleConcord,
}
