namespace Embervale.Companions;

/// <summary>What a companion wants to do this tick, given where its anchor and its target are.</summary>
public enum CompanionAction
{
    /// <summary>In position with nothing to do — stand and watch.</summary>
    Hold,

    /// <summary>Move back into position (formation slot / hold anchor).</summary>
    Regroup,

    /// <summary>Close the gap on the current hostile.</summary>
    Chase,

    /// <summary>In reach of the hostile — swing.</summary>
    Attack,
}

/// <summary>
/// The pure decision brain for a follower (Phase 32A). A companion answers to two pulls: stay near
/// its <em>anchor</em> (the player's formation slot, or the spot it was told to hold) and fight what
/// threatens it. The leash is what keeps those from fighting each other — a companion that has been
/// dragged past <c>leashRadius</c> from its anchor breaks off and regroups <em>even mid-fight</em>,
/// so it can never be kited across the map away from the player.
///
/// Godot-free so the rule is unit-testable apart from the locomotion/navmesh it drives in
/// <see cref="CompanionAIComponent"/>.
/// </summary>
public static class CompanionDecision
{
    /// <summary>
    /// Decides this tick's action. <paramref name="distanceToAnchor"/> is the planar distance from the
    /// companion to its anchor, <paramref name="distanceToTarget"/> to its hostile (ignored when
    /// <paramref name="hasTarget"/> is false), <paramref name="leashRadius"/> how far it may stray from
    /// the anchor before disengaging, <paramref name="slotTolerance"/> the arrival deadzone around the
    /// anchor (so it doesn't jitter in place), and <paramref name="attackRange"/> its weapon reach.
    /// </summary>
    public static CompanionAction Decide(
        float distanceToAnchor,
        bool hasTarget,
        float distanceToTarget,
        float leashRadius,
        float slotTolerance,
        float attackRange)
    {
        // The leash wins over everything: fighting stops when the companion has been pulled too far
        // from where it belongs, so a fleeing enemy can't drag it away from the player.
        if (distanceToAnchor > leashRadius)
        {
            return CompanionAction.Regroup;
        }

        if (hasTarget)
        {
            return distanceToTarget > attackRange ? CompanionAction.Chase : CompanionAction.Attack;
        }

        return distanceToAnchor > slotTolerance ? CompanionAction.Regroup : CompanionAction.Hold;
    }
}
