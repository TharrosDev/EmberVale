using System;

namespace Embervale.World;

/// <summary>How deep an actor is standing, in the bands the body reads. Ordered shallow to deep, so a
/// comparison (<c>band &gt;= WadingBand.Knee</c>) reads the way it sounds.</summary>
public enum WadingBand
{
    /// <summary>No water at the feet, or a film too thin to matter.</summary>
    Dry,

    /// <summary>Wet boots. Full speed; the band exists so footsteps and splashes can tell.</summary>
    Ankle,

    /// <summary>Knee-deep. Noticeably slower.</summary>
    Knee,

    /// <summary>Waist-deep. Heavy going, and the "too deep" warning lives at the top of it.</summary>
    Waist,

    /// <summary>Over <see cref="WorldWater.WadeDepth"/>: the water pushes back towards the shallows.</summary>
    TooDeep,

    /// <summary>Over <see cref="WorldWater.DrownDepth"/>: <see cref="WorldRecovery"/>'s territory.</summary>
    Drowning,
}

/// <summary>
/// The tunable half of the wading contract. Every value is a knob <see cref="WorldWading"/> exports;
/// the defaults here are the shipped feel, so a query that never sees the node (a footstep, a test)
/// classifies depth exactly the way the node does.
/// </summary>
public sealed record WadingTuning
{
    public static WadingTuning Default { get; } = new();

    /// <summary>Water shallower than this is dry ground for every purpose.</summary>
    public float AnkleDepth { get; init; } = 0.12f;

    /// <summary>Knee band starts here.</summary>
    public float KneeDepth { get; init; } = 0.4f;

    /// <summary>Waist band starts here.</summary>
    public float WaistDepth { get; init; } = 0.75f;

    /// <summary>Depth at which the "too deep" warning fires and the push-back begins to build.</summary>
    public float WarnDepth { get; init; } = 0.9f;

    /// <summary>Speed multiplier reached at <see cref="KneeDepth"/>.</summary>
    public float KneeSpeed { get; init; } = 0.88f;

    /// <summary>Speed multiplier reached at <see cref="WaistDepth"/>.</summary>
    public float WaistSpeed { get; init; } = 0.68f;

    /// <summary>Speed multiplier at exactly <see cref="WorldWater.WadeDepth"/>.</summary>
    public float LimitSpeed { get; init; } = 0.48f;

    /// <summary>Speed multiplier past the wade limit — floor of the curve.</summary>
    public float DeepSpeed { get; init; } = 0.35f;

    /// <summary>How far past the wade limit the curve takes to reach <see cref="DeepSpeed"/>.</summary>
    public float DeepRamp { get; init; } = 0.2f;

    /// <summary>
    /// Horizontal push-back towards the shallows, metres per second, reached at the wade limit.
    ///
    /// ⚠️ <b>IT MUST OUT-PULL A SPRINT AT <see cref="DeepSpeed"/>.</b> A walk at the base 5 m/s
    /// times 0.35 times the 1.6 sprint multiplier is 2.8 m/s; a push under that is a current the
    /// player can simply out-run into the drown band, which turns the warning into a lie.
    /// </summary>
    public float PushBackSpeed { get; init; } = 3.0f;

    /// <summary>Speed multipliers are applied in steps of this size, so a stat is not re-dirtied
    /// every frame by a depth that moves a millimetre.</summary>
    public float SpeedStep { get; init; } = 0.02f;
}

/// <summary>
/// Pure rules for standing in water: which band a depth is in, how much it slows you, and how hard
/// it pushes you back as you approach the wade limit.
///
/// ⚠️ <b>WADING IS A GRADIENT, NOT A THRESHOLD.</b> The old contract had one number — under
/// <see cref="WorldWater.WadeDepth"/> walk as normal, over it the land refuses — so the first thing a
/// player ever learned about deep water was that it had stopped them. Skyrim's shallows slow you as
/// they rise; so do these. Speed falls smoothly through the knee and waist bands, the warning comes
/// while there is still ground to turn back on, and past the limit the water leans on you towards the
/// shallows rather than a wall appearing. There is still no swimming: everything here happens with
/// the player's feet on the bottom.
///
/// Godot-free on purpose, like <see cref="WorldWater"/>: the unit suite drives it directly.
/// </summary>
public static class WadingRules
{
    /// <summary>The band a depth falls in.</summary>
    public static WadingBand BandFor(float depth, WadingTuning? tuning = null)
    {
        WadingTuning t = tuning ?? WadingTuning.Default;
        if (!(depth >= t.AnkleDepth)) // NaN reads as dry: a poisoned sample must never slow anyone
        {
            return WadingBand.Dry;
        }
        if (depth >= WorldWater.DrownDepth)
        {
            return WadingBand.Drowning;
        }
        if (depth > WorldWater.WadeDepth)
        {
            return WadingBand.TooDeep;
        }
        if (depth >= t.WaistDepth)
        {
            return WadingBand.Waist;
        }
        return depth >= t.KneeDepth ? WadingBand.Knee : WadingBand.Ankle;
    }

    /// <summary>
    /// The move-speed multiplier at a depth: 1 on dry ground and in ankle water, then a piecewise
    /// linear fall through the knee and waist bands to <see cref="WadingTuning.LimitSpeed"/> at the
    /// wade limit and <see cref="WadingTuning.DeepSpeed"/> just past it. Continuous everywhere, so
    /// walking down a shelving bank never produces a step change in pace.
    /// </summary>
    public static float SpeedScale(float depth, WadingTuning? tuning = null)
    {
        WadingTuning t = tuning ?? WadingTuning.Default;
        if (!(depth > t.AnkleDepth))
        {
            return 1f;
        }
        if (depth <= t.KneeDepth)
        {
            return Lerp(1f, t.KneeSpeed, Progress(depth, t.AnkleDepth, t.KneeDepth));
        }
        if (depth <= t.WaistDepth)
        {
            return Lerp(t.KneeSpeed, t.WaistSpeed, Progress(depth, t.KneeDepth, t.WaistDepth));
        }
        if (depth <= WorldWater.WadeDepth)
        {
            return Lerp(t.WaistSpeed, t.LimitSpeed, Progress(depth, t.WaistDepth, WorldWater.WadeDepth));
        }
        return Lerp(t.LimitSpeed, t.DeepSpeed,
            Progress(depth, WorldWater.WadeDepth, WorldWater.WadeDepth + t.DeepRamp));
    }

    /// <summary><see cref="SpeedScale"/> rounded to <see cref="WadingTuning.SpeedStep"/>, so the
    /// stat modifier it becomes only changes when the pace visibly does.</summary>
    public static float QuantizedSpeedScale(float depth, WadingTuning? tuning = null)
    {
        WadingTuning t = tuning ?? WadingTuning.Default;
        float scale = SpeedScale(depth, t);
        if (t.SpeedStep <= 0f || scale >= 1f)
        {
            return scale;
        }
        return MathF.Min(1f, MathF.Round(scale / t.SpeedStep) * t.SpeedStep);
    }

    /// <summary>
    /// Push-back speed towards the shallows: zero until <see cref="WadingTuning.WarnDepth"/>, a
    /// smoothstep up to <see cref="WadingTuning.PushBackSpeed"/> at the wade limit, and full beyond.
    /// The ramp is what makes the refusal readable — the player feels the water lean on them for
    /// the last twenty centimetres before it stops giving ground.
    /// </summary>
    public static float PushBack(float depth, WadingTuning? tuning = null)
    {
        WadingTuning t = tuning ?? WadingTuning.Default;
        if (!(depth > t.WarnDepth))
        {
            return 0f;
        }
        float p = Progress(depth, t.WarnDepth, WorldWater.WadeDepth);
        return t.PushBackSpeed * p * p * (3f - (2f * p));
    }

    /// <summary>Has the depth just crossed the warning line on the way in?</summary>
    public static bool CrossedWarning(float previousDepth, float depth, WadingTuning? tuning = null)
    {
        WadingTuning t = tuning ?? WadingTuning.Default;
        return previousDepth < t.WarnDepth && depth >= t.WarnDepth;
    }

    /// <summary>
    /// The horizontal direction towards shallower water at a point, from a central difference of
    /// <paramref name="depthAt"/> over <paramref name="probe"/> metres, normalised. Zero where the
    /// bottom is flat — a caller needs a second opinion there (the last safe ground) rather than a
    /// push in an arbitrary direction.
    /// </summary>
    public static (float X, float Z) ShallowerDirection(
        Func<float, float, float> depthAt, float x, float z, float probe = 1.5f)
    {
        float dx = depthAt(x + probe, z) - depthAt(x - probe, z);
        float dz = depthAt(x, z + probe) - depthAt(x, z - probe);
        float length = MathF.Sqrt((dx * dx) + (dz * dz));
        // Under a centimetre of change across three metres is a flat bottom, not a direction.
        if (!(length > 0.01f))
        {
            return (0f, 0f);
        }
        return (-dx / length, -dz / length);
    }

    private static float Progress(float value, float from, float to) =>
        to <= from ? 1f : Math.Clamp((value - from) / (to - from), 0f, 1f);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
