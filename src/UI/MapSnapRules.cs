using System;
using System.Collections.Generic;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The map's gamepad cursor: which pin the reticle at the centre of the plot snaps to, what the
/// waypoint button does there, and how far one zoom step goes. Godot structs only, so it is tested
/// without an engine.
/// </summary>
public static class MapSnapRules
{
    /// <summary>How near the reticle a pin must be to take the snap, in pixels.</summary>
    public const float SnapRadius = 56f;

    /// <summary>One step of the zoom buttons.</summary>
    public const float ZoomStep = 1.3f;

    /// <summary>Lengths a scale bar may show, in metres.</summary>
    private static readonly int[] ScaleSteps = { 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000 };

    /// <summary>The index of the point nearest <paramref name="at"/> within
    /// <paramref name="radius"/>, or -1. The earlier point wins a tie.</summary>
    public static int Nearest(IReadOnlyList<Vector2> points, Vector2 at, float radius)
    {
        int best = -1;
        float bestDistance = radius * radius;
        for (int i = 0; i < points.Count; i++)
        {
            float distance = points[i].DistanceSquaredTo(at);
            if (distance < bestDistance || (best < 0 && distance <= bestDistance))
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Whether the waypoint button clears the mark (the reticle is on it) rather than
    /// moving it.</summary>
    public static bool ClearsWaypoint(Vector2? waypointOnScreen, Vector2 at, float radius) =>
        waypointOnScreen is { } mark && mark.DistanceSquaredTo(at) <= radius * radius;

    /// <summary>The zoom one step in (<paramref name="delta"/> above zero) or out from
    /// <paramref name="zoom"/>, inside the projection's limits.</summary>
    public static float StepZoom(float zoom, int delta)
    {
        float stepped = delta > 0 ? zoom * ZoomStep : delta < 0 ? zoom / ZoomStep : zoom;
        return Math.Clamp(stepped, MapProjection.MinZoom, MapProjection.MaxZoom);
    }

    /// <summary>The longest round length, in metres, whose bar is no wider than
    /// <paramref name="maxPixels"/> at <paramref name="zoom"/> pixels per metre.</summary>
    public static int ScaleBarMetres(float zoom, float maxPixels)
    {
        int best = ScaleSteps[0];
        foreach (int step in ScaleSteps)
        {
            if (step * zoom <= maxPixels)
            {
                best = step;
            }
        }

        return best;
    }
}
