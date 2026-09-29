namespace Embervale.Combat;

/// <summary>
/// Pure input-buffer rules for melee (Phase 29G, reworked in the offence pass). Godot-free so it is
/// unit-testable; <see cref="Actions.CharacterActionComponent"/> applies it.
///
/// <para><b>A buffered press is a promise, and the rules decide which promises are worth keeping.</b>
/// A press made shortly before the swing can cancel is held and fires the instant it can (so a combo
/// lands instead of dropping an input). A press made <i>far</i> from that moment is refused outright:
/// mashing through a long heavy wind-up must not queue a phantom swing that fires a second later,
/// long after the player stopped meaning it. Committing to a swing means living with it.</para>
/// </summary>
public static class AttackBuffer
{
    /// <summary>How far ahead of the cancel point a press is still accepted, in seconds. Longer than
    /// the hold window itself, because the buffer stretches to reach the cancel point.</summary>
    public const float DefaultLead = 0.28f;

    /// <summary>Slack kept past the cancel point so the press survives the frame that opens it.</summary>
    public const float Grace = 0.06f;

    /// <summary>True when a live buffer should fire: there's time left on it and we're no longer
    /// committed to the current swing.</summary>
    public static bool ShouldRelease(double bufferRemaining, bool committed) =>
        bufferRemaining > 0d && !committed;

    /// <summary>Whether a press made <paramref name="secondsUntilCancel"/> before the running action
    /// becomes cancellable is worth holding. Outside <paramref name="lead"/> it is dropped.</summary>
    public static bool Accepts(float secondsUntilCancel, float lead) => secondsUntilCancel <= lead;

    /// <summary>How long an accepted press lives: at least the authored window, and always long enough
    /// to reach the cancel point (plus <see cref="Grace"/>), so an accepted press cannot expire early.</summary>
    public static double Lifetime(float window, float secondsUntilCancel)
    {
        double toCancel = secondsUntilCancel + Grace;
        return toCancel > window ? toCancel : window;
    }
}
