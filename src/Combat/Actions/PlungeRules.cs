namespace Embervale.Combat.Actions;

/// <summary>
/// Pure rules for the plunge attack: striking downward out of a jump or a fall, landing as a blow
/// that carries the drop. Godot-free; <see cref="CharacterActionComponent"/> applies them.
///
/// <para>The dive is the commitment: once it starts the actor is falling at
/// <see cref="DiveSpeed"/> with no steering, and the blow lands wherever the ground is. A plunge
/// that misses leaves the actor in its long recovery, which is the price of the damage.</para>
/// </summary>
public static class PlungeRules
{
    /// <summary>Lowest drop, in metres, worth a plunge. A plain jump peaks near a metre, so this is
    /// reachable from a normal jump and refuses a step off a kerb.</summary>
    public const float MinHeight = 0.7f;

    /// <summary>Downward speed of the dive, m/s.</summary>
    public const float DiveSpeed = 18f;

    /// <summary>Highest drop that adds damage; a plunge from a cliff is not a bigger plunge than one
    /// from a rooftop.</summary>
    public const float MaxScaledHeight = 8f;

    /// <summary>Extra damage multiplier at the top of the range.</summary>
    public const float MaxBonus = 1f;

    /// <summary>Whether a drop of <paramref name="height"/> metres allows a plunge at all.</summary>
    public static bool CanStart(float height, bool grounded, bool mounted) =>
        !grounded && !mounted && height >= MinHeight;

    /// <summary>The damage multiplier for a plunge begun <paramref name="height"/> metres up: 1 at the
    /// minimum, <c>1 + MaxBonus</c> at <see cref="MaxScaledHeight"/>.</summary>
    public static float HeightScale(float height)
    {
        if (height <= MinHeight)
        {
            return 1f;
        }

        float t = (height - MinHeight) / (MaxScaledHeight - MinHeight);
        return 1f + ((t >= 1f ? 1f : t) * MaxBonus);
    }
}
