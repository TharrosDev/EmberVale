using System;

namespace Embervale.Movement;

/// <summary>The horse's gaits, slowest first. <see cref="ReinBack"/> is the one gait that goes
/// backwards, and it sits below <see cref="Halt"/> so "at least a trot" is a plain comparison.</summary>
public enum MountGait
{
    ReinBack,
    Halt,
    Walk,
    Trot,
    Canter,
    Gallop,
}

/// <summary>
/// Every number the ride is shaped by, in one place so <see cref="MountComponent"/> can export each
/// of them. Speeds are multiples of the rider's own unmounted <c>StatType.MoveSpeed</c>, not metres
/// per second, so a buff to the rider's pace carries the horse with it the way it always has;
/// accelerations are those multiples per second, and turn rates are radians per second.
/// </summary>
public readonly record struct GaitTuning(
    float ReinBackSpeed,
    float WalkSpeed,
    float TrotSpeed,
    float CanterSpeed,
    float GallopSpeed,
    float Acceleration,
    float Deceleration,
    float BalkDeceleration,
    float PivotTurnRate,
    float GallopTurnRate,
    float CollectAngle,
    float WalkInput,
    float TrotInput)
{
    /// <summary>
    /// The first authored ride (Phase 39A's speeds, re-measured). ⚠️ Phase 56 owns every number.
    ///
    /// ⚠️ <b>THE GALLOP IS 2.72x, AND THE OLD MODIFIER WAS NOT THE 1.7x ITS DOCSTRING SAID.</b> 39A
    /// wrote <c>PercentMult 1.7</c> and documented it as "1.7x"; <c>Stat.Recalculate</c> applies a
    /// PercentMult as <c>value *= 1 + m</c>, so the horse was really 2.7x at a canter and 4.3x at a
    /// gallop (~21 m/s on a 5 m/s rider). The canter and gallop below are the speeds the old comment
    /// promised, and <see cref="MountComponent"/> now writes <c>GallopSpeed - 1</c> into the modifier.
    /// </summary>
    public static GaitTuning Default => new(
        ReinBackSpeed: 0.3f,
        WalkSpeed: 0.55f,
        TrotSpeed: 1.1f,
        CanterSpeed: 1.7f,
        GallopSpeed: 2.72f,
        Acceleration: 1.4f,
        Deceleration: 2.2f,
        BalkDeceleration: 4.5f,
        PivotTurnRate: 3.2f,
        GallopTurnRate: 1.0f,
        CollectAngle: 1.05f,
        WalkInput: 0.4f,
        TrotInput: 0.75f);
}

/// <summary>
/// The horse's gaits, speed and heading (the Movement upgrade) — pure, Godot-free and unit-tested,
/// in the <see cref="MountRules"/> tradition: <see cref="MountComponent"/> owns the state and this
/// owns the arithmetic.
///
/// <b>A horse carves; it does not pivot.</b> The rider's body still turns with the mouse exactly as
/// it does on foot, but the horse under it keeps a heading of its own and swings toward where it is
/// asked to go at a rate that falls as it speeds up — a standing horse turns on its haunches, a
/// galloping one needs the width of a field. A turn sharper than <see cref="GaitTuning.CollectAngle"/>
/// also collects it to a trot, so the tight turn costs pace rather than being free at full speed.
///
/// <b>Gaits are discrete and speed is not.</b> Stick deflection picks walk, trot or canter; held
/// sprint (granted by the gallop pool) is the gallop. The speed then ramps toward the chosen gait,
/// accelerating gently and braking harder, so a horse gathers itself rather than snapping between
/// two velocities — which the single rider-speed modifier of 39A did every time sprint was pressed.
/// </summary>
public static class MountGaits
{
    /// <summary>Below this input magnitude the rider is not asking for anything.</summary>
    public const float InputDeadzone = 0.05f;

    /// <summary>The gait the rider is asking for this frame.</summary>
    /// <param name="inputMagnitude">Stick deflection, 0..1. A keyboard is always 1.</param>
    /// <param name="turnAngle">Radians between the horse's heading and the asked-for direction.</param>
    /// <param name="backing">The rider is pulling straight back (the back key alone) rather than
    /// asking to go somewhere behind the horse — that is the rein-back. Looking behind you and
    /// pressing forward is a turn, not a reverse, which is why this is an input fact and not an angle.</param>
    /// <param name="gallopGranted">Whether <see cref="MountRules.Step"/> let the horse gallop.</param>
    public static MountGait Select(
        float inputMagnitude, float turnAngle, bool backing, bool gallopGranted, GaitTuning t)
    {
        if (!(inputMagnitude > InputDeadzone))
        {
            return MountGait.Halt; // also the NaN answer: no usable request is no request
        }

        if (backing)
        {
            return MountGait.ReinBack;
        }

        MountGait gait = inputMagnitude < t.WalkInput ? MountGait.Walk
            : inputMagnitude < t.TrotInput ? MountGait.Trot
            : gallopGranted ? MountGait.Gallop
            : MountGait.Canter;

        // Collect for a sharp turn. A walk stays a walk: collecting never *raises* a gait.
        if (turnAngle > t.CollectAngle && gait > MountGait.Trot)
        {
            gait = MountGait.Trot;
        }

        return gait;
    }

    /// <summary>The signed speed a gait settles at, in rider speeds (negative is backwards).</summary>
    public static float SpeedOf(MountGait gait, GaitTuning t) => gait switch
    {
        MountGait.ReinBack => -t.ReinBackSpeed,
        MountGait.Walk => t.WalkSpeed,
        MountGait.Trot => t.TrotSpeed,
        MountGait.Canter => t.CanterSpeed,
        MountGait.Gallop => t.GallopSpeed,
        _ => 0f,
    };

    /// <summary>
    /// Ramps the signed speed toward <paramref name="target"/>. Speeding up uses
    /// <see cref="GaitTuning.Acceleration"/>; anything that brings the speed toward zero — slowing,
    /// and the half of a reversal before the horse stops — uses the harder
    /// <see cref="GaitTuning.Deceleration"/>, or <see cref="GaitTuning.BalkDeceleration"/> when the
    /// horse is refusing ground ahead of it.
    /// </summary>
    public static float StepSpeed(float current, float target, float delta, GaitTuning t, bool balking = false)
    {
        if (!float.IsFinite(current))
        {
            current = 0f;
        }

        if (!float.IsFinite(delta) || delta <= 0f || !float.IsFinite(target))
        {
            return current;
        }

        // Braking toward zero first: a horse going forward stops before it backs, and vice versa.
        bool reversing = current != 0f && target != 0f && MathF.Sign(current) != MathF.Sign(target);
        float goal = reversing ? 0f : target;
        bool slowing = MathF.Abs(goal) < MathF.Abs(current);
        float rate = slowing ? (balking ? t.BalkDeceleration : t.Deceleration) : t.Acceleration;

        float step = rate * delta;
        return MathF.Abs(goal - current) <= step ? goal : current + (MathF.Sign(goal - current) * step);
    }

    /// <summary>How fast the horse may turn at <paramref name="speed"/>, in radians per second —
    /// <see cref="GaitTuning.PivotTurnRate"/> standing, falling linearly to
    /// <see cref="GaitTuning.GallopTurnRate"/> at a full gallop.</summary>
    public static float TurnRate(float speed, GaitTuning t)
    {
        float top = t.GallopSpeed > 0f ? t.GallopSpeed : 1f;
        float k = float.IsFinite(speed) ? Math.Clamp(MathF.Abs(speed) / top, 0f, 1f) : 0f;
        return t.PivotTurnRate + ((t.GallopTurnRate - t.PivotTurnRate) * k);
    }

    /// <summary>Swings <paramref name="heading"/> toward <paramref name="desired"/> (both yaw
    /// radians) by at most <paramref name="maxRate"/> x <paramref name="delta"/>, the short way round.</summary>
    public static float StepHeading(float heading, float desired, float maxRate, float delta)
    {
        if (!float.IsFinite(heading))
        {
            return float.IsFinite(desired) ? desired : 0f;
        }

        if (!float.IsFinite(desired) || !float.IsFinite(delta) || delta <= 0f || !(maxRate > 0f))
        {
            return heading;
        }

        float diff = Wrap(desired - heading);
        float step = maxRate * delta;
        return Wrap(MathF.Abs(diff) <= step ? desired : heading + (MathF.Sign(diff) * step));
    }

    /// <summary>The unsigned angle between two yaws, 0..π.</summary>
    public static float AngleBetween(float a, float b) => MathF.Abs(Wrap(a - b));

    /// <summary>A yaw wrapped into -π..π.</summary>
    public static float Wrap(float angle)
    {
        if (!float.IsFinite(angle))
        {
            return 0f;
        }

        angle %= MathF.Tau;
        if (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }
        else if (angle < -MathF.PI)
        {
            angle += MathF.Tau;
        }

        return angle;
    }

    /// <summary>The Godot yaw that faces the horizontal direction (x, z): forward is -Z, so a yaw
    /// of zero faces (0, -1) and a quarter turn left faces (-1, 0).</summary>
    public static float YawOf(float x, float z) => MathF.Atan2(-x, -z);

    /// <summary>A horse jumps from impulsion, not from a standstill: at least a trot, going forward.</summary>
    public static bool CanJump(MountGait gait) => gait >= MountGait.Trot;

    /// <summary>
    /// How much of the gallop pool comes back per second at a gait, as a multiple of
    /// <see cref="MountRules.RegenPerSecond"/>. A standing horse gets its wind back fastest; a
    /// cantering one barely does, so the gallop is paid for by the cruise after it as well as by
    /// the pool — without this, riding at a canter was exactly as restful as standing still.
    /// </summary>
    public static float RegenScale(MountGait gait) => gait switch
    {
        MountGait.Halt => 1.5f,
        MountGait.ReinBack or MountGait.Walk => 1.25f,
        MountGait.Trot => 1f,
        MountGait.Canter => 0.5f,
        _ => 1f,
    };
}
