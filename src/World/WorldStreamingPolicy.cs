using Godot;

namespace Embervale.World;

/// <summary>Runtime fidelity of one prepared streaming cell.</summary>
public enum WorldStreamingTier
{
    Unloaded = 0,
    Backdrop = 1,
    Far = 2,
    Mid = 3,
    Near = 4,
}

public readonly record struct WorldStreamingLimits(
    float NearDistance,
    float MidDistance,
    float FarDistance,
    float BackdropDistance,
    float Hysteresis,
    float PredictionSeconds,
    float PredictionDistanceWeight);

/// <summary>Pure predictive tier selection, shared by runtime and unit tests.</summary>
public static class WorldStreamingPolicy
{
    public static WorldStreamingTier DesiredTier(
        Vector3 position, Vector3 velocity, Vector3 center, Vector2 halfExtent,
        WorldStreamingTier current, WorldStreamingLimits limits, bool required)
    {
        if (required)
        {
            return WorldStreamingTier.Near;
        }

        Vector3 predicted = position + (velocity * limits.PredictionSeconds);
        float now = DistanceToFootprint(position, center, halfExtent);
        float predictedDistance = DistanceToFootprint(predicted, center, halfExtent);
        float distance = velocity.LengthSquared() > 0.01f && predictedDistance < now
            ? Mathf.Min(now, predictedDistance * limits.PredictionDistanceWeight)
            : now;
        float hysteresis = current == WorldStreamingTier.Unloaded ? 0f : limits.Hysteresis;

        if (distance <= limits.NearDistance + (current == WorldStreamingTier.Near ? hysteresis : 0f))
        {
            return WorldStreamingTier.Near;
        }
        if (distance <= limits.MidDistance + (current >= WorldStreamingTier.Mid ? hysteresis : 0f))
        {
            return WorldStreamingTier.Mid;
        }
        if (distance <= limits.FarDistance + (current >= WorldStreamingTier.Far ? hysteresis : 0f))
        {
            return WorldStreamingTier.Far;
        }
        if (distance <= limits.BackdropDistance + (current >= WorldStreamingTier.Backdrop ? hysteresis : 0f))
        {
            return WorldStreamingTier.Backdrop;
        }
        return WorldStreamingTier.Unloaded;
    }

    /// <summary>
    /// The authored radii under a quality tier's draw-distance multiplier. Only Far moves: Near and
    /// Mid decide where collision, navigation and gameplay exist, and Backdrop decides which cells
    /// are resident at all — the backdrop mesh has a hole over the lattice, so a cell that unloads
    /// is a hole in the ground. Far only decides how far away a cell's props and buildings are
    /// drawn, which is the part a weaker machine can give up. Identity at 1.
    /// </summary>
    public static WorldStreamingLimits ScaleForQuality(WorldStreamingLimits limits, float drawDistance)
    {
        if (Mathf.IsEqualApprox(drawDistance, 1f))
        {
            return limits;
        }
        float far = Mathf.Clamp(
            limits.FarDistance * drawDistance, limits.MidDistance,
            Mathf.Max(limits.MidDistance, limits.BackdropDistance));
        return limits with { FarDistance = far };
    }

    public static float DistanceToFootprint(Vector3 point, Vector3 center, Vector2 halfExtent)
    {
        float dx = Mathf.Max(Mathf.Abs(point.X - center.X) - halfExtent.X, 0f);
        float dz = Mathf.Max(Mathf.Abs(point.Z - center.Z) - halfExtent.Y, 0f);
        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }
}
