using System;
using Embervale.Combat;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Pure rules behind <see cref="CameraOcclusion"/>: when it runs, the volume it looks through, what may
/// be faded and how fast. Engine-free (Godot value types only) so they are unit-testable headlessly.
/// </summary>
public static class CameraOcclusionMath
{
    /// <summary>The camera must sit at least this far from the pivot before first person counts as
    /// pulled back. Under it the camera is at the eye and nothing can be between it and the body.</summary>
    public const float MinPullback = 0.6f;

    /// <summary>Radius of the capsule looked through, in metres: a little wider than the player so a
    /// prop grazing the sight line still thins out instead of clipping the silhouette.</summary>
    public const float ProbeRadius = 0.4f;

    /// <summary>How far short of the target the volume stops, so a companion standing beside the
    /// player or a rail they lean on is not counted as being in the way.</summary>
    public const float EndMargin = 0.5f;

    /// <summary>The segment must be at least this long to be worth a query.</summary>
    public const float MinSegment = 0.4f;

    /// <summary>Seconds a prop takes to reach full fade and to come back. Quick in so the view
    /// clears before the player notices it was blocked, slower out so a prop does not flicker back
    /// as the camera drifts past its edge.</summary>
    public const float FadeInSeconds = 0.15f;

    public const float FadeOutSeconds = 0.3f;

    /// <summary>Instance transparency at full fade. Not 1: a fully invisible prop stops being a
    /// landmark, and the dither leaves enough of it to read as a thing that is there.</summary>
    public const float MaxTransparency = 0.8f;

    /// <summary>A mesh larger than this in any axis is never faded when it belongs to no entity: it is
    /// terrain, a floor or a merged block of architecture, and thinning it would thin the world.</summary>
    public const float MaxPropExtent = 12f;

    /// <summary>The same limit for a mesh that belongs to an entity (a house, a dragon). Generous, but
    /// it still keeps a ground-sized entity from being treated as a prop.</summary>
    public const float MaxEntityExtent = 30f;

    /// <summary>Whether the fade runs this frame: the setting is on, and the camera is in third
    /// person or has been pulled back from the eye.</summary>
    public static bool Applies(bool settingOn, bool firstPerson, float pullback) =>
        settingOn && (!firstPerson || pullback >= MinPullback);

    /// <summary>Whether a collider on <paramref name="collisionLayer"/> may be faded at all. Anything
    /// on the camera-blocker layer is a wall the wall spring already keeps the camera in front of,
    /// and it must stay solid to the eye too.</summary>
    public static bool IsFadeableLayer(uint collisionLayer) =>
        (collisionLayer & CombatLayers.CameraBlocker) == 0u;

    /// <summary>Whether a mesh of <paramref name="extent"/> (its longest bounding-box side) may be faded.
    /// An entity's meshes get the wider limit; a mesh belonging to nothing gets the narrow one.</summary>
    public static bool IsFadeableMesh(float extent, bool ownedByEntity) =>
        extent <= (ownedByEntity ? MaxEntityExtent : MaxPropExtent);

    /// <summary>
    /// The capsule to look through between the camera and the point it must see. Returns false when
    /// there is no room for one. The capsule's axis runs from <paramref name="camera"/> to
    /// <see cref="EndMargin"/> short of <paramref name="target"/>; <paramref name="height"/> is the
    /// shape's overall height (both hemispheres included, as Godot defines it).
    /// </summary>
    public static bool TrySegment(
        Vector3 camera, Vector3 target, out Vector3 centre, out float height, out Vector3 axis)
    {
        Vector3 to = target - camera;
        float length = to.Length();
        float span = length - EndMargin;
        if (length < 0.0001f || span < MinSegment)
        {
            centre = Vector3.Zero;
            height = 0f;
            axis = Vector3.Up;
            return false;
        }

        axis = to / length;
        centre = camera + (axis * (span * 0.5f));
        height = span + (2f * ProbeRadius);
        return true;
    }

    /// <summary>A basis that turns a capsule's local Y onto <paramref name="axis"/>. Straight up is
    /// the identity and straight down a half turn, so a camera directly above the player is not a
    /// degenerate rotation.</summary>
    public static Basis CapsuleBasis(Vector3 axis)
    {
        if (axis.Y > 0.9999f)
        {
            return Basis.Identity;
        }

        if (axis.Y < -0.9999f)
        {
            return new Basis(Vector3.Right, MathF.PI);
        }

        return new Basis(new Quaternion(Vector3.Up, axis));
    }

    /// <summary>Advances a 0..1 fade toward <paramref name="wanted"/>.</summary>
    public static float StepFade(float fade, bool wanted, float dt) =>
        SpecialViewMath.Advance(fade, wanted, dt, FadeInSeconds, FadeOutSeconds);

    /// <summary>The instance transparency for a mesh whose authored value is <paramref name="original"/>
    /// at fade <paramref name="fade"/>. Eased, never below the original, and exactly the original at
    /// zero so releasing a prop leaves nothing behind.</summary>
    public static float TransparencyAt(float original, float fade)
    {
        float full = MathF.Max(original, MaxTransparency);
        return original + ((full - original) * SpecialViewMath.Ease(fade));
    }

    /// <summary>Whether a fade entry has finished and can be released: it is no longer wanted and it
    /// has faded all the way back.</summary>
    public static bool IsReleased(float fade, bool wanted) => !wanted && fade <= 0f;
}
