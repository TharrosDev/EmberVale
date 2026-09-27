using System;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Headless cartography: <c>godot --headless --path . -- --worldmap [out_dir]</c> renders each region's
/// real <see cref="WorldHeightfield"/> — the generator plus every authored landform, road, pad and
/// water body — as a lit relief map, then quits 0.
///
/// ⚠️ <b>WHY IT EXISTS.</b> A realm is designed in a region spec as numbers, and the only other way
/// to see what those numbers make is a full bake and a rendered walk. This is the planning view the
/// 2026-09 world rebuild needed and did not have: where the ridges really run, where the drainage
/// solve put the rivers, whether a road climbs a hill or rounds it, and — with <c>--grid</c> — whether
/// the places line up on the streaming lattice. No cell lines are drawn unless asked for, because a
/// map a designer reads with the grid on is a map they compose to the grid.
///
/// Baked place positions (<see cref="WorldPlaceIndex"/>) are drawn when a bake exists. Always exits 0.
/// </summary>
public static class HeadlessWorldMap
{
    public const string FlagArgument = "--worldmap";

    /// <summary>Metres per pixel.</summary>
    private static float Scale = 1.5f;

    public static bool Requested() => HeadlessValidation.HasFlag(FlagArgument);

    public static void Run(SceneTree tree)
    {
        ContentDatabases.InitializeAll();
        string output = "user://worldmap";
        bool grid = false;
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == FlagArgument && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                output = args[i + 1];
            }
            grid |= args[i] == "--grid";
            if (args[i] == "--scale" && i + 1 < args.Length)
            {
                Scale = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(output));

        // --raw <region.id> minX,minZ,maxX,maxZ : the bare generator over any rectangle, no authoring,
        // with a 100 m graticule — the ground a new layout will be laid onto, before it exists.
        int raw = Array.IndexOf(args, "--raw");
        if (raw >= 0 && raw + 2 < args.Length && RegionDatabase.Get(args[raw + 1])?.GenerationProfile is { } profile)
        {
            float[] r = Array.ConvertAll(args[raw + 2].Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
            WorldGenerationSettings settings = profile.Settings();
            // --set Key=Value,... tries a profile change on the preview only (art-direction iteration).
            int set = Array.IndexOf(args, "--set");
            if (set >= 0 && set + 1 < args.Length)
            {
                foreach (string pair in args[set + 1].Split(','))
                {
                    string[] kv = pair.Split('=');
                    float v = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
                    settings = kv[0] switch
                    {
                        "MacroScale" => settings with { MacroScale = v },
                        "MacroRelief" => settings with { MacroRelief = v },
                        "MountainScale" => settings with { MountainScale = v },
                        "MountainPrevalence" => settings with { MountainPrevalence = v },
                        "MountainHeight" => settings with { MountainHeight = v },
                        "ValleyStrength" => settings with { ValleyStrength = v },
                        "ErosionStrength" => settings with { ErosionStrength = v },
                        "LocalRelief" => settings with { LocalRelief = v },
                        "RiverThreshold" => settings with { RiverThreshold = v },
                        _ => settings,
                    };
                }
            }
            var bare = new WorldHeightfield(settings, r[0] - 96f, r[1] - 96f, r[2] + 96f, r[3] + 96f,
                new System.Collections.Generic.List<WorldTerrainMath.Path>(),
                new System.Collections.Generic.List<WorldTerrainMath.GroundArea>());
            Render(bare, new Aabb(new Vector3(r[0], 0f, r[1]), new Vector3(r[2] - r[0], 1f, r[3] - r[1])),
                $"{output}/raw_{args[raw + 1].Replace('.', '_')}.png", null, grid: false, graticule: true);
            tree.Quit(0);
            return;
        }

        foreach (RegionResource region in RegionDatabase.All)
        {
            Render(WorldTerrainMeshBuilder.HeightfieldFor(region), region.Bounds,
                $"{output}/{region.Id.Replace('.', '_')}.png", region, grid, graticule: false);
        }

        tree.Quit(0);
    }

    private static void Render(WorldHeightfield field, Aabb bounds, string path, RegionResource? region,
        bool grid, bool graticule)
    {
        int width = (int)(bounds.Size.X / Scale);
        int height = (int)(bounds.Size.Z / Scale);
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);

        float low = float.MaxValue;
        float high = float.MinValue;
        var heights = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = field.Height(bounds.Position.X + (x * Scale), bounds.Position.Z + (y * Scale));
                heights[(y * width) + x] = h;
                low = Math.Min(low, h);
                high = Math.Max(high, h);
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float wx = bounds.Position.X + (x * Scale);
                float wz = bounds.Position.Z + (y * Scale);
                float h = heights[(y * width) + x];
                float east = heights[(y * width) + Math.Min(width - 1, x + 1)];
                float south = heights[(Math.Min(height - 1, y + 1) * width) + x];
                // North-west light over a 1.5 m grid: a 45 degree slope reads as full shadow.
                float shade = Math.Clamp(0.72f + (((h - east) + (h - south)) * 0.35f / Scale), 0.3f, 1.1f);
                float t = high > low ? (h - low) / (high - low) : 0f;
                var ground = new Color(0.36f + (0.34f * t), 0.40f + (0.26f * t), 0.28f + (0.30f * t));
                Color colour = ground * shade;

                if (field.GeneratedWaterSurface(wx, wz) is { } water && water > h + 0.05f)
                {
                    colour = new Color(0.22f, 0.38f, 0.52f);
                }
                float road = field.PathMask(wx, wz);
                if (road > 0.35f)
                {
                    colour = colour.Lerp(new Color(0.86f, 0.78f, 0.58f), Math.Min(1f, road));
                }
                float pad = field.AreaMask(wx, wz);
                if (pad > 0.5f)
                {
                    colour = colour.Lerp(new Color(0.72f, 0.62f, 0.52f), 0.35f);
                }
                // Ten-metre contours, faint: enough to read a ridge's line without drawing a grid.
                if (Scale <= 2f && Math.Abs((h % 10f) - 5f) > 4.6f)
                {
                    colour = colour.Darkened(0.12f);
                }
                image.SetPixel(x, y, colour);
            }
        }

        if (region != null)
        {
            DrawAuthoredWater(image, region, bounds);
            if (grid)
            {
                DrawCells(image, region, bounds);
            }
            DrawPlaces(image, region, bounds);
        }
        if (graticule)
        {
            float step = Scale > 2f ? 500f : 100f;
            for (float gx = Mathf.Ceil(bounds.Position.X / step) * step; gx < bounds.End.X; gx += step)
            {
                Outline(image, bounds, new Vector2(gx, bounds.Position.Z), new Vector2(gx, bounds.End.Z), new Color(1f, 1f, 1f));
            }
            for (float gz = Mathf.Ceil(bounds.Position.Z / step) * step; gz < bounds.End.Z; gz += step)
            {
                Outline(image, bounds, new Vector2(bounds.Position.X, gz), new Vector2(bounds.End.X, gz), new Color(1f, 1f, 1f));
            }
        }

        image.SavePng(path);
        Log.Info($"World map: {width}x{height} px at {Scale} m/px, relief {low:0.0}..{high:0.0} m -> {ProjectSettings.GlobalizePath(path)}");
    }

    private static void DrawAuthoredWater(Image image, RegionResource region, Aabb bounds)
    {
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
                Vector2 centre = new(cell.Center.X + water.Center.X, cell.Center.Z + water.Center.Y);
                Outline(image, bounds, centre - water.Extent, centre + water.Extent, new Color(0.3f, 0.5f, 0.75f));
            }
        }
    }

    private static void DrawCells(Image image, RegionResource region, Aabb bounds)
    {
        foreach (RegionCellResource? cell in region.Cells)
        {
            if (cell?.Presentation is not { } p)
            {
                continue;
            }
            Vector2 half = new(p.Width * 0.5f, p.Depth * 0.5f);
            Vector2 centre = new(cell.Center.X, cell.Center.Z);
            Outline(image, bounds, centre - half, centre + half, new Color(0.9f, 0.2f, 0.2f));
        }
    }

    private static void DrawPlaces(Image image, RegionResource region, Aabb bounds)
    {
        string path = WorldBakePaths.Region(region.Id);
        if (!ResourceLoader.Exists(path) || ResidentResources.Load<WorldPreparedRegionResource>(path) is not { } prepared)
        {
            return;
        }
        foreach (string key in prepared.Places.Keys)
        {
            Vector3 at = prepared.Places[key];
            int px = (int)((at.X - bounds.Position.X) / Scale);
            int py = (int)((at.Z - bounds.Position.Z) / Scale);
            bool travel = key.StartsWith("travel:", StringComparison.Ordinal);
            string id = key.Substring(key.IndexOf(':') + 1);
            MapTier tier = MapLocationDatabase.Get(id)?.EffectiveTier ?? MapTier.Detail;
            if (!travel && tier == MapTier.Detail)
            {
                continue;
            }
            int r = travel ? 3 : tier == MapTier.Primary ? 5 : 2;
            Color c = travel ? new Color(0.3f, 0.9f, 1f) : tier == MapTier.Primary ? new Color(1f, 0.25f, 0.1f) : new Color(1f, 0.9f, 0.2f);
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if ((dx * dx) + (dy * dy) <= r * r && px + dx >= 0 && py + dy >= 0 &&
                        px + dx < image.GetWidth() && py + dy < image.GetHeight())
                    {
                        image.SetPixel(px + dx, py + dy, c);
                    }
                }
            }
        }
    }

    private static void Outline(Image image, Aabb bounds, Vector2 min, Vector2 max, Color colour)
    {
        int x0 = (int)((min.X - bounds.Position.X) / Scale);
        int x1 = (int)((max.X - bounds.Position.X) / Scale);
        int y0 = (int)((min.Y - bounds.Position.Z) / Scale);
        int y1 = (int)((max.Y - bounds.Position.Z) / Scale);
        for (int x = Math.Max(0, x0); x <= Math.Min(image.GetWidth() - 1, x1); x++)
        {
            Plot(image, x, y0, colour);
            Plot(image, x, y1, colour);
        }
        for (int y = Math.Max(0, y0); y <= Math.Min(image.GetHeight() - 1, y1); y++)
        {
            Plot(image, x0, y, colour);
            Plot(image, x1, y, colour);
        }
    }

    private static void Plot(Image image, int x, int y, Color colour)
    {
        if (x >= 0 && y >= 0 && x < image.GetWidth() && y < image.GetHeight())
        {
            image.SetPixel(x, y, colour);
        }
    }
}
