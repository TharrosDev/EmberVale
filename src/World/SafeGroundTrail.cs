using System;
using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>
/// A rolling trail of the last few places the player stood on safe ground, newest first — what
/// <see cref="WorldRecovery"/> returns them to.
///
/// ⚠️ <b>ONE REMEMBERED POINT WAS ONE POINT OF FAILURE.</b> The recovery used to hold a single
/// "last safe point", refreshed every third of a second while the player was dry. The newest point
/// is by construction the one nearest the hazard — the lip of the bank they then slid off — and when
/// anything about it had changed since (a companion standing on it, a cell's collision swapped, a
/// world event's barricade) there was nowhere else to go but a blind spiral from the water's edge.
/// A trail keeps a handful of points a few metres apart, so recovery can skip the one on the lip in
/// favour of one a little further back, and skip any that is no longer safe at all.
///
/// Pure and Godot-free apart from the <see cref="Vector3"/> value type, so the unit suite drives it.
/// </summary>
public sealed class SafeGroundTrail
{
    private readonly List<Vector3> _points = new();

    public SafeGroundTrail(int capacity = 8, float minSpacing = 4f)
    {
        Capacity = Math.Max(1, capacity);
        MinSpacing = MathF.Max(0f, minSpacing);
    }

    /// <summary>Most points kept; the oldest falls off the end.</summary>
    public int Capacity { get; }

    /// <summary>A new point closer than this to the newest one replaces it instead of being added,
    /// so standing still does not flush the whole trail with copies of one spot.</summary>
    public float MinSpacing { get; }

    public int Count => _points.Count;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Vector3> Points => _points;

    public Vector3? Newest => _points.Count == 0 ? null : _points[0];

    public void Clear() => _points.Clear();

    /// <summary>Remembers a point the caller has already judged safe.</summary>
    public void Record(Vector3 point)
    {
        if (_points.Count > 0 && Horizontal(_points[0], point) < MinSpacing)
        {
            _points[0] = point;
            return;
        }

        _points.Insert(0, point);
        if (_points.Count > Capacity)
        {
            _points.RemoveAt(_points.Count - 1);
        }
    }

    /// <summary>
    /// The best point to return to from <paramref name="hazard"/>: the newest one that
    /// <paramref name="stillSafe"/> accepts, is at least <paramref name="minHazardDistance"/> from the
    /// hazard and no more than <paramref name="maxDistance"/> from it. Failing that, the newest safe
    /// point within <paramref name="maxDistance"/> at any clearance. Null when nothing qualifies.
    ///
    /// ⚠️ <b>THE MAXIMUM DISTANCE IS A TELEPORT GUARD.</b> A fast-travel jump, a portal or a load
    /// moves the player without walking them there, so the trail can still hold the ground they left
    /// on the other side of the realm. Recovering to it would be a free fast travel out of any hole.
    /// </summary>
    public Vector3? Pick(
        Vector3 hazard, Func<Vector3, bool> stillSafe, float minHazardDistance, float maxDistance)
    {
        Vector3? fallback = null;
        foreach (Vector3 point in _points)
        {
            float distance = Horizontal(point, hazard);
            if (distance > maxDistance || !stillSafe(point))
            {
                continue;
            }
            if (distance >= minHazardDistance)
            {
                return point;
            }
            fallback ??= point;
        }
        return fallback;
    }

    private static float Horizontal(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }
}
