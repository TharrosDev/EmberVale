using System;

namespace Embervale.UI;

/// <summary>
/// How far away a map location is, and which way (Phase 39.5A) — the brief's §23, "do not force the
/// player to estimate distance visually".
///
/// Engine-free and returns <em>data, not a sentence</em>: a whole number of metres and a locale key
/// for the compass point. The screen composes them through <c>Loc.TF</c>, because "420 m northeast"
/// is not a string that survives translation as one unit — word order and pluralisation both move.
///
/// The bearing comes from <see cref="CompassMath.Angle"/> rather than a second implementation, so
/// the map and the HUD compass strip can never disagree about which way north is.
/// </summary>
public static class MapDistance
{
    /// <summary>Inside this many metres a destination is "here" rather than in a direction. Standing
    /// on top of a marker and being told it is 2 m north-east is noise.</summary>
    public const float HereRadius = 6f;

    private static readonly string[] Points =
    {
        "map.dir.north", "map.dir.northeast", "map.dir.east", "map.dir.southeast",
        "map.dir.south", "map.dir.southwest", "map.dir.west", "map.dir.northwest",
    };

    /// <summary>Planar distance in metres between two world (X, Z) points.</summary>
    public static float Metres(float fromX, float fromZ, float toX, float toZ)
    {
        float dx = toX - fromX;
        float dz = toZ - fromZ;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>
    /// Locale key for the compass point of a heading, to the nearest eighth.
    ///
    /// ⚠️ The rounding has to wrap: a bearing just west of due north rounds to index 8, which is
    /// north again and not off the end of the array.
    /// </summary>
    public static string DirectionKey(float dx, float dz)
    {
        float angle = CompassMath.Angle(dx, dz);          // 0 = North, +π/2 = East
        const float step = MathF.PI * 2f / 8f;
        int index = (int)MathF.Round(angle / step);
        index = ((index % 8) + 8) % 8;                    // wrap, and handle negatives
        return Points[index];
    }

    /// <summary>
    /// Distance and direction from one world point to another. <c>DirectionKey</c> is empty when the
    /// two are within <see cref="HereRadius"/>, which the screen renders as "here".
    /// </summary>
    public static (int Metres, string DirectionKey) Describe(
        float fromX, float fromZ, float toX, float toZ)
    {
        float metres = Metres(fromX, fromZ, toX, toZ);
        if (metres < HereRadius)
        {
            return (0, string.Empty);
        }

        return ((int)MathF.Round(metres), DirectionKey(toX - fromX, toZ - fromZ));
    }
}
