namespace Embervale.Combat;

/// <summary>
/// Pure parry-timing logic (Phase 29F). Godot-free so the window is unit-testable;
/// <see cref="CombatComponent"/> applies it. A hit landing within <c>window</c> seconds of the guard
/// being raised is parried; otherwise it's a (chip) block.
/// </summary>
public static class Parry
{
    /// <summary>True if a guard raised <paramref name="blockElapsed"/> seconds ago parries a hit now.</summary>
    public static bool IsParry(float blockElapsed, float window) =>
        blockElapsed >= 0f && blockElapsed <= window;
}
