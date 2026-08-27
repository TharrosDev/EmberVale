using System;

namespace Embervale.World;

/// <summary>Engine-free, continuous heightfield math used by terrain mesh generation and tests.</summary>
public static class WorldTerrainMath
{
    public static float Height(
        int seed, float worldX, float worldZ, float localX, float localZ,
        float width, float depth, float relief, float detailScale,
        int roadAxis, float roadWidth, float roadOffset)
    {
        if (width <= 0f || depth <= 0f || relief <= 0f)
        {
            return 0f;
        }

        float edge = MathF.Max(MathF.Abs(localX) / (width * 0.5f), MathF.Abs(localZ) / (depth * 0.5f));
        float seamFade = 1f - SmoothStep(0.76f, 1f, edge);
        float scale = MathF.Max(0.05f, detailScale);
        float macro = ValueNoise(seed, worldX * 0.055f * scale, worldZ * 0.055f * scale);
        float detail = ValueNoise(seed + 1709, worldX * 0.19f * scale, worldZ * 0.19f * scale);
        float height = (((macro - 0.5f) * 1.35f) + ((detail - 0.5f) * 0.28f)) * relief * seamFade;

        float crossAxis = roadAxis == 1 ? localX : localZ;
        float road = roadAxis == 0
            ? 0f
            : 1f - SmoothStep(roadWidth * 0.42f, roadWidth * 0.62f, MathF.Abs(crossAxis - roadOffset));
        return height * (1f - (road * 0.88f));
    }

    public static float ValueNoise(int seed, float x, float z)
    {
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        float tx = SmoothCurve(x - x0);
        float tz = SmoothCurve(z - z0);
        float a = Unit2D(seed, x0, z0);
        float b = Unit2D(seed, x0 + 1, z0);
        float c = Unit2D(seed, x0, z0 + 1);
        float d = Unit2D(seed, x0 + 1, z0 + 1);
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), tz);
    }

    private static float Unit2D(int seed, int x, int z)
    {
        int index = unchecked((x * 73856093) ^ (z * 19349663));
        return WorldSceneryMath.Unit(seed, index);
    }

    private static float SmoothCurve(float value) => value * value * (3f - (2f * value));
    private static float Lerp(float a, float b, float amount) => a + ((b - a) * amount);

    private static float SmoothStep(float from, float to, float value)
    {
        if (to <= from)
        {
            return value >= to ? 1f : 0f;
        }
        float t = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return SmoothCurve(t);
    }
}
