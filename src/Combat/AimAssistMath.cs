using System;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// The bow's soft pull toward a target near the crosshair. Pure, so what the aid does (and above all
/// that it does <i>nothing</i> at strength 0) is pinned by tests rather than by feel.
///
/// <para>It is an aid, not an aimbot: it bends the <i>arrow</i>, never the camera, only inside a small
/// cone around the crosshair, and its pull fades to nothing at the cone's edge, so a shot that was
/// clearly aimed elsewhere stays where it was aimed.</para>
/// </summary>
public static class AimAssistMath
{
    /// <summary>Cone half-angle, in degrees, at full strength.</summary>
    public const float MaxConeDegrees = 7f;

    /// <summary>A locked-on target is allowed a wider cone: the player has said what they mean.</summary>
    public const float LockedConeFactor = 1.6f;

    /// <summary>The share of the way to the target the pull reaches at full strength, dead centre.</summary>
    public const float MaxPull = 0.85f;

    /// <summary>A drawn shot is steadier, so the aid leans on it harder; a snap shot gets this share.</summary>
    public const float SnapAssistShare = 0.5f;

    /// <summary>The cone half-angle in radians for <paramref name="strength"/> 0..1; 0 means no aid.</summary>
    public static float ConeRadians(float strength, bool locked)
    {
        float s = Math.Clamp(strength, 0f, 1f);
        float cone = Mathf.DegToRad(MaxConeDegrees) * s;
        return locked ? cone * LockedConeFactor : cone;
    }

    /// <summary>How much of the way to the target the aim moves, 0..1.</summary>
    public static float Weight(float angle, float strength, float charge, bool locked)
    {
        float cone = ConeRadians(strength, locked);
        if (cone <= 0f || angle >= cone)
        {
            return 0f;
        }

        float edge = angle / cone;
        float steadiness = SnapAssistShare + ((1f - SnapAssistShare) * Math.Clamp(charge, 0f, 1f));
        return Math.Clamp(strength, 0f, 1f) * MaxPull * steadiness * (1f - (edge * edge));
    }

    /// <summary>
    /// The aim direction after the pull. Both directions must be unit length. Returns
    /// <paramref name="aim"/> unchanged when the target is outside the cone or strength is 0.
    /// </summary>
    public static Vector3 Pull(Vector3 aim, Vector3 toTarget, float strength, float charge, bool locked)
    {
        float angle = aim.AngleTo(toTarget);
        float weight = Weight(angle, strength, charge, locked);
        if (weight <= 0f)
        {
            return aim;
        }

        return aim.Lerp(toTarget, weight).Normalized();
    }
}
