using System;
using System.Collections.Generic;

namespace Embervale.Movement;

/// <summary>
/// Where a whistled horse comes from and how it closes the distance (the Movement upgrade) — pure,
/// Godot-free and unit-tested. <see cref="MountComponent"/> asks <see cref="World.SafePlacementService"/>
/// whether each candidate is real ground; this only proposes them, in order.
///
/// <b>The horse runs in; it does not appear.</b> 39A mounted the rider on the spot, which made the
/// whistle a teleport with a sound effect. Now the horse starts out of the rider's sight line — behind
/// first, then the flanks, ahead last — and gallops in. Every step of that can fail (no ground, deep
/// water, a wall between) and every failure lands on the old behaviour, so a whistle can never leave
/// a paying player horseless.
/// </summary>
public static class MountWhistle
{
    /// <summary>
    /// Start points for a horse whistled to (<paramref name="x"/>, <paramref name="z"/>), in the
    /// order they should be tried: directly behind the rider's <paramref name="yaw"/>, then
    /// alternating either side of that until the last one is straight ahead.
    /// </summary>
    public static IReadOnlyList<(float X, float Z)> Candidates(float x, float z, float yaw, float distance, int count)
    {
        count = Math.Max(1, count);
        var points = new List<(float X, float Z)>(count);
        float d = float.IsFinite(distance) ? MathF.Abs(distance) : 0f;
        float facing = float.IsFinite(yaw) ? yaw : 0f;

        // Behind is yaw + π. Each pair steps further round both sides; with an even count the last
        // point is the one straight ahead, where a horse is least like a surprise.
        float step = MathF.PI / MathF.Max(1f, MathF.Ceiling(count / 2f));
        for (int i = 0; i < count; i++)
        {
            int ring = (i + 1) / 2;
            float side = i % 2 == 1 ? 1f : -1f;
            float angle = facing + MathF.PI + (side * ring * step);

            // Godot forward is -Z: a yaw faces (-sin, -cos), matching MountGaits.YawOf.
            points.Add((x - (MathF.Sin(angle) * d), z - (MathF.Cos(angle) * d)));
        }

        return points;
    }

    /// <summary>
    /// One frame of the run-in: moves (<paramref name="x"/>, <paramref name="z"/>) toward the rider by
    /// <paramref name="speed"/> x <paramref name="delta"/>, easing to <paramref name="arriveSpeed"/> over
    /// the last <paramref name="slowRadius"/> metres so the horse pulls up rather than stops dead.
    /// Never overshoots. Returns the remaining distance alongside the new position.
    /// </summary>
    public static (float X, float Z, float Remaining) Approach(
        float x, float z, float targetX, float targetZ,
        float speed, float arriveSpeed, float slowRadius, float delta)
    {
        float dx = targetX - x;
        float dz = targetZ - z;
        float distance = MathF.Sqrt((dx * dx) + (dz * dz));
        if (!float.IsFinite(distance) || distance < 0.0001f || !float.IsFinite(delta) || delta <= 0f)
        {
            return (x, z, float.IsFinite(distance) ? distance : 0f);
        }

        float k = slowRadius > 0f ? Math.Clamp(distance / slowRadius, 0f, 1f) : 1f;
        float pace = arriveSpeed + ((speed - arriveSpeed) * k);
        float move = MathF.Min(MathF.Max(pace, 0f) * delta, distance);
        float nx = x + (dx / distance * move);
        float nz = z + (dz / distance * move);
        return (nx, nz, distance - move);
    }
}
