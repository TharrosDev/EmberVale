namespace Embervale.Enemies;

/// <summary>
/// Whether a territorial creature has been drawn too far from its ground (Phase 35D), in the shape of
/// <see cref="DragonMelee"/>, <see cref="FlightDecision"/> and <see cref="BreathWindow"/> — pure and
/// Godot-free, so the one rule that decides whether a world boss can be kited across the map is
/// testable on its own.
///
/// Before this, <see cref="EnemyAIComponent"/> had no leash at all: <c>_home</c> was read only by
/// patrol and retreat, and combat chased until line of sight broke. A flying dragon would have
/// followed the player out of Frostfang Reach entirely.
///
/// The band is hysteretic on purpose. A single threshold makes a creature hovering on the boundary
/// flicker between chasing and going home every frame; it must travel back a real distance before it
/// will commit to a fight again.
/// </summary>
public static class TerritoryLeash
{
    /// <summary>Fraction of the radius it must return within before it re-engages.</summary>
    public const float ReturnFraction = 0.75f;

    /// <summary>
    /// Whether to break off and go home.
    /// </summary>
    /// <param name="distanceFromHome">How far the creature currently is from where it started.</param>
    /// <param name="radius">Its territory. <c>0</c> — every profile before the dragon — never leashes.</param>
    /// <param name="returning">Whether it is already on its way home; widens the band it must
    /// re-enter before it will turn and fight again.</param>
    public static bool ShouldBreakOff(float distanceFromHome, float radius, bool returning)
    {
        if (radius <= 0f)
        {
            return false;
        }

        return returning
            ? distanceFromHome > radius * ReturnFraction
            : distanceFromHome > radius;
    }
}
