using Godot;

namespace Embervale.Magic;

/// <summary>
/// The pure containment rule behind a <see cref="SpellDelivery.Cone"/> cast (Phase 35C), in the shape
/// of <see cref="Embervale.Enemies.PackFlank"/>, <see cref="Embervale.Enemies.FlightDecision"/> and
/// <see cref="Embervale.Movement.PathSteering"/> — no nodes, no physics, so the geometry that decides
/// whether standing behind a dragon saves you is unit-testable on its own.
///
/// A cone is a sphere query the caller has already run, narrowed by one angle test. Keeping the two
/// apart is what let <see cref="SpellResolver.Sweep"/> reuse <see cref="SpellResolver.Detonate"/>'s
/// body instead of becoming a second resolver.
/// </summary>
public static class SpellCone
{
    /// <summary>
    /// Whether <paramref name="point"/> lies inside the cone with its apex at <paramref name="origin"/>,
    /// opening along <paramref name="direction"/> to a full width of <paramref name="angleDegrees"/>
    /// and a reach of <paramref name="length"/> metres.
    /// </summary>
    public static bool Contains(
        Vector3 origin, Vector3 direction, float angleDegrees, float length, Vector3 point)
    {
        if (angleDegrees <= 0f || length <= 0f)
        {
            return false;
        }

        Vector3 offset = point - origin;
        float distance = offset.Length();
        if (distance > length)
        {
            return false;
        }

        // The apex itself is inside any cone — and it is where a zero-length offset would make the
        // angle test meaningless.
        if (distance < 0.0001f)
        {
            return true;
        }

        if (direction.LengthSquared() < 0.0001f)
        {
            return false;
        }

        // The authored angle is the cone's full width, so a target may sit up to half of it off-axis.
        //
        // ⚠️ The epsilon is what makes "the boundary is inclusive" TRUE RATHER THAN LUCKY. The off-axis
        // angle is reconstructed through a normalize and an acos, so a point built to sit at exactly
        // half the angle arrives as 30.000002° about as often as 29.999998° — and which one depends on
        // the platform's libm, not on the geometry. This passed on Windows and failed on Linux for
        // three years' worth of the same input; the CI run that first executed these tests on Linux is
        // what surfaced it. A thousandth of a degree is far below anything a cone can express and far
        // above the error being corrected for.
        const float boundaryEpsilon = 0.001f;
        float offAxis = Mathf.RadToDeg(direction.Normalized().AngleTo(offset / distance));
        return offAxis <= (angleDegrees * 0.5f) + boundaryEpsilon;
    }
}
