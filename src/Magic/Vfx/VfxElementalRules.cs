using System;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The counts and sizes the fire, frost and lightning special cases are held to. Pure, so what a
/// tier is asked to draw is pinned by tests and cannot drift: the leanest tier never gains a draw
/// from the crystals of the Glacial Bulwark, the snow of a Blizzard or the arcs left on a struck
/// body.
/// </summary>
public static class VfxElementalRules
{
    /// <summary>The fewest and most crystals a Glacial Bulwark is ever built from.</summary>
    public const int MinPrisms = 3;

    public const int MaxPrisms = 7;

    /// <summary>The steepest a crystal leans off upright, degrees.</summary>
    public const float MaxPrismLean = 14f;

    /// <summary>How deep a crystal is against its width, and the deepest it is ever drawn (metres):
    /// deep enough to be a prism from the side, not so deep it stands clear of the wall it marks.</summary>
    public const float PrismDepthRatio = 0.7f;

    public const float PrismMaxDepth = 0.7f;

    /// <summary>The height of the sheet of ice between the crystals, against the wall's height. It
    /// stands well under the crystals so the top edge of the wall is theirs, broken and uneven.</summary>
    public const float PrismWebHeight = 0.62f;

    /// <summary>Seconds arcs are left crackling on a body a bolt of lightning struck.</summary>
    public const float StruckCrackleSeconds = 1.4f;

    /// <summary>Seconds frost takes to creep out under what a Rime Shard struck. The patch holds
    /// and fades over 2.4 times this, so it is on the floor for about two seconds.</summary>
    public const float RimeFrostSeconds = 0.85f;

    /// <summary>The largest a puff of a Blizzard's ground mist is drawn against the preset's own
    /// size (1.6 m at its widest): at this its top stays under the height of a chest.</summary>
    public const float ZoneMistScale = 1f;

    /// <summary>The size of the thunderclap at the end of a Thunder Step against the reach of its
    /// hit. A lightning blast's first ring runs to 1.2 times its size, so this lands that ring on
    /// the reach of the hit.</summary>
    public const float DashClapScale = 0.85f;

    /// <summary>How many crystals a tier stands along the wall. The leanest tier's three take the
    /// place of the two flat spires and the drifting glints it drew before: no draw is added.</summary>
    public static int PrismCount(VfxTier tier) => tier switch
    {
        VfxTier.Performance => MinPrisms,
        VfxTier.Low => 4,
        VfxTier.Medium => 5,
        VfxTier.High => 6,
        _ => MaxPrisms,
    };

    /// <summary>
    /// A crystal's height against the wall's. <paramref name="across"/> is where it stands along the
    /// wall (-0.5 at one end to 0.5 at the other) and <paramref name="roll"/> a number in 0..1: the
    /// middle of the wall stands tallest, the ends shortest, and no two the same.
    /// </summary>
    public static float PrismHeight(float across, float roll)
    {
        float middle = 1f - Math.Clamp(Math.Abs(across) * 2f, 0f, 1f);
        return 0.68f + (0.34f * middle) + (0.26f * Math.Clamp(roll, 0f, 1f));
    }

    /// <summary>How many emitters of driven snow a Blizzard stacks. One holds only so many streaks;
    /// the leanest tier draws the one.</summary>
    public static int SnowLayers(VfxTier tier) => tier switch
    {
        VfxTier.Medium or VfxTier.High => 2,
        VfxTier.Ultra => 3,
        _ => 1,
    };

    /// <summary>Whether a tier leaves arcs crackling on a struck body. Not the leanest: there a
    /// strike is its forks and its sparks, and nothing is added to them.</summary>
    public static bool StruckCrackle(VfxTier tier) => tier > VfxTier.Performance;
}
