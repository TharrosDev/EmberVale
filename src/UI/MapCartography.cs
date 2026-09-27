using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>Builds the map's land and road layers from the same data that builds the playable world:
/// the prepared region heightfield and the authored presentation paths.</summary>
public static class MapCartography
{
    /// <summary>Metres per relief-texture pixel. A kilometre realm is a 260-pixel texture.</summary>
    private const int ReliefStride = 4;

    private static readonly Dictionary<string, (ImageTexture Texture, Rect2 World)> ReliefCache = new();

    /// <summary>Every authored road in <paramref name="regionId"/>. A homeland map shows its roads;
    /// what the player has not found yet are the places on them.</summary>
    public static List<MapRoadSegment> Roads(string regionId)
    {
        var roads = new List<MapRoadSegment>();
        if (RegionDatabase.Get(regionId) is not { } region)
        {
            return roads;
        }

        foreach (RegionCellResource? cell in region.Cells)
        {
            if (cell?.Presentation is not { } presentation)
            {
                continue;
            }

            var offset = new Vector2(cell.Center.X, cell.Center.Z);
            foreach (WorldPathSegmentResource? path in presentation.Paths)
            {
                if (path != null)
                {
                    roads.Add(new MapRoadSegment(offset + path.Start, offset + path.End, path.Width));
                }
            }
        }

        return roads;
    }

    /// <summary>
    /// A shaded relief of the whole region, drawn from the baked heightfield (2026-09 world rebuild).
    ///
    /// ⚠️ <b>WHY NOT THE CELL FOOTPRINTS.</b> The land layer used to be one filled rectangle per
    /// streamed cell with a per-cell tone shift, which drew the streaming lattice onto the map in
    /// exactly the way the world rebuild removed it from the ground. A map is the country's shape,
    /// not its loading partition: ridges, the lake, the valley. Null when the region has no bake.
    /// </summary>
    public static (ImageTexture Texture, Rect2 World)? Relief(string regionId)
    {
        if (string.IsNullOrEmpty(regionId))
        {
            return null;
        }
        if (ReliefCache.TryGetValue(regionId, out (ImageTexture Texture, Rect2 World) cached))
        {
            return cached;
        }
        string path = WorldBakePaths.Region(regionId);
        if (!ResourceLoader.Exists(path) || ResidentResources.Load<WorldPreparedRegionResource>(path) is not { } prepared ||
            prepared.Columns < ReliefStride * 2 || prepared.Rows < ReliefStride * 2)
        {
            return null;
        }

        int width = prepared.Columns / ReliefStride;
        int height = prepared.Rows / ReliefStride;
        float low = float.MaxValue;
        float high = float.MinValue;
        foreach (float h in prepared.Heights)
        {
            low = Math.Min(low, h);
            high = Math.Max(high, h);
        }

        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        float metres = prepared.SampleStep * ReliefStride;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * ReliefStride * prepared.Columns) + (x * ReliefStride);
                float h = prepared.Heights[index];
                float east = prepared.Heights[(y * ReliefStride * prepared.Columns) + Math.Min(prepared.Columns - 1, (x + 1) * ReliefStride)];
                float south = prepared.Heights[(Math.Min(prepared.Rows - 1, (y + 1) * ReliefStride) * prepared.Columns) + (x * ReliefStride)];
                // North-west light, soft: a map hints at relief rather than rendering it.
                float shade = Math.Clamp(0.86f + (((h - east) + (h - south)) * 0.22f / metres), 0.55f, 1.08f);
                float t = high > low ? (h - low) / (high - low) : 0f;
                var ground = new Color(0.40f + (0.16f * t), 0.37f + (0.13f * t), 0.30f + (0.12f * t), 0.92f);
                Color colour = new(ground.R * shade, ground.G * shade, ground.B * shade, ground.A);
                if (prepared.GeneratedWaterSurfaces.Length > index &&
                    prepared.GeneratedWaterSurfaces[index] > h + 0.05f)
                {
                    colour = new Color(0.22f, 0.30f, 0.34f, 0.95f);
                }
                image.SetPixel(x, y, colour);
            }
        }

        PaintAuthoredWater(image, prepared, regionId, metres);

        var world = new Rect2(prepared.MinX, prepared.MinZ, width * metres, height * metres);
        (ImageTexture, Rect2) relief = (ImageTexture.CreateFromImage(image), world);
        ReliefCache[regionId] = relief;
        return relief;
    }

    /// <summary>A declared lake is drawn where its basin is: inside the body's extent, ground lower
    /// than the rim of that extent is under water. The shoreline is the terrain's own contour, the same
    /// rule the in-world water surface follows.</summary>
    private static void PaintAuthoredWater(Image image, WorldPreparedRegionResource prepared, string regionId, float metres)
    {
        if (RegionDatabase.Get(regionId) is not { } region)
        {
            return;
        }
        foreach (RegionCellResource? cell in region.Cells)
        {
            if (cell?.Presentation == null)
            {
                continue;
            }
            foreach (WorldWaterResource? water in cell.Presentation.Water)
            {
                if (water == null)
                {
                    continue;
                }
                float cx = cell.Center.X + water.Center.X;
                float cz = cell.Center.Z + water.Center.Y;
                var rim = new List<float>();
                for (int i = 0; i < 16; i++)
                {
                    float a = i * MathF.Tau / 16f;
                    rim.Add(HeightAt(prepared, cx + (MathF.Cos(a) * water.Extent.X), cz + (MathF.Sin(a) * water.Extent.Y)));
                }
                rim.Sort();
                float surface = rim[rim.Count / 2] - 0.6f;
                int x0 = Math.Max(0, (int)((cx - water.Extent.X - prepared.MinX) / metres));
                int x1 = Math.Min(image.GetWidth() - 1, (int)((cx + water.Extent.X - prepared.MinX) / metres));
                int y0 = Math.Max(0, (int)((cz - water.Extent.Y - prepared.MinZ) / metres));
                int y1 = Math.Min(image.GetHeight() - 1, (int)((cz + water.Extent.Y - prepared.MinZ) / metres));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (HeightAt(prepared, prepared.MinX + (x * metres), prepared.MinZ + (y * metres)) < surface)
                        {
                            image.SetPixel(x, y, new Color(0.22f, 0.30f, 0.34f, 0.95f));
                        }
                    }
                }
            }
        }
    }

    private static float HeightAt(WorldPreparedRegionResource prepared, float worldX, float worldZ)
    {
        int column = Math.Clamp((int)((worldX - prepared.MinX) / prepared.SampleStep), 0, prepared.Columns - 1);
        int row = Math.Clamp((int)((worldZ - prepared.MinZ) / prepared.SampleStep), 0, prepared.Rows - 1);
        return prepared.Heights[(row * prepared.Columns) + column];
    }
}
