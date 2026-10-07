using System;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// Where the player's own casting effect sits in first person. The casting hand is below the frame
/// there and half a metre from the eye, inside the band every effect shader fades out in, so an aura
/// anchored to the hand bone is simply not drawn. In first person the player's wind-up, charge and
/// channel aura, the release flash and the start of a bolt or beam are anchored to a point fixed in
/// the view instead: low and to the left (the casting hand), far enough out to be past the near fade, and clear of the
/// crosshair. Pure, so where it lands on screen is a test.
/// </summary>
public static class VfxViewRules
{
    /// <summary>Height of the eye above a standing body's origin, metres.</summary>
    public const float EyeHeight = 1.62f;

    /// <summary>How far the camera may sit from that eye and still count as first person.</summary>
    public const float FirstPersonReach = 0.75f;

    /// <summary>The view-space offset of the first-person casting point: right, up, and (negative Z)
    /// forward of the camera.</summary>
    public static readonly Vector3 HandOffset = new(-0.52f, -0.4f, -1.3f);

    /// <summary>Whether a camera <paramref name="cameraFromEye"/> away from the caster's eye is the
    /// caster's own first-person view.</summary>
    public static bool IsFirstPerson(Vector3 cameraFromEye) =>
        cameraFromEye.LengthSquared() < FirstPersonReach * FirstPersonReach;

    /// <summary>
    /// Where a view-space <paramref name="offset"/> lands on screen: X and Y from -1 (left, bottom)
    /// to 1 (right, top), under the field of view <see cref="VfxCoverageRules"/> assumes.
    /// </summary>
    public static Vector2 Screen(Vector3 offset)
    {
        float depth = MathF.Max(0.01f, -offset.Z);
        return new Vector2(
            offset.X / (depth * VfxCoverageRules.TanHalfFov * VfxCoverageRules.Aspect),
            offset.Y / (depth * VfxCoverageRules.TanHalfFov));
    }

    /// <summary>The first-person casting point in the world, for a camera at
    /// <paramref name="cameraPosition"/> with <paramref name="cameraBasis"/>.</summary>
    public static Vector3 HandPoint(Vector3 cameraPosition, Basis cameraBasis) =>
        cameraPosition + (cameraBasis * HandOffset);
}
