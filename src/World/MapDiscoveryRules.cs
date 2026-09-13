using System;

namespace Embervale.World;

/// <summary>
/// How far away a place can be discovered from, and whether the land has to let you see it
/// (2026-09 world rebuild). Engine-free, so it is unit-testable.
///
/// ⚠️ Discovery used to be two things: a 20 m walk-up radius, or "reveal with the cell" — which, once
/// every cell of a region was resident, meant every town in the realm appeared on the map the moment
/// the player loaded in. In a realm eight times the size that is a list, not a map. A place now reveals
/// itself the way it would to a traveller: a town when you can SEE it across the country, a ruin or a
/// mine when you are close enough to make it out, a shop when you walk up to its counter.
/// </summary>
public static class MapDiscoveryRules
{
    /// <summary>Walk-up radius: a counter, a stall, a service. No line of sight needed.</summary>
    public const float WalkUpRadius = 20f;

    /// <summary>A settlement's skyline — roofs, smoke, a keep — reads across open country.</summary>
    public const float PrimarySightRadius = 190f;

    /// <summary>A mine, a ruin, a waystone, a shrine: something you make out at a field's distance.</summary>
    public const float SecondarySightRadius = 80f;

    /// <summary>The distance at the end of a sight line where a place's own hill or wall may rise.</summary>
    public const float NearIgnore = 12f;

    /// <summary>How high above its marker a place's silhouette stands, for the line-of-sight test.
    /// A town shows its roofline over a rise that would hide a waystone.</summary>
    public static float SilhouetteHeight(MapTier tier) => tier switch
    {
        MapTier.Primary => 8f,
        MapTier.Secondary => 3f,
        _ => 1.5f,
    };

    /// <summary>The discovery radius: the authored one when positive, else the tier's default.</summary>
    public static float RadiusFor(MapTier tier, float authored) =>
        authored > 0f ? authored : tier switch
        {
            MapTier.Primary => PrimarySightRadius,
            MapTier.Secondary => SecondarySightRadius,
            _ => WalkUpRadius,
        };

    /// <summary>Anything seen from further than walk-up range must actually be in view.</summary>
    public static bool NeedsLineOfSight(float radius) => radius > WalkUpRadius;

    /// <summary>
    /// True when the straight line from <paramref name="eye"/> to the silhouette top clears the ground
    /// sampled by <paramref name="groundAt"/>. Samples every <paramref name="step"/> metres and ignores
    /// the last <see cref="NearIgnore"/> metres, where a place's own hill or wall is allowed to rise.
    /// </summary>
    public static bool HasLineOfSight(
        (float X, float Y, float Z) eye, (float X, float Y, float Z) target,
        Func<float, float, float> groundAt, float step = 6f)
    {
        float dx = target.X - eye.X;
        float dz = target.Z - eye.Z;
        float length = MathF.Sqrt((dx * dx) + (dz * dz));
        if (length <= NearIgnore)
        {
            return true;
        }
        int samples = (int)((length - NearIgnore) / step);
        for (int i = 1; i <= samples; i++)
        {
            float t = i * step / length;
            float lineY = eye.Y + ((target.Y - eye.Y) * t);
            if (groundAt(eye.X + (dx * t), eye.Z + (dz * t)) > lineY)
            {
                return false;
            }
        }
        return true;
    }
}
