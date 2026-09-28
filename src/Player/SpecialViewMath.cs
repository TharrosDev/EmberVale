using System;
using Godot;

namespace Embervale.Player;

/// <summary>Which special view is holding the camera. Ordered by priority, highest first.</summary>
public enum SpecialView
{
    None,
    Focus,
    Dialogue,
    BossIntro,
    Death,
}

/// <summary>
/// One special view's shape: how far it pulls the camera, how much it narrows the field of view,
/// how far it drops the eye, and how far it may turn the camera toward its subject. Every value is
/// the amount at full strength; the layer scales it by an eased 0..1 envelope.
///
/// <para>⚠️ <b>Every angle here is a ceiling, not a target.</b> The aim ray reads the camera's
/// forward, so a look-at that turned the camera all the way to its subject would move the crosshair
/// off whatever the player was aiming at. A view leans toward its subject by at most
/// <see cref="MaxYaw"/> / <see cref="MaxPitch"/> and the player's own look input cancels it.</para>
/// </summary>
public readonly record struct ViewRecipe(
    float DistanceScale,
    float FovDegrees,
    float DropMetres,
    float MaxYaw,
    float MaxPitch,
    float InSeconds,
    float OutSeconds)
{
    /// <summary>A gentle push-in on whoever is speaking. Slow in, slightly quicker out, so leaving
    /// a conversation never feels like being dragged back.</summary>
    public static readonly ViewRecipe Dialogue = new(
        0.82f, -3f, 0f, SpecialViewMath.Rad(14f), SpecialViewMath.Rad(7f), 0.9f, 0.7f);

    /// <summary>A boss's entrance: a touch further back so the whole of it fits, a tighter lens for
    /// weight, and a wider lean toward it than any other view because the player is held for it.</summary>
    public static readonly ViewRecipe BossIntro = new(
        1.10f, -5f, 0f, SpecialViewMath.Rad(12f), SpecialViewMath.Rad(8f), 0.7f, 1.1f);

    /// <summary>The player falling: the camera drifts back and sinks. Slow both ways; a fast drop is
    /// a cut in all but name.</summary>
    public static readonly ViewRecipe Death = new(
        1.35f, -4f, -0.3f, 0f, 0f, 1.4f, 2.2f);

    /// <summary>Looking at a shrine, a door or a lever: barely there. Distance and FOV are untouched.</summary>
    public static readonly ViewRecipe Focus = new(
        1f, 0f, 0f, SpecialViewMath.Rad(3.5f), SpecialViewMath.Rad(2f), 0.5f, 0.6f);

    public static ViewRecipe For(SpecialView view) => view switch
    {
        SpecialView.Dialogue => Dialogue,
        SpecialView.BossIntro => BossIntro,
        SpecialView.Death => Death,
        SpecialView.Focus => Focus,
        _ => new ViewRecipe(1f, 0f, 0f, 0f, 0f, 0.5f, 0.5f),
    };

    /// <summary>The distance multiplier at an already-eased <paramref name="eased"/> strength.</summary>
    public float Distance(float eased) => 1f + ((DistanceScale - 1f) * eased);

    /// <summary>Advances a linear 0..1 amount toward on or off at this recipe's in / out speed.</summary>
    public float Advance(float amount, bool on, float dt) =>
        SpecialViewMath.Advance(amount, on, dt, InSeconds, OutSeconds);
}

/// <summary>
/// Pure arithmetic behind <see cref="CameraDirectorLayer"/>. Engine-free (Godot value types only) so
/// the rules that decide whether the camera leans, how far and when it lets go are unit-testable
/// headlessly, the same way <see cref="CameraRigMath"/> is.
/// </summary>
public static class SpecialViewMath
{
    /// <summary>The mounted pull-back: a rider is faster and wider than a walker, so the camera sits
    /// back a little further and lets the horse fill the frame less.</summary>
    public const float MountedDistanceScale = 1.18f;

    /// <summary>Seconds the mounted pull-back takes to arrive or leave.</summary>
    public const float MountedBlendSeconds = 0.6f;

    /// <summary>A boss further than this from the player when its encounter starts is not the fight
    /// the player is in: no framing for it.</summary>
    public const float BossFramingRange = 80f;

    /// <summary>Seconds the death view holds before it lets go.</summary>
    public const float DeathHoldSeconds = 1.6f;

    /// <summary>Seconds an interactable must stay in focus before the camera leans toward it, so
    /// sweeping the crosshair across a room nudges nothing.</summary>
    public const float FocusDwellSeconds = 0.3f;

    /// <summary>Pixels of mouse motion in one sample that read as a full-scale flick (magnitude 1).</summary>
    public const float MouseFullPixels = 16f;

    /// <summary>Look magnitude that counts as the player steering: 4 px of mouse or a quarter of stick
    /// travel, just above the look deadzone. Below it is hand tremor, not intent.</summary>
    public const float SteeringThreshold = 0.25f;

    /// <summary>An interactable smaller than this in every axis (loot on the ground) gets no lean:
    /// it would tilt the camera toward the floor for every drop.</summary>
    public const float MinFocusExtent = 0.6f;

    /// <summary>Beyond this yaw away from the camera's forward the subject counts as behind the
    /// player and the look-at is faded out rather than swinging the camera round.</summary>
    public const float BehindFadeStart = 80f * (MathF.PI / 180f);

    public const float BehindFadeEnd = 130f * (MathF.PI / 180f);

    public static float Rad(float degrees) => degrees * (MathF.PI / 180f);

    /// <summary>Smoothstep, clamped: the ease every view's strength goes through.</summary>
    public static float Ease(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        return x * x * (3f - (2f * x));
    }

    /// <summary>Moves a linear 0..1 amount toward on / off, crossing the whole range in
    /// <paramref name="inSeconds"/> going up and <paramref name="outSeconds"/> going down. A
    /// non-positive duration snaps.</summary>
    public static float Advance(float amount, bool on, float dt, float inSeconds, float outSeconds)
    {
        float seconds = on ? inSeconds : outSeconds;
        float target = on ? 1f : 0f;
        if (seconds <= 0f)
        {
            return target;
        }

        float step = Math.Max(dt, 0f) / seconds;
        return on ? Math.Min(1f, amount + step) : Math.Max(0f, amount - step);
    }

    /// <summary>The single view that wins this frame. Death outranks a boss's entrance, which
    /// outranks a conversation, which outranks a lean toward a door.</summary>
    public static SpecialView Choose(bool death, bool bossIntro, bool dialogue, bool focus)
    {
        if (death)
        {
            return SpecialView.Death;
        }

        if (bossIntro)
        {
            return SpecialView.BossIntro;
        }

        if (dialogue)
        {
            return SpecialView.Dialogue;
        }

        return focus ? SpecialView.Focus : SpecialView.None;
    }

    /// <summary>Whether a boss encounter that just started is close enough to frame.</summary>
    public static bool BossFramingApplies(float distanceToPlayer) =>
        distanceToPlayer >= 0f && distanceToPlayer <= BossFramingRange;

    /// <summary>How long the boss framing holds: the fight's own intro lock, kept inside a sane
    /// window so a data value of 0 still gives a beat and 30 does not hold the lens for half a minute.</summary>
    public static float BossHoldSeconds(float introLockSeconds) => Math.Clamp(introLockSeconds, 1.5f, 4f);

    /// <summary>Whether the death view is still holding, <paramref name="secondsSinceDeath"/> after
    /// the player fell. Negative means the player has not died.</summary>
    public static bool DeathHolding(float secondsSinceDeath) =>
        secondsSinceDeath >= 0f && secondsSinceDeath < DeathHoldSeconds;

    /// <summary>Whether an interactable gets a look-at lean: it must be a thing of some size and the
    /// camera must be in the plain exploration context (not a fight, a sprint or an aim).</summary>
    public static bool FocusLeanApplies(CameraContext context, Vector3 size) =>
        context == CameraContext.Exploration &&
        MathF.Max(size.X, MathF.Max(size.Y, size.Z)) >= MinFocusExtent;

    /// <summary>The mounted distance multiplier at a 0..1 eased mount amount.</summary>
    public static float MountedDistance(float eased) => 1f + ((MountedDistanceScale - 1f) * eased);

    /// <summary>The world point to look at for a subject's bounds: its centre on the ground plane,
    /// and <paramref name="headBias"/> of the way from its middle to its top, which lands on a
    /// person's face rather than their belt and on a dragon's chest rather than its ankles.</summary>
    public static Vector3 LookPoint(Vector3 boundsMin, Vector3 boundsMax, float headBias)
    {
        Vector3 centre = (boundsMin + boundsMax) * 0.5f;
        float bias = Math.Clamp(headBias, 0f, 1f);
        return new Vector3(centre.X, centre.Y + ((boundsMax.Y - centre.Y) * bias), centre.Z);
    }

    /// <summary>
    /// The (pitch, yaw) radians that turn a camera toward <paramref name="direction"/>, expressed in
    /// the camera's rest space (looking down -Z, +X right, +Y up), soft-limited to
    /// <paramref name="maxPitch"/> / <paramref name="maxYaw"/> and faded out as the subject goes behind.
    ///
    /// <para>The limit is a <c>tanh</c> rather than a clamp so a subject that walks past the edge
    /// slides the lean toward its ceiling instead of hitting a wall in it, and the result is zero for
    /// a subject dead ahead so nothing moves when there is nothing to do.</para>
    /// </summary>
    public static Vector2 LookAngles(Vector3 direction, float maxPitch, float maxYaw)
    {
        float horizontal = MathF.Sqrt((direction.X * direction.X) + (direction.Z * direction.Z));
        if (horizontal < 0.0001f && MathF.Abs(direction.Y) < 0.0001f)
        {
            return Vector2.Zero;
        }

        // Rotating (0, 0, -1) about Y by +yaw gives (-sin, 0, -cos): so a target to the camera's
        // right (+X) needs a NEGATIVE yaw, and pitch is plain elevation.
        float yaw = MathF.Atan2(-direction.X, -direction.Z);
        float pitch = MathF.Atan2(direction.Y, horizontal);

        float behind = 1f - Math.Clamp(
            (MathF.Abs(yaw) - BehindFadeStart) / (BehindFadeEnd - BehindFadeStart), 0f, 1f);
        return new Vector2(SoftLimit(pitch, maxPitch) * behind, SoftLimit(yaw, maxYaw) * behind);
    }

    /// <summary>Squashes <paramref name="value"/> smoothly into ±<paramref name="limit"/>. Near zero it
    /// is the identity, so a small offset is followed exactly and only a large one is held back.</summary>
    public static float SoftLimit(float value, float limit) =>
        limit <= 0f ? 0f : limit * MathF.Tanh(value / limit);

    /// <summary>How much steering the player is doing this frame, 0..1: the larger of the mouse
    /// (pixels moved since the last sample over a full-scale flick) and the stick's deflection.</summary>
    public static float LookMagnitude(float mousePixels, float stickDeflection)
    {
        float mouse = Math.Clamp(mousePixels / MouseFullPixels, 0f, 1f);
        return Math.Max(mouse, Math.Clamp(stickDeflection, 0f, 1f));
    }

    /// <summary>Whether <paramref name="lookMagnitude"/> (from <see cref="LookMagnitude"/>) is the
    /// player steering rather than noise.</summary>
    public static bool IsSteering(float lookMagnitude) => lookMagnitude >= SteeringThreshold;
}

/// <summary>
/// How much of a look-at lean a view still has, given that the player may take the camera back.
/// Steering drops it to zero at once; after that it either stays gone (a latch, for a view that
/// should not creep back mid-scene) or returns after a still hold.
/// </summary>
public struct LookYield
{
    /// <summary>1 = the lean is in full force, 0 = the player has taken the camera.</summary>
    public float Value;

    private float _still;

    public LookYield()
    {
        Value = 1f;
        _still = 0f;
    }

    /// <summary>Puts the yield back to full. Called when a view starts afresh.</summary>
    public void Reset()
    {
        Value = 1f;
        _still = 0f;
    }

    /// <summary>Advances one frame. <paramref name="recoverSeconds"/> at or below zero latches the
    /// yield at zero until <see cref="Reset"/>.</summary>
    public void Update(float dt, bool steering, float holdSeconds, float recoverSeconds)
    {
        if (steering)
        {
            Value = 0f;
            _still = 0f;
            return;
        }

        _still += Math.Max(dt, 0f);
        if (recoverSeconds > 0f && _still >= holdSeconds)
        {
            Value = Math.Min(1f, Value + (Math.Max(dt, 0f) / recoverSeconds));
        }
    }
}
