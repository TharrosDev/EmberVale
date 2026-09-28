using System;

namespace Embervale.Movement;

/// <summary>Why a horse will not go on. <see cref="None"/> is the common answer.</summary>
public enum MountRefusal
{
    None,
    DeepWater,
    SteepDrop,
}

/// <summary>
/// The ground a horse refuses (the Movement upgrade) — pure, Godot-free and unit-tested.
/// <see cref="MountComponent"/> gathers the facts with one physics ray and <see cref="World.WorldWater"/>;
/// this decides what they mean.
///
/// <b>A horse is a better judge of ground than its rider, and that is the whole feature.</b> On foot,
/// <see cref="World.WorldWater"/>'s contract is that the land says "not that way" and
/// <see cref="World.WorldRecovery"/> catches anyone who ignores it. A horse at 13 m/s arrives at the
/// bank before the land has said anything, so the mount looks ahead by its own stopping distance and
/// balks — slows hard to a halt and stands, still free to turn away — at water deeper than it will
/// wade or a drop steeper than it will take. Recovery stays the backstop; this is what keeps a
/// gallop from needing it.
///
/// ⚠️ <b>IT JUDGES THE GROUND THE RAY FOUND, NOT THE HEIGHTFIELD.</b> A bridge over a river has deep
/// water under it on every map the heightfield can draw. Measuring depth from the surface the probe
/// actually hit — the deck — is what lets a horse cross a bridge and refuse the ford beside it.
/// </summary>
public static class MountTerrain
{
    /// <summary>
    /// What the ground ahead means for a horse.
    /// </summary>
    /// <param name="feetY">The rider's (and so the horse's) feet now.</param>
    /// <param name="groundAheadY">The first solid surface under the probe point, or null when the
    /// probe ran out of reach without finding one.</param>
    /// <param name="waterSurfaceAheadY">Declared water over the probe point, or null.</param>
    /// <param name="depthHere">Water depth at the horse now — a horse already in the river is
    /// never refused the way out of it, only the way further in.</param>
    /// <param name="probeDistance">Horizontal distance to the probe point, for the drop's grade.</param>
    /// <param name="refuseDepth">Deepest water the horse will enter.</param>
    /// <param name="maxStepDown">A drop this small is a step, whatever its grade.</param>
    /// <param name="maxDescentGrade">Steepest descent (drop over distance) the horse will take.</param>
    public static MountRefusal Judge(
        float feetY, float? groundAheadY, float? waterSurfaceAheadY, float depthHere,
        float probeDistance, float refuseDepth, float maxStepDown, float maxDescentGrade)
    {
        if (!float.IsFinite(feetY))
        {
            return MountRefusal.None; // no position is no judgement — never strand a rider on bad data
        }

        if (groundAheadY is not { } ground || !float.IsFinite(ground))
        {
            return MountRefusal.SteepDrop;
        }

        float drop = feetY - ground;
        float reach = MathF.Max(probeDistance, 0.01f);
        if (drop > maxStepDown && drop / reach > maxDescentGrade)
        {
            return MountRefusal.SteepDrop;
        }

        if (waterSurfaceAheadY is { } surface && float.IsFinite(surface))
        {
            float depth = surface - ground;
            float here = float.IsFinite(depthHere) ? depthHere : 0f;
            if (depth > refuseDepth && depth > here + 0.05f)
            {
                return MountRefusal.DeepWater;
            }
        }

        return MountRefusal.None;
    }

    /// <summary>
    /// How far ahead to look: the distance the horse needs to balk to a stop from
    /// <paramref name="speed"/> m/s at <paramref name="balkDeceleration"/> m/s², plus a margin so a
    /// standing horse still sees the edge in front of its nose. Clamped to
    /// <paramref name="maxDistance"/> so one bad speed cannot send the probe across the realm.
    /// </summary>
    public static float ProbeDistance(float speed, float balkDeceleration, float margin, float maxDistance)
    {
        float v = float.IsFinite(speed) ? MathF.Abs(speed) : 0f;
        float stop = balkDeceleration > 0f ? (v * v) / (2f * balkDeceleration) : 0f;
        return Math.Clamp(stop + margin, margin, MathF.Max(maxDistance, margin));
    }
}
