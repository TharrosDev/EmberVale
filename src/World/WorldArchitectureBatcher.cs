using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>
/// Merges a prepared cell's static architecture and props into a handful of meshes at bake time
/// (2026-09 world rebuild).
///
/// ⚠️ <b>WHY.</b> Godot draws every surface of every <see cref="MeshInstance3D"/> separately, and a
/// composed building is forty-odd module instances. Before the rebuild the Embermarket's twenty
/// structures already cost 1,383 draw calls and the realm's worst frame; a capital with three or four
/// times the fabric would have cost the frame outright. Scatter was already batched (MultiMesh); the
/// buildings never were. This does for authored static geometry what scatter does for vegetation,
/// offline, so runtime pays nothing and loads nothing new.
///
/// <b>What is merged.</b> A mesh under the cell's <c>Nav</c> (the static-geometry parent by
/// convention) that is visible, unskinned, carries no script, has no scripted or animated ancestor
/// below <c>Nav</c> and is not already range-culled. Interactables, NPCs, flag-hidden props, lights
/// and anything opted out with the <c>no_batch</c> group keep their own nodes.
///
/// <b>How.</b> Surfaces are grouped by a <see cref="ClusterSize"/> grid square, material, shadow mode,
/// vertex format and landmark status, and appended into one <see cref="ArrayMesh"/> per group, so a
/// batch still frustum-culls at street scale. Colliders are untouched — only the source mesh is
/// cleared, and the instanced building is flattened into the cell so that clearing survives packing.
/// A merged mesh that came from a <c>world_landmark</c> keeps the group, so the Backdrop streaming
/// tier still draws the skyline.
/// </summary>
public static class WorldArchitectureBatcher
{
    /// <summary>Grid square, in metres, that bounds one batch. Street scale: big enough to collapse a
    /// block into a few draws, small enough that frustum culling still discards what is behind you.</summary>
    public const float ClusterSize = 40f;

    public const string OptOutGroup = "no_batch";
    public const string LandmarkGroup = "world_landmark";

    private readonly record struct BucketKey(
        int X, int Z, ulong MaterialId, GeometryInstance3D.ShadowCastingSetting Shadow, ulong Format,
        bool Landmark);

    private readonly record struct Piece(Mesh Mesh, int Surface, Transform3D Transform);

    /// <summary>Batches <paramref name="root"/>'s static geometry. The root must be in the tree at
    /// the origin. Returns how many source surfaces were merged.</summary>
    public static int Apply(Node3D root)
    {
        if (root.GetNodeOrNull<Node3D>("Nav") is not { } nav)
        {
            return 0;
        }

        var buckets = new Dictionary<BucketKey, List<Piece>>();
        var materials = new Dictionary<ulong, Material?>();
        var consumed = new List<MeshInstance3D>();
        Collect(nav, nav, root, buckets, materials, consumed);
        if (consumed.Count == 0)
        {
            return 0;
        }

        int merged = 0;
        int index = 0;
        foreach (KeyValuePair<BucketKey, List<Piece>> bucket in buckets)
        {
            var tool = new SurfaceTool();
            foreach (Piece piece in bucket.Value)
            {
                tool.AppendFrom(piece.Mesh, piece.Surface, piece.Transform);
                merged++;
            }
            Material? material = materials[bucket.Key.MaterialId];
            ArrayMesh merged_ = tool.Commit();

            // ⚠️ REGENERATE LODS. Every imported glTF carries automatic LODs; a mesh built by
            // SurfaceTool has none, so the first bake without this drew MORE primitives than the
            // unbatched cell (409k -> 621k per frame) while drawing fewer calls.
            var importer = new ImporterMesh();
            importer.AddSurface(Mesh.PrimitiveType.Triangles, merged_.SurfaceGetArrays(0), material: material);
            importer.GenerateLods(25f, 60f, new Godot.Collections.Array());
            var batch = new MeshInstance3D
            {
                Name = $"ArchitectureBatch{index++}",
                Mesh = importer.GetMesh(),
                CastShadow = bucket.Key.Shadow,
            };
            nav.AddChild(batch);
            batch.Owner = root;
            if (bucket.Key.Landmark)
            {
                batch.AddToGroup(LandmarkGroup, persistent: true);
            }
        }

        foreach (MeshInstance3D source in consumed)
        {
            Flatten(source, root);
            source.Mesh = null;
        }
        return merged;
    }

    private static void Collect(
        Node node, Node3D nav, Node3D root, Dictionary<BucketKey, List<Piece>> buckets,
        Dictionary<ulong, Material?> materials, List<MeshInstance3D> consumed)
    {
        if (node != nav && (node.GetScript().VariantType != Variant.Type.Nil || node.IsInGroup(OptOutGroup) ||
                            node is AnimationPlayer || node is Node3D { Visible: false } || HasScriptedChild(node)))
        {
            return;
        }

        if (node is MeshInstance3D mesh && Eligible(mesh))
        {
            Transform3D transform = root.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
            bool landmark = InLandmark(mesh, nav);
            int cx = Mathf.FloorToInt(transform.Origin.X / ClusterSize);
            int cz = Mathf.FloorToInt(transform.Origin.Z / ClusterSize);
            for (int surface = 0; surface < mesh.Mesh!.GetSurfaceCount(); surface++)
            {
                Material? material = mesh.GetActiveMaterial(surface);
                ulong materialId = MaterialKey(material);
                materials[materialId] = material;
                var key = new BucketKey(cx, cz, materialId, mesh.CastShadow,
                    mesh.Mesh is ArrayMesh arrays ? (ulong)arrays.SurfaceGetFormat(surface) & 0xFFFFUL : 0UL, landmark);
                if (!buckets.TryGetValue(key, out List<Piece>? pieces))
                {
                    pieces = new List<Piece>();
                    buckets[key] = pieces;
                }
                pieces.Add(new Piece(mesh.Mesh, surface, transform));
            }
            consumed.Add(mesh);
        }

        foreach (Node child in node.GetChildren())
        {
            Collect(child, nav, root, buckets, materials, consumed);
        }
    }

    /// <summary>
    /// Two materials that would draw identically share a batch. Every imported glTF brings its own
    /// material instances, so keying on the instance would give each module file its own draw call and
    /// undo most of the merge; a standard material is keyed on what it actually renders instead.
    /// </summary>
    private static ulong MaterialKey(Material? material)
    {
        if (material is not BaseMaterial3D standard)
        {
            return material?.GetInstanceId() ?? 0UL;
        }
        var hash = new System.HashCode();
        hash.Add(standard.GetType());
        hash.Add(standard.AlbedoColor);
        hash.Add(standard.AlbedoTexture?.GetRid());
        hash.Add(standard.NormalEnabled ? standard.NormalTexture?.GetRid() : null);
        hash.Add(standard.OrmTexture?.GetRid());
        hash.Add(standard.RoughnessTexture?.GetRid());
        hash.Add(standard.MetallicTexture?.GetRid());
        hash.Add(standard.Metallic);
        hash.Add(standard.Roughness);
        hash.Add(standard.Transparency);
        hash.Add(standard.CullMode);
        hash.Add(standard.ShadingMode);
        hash.Add(standard.VertexColorUseAsAlbedo);
        hash.Add(standard.EmissionEnabled ? standard.Emission : Colors.Black);
        hash.Add(standard.EmissionEnabled ? standard.EmissionEnergyMultiplier : 0f);
        hash.Add(standard.TextureFilter);
        hash.Add(standard.Uv1Scale);
        hash.Add(standard.Uv1Offset);
        return unchecked((ulong)(uint)hash.ToHashCode() | (1UL << 63));
    }

    private static bool Eligible(MeshInstance3D mesh) =>
        mesh.Mesh != null &&
        mesh.Mesh.GetSurfaceCount() > 0 &&
        mesh.Skin == null &&
        mesh.MaterialOverlay == null &&
        mesh.VisibilityRangeEnd <= 0f &&
        mesh.Mesh.GetSurfaceCount() == SurfaceTriangles(mesh.Mesh);

    /// <summary>Counts surfaces drawn as triangles; a line or point surface cannot be appended.</summary>
    private static int SurfaceTriangles(Mesh mesh)
    {
        int count = 0;
        if (mesh is ArrayMesh arrays)
        {
            for (int i = 0; i < arrays.GetSurfaceCount(); i++)
            {
                if (arrays.SurfaceGetPrimitiveType(i) == Mesh.PrimitiveType.Triangles)
                {
                    count++;
                }
            }
            return count;
        }
        return mesh.GetSurfaceCount();
    }

    private static bool HasScriptedChild(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is not Node3D && child.GetScript().VariantType != Variant.Type.Nil)
            {
                return true; // a component on this node: it is gameplay, not architecture
            }
        }
        return false;
    }

    private static bool InLandmark(Node node, Node3D nav)
    {
        for (Node? current = node; current != null && current != nav; current = current.GetParent())
        {
            if (current.IsInGroup(LandmarkGroup))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Makes every instanced scene between <paramref name="node"/> and the cell root local to the
    /// cell, so an edit to one of its nodes is serialised when the cell is packed. Without this a
    /// cleared mesh inside an instanced building would silently come back on load.
    /// </summary>
    private static void Flatten(Node node, Node3D root)
    {
        for (Node? current = node; current != null && current != root; current = current.GetParent())
        {
            if (!string.IsNullOrEmpty(current.SceneFilePath))
            {
                current.SceneFilePath = string.Empty;
                OwnSubtree(current, root);
            }
        }
    }

    private static void OwnSubtree(Node node, Node3D root)
    {
        foreach (Node child in node.GetChildren())
        {
            child.Owner = root;
            OwnSubtree(child, root);
        }
    }
}
