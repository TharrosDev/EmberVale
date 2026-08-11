using System;

namespace Embervale.UI;

/// <summary>
/// The pure maths behind the Phase 25F HUD compass strip. Godot-free so it is unit-testable: the
/// <see cref="CompassStrip"/> reads the player's facing and target world positions, but the angle
/// arithmetic — heading, bearing, the wrapped relative angle and its position on the strip — lives
/// here. All angles are radians.
///
/// Convention: North is <c>-Z</c> and the angle increases clockwise toward <c>+X</c> (East), so a
/// heading and a bearing share one frame and their wrapped difference is the on-strip angle
/// (positive = to the player's right).
/// </summary>
public static class CompassMath
{
    /// <summary>Wraps an angle into (-π, π].</summary>
    public static float WrapPi(float a)
    {
        const float tau = MathF.PI * 2f;
        a %= tau;
        if (a <= -MathF.PI)
        {
            a += tau;
        }
        else if (a > MathF.PI)
        {
            a -= tau;
        }

        return a;
    }

    /// <summary>Compass angle of a planar direction: 0 = North (-Z), +π/2 = East (+X).</summary>
    public static float Angle(float dx, float dz) => MathF.Atan2(dx, -dz);

    /// <summary>Heading from a forward vector (the player's -Z facing supplies fx/fz directly).</summary>
    public static float HeadingFromForward(float fx, float fz) => Angle(fx, fz);

    /// <summary>Bearing from the player to a target offset (dx, dz = target - player).</summary>
    public static float BearingTo(float dx, float dz) => Angle(dx, dz);

    /// <summary>Signed relative angle of a bearing against the heading, wrapped to (-π, π].
    /// Positive = to the player's right.</summary>
    public static float Relative(float bearing, float heading) => WrapPi(bearing - heading);

    /// <summary>True when a relative angle falls inside the ±<paramref name="fov"/> strip window.</summary>
    public static bool InView(float relAngle, float fov) => MathF.Abs(relAngle) <= fov;

    /// <summary>Horizontal pixel offset from strip centre for a relative angle (right = +).</summary>
    public static float StripOffset(float relAngle, float fov, float halfWidth) =>
        (relAngle / fov) * halfWidth;

    /// <summary>
    /// The eight compass point locale keys, in bearing order from North, clockwise.
    ///
    /// ⚠️ <b>Declared as a set, not built from a format string</b> (invariant 26). A key assembled at
    /// runtime is invisible to every data-driven check this repo has: no <c>.tres</c> mentions it and
    /// no database walk can reach it, which is how <c>map.category.crafting</c> shipped missing and
    /// showed the player a raw key in three places. Exposing the array is what lets
    /// <c>ContentValidator</c> enumerate exactly what <see cref="CardinalKey"/> can return.
    /// </summary>
    public static readonly string[] CardinalKeys =
    {
        "hud.compass.n", "hud.compass.ne", "hud.compass.e", "hud.compass.se",
        "hud.compass.s", "hud.compass.sw", "hud.compass.w", "hud.compass.nw",
    };

    /// <summary>The locale key for the compass point nearest <paramref name="bearing"/>. Every
    /// return value is an element of <see cref="CardinalKeys"/>, for any input including NaN-free
    /// extremes and unwrapped angles.</summary>
    public static string CardinalKey(float bearing)
    {
        const float tau = MathF.PI * 2f;
        const float sector = tau / 8f;

        // +half a sector then floor: the North bucket straddles 0, so rounding to the nearest
        // multiple of 45° is a shift, not a truncation. Truncating puts due North in the NE bucket.
        float turns = (bearing + (sector * 0.5f)) / tau;
        int index = (int)MathF.Floor((turns - MathF.Floor(turns)) * 8f);
        return CardinalKeys[Math.Clamp(index, 0, 7)];
    }

    /// <summary>
    /// A distance for the player to read: whole metres up to a kilometre, then one decimal of a
    /// kilometre. Returns the number and the unit's locale key separately so the caller formats it
    /// through <c>Loc</c> rather than concatenating a unit into a string (§46).
    /// </summary>
    public static (string Value, string UnitKey) Distance(float metres)
    {
        if (metres < 0f)
        {
            metres = 0f;
        }

        return metres < 1000f
            ? (MathF.Round(metres).ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                "hud.unit.metres")
            : ((metres / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                "hud.unit.kilometres");
    }
}
