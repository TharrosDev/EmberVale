using System;
using Godot;

namespace Embervale.Player;

/// <summary>
/// One layer's contribution to the camera this frame. The rig sums every layer's nudge on top of
/// the profile-driven rest pose and is the only thing that writes the camera transform.
///
/// <para><see cref="Offset"/> is metres in the camera's local rest space. <see cref="Euler"/> is
/// radians (pitch, yaw, roll) applied to the camera node; the aim ray reads the camera's forward, so
/// a layer that nudges pitch or yaw is moving the crosshair and should keep it small — roll is free.
/// <see cref="FovOffset"/> is degrees and <see cref="DistanceScale"/> multiplies the third-person
/// pull-back (1 = no change). The rig clamps the sum.</para>
/// </summary>
public readonly record struct CameraNudge(Vector3 Offset, Vector3 Euler, float FovOffset, float DistanceScale)
{
    public static readonly CameraNudge Identity = new(Vector3.Zero, Vector3.Zero, 0f, 1f);

    /// <summary>Adds another nudge: offsets, angles and FOV sum, distance scales multiply.</summary>
    public CameraNudge Combine(in CameraNudge other) => new(
        Offset + other.Offset, Euler + other.Euler, FovOffset + other.FovOffset, DistanceScale * other.DistanceScale);
}

/// <summary>What the rig knows this frame, handed to every layer so none of them has to query the rig
/// or the player. <see cref="Speed01"/> is horizontal speed over sprint speed, clamped to 0..1.</summary>
public readonly record struct CameraSnapshot(
    CameraContext Context,
    bool FirstPerson,
    float ModeBlend,
    float Speed01,
    bool Grounded,
    bool Sprinting,
    bool Mounted);

/// <summary>An entity component that shapes the camera. The rig finds every one through
/// <c>Entity.GetComponents&lt;ICameraLayer&gt;()</c>, so a layer needs no registration call.</summary>
public interface ICameraLayer
{
    CameraNudge Sample(float dt, in CameraSnapshot snapshot);
}

/// <summary>
/// How much camera motion the player has asked for, each 0..1. Every layer that moves the camera for
/// feel rather than for framing (shake, bob, FOV kick) multiplies its output by the matching scale, so
/// the settings and the Reduced Motion toggle reach all of them without each reading Settings.
/// </summary>
public readonly record struct CameraComfort(float Shake, float Bob, float FovKick)
{
    /// <summary>Shake left when Reduced Motion is on: a hit still reads, it just no longer shudders.</summary>
    public const float ReducedShake = 0.25f;

    public static readonly CameraComfort Full = new(1f, 1f, 1f);

    public static CameraComfort From(float shake, float bob, float fovKick, bool reducedMotion) => new(
        Math.Min(Math.Clamp(shake, 0f, 1f), reducedMotion ? ReducedShake : 1f),
        reducedMotion ? 0f : Math.Clamp(bob, 0f, 1f),
        reducedMotion ? 0f : Math.Clamp(fovKick, 0f, 1f));

    /// <summary>The live comfort for the current settings; <see cref="Full"/> with no settings service.</summary>
    public static CameraComfort From(Settings.Settings? s) => s == null
        ? Full
        : From(s.CameraShakeIntensity, s.HeadBob, s.FovKick, s.ReducedMotion);
}
