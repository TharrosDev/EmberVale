using System;

namespace Embervale.World;

/// <summary>Pure transition math, also exercised without Godot native interop.</summary>
public static class EnvironmentMath
{
    public static float BlendWeight(float delta, float seconds) =>
        1f - MathF.Exp(-Math.Max(0f, delta) / Math.Max(0.01f, seconds));

    public static float Hour(float hour) => ((hour % 24f) + 24f) % 24f;

    public static float KeyWeight(float hour, float from, float to)
    {
        float duration = Hour(to - from);
        if (duration < 0.001f) return 0f;
        float t = Math.Clamp(Hour(hour - from) / duration, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static float SnowFraction(float temperature) => Math.Clamp((0.32f - temperature) / 0.16f, 0f, 1f);
}
