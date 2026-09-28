namespace Embervale.Movement;

/// <summary>
/// Whether a body on foot may sprint this frame, given its stamina — pure, Godot-free, unit-tested.
/// <see cref="LocomotionComponent"/> spends the stamina through <c>StatsComponent.ModifyCurrent</c>;
/// regeneration stays <see cref="Stats.StaminaPacing"/>'s business, which is why this only decides.
///
/// It is <see cref="MountRules"/>' latch applied to the rider's own legs, and deliberately the same
/// rule: once the pool bottoms out, sprint stays refused until stamina has climbed back to
/// <c>resumeAt</c> <b>and</b> the player has let go of sprint once. Without the latch a body at zero
/// sprints for one frame per regen tick, and a held key stutters between gaits several times a second.
/// Without the let-go it still sawtooths, just slowly enough to look deliberate — MountRules found that
/// the hard way, and one learnable rule for both gaits beats two.
/// </summary>
public static class SprintStamina
{
    /// <summary>The latch after this frame, and whether the body sprints on it.</summary>
    public readonly record struct Result(bool Exhausted, bool Sprinting);

    public static Result Step(bool exhausted, bool wantSprint, float stamina, float resumeAt)
    {
        // A non-finite pool is not "some stamina": refuse, and latch, rather than sprint on a NaN.
        if (!float.IsFinite(stamina) || stamina <= 0f)
        {
            return new Result(true, false);
        }

        if (exhausted && !wantSprint && stamina >= resumeAt)
        {
            exhausted = false;
        }

        return new Result(exhausted, wantSprint && !exhausted);
    }
}
