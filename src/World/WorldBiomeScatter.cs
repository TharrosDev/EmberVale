using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.World;

/// <summary>Turns a cell's deterministic ecology profile into one MultiMesh draw source per layer.</summary>
public sealed partial class WorldBiomeScatter : Node3D
{
    private sealed record MeshSource(Mesh Mesh, Material? Material);
    private static readonly Dictionary<string, MeshSource> MeshCache = new();

    public int InstanceCount { get; private set; }
    public int LayerCount { get; private set; }

    public static void ClearSourceCache() => MeshCache.Clear();

    public override void _ExitTree()
    {
        foreach (Node child in GetChildren())
        {
            if (child is not MultiMeshInstance3D instance || instance.Multimesh is not { } multiMesh)
            {
                continue;
            }

            bool ownsProxyMesh = instance.Name == "HlodLayer";
            Mesh? proxyMesh = ownsProxyMesh ? multiMesh.Mesh : null;
            Material? proxyMaterial = proxyMesh?.GetSurfaceCount() > 0
                ? proxyMesh.SurfaceGetMaterial(0)
                : null;
            instance.MaterialOverride = null;
            instance.Multimesh = null;
            multiMesh.Dispose();
            proxyMaterial?.Dispose();
            proxyMesh?.Dispose();
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
            if (layer == null || layer.Count <= 0 || !TryLoadMesh(layer.ScenePath, out Mesh? mesh, out Material? material))
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
            IReadOnlyList<WorldScatterPlacement> placements = WorldScatterPlanner.Plan(
                profile.Seed + (layerIndex * 1009), count,
                presentation.Width, presentation.Depth, profile.EdgePadding,
                layer.MinimumSpacing, exclusions, paths, groundAreas);
            if (placements.Count == 0)
            {
                continue;
            }

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
                var basis = new Basis(Vector3.Up, placement.Yaw).Scaled(Vector3.One * scale);
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
                MaterialOverride = material,
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
                MultiMeshInstance3D proxy = BuildHlod(layer, placements, field, worldOrigin);
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

    private static MultiMeshInstance3D BuildHlod(
        BiomeScatterLayerResource layer, IReadOnlyList<WorldScatterPlacement> placements,
        WorldHeightfield? field, Vector3 worldOrigin)
    {
        int reduction = Mathf.Max(2, layer.HlodReduction);
        int count = Mathf.CeilToInt(placements.Count / (float)reduction);
        Mesh proxyMesh = layer.HlodShape == 1
            ? new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.55f, Height = 2f, RadialSegments = 5, Rings = 1 }
            : new BoxMesh { Size = Vector3.One };
        proxyMesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = layer.HlodColor,
            Roughness = 1f,
        });

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = proxyMesh,
            InstanceCount = count,
        };
        for (int proxyIndex = 0; proxyIndex < count; proxyIndex++)
        {
            WorldScatterPlacement placement = placements[Mathf.Min(proxyIndex * reduction, placements.Count - 1)];
            float scale = Mathf.Lerp(layer.MinimumScale, layer.MaximumScale, placement.ScaleUnit);
            var basis = new Basis(Vector3.Up, placement.Yaw).Scaled(layer.HlodScale * scale);
            float ground = field?.Height(worldOrigin.X + placement.X, worldOrigin.Z + placement.Z) ?? 0f;
            multiMesh.SetInstanceTransform(proxyIndex, new Transform3D(
                basis, new Vector3(placement.X, ground + (layer.HlodScale.Y * scale), placement.Z)));
        }

        return new MultiMeshInstance3D
        {
            Name = "HlodLayer",
            Multimesh = multiMesh,
            VisibilityRangeBegin = layer.HlodRangeBegin,
            VisibilityRangeBeginMargin = layer.VisibilityFadeMargin,
            VisibilityRangeEnd = layer.HlodRangeEnd,
            VisibilityRangeEndMargin = layer.VisibilityFadeMargin,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
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

    private static bool TryLoadMesh(string path, out Mesh? mesh, out Material? material)
    {
        mesh = null;
        material = null;
        if (MeshCache.TryGetValue(path, out MeshSource? cached))
        {
            mesh = cached.Mesh;
            material = cached.Material;
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
            material = source.MaterialOverride;
        }
        instance.Free();

        if (mesh == null)
        {
            Log.Warn($"WorldBiomeScatter: layer source '{path}' contains no MeshInstance3D.");
            return false;
        }
        MeshCache[path] = new MeshSource(mesh, material);
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
