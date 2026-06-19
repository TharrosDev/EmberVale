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
}
