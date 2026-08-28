using System;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>Engine-free, continuous heightfield math used by terrain mesh generation and tests.</summary>
public static class WorldTerrainMath
{
    public readonly record struct Path(float StartX, float StartZ, float EndX, float EndZ, float Width, float Shoulder);
    public readonly record struct GroundArea(float X, float Z, float RadiusX, float RadiusZ, float Feather, float SurfaceBlend);

    public static float Height(
        int seed, float worldX, float worldZ, float localX, float localZ,
        float width, float depth, float relief, float detailScale,
        int roadAxis, float roadWidth, float roadOffset,
        IReadOnlyList<Path>? paths = null,
        IReadOnlyList<GroundArea>? groundAreas = null)
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
        road = MathF.Max(road, PathMask(localX, localZ, paths));
        float activity = GroundAreaMask(localX, localZ, groundAreas);
        return height * (1f - (MathF.Max(road * 0.88f, activity * 0.94f)));
    }

    public static float PathMask(float x, float z, IReadOnlyList<Path>? paths)
    {
        if (paths == null)
        {
            return 0f;
        }

        float mask = 0f;
        foreach (Path path in paths)
        {
            float halfWidth = MathF.Max(0.1f, path.Width * 0.5f);
            float feather = MathF.Max(0.1f, path.Shoulder);
            float distance = DistanceToSegment(x, z, path.StartX, path.StartZ, path.EndX, path.EndZ);
            mask = MathF.Max(mask, 1f - SmoothStep(halfWidth, halfWidth + feather, distance));
        }
        return mask;
    }

    public static float GroundAreaMask(float x, float z, IReadOnlyList<GroundArea>? areas)
    {
        if (areas == null)
        {
            return 0f;
        }

        float mask = 0f;
        foreach (GroundArea area in areas)
        {
            float radiusX = MathF.Max(0.1f, area.RadiusX);
            float radiusZ = MathF.Max(0.1f, area.RadiusZ);
            float normalized = MathF.Sqrt(((x - area.X) * (x - area.X) / (radiusX * radiusX)) +
                                          ((z - area.Z) * (z - area.Z) / (radiusZ * radiusZ)));
            float feather = MathF.Max(0.01f, area.Feather / MathF.Max(radiusX, radiusZ));
            mask = MathF.Max(mask, (1f - SmoothStep(1f, 1f + feather, normalized)) *
                                  Math.Clamp(area.SurfaceBlend, 0f, 1f));
        }
        return mask;
    }

    public static bool InsidePath(float x, float z, Path path, float extra = 0f) =>
        DistanceToSegment(x, z, path.StartX, path.StartZ, path.EndX, path.EndZ) <
        MathF.Max(0f, (path.Width * 0.5f) + path.Shoulder + extra);

    public static bool InsideGroundArea(float x, float z, GroundArea area, float extra = 0f)
    {
        float radiusX = MathF.Max(0.1f, area.RadiusX + area.Feather + extra);
        float radiusZ = MathF.Max(0.1f, area.RadiusZ + area.Feather + extra);
        float dx = (x - area.X) / radiusX;
        float dz = (z - area.Z) / radiusZ;
        return (dx * dx) + (dz * dz) < 1f;
    }

    private static float DistanceToSegment(float x, float z, float ax, float az, float bx, float bz)
    {
        float abx = bx - ax;
        float abz = bz - az;
        float lengthSquared = (abx * abx) + (abz * abz);
        if (lengthSquared <= 0.0001f)
        {
            float dx = x - ax;
            float dz = z - az;
            return MathF.Sqrt((dx * dx) + (dz * dz));
        }
        float t = Math.Clamp((((x - ax) * abx) + ((z - az) * abz)) / lengthSquared, 0f, 1f);
        float px = ax + (abx * t);
        float pz = az + (abz * t);
        float offsetX = x - px;
        float offsetZ = z - pz;
        return MathF.Sqrt((offsetX * offsetX) + (offsetZ * offsetZ));
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
