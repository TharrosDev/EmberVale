namespace Embervale.Companions;

/// <summary>The behaviour states of a <see cref="CompanionAIComponent"/> brain.</summary>
public enum CompanionState
{
    /// <summary>In position (formation slot or hold anchor) with nothing to fight.</summary>
    Idle,

    /// <summary>Moving back into position — trailing the player, or returning to the hold anchor.</summary>
    Follow,

    /// <summary>Engaging a hostile: closing distance and attacking.</summary>
    Combat,

    /// <summary>Too far from its anchor to keep fighting; breaking off to regroup.</summary>
    Regroup,

    /// <summary>Out of health: on the ground, recovering. Companions are never lost permanently.</summary>
    Downed,
}
