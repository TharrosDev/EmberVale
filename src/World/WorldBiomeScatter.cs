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

                float height = gate.Height(worldX, worldZ);
                if (height < gateLayer.HeightRange.X || height > gateLayer.HeightRange.Y)
                {
                    return false;
                }
                return gateLayer.MaxSlope <= 0f ||
                       gate.SlopeAt(worldX, worldZ, height) <= gateLayer.MaxSlope;
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

            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = placements.Count,
            };

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
                multiMesh.SetInstanceTransform(i,
                    new Transform3D(basis, new Vector3(placement.X, ground + 0.025f, placement.Z)));

                float tint = 1f - (layer.TintVariation * 0.5f) +
                             (WorldSceneryMath.Unit(profile.Seed + 7301, i + (layerIndex * 521)) * layer.TintVariation);
                multiMesh.SetInstanceColor(i, new Color(
                    layer.Tint.R * tint, layer.Tint.G * tint, layer.Tint.B * tint, layer.Tint.A));
            }

            scatter.AddChild(new MultiMeshInstance3D
            {
                Name = $"Layer{layerIndex + 1}",
                Multimesh = multiMesh,
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
                MultiMeshInstance3D proxy = BuildHlod(layer, mesh!, layerMaterial, placements, field, worldOrigin);
                scatter.AddChild(proxy);
                scatter.InstanceCount += proxy.Multimesh?.InstanceCount ?? 0;
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
    private static MultiMeshInstance3D BuildHlod(
        BiomeScatterLayerResource layer, Mesh mesh, Material? material,
        IReadOnlyList<WorldScatterPlacement> placements, WorldHeightfield? field, Vector3 worldOrigin)
    {
        int reduction = Mathf.Max(2, layer.HlodReduction);
        int count = Mathf.CeilToInt(placements.Count / (float)reduction);

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = count,
        };
        for (int proxyIndex = 0; proxyIndex < count; proxyIndex++)
        {
            WorldScatterPlacement placement = placements[Mathf.Min(proxyIndex * reduction, placements.Count - 1)];
            float scale = Mathf.Lerp(layer.MinimumScale, layer.MaximumScale, placement.ScaleUnit);
            Vector3 mass = layer.HlodScale == Vector3.Zero ? Vector3.One : layer.HlodScale;
            var basis = new Basis(Vector3.Up, placement.Yaw).Scaled(mass * scale);
            float ground = field?.Height(worldOrigin.X + placement.X, worldOrigin.Z + placement.Z) ?? 0f;
            multiMesh.SetInstanceTransform(proxyIndex, new Transform3D(
                basis, new Vector3(placement.X, ground + 0.025f, placement.Z)));
            multiMesh.SetInstanceColor(proxyIndex, layer.HlodColor);
        }

        return new MultiMeshInstance3D
        {
            Name = "HlodLayer",
            Multimesh = multiMesh,
            MaterialOverride = material,
            VisibilityRangeBegin = layer.HlodRangeBegin,
            VisibilityRangeBeginMargin = layer.VisibilityFadeMargin,
            VisibilityRangeEnd = layer.HlodRangeEnd,
            VisibilityRangeEndMargin = layer.VisibilityFadeMargin,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
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
        if (layer.Saturation >= 0.999f || source is not StandardMaterial3D standard)
        {
            return null;
        }

        // Same reason, the other way round: a desaturating override is a single material, so it can
        // only be applied to a single-surface mesh. Say so rather than silently repainting a tree.
        if (surfaces > 1)
        {
            Log.Warn($"WorldBiomeScatter: '{path}' has {surfaces} surfaces, so Saturation " +
                     $"{layer.Saturation:0.00} is ignored — a MaterialOverride would repaint every " +
                     "surface with the first one's material. Desaturate a single-surface source.");
            return null;
        }

        string key = $"{standard.GetInstanceId()}|{layer.Saturation:F2}";
        if (RecolouredCache.TryGetValue(key, out ShaderMaterial? cached))
        {
            return cached;
        }

        var replacement = new ShaderMaterial { Shader = GD.Load<Shader>(ScatterShaderPath) };
        replacement.SetShaderParameter("albedo_texture", standard.AlbedoTexture);
        replacement.SetShaderParameter("albedo_color", standard.AlbedoColor);
        replacement.SetShaderParameter("saturation", layer.Saturation);
        replacement.SetShaderParameter("roughness_value", standard.Roughness);
        replacement.SetShaderParameter(
            "alpha_cut",
            standard.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor
                ? standard.AlphaScissorThreshold
                : 0f);
        RecolouredCache[key] = replacement;
        return replacement;
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
            mesh = source.Mesh;
            surfaces = source.Mesh.GetSurfaceCount();
            // ⚠️ The override is usually NULL on an imported .glb — its material lives on the mesh
            // surface. Reading only the override meant Recolour() had nothing to work from and
            // silently did nothing, which looks exactly like a saturation value that has no effect.
            material = source.MaterialOverride ?? source.Mesh.SurfaceGetMaterial(0);
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
