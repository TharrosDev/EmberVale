namespace Embervale.Combat;

/// <summary>
/// Pure input-buffer logic for the melee FSM (Phase 29G). Godot-free so it's unit-testable;
/// <see cref="MeleeWeaponComponent"/> applies it. A buffered attack is released only once the swing is out
/// of its committed (un-cancellable) window, turning an early press into a landed combo hit instead of a
/// dropped input.
/// </summary>
public static class AttackBuffer
{
    /// <summary>True when a live buffer should fire: there's time left on it and we're no longer
    /// committed to the current swing.</summary>
    public static bool ShouldRelease(double bufferRemaining, bool committed) =>
        bufferRemaining > 0d && !committed;
}
