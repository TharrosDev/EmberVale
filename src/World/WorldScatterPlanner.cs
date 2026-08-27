using System;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>A circular no-dressing area in cell-local X/Z space.</summary>
public readonly record struct WorldScatterExclusion(float X, float Z, float Radius);

/// <summary>One deterministic, cell-local cosmetic placement.</summary>
public readonly record struct WorldScatterPlacement(float X, float Z, float Yaw, float ScaleUnit);

/// <summary>
/// Engine-free rejection sampler for cosmetic biome dressing. The planner owns the placement
/// contract; the Godot renderer only turns the accepted points into MultiMesh transforms.
/// </summary>
public static class WorldScatterPlanner
{
    public static IReadOnlyList<WorldScatterPlacement> Plan(
        int seed,
        int requestedCount,
        float width,
        float depth,
        float edgePadding,
        int roadAxis,
        float roadWidth,
        float roadOffset,
        float minimumSpacing,
        IReadOnlyList<WorldScatterExclusion>? exclusions = null)
    {
        var accepted = new List<WorldScatterPlacement>(Math.Max(0, requestedCount));
        if (requestedCount <= 0 || width <= 0f || depth <= 0f)
        {
            return accepted;
        }

        float halfWidth = MathF.Max(0f, (width * 0.5f) - edgePadding);
        float halfDepth = MathF.Max(0f, (depth * 0.5f) - edgePadding);
        float spacingSquared = MathF.Max(0f, minimumSpacing * minimumSpacing);
        int attemptBudget = Math.Max(128, requestedCount * 40);

        for (int attempt = 0; attempt < attemptBudget && accepted.Count < requestedCount; attempt++)
        {
            float x = ((WorldSceneryMath.Unit(seed, attempt * 4) * 2f) - 1f) * halfWidth;
            float z = ((WorldSceneryMath.Unit(seed, (attempt * 4) + 1) * 2f) - 1f) * halfDepth;

            if (InsideRoad(x, z, roadAxis, roadWidth, roadOffset) ||
                InsideExclusion(x, z, exclusions) || TooClose(x, z, accepted, spacingSquared))
            {
                continue;
            }

            accepted.Add(new WorldScatterPlacement(
                x,
                z,
                WorldSceneryMath.Unit(seed, (attempt * 4) + 2) * MathF.Tau,
                WorldSceneryMath.Unit(seed, (attempt * 4) + 3)));
        }

        return accepted;
    }

    private static bool InsideRoad(float x, float z, int axis, float width, float offset)
    {
        float half = MathF.Max(0f, width * 0.5f);
        return axis switch
        {
            1 => MathF.Abs(x - offset) < half, // north/south road varies along Z
            2 => MathF.Abs(z - offset) < half, // east/west road varies along X
            _ => false,
        };
    }

    private static bool InsideExclusion(
        float x, float z, IReadOnlyList<WorldScatterExclusion>? exclusions)
    {
        if (exclusions == null)
        {
            return false;
        }

        foreach (WorldScatterExclusion exclusion in exclusions)
        {
            float dx = x - exclusion.X;
            float dz = z - exclusion.Z;
            if ((dx * dx) + (dz * dz) < exclusion.Radius * exclusion.Radius)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TooClose(
        float x, float z, IReadOnlyList<WorldScatterPlacement> accepted, float spacingSquared)
    {
        if (spacingSquared <= 0f)
        {
            return false;
        }

        foreach (WorldScatterPlacement placement in accepted)
        {
            float dx = x - placement.X;
            float dz = z - placement.Z;
            if ((dx * dx) + (dz * dz) < spacingSquared)
            {
                return true;
            }
        }

        return false;
    }
}
