using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Embervale.Core.Diagnostics;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Headless world-generation report. <c>--worldgen</c> loads every region, builds its real
/// <see cref="WorldHeightfield"/>, and prints what the generator actually produces, then quits:
/// <code>godot --headless --path . -- --worldgen</code>
///
/// It answers the two questions no file can. <b>What does the ground do</b> — the elevation range,
/// the steepest grade, how much of the realm the generator calls mountain, valley, wetland or
/// alpine, and how many samples the drainage solve put water on. And <b>where is every authored pad
/// sitting</b> — for each <c>GroundArea</c>, its authored elevation, the generated ground beneath
/// its own centre, and the difference between them.
///
/// ⚠️ <b>THAT SECOND TABLE IS THE MIGRATION, AND IT IS WHY THIS TOOL EXISTS.</b> A pad's elevation
/// was authored as an absolute world Y against a field that was two octaves of noise and never more
/// than about a metre and a half from zero. Under real geography the same number is a step with a
/// cliff on its uphill side. Running this against the OLD field prints the offset each pad should
/// carry once it becomes <c>ElevationMode = RelativeToBase</c> — so the migration is measured out of
/// the running generator rather than recomputed by a second implementation in Python that would
/// drift from this one by the following week.
///
/// Always exits <b>0</b>. It is a report; <c>--validate</c> is the gate.
/// </summary>
public static class HeadlessWorldGen
{
    /// <summary>The command-line argument that triggers the report.</summary>
    public const string FlagArgument = "--worldgen";

    /// <summary>True when <see cref="FlagArgument"/> was passed on the command line.</summary>
    public static bool Requested() => HeadlessValidation.HasFlag(FlagArgument);

    /// <summary>Loads the regions, prints the report, and quits 0.</summary>
    public static void Run(SceneTree tree)
    {
        ContentDatabases.InitializeAll();

        var text = new StringBuilder();
        text.AppendLine("=== Embervale world generation (--worldgen) ===");

        foreach (RegionResource region in RegionDatabase.All)
        {
            WorldHeightfield field = WorldTerrainMeshBuilder.HeightfieldFor(region);
            WorldGenerationSettings settings = field.Settings;

            text.AppendLine();
            text.AppendLine($"--- {region.Id} ---");
            text.AppendLine($"generator: v{settings.Version} seed {settings.Seed}");
            text.AppendLine($"signature: {settings.Signature}");
            text.AppendLine($"hydrology cache: {field.HydrologyBytes / 1024L} KiB");

            Aabb bounds = region.Bounds;
            float minX = bounds.Position.X;
            float minZ = bounds.Position.Z;
            float maxX = minX + bounds.Size.X;
            float maxZ = minZ + bounds.Size.Z;

            float lowest = float.MaxValue;
            float highest = float.MinValue;
            float steepest = 0f;
            int samples = 0;
            int wet = 0;
            int mountain = 0;
            int valley = 0;
            int wetland = 0;
            int alpine = 0;
            int barren = 0;
            var continentalness = new List<float>();
            var mountains = new List<float>();
            var erosions = new List<float>();
            var valleys = new List<float>();
            var temperatures = new List<float>();
            var moistures = new List<float>();
            const float step = 4f;
            for (float z = minZ; z <= maxZ; z += step)
            {
                for (float x = minX; x <= maxX; x += step)
                {
                    WorldSample s = field.Sample(x, z);
                    samples++;
                    lowest = Mathf.Min(lowest, s.Elevation);
                    highest = Mathf.Max(highest, s.Elevation);
                    steepest = Mathf.Max(steepest, s.Slope);
                    if (field.GeneratedWaterSurface(x, z) != null)
                    {
                        wet++;
                    }
                    continentalness.Add(s.Continentalness);
                    mountains.Add(s.Mountain);
                    erosions.Add(s.Erosion);
                    valleys.Add(s.Valley);
                    temperatures.Add(s.Temperature);
                    moistures.Add(s.Moisture);
                    if (s.Mountain > 0.5f)
                    {
                        mountain++;
                    }
                    if (s.Valley > 0.5f)
                    {
                        valley++;
                    }
                    if (s.WetlandWeight > 0.4f)
                    {
                        wetland++;
                    }
                    if (s.AlpineWeight > 0.4f)
                    {
                        alpine++;
                    }
                    if (s.BarrenWeight > 0.4f)
                    {
                        barren++;
                    }
                }
            }

            string Share(int count) =>
                (100f * count / Mathf.Max(1, samples)).ToString("F1", CultureInfo.InvariantCulture) + "%";

            text.AppendLine(FormattableString.Invariant(
                $"ground: {lowest:F1} m .. {highest:F1} m (relief {highest - lowest:F1} m), steepest grade {steepest:F2}"));
            text.AppendLine($"regimes over {samples} samples: mountain {Share(mountain)}, " +
                            $"valley {Share(valley)}, wetland {Share(wetland)}, alpine {Share(alpine)}, " +
                            $"barren {Share(barren)}, generated water {Share(wet)}");

            text.AppendLine(Percentiles("continentalness", continentalness));
            text.AppendLine(Percentiles("mountain", mountains));
            text.AppendLine(Percentiles("erosion", erosions));
            text.AppendLine(Percentiles("valley", valleys));
            text.AppendLine(Percentiles("temperature", temperatures));
            text.AppendLine(Percentiles("moisture", moistures));

            // ⚠️ THE STEEPEST ROUTES, ALWAYS, NOT ONLY THE FAILING ONES. `--validate` names a route
            // once it is over 0.80, which tells you nothing about the one sitting at 0.79 — and a
            // route at 0.79 is exactly the one that a re-tuned region profile, or a finer collision
            // lattice, will push over. Ranking them is how a marginal route is found before it fails.
            var routes = new List<(string Cell, int Index, float Grade)>();
            foreach (RegionCellResource? routeCell in region.Cells)
            {
                WorldCellPresentationResource? routePresentation = routeCell?.Presentation;
                if (routeCell == null || routePresentation == null)
                {
                    continue;
                }

                for (int i = 0; i < routePresentation.Paths.Count; i++)
                {
                    WorldPathSegmentResource? path = routePresentation.Paths[i];
                    if (path == null)
                    {
                        continue;
                    }

                    var from = new Vector2(routeCell.Center.X + path.Start.X, routeCell.Center.Z + path.Start.Y);
                    var to = new Vector2(routeCell.Center.X + path.End.X, routeCell.Center.Z + path.End.Y);
                    float length = from.DistanceTo(to);
                    if (length < 2f)
                    {
                        continue;
                    }

                    int steps = Mathf.CeilToInt(length / 2f);
                    float previous = field.Height(from.X, from.Y);
                    float worst = 0f;
                    for (int step2 = 1; step2 <= steps; step2++)
                    {
                        Vector2 point = from.Lerp(to, step2 / (float)steps);
                        float here = field.Height(point.X, point.Y);
                        worst = Mathf.Max(worst, Mathf.Abs(here - previous) / (length / steps));
                        previous = here;
                    }
                    routes.Add((routeCell.Id, i, worst));
                }
            }

            routes.Sort((a, b) => b.Grade.CompareTo(a.Grade));
            text.AppendLine("steepest authored routes (walk limit 0.80):");
            foreach ((string cellId, int index, float grade) in routes.GetRange(0, Math.Min(8, routes.Count)))
            {
                text.AppendLine(FormattableString.Invariant($"  {cellId,-34} path[{index}]  {grade,5:F2}"));
            }

            text.AppendLine("pads (authored elevation | generated ground | offset):");
            foreach (RegionCellResource? cell in region.Cells)
            {
                WorldCellPresentationResource? presentation = cell?.Presentation;
                if (cell == null || presentation == null)
                {
                    continue;
                }

                foreach (WorldGroundAreaResource? area in presentation.GroundAreas)
                {
                    if (area == null)
                    {
                        continue;
                    }

                    float x = cell.Center.X + area.Center.X;
                    float z = cell.Center.Z + area.Center.Y;
                    // ⚠️ The GENERATED ground, not Height(): a pad must not be levelled against the
                    // pads and roads laid over the same spot, or the offset it reports is a measure
                    // of its own neighbours rather than of the country underneath it.
                    float ground = area.ElevationMode == 1
                        ? area.Elevation
                        : area.Elevation - GroundUnder(field, x, z);
                    float under = GroundUnder(field, x, z);
                    string tag = area.ElevationMode == 1 ? "  [relative]" : string.Empty;
                    // ⚠️ The cell-local centre, not a name. A loaded sub-resource carries no id
                    // back from the .tres, so the local centre is the only thing that identifies a
                    // pad across the report and the file the migration has to edit.
                    text.AppendLine(FormattableString.Invariant(
                        $"  {cell.Id,-30} at=({area.Center.X,7:F1},{area.Center.Y,7:F1}) world=({x,7:F1},{z,7:F1}) {area.Elevation,7:F2} | {under,7:F2} | {ground,7:F2}{tag}"));
                }
            }
        }

        GD.Print(text.ToString());
        Log.Info("worldgen: report complete");
        tree.Quit(0);
    }

    /// <summary>
    /// The shape of one generated field across a whole region, as deciles.
    ///
    /// ⚠️ <b>THIS IS THE ONLY HONEST WAY TO TUNE A RESPONSE CURVE AND IT IS WHY IT IS IN THE
    /// REPORT.</b> Every threshold in the generator — <c>MountainPrevalence</c>, the wetland and
    /// alpine bands, the erosion response — is a number compared against a field whose actual
    /// distribution nobody can predict from the source. Two of them were mis-set on the day the
    /// generator was wired in: the valley field sat above 0.9 over most of the realm, so
    /// <c>ValleyStrength</c> was a constant offset rather than a valley, and it dragged moisture up
    /// with it until a third of the Ember Crown classified as fen. Neither was visible in the code
    /// and both were obvious in one line of deciles.
    /// </summary>
    private static string Percentiles(string name, List<float> values)
    {
        if (values.Count == 0)
        {
            return $"  {name}: (no samples)";
        }

        values.Sort();
        var text = new StringBuilder($"  {name,-16}");
        for (int i = 0; i <= 10; i++)
        {
            int index = Math.Clamp((int)((values.Count - 1) * (i / 10f)), 0, values.Count - 1);
            text.Append(FormattableString.Invariant($" {values[index],5:F2}"));
        }
        return text.ToString();
    }

    /// <summary>The generated ground plus landforms under a point — the field a pad is levelled
    /// against, and the one a relative pad's offset is measured from.</summary>
    private static float GroundUnder(WorldHeightfield field, float x, float z) =>
        field.BaseHeight(x, z);
}
