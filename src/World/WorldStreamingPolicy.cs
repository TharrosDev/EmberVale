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

    public static float DistanceToFootprint(Vector3 point, Vector3 center, Vector2 halfExtent)
    {
        float dx = Mathf.Max(Mathf.Abs(point.X - center.X) - halfExtent.X, 0f);
        float dz = Mathf.Max(Mathf.Abs(point.Z - center.Z) - halfExtent.Y, 0f);
        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }
}
