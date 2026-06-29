namespace Embervale.Combat;

/// <summary>
/// Pure dodge-roll timing/gating logic (Phase 29E). Godot-free so the i-frame window and start conditions
/// are unit-testable; <see cref="DodgeComponent"/> drives the roll and toggles invulnerability from these.
/// </summary>
public static class Dodge
{
    /// <summary>True while the roll's invulnerability window is open: <paramref name="elapsed"/> in
    /// [<paramref name="iframeStart"/>, <paramref name="iframeEnd"/>).</summary>
    public static bool IsInvulnerable(float elapsed, float iframeStart, float iframeEnd) =>
        elapsed >= iframeStart && elapsed < iframeEnd;

    /// <summary>Whether a dodge may start: grounded, enough stamina, not already rolling, not staggered.</summary>
    public static bool CanStart(bool grounded, float stamina, float cost, bool rolling, bool staggered) =>
        grounded && stamina >= cost && !rolling && !staggered;
}
