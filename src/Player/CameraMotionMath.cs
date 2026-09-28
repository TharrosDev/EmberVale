using System;
using Embervale.Movement;

namespace Embervale.Player;

/// <summary>
/// A critically damped spring on one float: it eases to zero with no overshoot, however large a step
/// it is given (the update is the exact solution, not an integration). A <see cref="Kick"/> is how an
/// impact enters it — as a velocity chosen so the value peaks at exactly the size asked for, a beat
/// later, so the camera dips into a landing rather than teleporting there. Engine-free.
/// </summary>
public struct CriticalSpring
{
    public float X;
    public float V;

    /// <summary>Advances the spring by <paramref name="dt"/> seconds at stiffness <paramref name="omega"/> (rad/s).</summary>
    public void Step(float dt, float omega)
    {
        if (dt <= 0f)
        {
            return;
        }

        float e = MathF.Exp(-omega * dt);
        float c = V + (omega * X);
        X = (X + (c * dt)) * e;
        V = (V - (omega * c * dt)) * e;
    }

    /// <summary>Adds an impulse that peaks at <paramref name="peak"/> (signed) from rest, at 1/omega
    /// seconds. Stacked kicks are held to a speed worth <paramref name="limit"/> of peak, so a burst of
    /// impacts cannot build without bound.</summary>
    public void Kick(float peak, float omega, float limit)
    {
        float vmax = limit * omega * MathF.E;
        V = Math.Clamp(V + (peak * omega * MathF.E), -vmax, vmax);
    }
}

/// <summary>What <see cref="CameraMotion.Step"/> reads each frame. Everything is a plain value so the
/// motion rules run without an engine. <see cref="FpWeight"/> is 1 in first person and 0 in third;
/// <see cref="BobScale"/> and <see cref="FovScale"/> are the player's comfort scales (0..1).
/// <see cref="DodgeLateral"/> / <see cref="DodgeForward"/> are the dodge's velocity in the body's
/// frame (any length); <see cref="DodgeProgress"/> is 0..1 across the roll and its recovery.</summary>
public readonly record struct MotionInput(
    float Speed01,
    bool Grounded,
    bool Sprinting,
    bool Mounted,
    MountGait Gait,
    float FpWeight,
    float BodyY,
    bool Dodging,
    bool DodgeIsRoll,
    float DodgeProgress,
    float DodgeLateral,
    float DodgeForward,
    float BobScale,
    float FovScale);

/// <summary>One frame of motion, in the camera's rest space: offsets in metres, roll in radians, FOV in
/// degrees. <see cref="LandingDrop"/> is the drop in metres on the frame a landing happened (else 0),
/// unscaled by comfort so the shake layer can size its own response.</summary>
public readonly record struct MotionOut(float X, float Y, float Z, float Roll, float Fov, float LandingDrop)
{
    public static readonly MotionOut Zero = new(0f, 0f, 0f, 0f, 0f, 0f);
}

/// <summary>How a mounted gait sways the rider: stride rate (Hz), vertical bounce (metres), roll (radians).</summary>
public readonly record struct MountSway(float Hz, float Bounce, float Roll)
{
    public static readonly MountSway None = new(0f, 0f, 0f);
}

/// <summary>
/// Pure rules for <b>camera movement feel</b>: landing dip, first-person head bob and sway, the settle
/// after a sprint, dodge tilt, mounted sway and the sprint/landing FOV punch. Every number is a
/// restrained default — centimetres and a fraction of a degree — because the camera is what the player
/// looks through for hours. <see cref="CameraMotionLayer"/> reads the game, calls <see cref="CameraMotion"/>
/// and hands the result to the rig.
/// </summary>
public static class CameraMotionMath
{
    /// <summary>Longest frame the state integrates, so a hitch cannot fling a spring.</summary>
    public const float MaxDt = 0.1f;

    // ---- landing -----------------------------------------------------------------------------
    /// <summary>Drops shorter than this (metres) do not dip: stairs and kerbs are not landings. A plain
    /// jump comes down about a metre from its peak and dips a little.</summary>
    public const float MinLandingDrop = 0.5f;

    /// <summary>The drop at which the dip and FOV punch top out; past it a fall is a fall.</summary>
    public const float FullLandingDrop = 6f;

    /// <summary>Deepest landing dip (metres) and the FOV narrowing (degrees) that goes with it.</summary>
    public const float MaxLandingDip = 0.11f;
    public const float MaxLandingFov = 1.5f;

    /// <summary>Stiffness (rad/s) the dip recovers with: about half a second, soft, no rebound.</summary>
    public const float DipOmega = 8f;

    /// <summary>Most a stack of dip kicks may reach (metres).</summary>
    public const float DipKickLimit = MaxLandingDip * 1.25f;

    /// <summary>Stiffness for the FOV punch, and how large stacked FOV impulses may get (degrees).</summary>
    public const float FovOmega = 9f;
    public const float MaxFovPunch = 2.5f;

    /// <summary>Third-person camera keeps this share of a first-person body motion (dip, settle, dodge).</summary>
    public const float ThirdPersonShare = 0.5f;

    // ---- sprint ------------------------------------------------------------------------------
    /// <summary>FOV widening (degrees) on the first grounded frame of a sprint.</summary>
    public const float SprintPunchFov = 1.2f;

    /// <summary>The settle after a sprint ends: nothing below <see cref="SettleMinSpeed"/> of sprint
    /// speed, up to <see cref="MaxSettleDip"/> metres of dip at a full run.</summary>
    public const float SettleMinSpeed = 0.5f;
    public const float MaxSettleDip = 0.02f;

    // ---- head bob ----------------------------------------------------------------------------
    /// <summary>Below this Speed01 there is no bob (standing, or an inch of drift).</summary>
    public const float BobDeadzone = 0.04f;

    /// <summary>Vertical bob (metres) and roll sway (radians) at a full sprint; lateral sway is half the bob.</summary>
    public const float MaxBobY = 0.02f;
    public const float MaxBobRoll = 0.006f;

    /// <summary>Extra bob while the sprint is granted: the body is throwing itself forward.</summary>
    public const float SprintBobBoost = 1.15f;

    /// <summary>How quickly the bob fades in and out (per second, exponential).</summary>
    public const float BobFadeIn = 6f;
    public const float BobFadeOut = 9f;

    // ---- dodge -------------------------------------------------------------------------------
    /// <summary>Peak roll (radians) into a sideways roll, dip (metres) of any roll, and the pull-back of a backstep.</summary>
    public const float DodgeRoll = 0.05f;
    public const float DodgeDip = 0.07f;
    public const float DodgeBack = 0.05f;

    /// <summary>Follow rate (per second) that smooths the dodge envelope, so a cancelled roll eases out.</summary>
    public const float DodgeFollow = 20f;

    // ---- final clamps ------------------------------------------------------------------------
    public const float MaxOffsetUp = 0.05f;
    public const float MaxOffsetDown = 0.18f;
    public const float MaxRoll = 0.09f;

    internal const float TwoPi = MathF.PI * 2f;

    /// <summary>A landing's 0..1 size: 0 at <see cref="MinLandingDrop"/>, 1 at <see cref="FullLandingDrop"/>,
    /// square-rooted so a small hop still registers and a big fall does not grow linearly with it.</summary>
    public static float LandingSize(float dropMetres)
    {
        float t = (dropMetres - MinLandingDrop) / (FullLandingDrop - MinLandingDrop);
        return t <= 0f ? 0f : MathF.Sqrt(Math.Min(t, 1f));
    }

    /// <summary>Metres the camera dips for a drop; zero below <see cref="MinLandingDrop"/>.</summary>
    public static float LandingDip(float dropMetres) => MaxLandingDip * LandingSize(dropMetres);

    /// <summary>Degrees the FOV narrows for a drop (an impact squeezes the view a touch).</summary>
    public static float LandingFov(float dropMetres) => MaxLandingFov * LandingSize(dropMetres);

    /// <summary>The share of a body motion a view keeps: all in first person, <see cref="ThirdPersonShare"/>
    /// in third, blended by <paramref name="fpWeight"/>.</summary>
    public static float BodyShare(float fpWeight) =>
        ThirdPersonShare + ((1f - ThirdPersonShare) * Math.Clamp(fpWeight, 0f, 1f));

    /// <summary>Metres of dip when a sprint ends at <paramref name="speed01"/>; zero below
    /// <see cref="SettleMinSpeed"/>.</summary>
    public static float SettleDip(float speed01)
    {
        float t = (speed01 - SettleMinSpeed) / (1f - SettleMinSpeed);
        return t <= 0f ? 0f : MaxSettleDip * Math.Min(t, 1f);
    }

    /// <summary>Steps per second at a Speed01: a walk is unhurried, a sprint is quick.</summary>
    public static float StepHz(float speed01) => 1.3f + (1.7f * Math.Clamp(speed01, 0f, 1f));

    /// <summary>Vertical head bob (metres) at a Speed01. Sub-linear, so a walk still shows a bob; zero in
    /// the dead zone; a little more while sprinting.</summary>
    public static float BobAmplitude(float speed01, bool sprinting)
    {
        if (speed01 <= BobDeadzone)
        {
            return 0f;
        }

        float a = MaxBobY * MathF.Pow(Math.Min(speed01, 1f), 0.7f);
        return sprinting ? Math.Min(a * SprintBobBoost, MaxBobY * SprintBobBoost) : a;
    }

    /// <summary>Roll sway (radians) at a Speed01, same curve as the bob.</summary>
    public static float BobRoll(float speed01) =>
        speed01 <= BobDeadzone ? 0f : MaxBobRoll * MathF.Pow(Math.Min(speed01, 1f), 0.7f);

    /// <summary>The sway of a mounted gait. Halt and rein-back sway nothing; a gallop sways most.</summary>
    public static MountSway SwayFor(MountGait gait) => gait switch
    {
        MountGait.Walk => new MountSway(0.9f, 0.006f, 0.005f),
        MountGait.Trot => new MountSway(1.3f, 0.010f, 0.006f),
        MountGait.Canter => new MountSway(1.6f, 0.016f, 0.009f),
        MountGait.Gallop => new MountSway(2.0f, 0.022f, 0.012f),
        _ => MountSway.None,
    };

    /// <summary>The dodge's 0..1 shape over its progress: a quick lean in and a slower return, zero at
    /// both ends.</summary>
    public static float DodgeEnvelope(float progress)
    {
        float p = Math.Clamp(progress, 0f, 1f);
        return MathF.Sin(MathF.PI * MathF.Pow(p, 0.7f));
    }

    /// <summary>Exponential approach of <paramref name="value"/> to <paramref name="target"/> at
    /// <paramref name="rate"/> per second — frame-rate independent.</summary>
    public static float Approach(float value, float target, float rate, float dt) =>
        value + ((target - value) * (1f - MathF.Exp(-rate * Math.Max(dt, 0f))));
}

/// <summary>
/// The stateful half of <see cref="CameraMotionMath"/>: springs, bob phase and edge detection
/// (landing, sprint start and stop) carried between frames. Engine-free — <see cref="Step"/> takes a
/// <see cref="MotionInput"/> and returns a <see cref="MotionOut"/>, so a whole walk, landing or roll
/// can be replayed in a unit test.
/// </summary>
public sealed class CameraMotion
{
    private CriticalSpring _dip;
    private CriticalSpring _fov;
    private bool _grounded = true;
    private bool _sprinting;
    private float _peakY;
    private float _lastSpeed01;
    private float _phase;
    private float _bobWeight;
    private float _swayPhase;
    private float _swayWeight;
    private float _dodgeEnv;
    private float _dodgeLateral;
    private float _dodgeForward = 1f;
    private bool _dodgeIsRoll = true;

    public MotionOut Step(float dt, in MotionInput input)
    {
        dt = Math.Clamp(dt, 0f, CameraMotionMath.MaxDt);
        float bodyShare = CameraMotionMath.BodyShare(input.FpWeight);
        float bob = Math.Clamp(input.BobScale, 0f, 1f);
        float fovScale = Math.Clamp(input.FovScale, 0f, 1f);
        float landingDrop = 0f;

        // Landing: the peak height while airborne against where the body comes down.
        if (!input.Grounded)
        {
            _peakY = _grounded ? input.BodyY : MathF.Max(_peakY, input.BodyY);
        }
        else
        {
            if (!_grounded)
            {
                float drop = _peakY - input.BodyY;
                if (drop >= CameraMotionMath.MinLandingDrop)
                {
                    landingDrop = drop;
                    _dip.Kick(-CameraMotionMath.LandingDip(drop) * bob * bodyShare, CameraMotionMath.DipOmega, CameraMotionMath.DipKickLimit);
                    _fov.Kick(-CameraMotionMath.LandingFov(drop) * fovScale, CameraMotionMath.FovOmega, CameraMotionMath.MaxFovPunch);
                }
            }

            _peakY = input.BodyY;
        }

        _grounded = input.Grounded;

        // Sprint edges: a small FOV widening as it starts, a settle dip as it ends.
        if (input.Sprinting && !_sprinting && input.Grounded && !input.Mounted)
        {
            _fov.Kick(CameraMotionMath.SprintPunchFov * fovScale, CameraMotionMath.FovOmega, CameraMotionMath.MaxFovPunch);
        }
        else if (!input.Sprinting && _sprinting && input.Grounded && !input.Mounted)
        {
            float dip = CameraMotionMath.SettleDip(MathF.Max(_lastSpeed01, input.Speed01));
            _dip.Kick(-dip * bob * bodyShare, CameraMotionMath.DipOmega, CameraMotionMath.DipKickLimit);
        }

        _sprinting = input.Sprinting;
        _lastSpeed01 = input.Speed01;

        // Head bob: on foot (not mid-roll), first person only, faded so stopping and starting are soft.
        bool walking = input.Grounded && !input.Mounted && !input.Dodging && input.Speed01 > CameraMotionMath.BobDeadzone;
        float bobTarget = walking ? Math.Clamp(input.FpWeight, 0f, 1f) * bob : 0f;
        _bobWeight = CameraMotionMath.Approach(
            _bobWeight, bobTarget, bobTarget > _bobWeight ? CameraMotionMath.BobFadeIn : CameraMotionMath.BobFadeOut, dt);
        _phase = (_phase + (dt * CameraMotionMath.StepHz(input.Speed01) * 0.5f * CameraMotionMath.TwoPi)) % CameraMotionMath.TwoPi;

        float bobY = CameraMotionMath.BobAmplitude(input.Speed01, input.Sprinting) * _bobWeight * MathF.Sin(2f * _phase);
        float bobX = CameraMotionMath.BobAmplitude(input.Speed01, input.Sprinting) * 0.5f * _bobWeight * MathF.Sin(_phase);
        float bobRoll = CameraMotionMath.BobRoll(input.Speed01) * _bobWeight * MathF.Sin(_phase);

        // Mounted sway: the horse's stride, in either view, gentler in third person.
        MountSway sway = input.Mounted ? CameraMotionMath.SwayFor(input.Gait) : MountSway.None;
        float swayTarget = sway.Hz > 0f ? bodyShare * bob : 0f;
        _swayWeight = CameraMotionMath.Approach(
            _swayWeight, swayTarget, swayTarget > _swayWeight ? CameraMotionMath.BobFadeIn : CameraMotionMath.BobFadeOut, dt);
        _swayPhase = (_swayPhase + (dt * sway.Hz * CameraMotionMath.TwoPi)) % CameraMotionMath.TwoPi;
        float swayY = sway.Bounce * _swayWeight * MathF.Sin(2f * _swayPhase);
        float swayRoll = sway.Roll * _swayWeight * MathF.Sin(_swayPhase);

        // Dodge: lean into a roll's side and dip under it; a backstep pulls the camera back a touch.
        float flat = MathF.Sqrt((input.DodgeLateral * input.DodgeLateral) + (input.DodgeForward * input.DodgeForward));
        if (input.Dodging)
        {
            _dodgeIsRoll = input.DodgeIsRoll;
        }

        if (input.Dodging && flat > 0.5f)
        {
            _dodgeLateral = input.DodgeLateral / flat;
            _dodgeForward = input.DodgeForward / flat;
        }

        float envTarget = input.Dodging ? CameraMotionMath.DodgeEnvelope(input.DodgeProgress) : 0f;
        _dodgeEnv = CameraMotionMath.Approach(_dodgeEnv, envTarget, CameraMotionMath.DodgeFollow, dt);
        float dodgeScale = _dodgeEnv * bob * bodyShare;
        float dodgeRoll = _dodgeIsRoll ? -_dodgeLateral * CameraMotionMath.DodgeRoll * dodgeScale : 0f;
        float dodgeY = _dodgeIsRoll ? -CameraMotionMath.DodgeDip * dodgeScale : 0f;
        float dodgeZ = _dodgeIsRoll ? 0f : CameraMotionMath.DodgeBack * dodgeScale;

        _dip.Step(dt, CameraMotionMath.DipOmega);
        _fov.Step(dt, CameraMotionMath.FovOmega);

        return new MotionOut(
            bobX,
            Math.Clamp(bobY + swayY + _dip.X + dodgeY, -CameraMotionMath.MaxOffsetDown, CameraMotionMath.MaxOffsetUp),
            dodgeZ,
            Math.Clamp(bobRoll + swayRoll + dodgeRoll, -CameraMotionMath.MaxRoll, CameraMotionMath.MaxRoll),
            Math.Clamp(_fov.X, -CameraMotionMath.MaxFovPunch, CameraMotionMath.MaxFovPunch),
            landingDrop);
    }
}
