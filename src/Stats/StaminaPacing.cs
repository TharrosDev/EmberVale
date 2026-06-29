namespace Embervale.Stats;

/// <summary>
/// Pure stamina-pacing rule (Phase 29I, the anti-mash lever from DESIGN §1.4/§1.6). Godot-free so it's
/// unit-testable; <see cref="StatsComponent"/> applies it. Stamina regen is <b>paused under load</b>: it
/// only ticks once the player has gone <c>delay</c> seconds without spending. Mashing keeps resetting that
/// timer, so "attack, attack, attack" drains to empty, while "read, punish, recover" lets it refill.
/// </summary>
public static class StaminaPacing
{
    /// <summary>True once enough idle time has passed since the last spend for stamina to regen again.</summary>
    public static bool CanRegen(double idleElapsed, float delay) => idleElapsed >= delay;
}
