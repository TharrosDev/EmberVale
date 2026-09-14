using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.World;

/// <summary>Turns a cell's deterministic ecology profile into one MultiMesh draw source per layer.</summary>
public sealed partial class WorldBiomeScatter : Node3D
{
    private sealed record MeshSource(Mesh Mesh, Material? Material, int Surfaces);
    private static readonly Dictionary<string, MeshSource> MeshCache = new();

    public int InstanceCount { get; private set; }
    public int LayerCount { get; private set; }

    public static void ClearSourceCache()
    {
        MeshCache.Clear();
        RecolouredCache.Clear();
    }

    public override void _ExitTree()
    {
        foreach (Node child in GetChildren())
        {
            if (child is not MultiMeshInstance3D instance || instance.Multimesh is not { } multiMesh)
            {
                continue;
            }

            // Both tiers now share the cached source mesh and material, so neither owns either:
            // dropping the MultiMesh is the whole teardown. (The proxy tier used to build its own
            // primitive mesh and material and had to free them here.)
            instance.MaterialOverride = null;
            instance.Multimesh = null;
            multiMesh.Dispose();
        }
    }

    // ⚠️ WRITE THE WHOLE BUFFER, NEVER SetInstanceTransform. The offline bake runs headless, where
    // the dummy renderer drops per-instance writes but keeps a buffer assigned whole; every baked
    // scatter layer shipped with 0x0 transforms (and so no vegetation at all) until 2026-09-13.
    private const int BufferStride = 16; // Transform3D rows (12) + colour (4)

    private static void WriteInstance(float[] buffer, int index, Transform3D transform, Color color)
    {
        int o = index * BufferStride;
        Basis b = transform.Basis;
        buffer[o] = b.X.X; buffer[o + 1] = b.Y.X; buffer[o + 2] = b.Z.X; buffer[o + 3] = transform.Origin.X;
        buffer[o + 4] = b.X.Y; buffer[o + 5] = b.Y.Y; buffer[o + 6] = b.Z.Y; buffer[o + 7] = transform.Origin.Y;
        buffer[o + 8] = b.X.Z; buffer[o + 9] = b.Y.Z; buffer[o + 10] = b.Z.Z; buffer[o + 11] = transform.Origin.Z;
        buffer[o + 12] = color.R; buffer[o + 13] = color.G; buffer[o + 14] = color.B; buffer[o + 15] = color.A;
    }

    public static WorldBiomeScatter? Attach(
        Node3D cellRoot, WorldCellPresentationResource? presentation, WorldBiomeScatterResource? profile,
        WorldHeightfield? field, Vector3 worldOrigin)
    {
        if (presentation == null || profile == null || profile.Layers.Count == 0)
        {
            return null;
        }

        var scatter = new WorldBiomeScatter { Name = "BiomeScatter" };
        List<WorldScatterExclusion> exclusions = BuildExclusions(profile);

        // The planner works in cell-local X/Z; the field is world-space. Shift the region's routes
        // and yards into cell space once so a road that crosses the seam still clears vegetation on
        // BOTH sides of it — the old per-cell lists grew grass up to the edge and stopped.
        var paths = new List<WorldTerrainMath.Path>();
        var groundAreas = new List<WorldTerrainMath.GroundArea>();
        if (field != null)
        {
            foreach (WorldTerrainMath.Path path in field.Paths)
            {
                paths.Add(path with
                {
                    StartX = path.StartX - worldOrigin.X, StartZ = path.StartZ - worldOrigin.Z,
                    EndX = path.EndX - worldOrigin.X, EndZ = path.EndZ - worldOrigin.Z,
                });
            }
            foreach (WorldTerrainMath.GroundArea area in field.Areas)
            {
                groundAreas.Add(area with { X = area.X - worldOrigin.X, Z = area.Z - worldOrigin.Z });
            }
        }

        for (int layerIndex = 0; layerIndex < profile.Layers.Count; layerIndex++)
        {
            BiomeScatterLayerResource? layer = profile.Layers[layerIndex];
            if (layer == null || layer.Count <= 0 || !TryLoadMesh(layer.ScenePath, out Mesh? mesh, out Material? material, out int surfaces))
            {
                continue;
            }

            // ⚠️ Count IS A DENSITY, NOT A HEADCOUNT (the 2026-08-29 geography overhaul). Cells now
            // range from 50 x 90 to 200 x 110, and a flat per-cell count made a 200 m transitional
            // cell four times emptier than the 50 m one beside it — which drew the cell lattice back
            // onto the ground in vegetation after the terrain had stopped drawing it. The authored
            // number is instances per 100 x 100 m; the cell's own footprint scales it.
            int count = Mathf.RoundToInt(
                layer.Count * presentation.Width * presentation.Depth / 10000f);
            // ⚠️ THE TERRAIN GATE. A species declares the steepest ground it stands on and the
            // altitude band it survives in; the planner refuses everything else. Without it the
            // sampler is uniform over a cell that now has 60-degree faces in it, which is how the
            // corrie walls and the glacier's buttresses grew a full density of trees and boulders
            // sideways out of them.
            WorldHeightfield? gate = field;
            BiomeScatterLayerResource gateLayer = layer;
            Func<float, float, bool>? terrainAccepts = gate == null ? null : (x, z) =>
            {
                float worldX = worldOrigin.X + x;
                float worldZ = worldOrigin.Z + z;

                // Clumping first: one noise sample, and for a stand species it refuses about half
                // the cell. Height and slope each cost a full field evaluation over every landform
                // that reaches this cell, so they are worth doing on the survivors only.
                if (gateLayer.Clumping > 0f)
                {
                    float scale = Mathf.Max(5f, gateLayer.ClumpScale);
                    float stand = WorldTerrainMath.ValueNoise(
                        profile.Seed + 4409, worldX / scale, worldZ / scale);
                    // The threshold rises with Clumping, so the field goes from "everywhere" to
                    // "only the high ground of the stand field" without changing the density dial.
                    if (stand <= gateLayer.Clumping * 0.62f)
                    {
                        return false;
                    }
                }

                // ONE SAMPLE, NOT SIX. Height, slope, curvature, moisture, temperature and water
                // distance all come out of a single WorldSample, which costs about what the old
                // Height + SlopeAt pair did on its own - the expensive part of a query is walking
                // the landforms that reach this cell, and that happens once either way. Asking the
                // generator six separate questions would have made ecology the most expensive thing
                // in a region load; asking it one makes it free.
                WorldSample sample = gate.Sample(worldX, worldZ);
                if (sample.Elevation < gateLayer.HeightRange.X ||
                    sample.Elevation > gateLayer.HeightRange.Y)
                {
                    return false;
                }

                if (gateLayer.MaxSlope > 0f && sample.Slope > gateLayer.MaxSlope)
                {
                    return false;
                }

                if (gateLayer.MaxCurvature > 0f &&
                    Mathf.Abs(sample.Curvature) > gateLayer.MaxCurvature)
                {
                    return false;
                }

                if (sample.Moisture < gateLayer.MoistureRange.X ||
                    sample.Moisture > gateLayer.MoistureRange.Y ||
                    sample.Temperature < gateLayer.TemperatureRange.X ||
                    sample.Temperature > gateLayer.TemperatureRange.Y)
                {
                    return false;
                }

                if (gateLayer.RiparianAffinity != 0f)
                {
                    // A BIAS, NOT A FENCE. Thresholding on water distance draws a hard edge along
                    // the riparian belt, and a hard edge is the one thing vegetation never has. The
                    // species' own deterministic noise decides each candidate, so the belt thins out
                    // through a scatter of individuals the way a real one does.
                    float want = Mathf.Clamp(
                        0.5f + (gateLayer.RiparianAffinity * (sample.WaterProximity - 0.35f) * 1.6f),
                        0f, 1f);
                    float roll = WorldTerrainMath.ValueNoise(
                        profile.Seed + 7717, worldX * 0.41f, worldZ * 0.41f);
                    if (roll > want)
                    {
                        return false;
                    }
                }

                return true;
            };

            IReadOnlyList<WorldScatterPlacement> placements = WorldScatterPlanner.Plan(
                profile.Seed + (layerIndex * 1009), count,
                presentation.Width, presentation.Depth, profile.EdgePadding,
                layer.MinimumSpacing, exclusions, paths, groundAreas, terrainAccepts);
            if (placements.Count == 0)
            {
                continue;
            }

            Material? layerMaterial = Recolour(material, layer, surfaces, layer.ScenePath);

            var transforms = new Transform3D[placements.Count];
            var colors = new Color[placements.Count];

            for (int i = 0; i < placements.Count; i++)
            {
                WorldScatterPlacement placement = placements[i];
                float scale = Mathf.Lerp(layer.MinimumScale, layer.MaximumScale, placement.ScaleUnit);
                // Lean with the hillside, but only part of the way. Fully aligning a tree to a
                // 30-degree slope makes it grow perpendicular to the ground, which real trees do not;
                // leaving it dead upright makes a hillside of grass look like a pin cushion. 0.55 is
                // the blend that reads as "growing on a slope" from a player's eye height.
                var basis = new Basis(Vector3.Up, placement.Yaw).Scaled(Vector3.One * scale);
                if (field != null)
                {
                    (float nx, float ny, float nz) = field.NormalAt(
                        worldOrigin.X + placement.X, worldOrigin.Z + placement.Z);
                    var normal = new Vector3(nx, ny, nz);
                    Vector3 leaned = Vector3.Up.Lerp(normal, 0.55f).Normalized();
                    if (leaned.Dot(Vector3.Up) < 0.9999f)
                    {
                        Vector3 axis = Vector3.Up.Cross(leaned);
                        if (axis.LengthSquared() > 1e-8f)
                        {
                            basis = new Basis(axis.Normalized(), Vector3.Up.AngleTo(leaned)) * basis;
                        }
                    }
                }
                float ground = field?.Height(worldOrigin.X + placement.X, worldOrigin.Z + placement.Z) ?? 0f;
                float tint = 1f - (layer.TintVariation * 0.5f) +
                             (WorldSceneryMath.Unit(profile.Seed + 7301, i + (layerIndex * 521)) * layer.TintVariation);
                transforms[i] = new Transform3D(basis, new Vector3(placement.X, ground + 0.025f, placement.Z));
                colors[i] = new Color(layer.Tint.R * tint, layer.Tint.G * tint, layer.Tint.B * tint, layer.Tint.A);
            }

            AddTiled(scatter, $"Layer{layerIndex + 1}", mesh!, transforms, colors, new MultiMeshInstance3D
            {
                MaterialOverride = layerMaterial,
                VisibilityRangeEnd = layer.VisibilityRangeEnd,
                VisibilityRangeEndMargin = layer.VisibilityFadeMargin,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                CastShadow = layer.CastShadows
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off,
            });
            scatter.InstanceCount += placements.Count;
            scatter.LayerCount++;

            if (layer.HlodShape != 0)
            {
                scatter.InstanceCount += BuildHlod(scatter, $"HlodLayer{layerIndex + 1}", layer, mesh!, layerMaterial, placements, field, worldOrigin);
            }
        }

        if (scatter.LayerCount == 0)
        {
            scatter.Free();
            return null;
        }

        cellRoot.AddChild(scatter);
        return scatter;
    }

    /// <summary>
    /// The distant tier of one layer: the SAME mesh at a fraction of the density, faded in as the
    /// detailed tier fades out.
    ///
    /// ⚠️ <b>IT USED TO BE A CYLINDER OR A BOX AND YOU COULD SEE THAT FROM THE TOWN SQUARE.</b> The
    /// proxies were five-sided cones and unit cubes in a flat dark colour, and at the ranges they
    /// actually engaged — 92 m for scrub, 130 m for trees — a hillside of them read as a scattering
    /// of black crates on the ground. An HLOD tier is a silhouette contract; a primitive keeps the
    /// mass and throws away the silhouette, which is the half that matters at distance.
    ///
    /// Reusing the source mesh costs vertices and saves everything else: it is still ONE draw call
    /// per layer, still <c>HlodReduction</c> times fewer instances, still shadow-free, and the
    /// meshes in question are 44–650 triangles. <see cref="BiomeScatterLayerResource.HlodScale"/>
    /// survives as a mass multiplier — a distant stand wants to be slightly larger than its members
    /// to hold the same silhouette at a quarter of the count.
    /// </summary>
    private static int BuildHlod(
        Node3D scatter, string name, BiomeScatterLayerResource layer, Mesh mesh, Material? material,
        IReadOnlyList<WorldScatterPlacement> placements, WorldHeightfield? field, Vector3 worldOrigin)
    {
        int reduction = Mathf.Max(2, layer.HlodReduction);
        int count = Mathf.CeilToInt(placements.Count / (float)reduction);

        var transforms = new Transform3D[count];
        var colors = new Color[count];
        for (int proxyIndex = 0; proxyIndex < count; proxyIndex++)
        {
            WorldScatterPlacement placement = placements[Mathf.Min(proxyIndex * reduction, placements.Count - 1)];
            float scale = Mathf.Lerp(layer.MinimumScale, layer.MaximumScale, placement.ScaleUnit);
            Vector3 mass = layer.HlodScale == Vector3.Zero ? Vector3.One : layer.HlodScale;
            var basis = new Basis(Vector3.Up, placement.Yaw).Scaled(mass * scale);
            float ground = field?.Height(worldOrigin.X + placement.X, worldOrigin.Z + placement.Z) ?? 0f;
            transforms[proxyIndex] = new Transform3D(basis, new Vector3(placement.X, ground + 0.025f, placement.Z));
            colors[proxyIndex] = layer.HlodColor;
        }

        AddTiled(scatter, name, mesh, transforms, colors, new MultiMeshInstance3D
        {
            MaterialOverride = material,
            VisibilityRangeBegin = layer.HlodRangeBegin,
            VisibilityRangeBeginMargin = layer.VisibilityFadeMargin,
            VisibilityRangeEnd = layer.HlodRangeEnd,
            VisibilityRangeEndMargin = layer.VisibilityFadeMargin,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return count;
    }

    // ⚠️ A VISIBILITY RANGE IS JUDGED PER NODE, NOT PER INSTANCE. One MultiMesh for a whole cell drew
    // every tuft of a 200 m cell whenever the camera came within 62 m of the cell, which is 2.7 M
    // triangles in one wilderness cell after the 2026-09 world rebuild made cells that large. Each
    // layer is filed into square tiles, each its own node at its own centre, so the range culls by area.
    private const float TileSize = 48f;

    private static void AddTiled(
        Node3D scatter, string name, Mesh mesh, Transform3D[] transforms, Color[] colors,
        MultiMeshInstance3D settings)
    {
        var tiles = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < transforms.Length; i++)
        {
            (int, int) key = (Mathf.FloorToInt(transforms[i].Origin.X / TileSize),
                              Mathf.FloorToInt(transforms[i].Origin.Z / TileSize));
            if (!tiles.TryGetValue(key, out List<int>? members))
            {
                tiles[key] = members = new List<int>();
            }
            members.Add(i);
        }

        foreach (((int tx, int tz), List<int> members) in tiles)
        {
            var origin = new Vector3((tx + 0.5f) * TileSize, 0f, (tz + 0.5f) * TileSize);
            var buffer = new float[members.Count * BufferStride];
            for (int n = 0; n < members.Count; n++)
            {
                Transform3D local = transforms[members[n]];
                local.Origin -= origin;
                WriteInstance(buffer, n, local, colors[members[n]]);
            }

            var instance = (MultiMeshInstance3D)settings.Duplicate();
            instance.Name = $"{name}_{tx}_{tz}";
            instance.Position = origin;
            instance.Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = members.Count,
                Buffer = buffer,
            };
            scatter.AddChild(instance);
        }
        settings.Free();
    }

    private const string ScatterShaderPath = "res://assets/shaders/world/world_scatter.gdshader";
    private static readonly Dictionary<string, ShaderMaterial> RecolouredCache = new();

    /// <summary>
    /// The layer's material, or a desaturating replacement when it asks for one. Cached by
    /// (source, saturation) so the twenty cells sharing a species share one material, as they did
    /// when they all used the source's own.
    /// </summary>
    private static Material? Recolour(
        Material? source, BiomeScatterLayerResource layer, int surfaces, string path)
    {
        // ⚠️ NULL, NOT `source`, WHEN THERE IS NOTHING TO DO — AND THAT IS NOT A TIDY-UP.
        // A MultiMeshInstance3D's MaterialOverride replaces the material on EVERY surface. Handing
        // it surface 0's material back "unchanged" paints a two-surface tree entirely in its BARK,
        // so every broadleaf in the Ember Crown grew salmon-pink foliage. A null override lets each
        // surface keep its own material, which is what the layer had before any of this existed.
        // Every eligible standard surface was adapted by WeatherMaterial already. A remaining
        // standard or custom shader has features we preserve; the old saturation fallback would
        // strip its normals/emission/packed textures after the adapter deliberately retained them.
        if (surfaces != 1 || source is not ShaderMaterial shaderMaterial ||
            shaderMaterial.Shader?.ResourcePath != ScatterShaderPath) return null;
        if (layer.Saturation >= .999f) return null;
        string key = $"{shaderMaterial.GetInstanceId()}|{layer.Saturation:F2}";
        if (RecolouredCache.TryGetValue(key, out ShaderMaterial? cached)) return cached;
        var tinted = (ShaderMaterial)shaderMaterial.Duplicate();
        tinted.SetShaderParameter("saturation", layer.Saturation);
        RecolouredCache[key] = tinted;
        return tinted;
    }

    private static List<WorldScatterExclusion> BuildExclusions(WorldBiomeScatterResource profile)
    {
        var exclusions = new List<WorldScatterExclusion>(profile.Exclusions.Count);
        foreach (BiomeScatterExclusionResource? exclusion in profile.Exclusions)
        {
            if (exclusion != null && exclusion.Radius > 0f)
            {
                exclusions.Add(new WorldScatterExclusion(
                    exclusion.Center.X, exclusion.Center.Y, exclusion.Radius));
            }
        }
        return exclusions;
    }

    private static bool TryLoadMesh(string path, out Mesh? mesh, out Material? material, out int surfaces)
    {
        mesh = null;
        material = null;
        surfaces = 0;
        if (MeshCache.TryGetValue(path, out MeshSource? cached))
        {
            mesh = cached.Mesh;
            material = cached.Material;
            surfaces = cached.Surfaces;
            return true;
        }

        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path) || GD.Load<PackedScene>(path) is not { } scene)
        {
            Log.Warn($"WorldBiomeScatter: layer source '{path}' is not a loadable scene.");
            return false;
        }

        Node instance = scene.Instantiate();
        MeshInstance3D? source = FindMesh(instance);
        if (source?.Mesh != null)
        {
            mesh = (Mesh)source.Mesh.Duplicate();
            surfaces = source.Mesh.GetSurfaceCount();
            for (int surface = 0; surface < surfaces; surface++)
            {
                Material? original = source.GetActiveMaterial(surface);
                mesh.SurfaceSetMaterial(surface, WeatherMaterial(original, path));
            }
            // ⚠️ The override is usually NULL on an imported .glb — its material lives on the mesh
            // surface. Reading only the override meant Recolour() had nothing to work from and
            // silently did nothing, which looks exactly like a saturation value that has no effect.
            material = mesh.SurfaceGetMaterial(0);
        }
        instance.Free();

        if (mesh == null)
        {
            Log.Warn($"WorldBiomeScatter: layer source '{path}' contains no MeshInstance3D.");
            return false;
        }
        MeshCache[path] = new MeshSource(mesh, material, surfaces);
        return true;
    }

    private static Material? WeatherMaterial(Material? source, string path)
    {
        // Do not approximate advanced material features or replace rigged production models.
        // This adapter is restricted to the static ecology mesh pipeline.
        if (source is not StandardMaterial3D m || m.NormalEnabled || m.EmissionEnabled ||
            m.MetallicTexture != null || m.RoughnessTexture != null ||
            (m.Transparency != BaseMaterial3D.TransparencyEnum.Disabled &&
             m.Transparency != BaseMaterial3D.TransparencyEnum.AlphaScissor)) return source;
        var result = new ShaderMaterial { Shader = GD.Load<Shader>(ScatterShaderPath) };
        result.SetShaderParameter("has_texture", m.AlbedoTexture != null);
        if (m.AlbedoTexture is { } texture) result.SetShaderParameter("albedo_texture", texture);
        result.SetShaderParameter("albedo_color", m.AlbedoColor);
        result.SetShaderParameter("roughness_value", m.Roughness);
        result.SetShaderParameter("metallic_value", m.Metallic);
        result.SetShaderParameter("alpha_cut", m.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor ? m.AlphaScissorThreshold : 0f);
        string family = path.ToLowerInvariant();
        bool plant = family.Contains("tree") || family.Contains("pine") || family.Contains("grass") ||
                     family.Contains("bush") || family.Contains("flower") || family.Contains("fern");
        result.SetShaderParameter("wind_response", plant ? 1f : 0f);
        return result;
    }

    private static MeshInstance3D? FindMesh(Node node)
    {
        if (node is MeshInstance3D { Mesh: not null } mesh)
        {
            return mesh;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindMesh(child) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
