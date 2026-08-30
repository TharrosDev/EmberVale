using System;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>
/// The realm's water, in world space, and the one place anything can ask "how deep is it here?".
///
/// ⚠️ <b>EMBERVALE'S NON-SWIMMING WATER CONTRACT LIVES HERE, AND IT APPLIES TO EVERY REGION EVER
/// ADDED.</b> There is no swimming in this game and there is not going to be one for this pass, but
/// the world now has real basins with steep banks in it. The contract is three lines and every
/// future water body inherits it by being declared:
/// <list type="number">
/// <item><b>Under <see cref="WadeDepth"/> is ordinary ground.</b> The player walks it, fights in it
/// and is not interrupted. Shallow margins are a feature — they are what makes a shore read as a
/// shore rather than as a wall.</item>
/// <item><b>Over <see cref="WadeDepth"/> is out of bounds, and the LAND is what says so.</b> Author
/// a bank steeper than the 45-degree floor limit, exactly as Hollowreach's drop-off and the Tarn's
/// shelf do. An invisible wall is forbidden: the terrain can communicate this and a collider cannot.
/// </item>
/// <item><b>Over <see cref="DrownDepth"/>, the player is recovered.</b> Not killed, not respawned at
/// a shrine — put back on the last dry ground they stood on, which
/// <see cref="WorldWaterSafety"/> remembers. A trap that costs progress is still a trap.</item>
/// </list>
///
/// Rule 3 is what makes rules 1 and 2 safe to author against: no arrangement of terrain, physics
/// bug, knockback, dismount or dragon breath can leave a player stuck in water, so an author may
/// dig a real basin without having to prove that every metre of its rim is climbable.
///
/// Engine-free on purpose, like <see cref="WorldTerrainMath"/> — the unit suite drives it directly.
/// </summary>
public static class WorldWater
{
    /// <summary>Deepest water the player simply walks through.</summary>
    public const float WadeDepth = 1.1f;

    /// <summary>Depth at which the recovery contract engages.</summary>
    public const float DrownDepth = 1.9f;

    /// <summary>One declared body, in world X/Z, with an absolute surface height.</summary>
    public readonly record struct Body(float X, float Z, float ExtentX, float ExtentZ, float SurfaceY);

    private static IReadOnlyList<Body> _bodies = Array.Empty<Body>();

    public static IReadOnlyList<Body> Bodies => _bodies;

    /// <summary>Set by <see cref="RegionStreamer.Configure"/>; cleared when no region is active.</summary>
    public static void Set(IReadOnlyList<Body>? bodies) => _bodies = bodies ?? Array.Empty<Body>();

    /// <summary>Every body of a region, pooled into world space. Pure — the streamer and the
    /// validators both call it, so they can never disagree about where the water is.</summary>
    public static List<Body> BodiesFor(RegionResource region)
    {
        var bodies = new List<Body>();
        foreach (RegionCellResource cell in region.Cells)
        {
            if (cell?.Presentation == null)
            {
                continue;
            }
            foreach (WorldWaterResource? water in cell.Presentation.Water)
            {
                if (water == null || water.Extent.X <= 0f || water.Extent.Y <= 0f)
                {
                    continue;
                }
                bodies.Add(new Body(
                    cell.Center.X + water.Center.X, cell.Center.Z + water.Center.Y,
                    water.Extent.X, water.Extent.Y, water.SurfaceY));
            }
        }
        return bodies;
    }

    /// <summary>The highest water surface covering a point, or null where there is none.</summary>
    public static float? SurfaceAt(float worldX, float worldZ) =>
        SurfaceAt(worldX, worldZ, _bodies);

    public static float? SurfaceAt(float worldX, float worldZ, IReadOnlyList<Body> bodies)
    {
        float? best = null;
        for (int i = 0; i < bodies.Count; i++)
        {
            Body body = bodies[i];
            if (MathF.Abs(worldX - body.X) <= body.ExtentX &&
                MathF.Abs(worldZ - body.Z) <= body.ExtentZ &&
                (best == null || body.SurfaceY > best))
            {
                best = body.SurfaceY;
            }
        }
        return best;
    }

    /// <summary>
    /// Water depth over the ground at a point: 0 on dry land and everywhere outside a body.
    /// A body whose surface lies below the terrain inside it contributes nothing, which is how a
    /// generously-drawn rectangle costs nothing where it overhangs the bank.
    /// </summary>
    public static float DepthAt(float worldX, float worldZ, WorldHeightfield? field)
    {
        float? surface = SurfaceAt(worldX, worldZ);
        return surface == null || field == null
            ? 0f
            : MathF.Max(0f, surface.Value - field.Height(worldX, worldZ));
    }
}
