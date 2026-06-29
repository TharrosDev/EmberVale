namespace Embervale.Combat;

/// <summary>
/// Pure mapping from a hit's flavour to its placeholder feedback (Phase 29C): the sound-cue id and the
/// impact-spark tint for a crit / blocked / plain hit. Godot-free so it's unit-testable; the
/// <see cref="CombatFeedbackDirector"/> consumes it.
/// </summary>
public static class CombatFx
{
    public const string CritCue = "sfx.combat.crit";
    public const string BlockCue = "sfx.combat.block";
    public const string HitCue = "sfx.combat.hit";

    /// <summary>The sound-cue id for a resolved hit (crit takes precedence over block over a plain hit).</summary>
    public static string CueId(bool isCrit, bool isBlocked) =>
        isCrit ? CritCue : isBlocked ? BlockCue : HitCue;

    /// <summary>The impact-spark tint (r,g,b 0..1): crit gold, block grey, plain hit white.</summary>
    public static (float R, float G, float B) Tint(bool isCrit, bool isBlocked)
    {
        if (isCrit)
        {
            return (1.0f, 0.82f, 0.35f); // gold
        }

        if (isBlocked)
        {
            return (0.65f, 0.68f, 0.74f); // grey
        }

        return (1.0f, 0.95f, 0.85f); // warm white
    }
}
