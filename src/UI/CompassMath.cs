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
}
