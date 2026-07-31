namespace Embervale.Enemies;

/// <summary>The behaviour states of an <see cref="EnemyAIComponent"/> brain.</summary>
public enum EnemyState
{
    /// <summary>Standing at home, occasionally scanning for threats.</summary>
    Idle,

    /// <summary>Wandering around the home position.</summary>
    Patrol,

    /// <summary>Moving to a last-known/alerted position to search.</summary>
    Investigate,

    /// <summary>Engaging the target: closing distance and attacking.</summary>
    Combat,

    /// <summary>Backing away from the target (low health / disengage).</summary>
    Retreat,

    /// <summary>Defeated; no longer acting.</summary>
    Dead,

    /// <summary>Walking back to its territory after being drawn out of it (Phase 35D). Distinct from
    /// <see cref="Retreat"/>, which flees *from* a threat — this one heads *home*, and deliberately
    /// ignores the player on the way so a leash cannot be defeated by standing in the doorway.</summary>
    Returning,
}
