namespace Embervale.World;

/// <summary>
/// The behaviour archetype of a <see cref="WorldEventResource"/>. Each kind has a
/// different objective and spawn, but all share the announce → track → reward lifecycle
/// the <see cref="WorldEventDirector"/> runs. New kinds slot in here + the director's
/// start/tracking switch.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum WorldEventKind
{
    /// <summary>A band of hostiles attacks near the player; objective: defeat them all.</summary>
    Raid,

    /// <summary>A loot cache appears; objective: collect its contents.</summary>
    Cache,

    /// <summary>A single tougher foe appears; objective: slay it.</summary>
    Hunt,
}

/// <summary>The lifecycle state of an active <see cref="WorldEvent"/>.</summary>
public enum WorldEventStatus
{
    Active,
    Completed,
    Failed,
}
