using System;
using Embervale.Core.Diagnostics;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Engine-side half of the deterministic world bake. Invoke through <c>tools/world_bake.py --bake</c>
/// so the source/output manifest is written only after every artifact succeeds.
/// </summary>
public static partial class HeadlessWorldBake
{
    public const string FlagArgument = "--world-bake";

    public static bool Requested() => HeadlessValidation.HasFlag(FlagArgument);

    private static string SourceSignature()
    {
        const string prefix = "--world-bake-signature=";
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                return argument[prefix.Length..];
            }
        }
        return string.Empty;
    }

    public static void Run(SceneTree tree)
    {
        ContentDatabases.InitializeAll();
        var runner = new WorldBakeRunner { Name = "WorldBakeRunner" };
        runner.TreeEntered += () => runner.Begin(tree);
        // ApplicationRoot calls this from its own _Ready while the root viewport is still setting
        // up children. Godot rejects a synchronous AddChild in that window; starting on
        // TreeEntered guarantees every navigation/process-frame await has a live SceneTree.
        tree.Root.CallDeferred(Node.MethodName.AddChild, runner);
    }

    private sealed partial class WorldBakeRunner : Node
    {
        // One committed grid supplies the terrain mesh, collision, navigation, runtime recovery and
        // pre-activation candidates. Three metres matches the traversal validator's sampling scale
        // while keeping a full deterministic rebuild practical in CI.
        private const float GroundSampleStep = 3f;
        private const float GroundSampleMargin = 100f;

        public async void Begin(SceneTree tree)
        {
            int failures = 0;
            string sourceSignature = SourceSignature();
            if (sourceSignature.Length != 64)
            {
                Log.Error("World bake: missing or invalid source signature. Invoke through tools/world_bake.py.");
                tree.Quit(1);
                return;
            }
            CellNavBaker.RuntimeBakeSuppressed = true;
            try
            {
                EnsureDirectories();
                foreach (RegionResource region in RegionDatabase.All)
                {
                    ulong started = Time.GetTicksMsec();
                    WorldHeightfield sourceField = WorldTerrainMeshBuilder.HeightfieldFor(region);
                    WorldPreparedRegionResource prepared = BakeRegion(
                        region, sourceField, sourceSignature);
                    // This sampled field is the production authority from here onward. Terrain
                    // visuals, collision, navigation, backdrop and runtime safety queries all read
                    // these exact values rather than independently approximating the generator.
                    WorldHeightfield preparedField = prepared.CreateBakeField(region, sourceField);

                    foreach (RegionCellResource? cell in region.Cells)
                    {
                        if (cell == null || !await BakeCell(region, cell, preparedField))
                        {
                            failures++;
                        }
                    }

                    Error saved = ResourceSaver.Save(
                        prepared, WorldBakePaths.Region(region.Id), ResourceSaver.SaverFlags.Compress);
                    if (saved != Error.Ok)
                    {
                        failures++;
                        Log.Error($"World bake: could not save region '{region.Id}' ({saved}).");
                    }
                    else
                    {
                        Log.Info($"World bake: prepared '{region.Id}' in {Time.GetTicksMsec() - started} ms.");
                    }
                }
            }
            catch (Exception error)
            {
                failures++;
                Log.Error($"World bake failed: {error}");
            }
            finally
            {
                CellNavBaker.RuntimeBakeSuppressed = false;
            }

            tree.Quit(failures == 0 ? 0 : 1);
        }

        private static void EnsureDirectories()
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(WorldBakePaths.Root));
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(WorldBakePaths.Root + "/regions"));
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(WorldBakePaths.Root + "/cells"));
            foreach (RegionResource region in RegionDatabase.All)
            {
                DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(
                    WorldBakePaths.Root + "/cells/" + region.Id.Replace('.', '_').Replace(':', '_')));
            }
        }

        private static WorldPreparedRegionResource BakeRegion(
            RegionResource region, WorldHeightfield field, string sourceSignature)
        {
            Aabb bounds = region.Bounds;
            float minX = bounds.Position.X - GroundSampleMargin;
            float minZ = bounds.Position.Z - GroundSampleMargin;
            float maxX = bounds.End.X + GroundSampleMargin;
            float maxZ = bounds.End.Z + GroundSampleMargin;
            int columns = Mathf.CeilToInt((maxX - minX) / GroundSampleStep) + 1;
            int rows = Mathf.CeilToInt((maxZ - minZ) / GroundSampleStep) + 1;
            var heights = new float[columns * rows];
            var water = new float[heights.Length];

            for (int z = 0; z < rows; z++)
            {
                float worldZ = minZ + (z * GroundSampleStep);
                for (int x = 0; x < columns; x++)
                {
                    float worldX = minX + (x * GroundSampleStep);
                    int index = (z * columns) + x;
                    heights[index] = field.Height(worldX, worldZ);
                    // Most of a region is dry. The hydrology cache can reject a tiny footprint with
                    // array lookups; only a possible channel pays for the full generated-water query.
                    water[index] = field.MayHaveGeneratedWater(
                            worldX - GroundSampleStep, worldZ - GroundSampleStep,
                            worldX + GroundSampleStep, worldZ + GroundSampleStep)
                        ? field.GeneratedWaterSurface(worldX, worldZ) ??
                          WorldPreparedRegionResource.MissingWater
                        : WorldPreparedRegionResource.MissingWater;
                }
            }

            var prepared = new WorldPreparedRegionResource
            {
                RegionId = region.Id,
                SourceSignature = sourceSignature,
                MinX = minX,
                MinZ = minZ,
                SampleStep = GroundSampleStep,
                Columns = columns,
                Rows = rows,
                Heights = heights,
                GeneratedWaterSurfaces = water,
            };

            PackedScene? backdrop = null;
            if (region.EnvironmentProfile is { } environment)
            {
                WorldHeightfield preparedField = prepared.CreateBakeField(region, field);
                WorldRegionBackdrop backdropRoot = WorldRegionBackdrop.Create(
                    environment, region, preparedField);
                backdropRoot.Name = "PreparedBackdrop";
                backdrop = new PackedScene();
                if (backdrop.Pack(backdropRoot) != Error.Ok)
                {
                    throw new InvalidOperationException($"Could not pack backdrop for '{region.Id}'.");
                }
                backdropRoot.Free();
            }
            prepared.Backdrop = backdrop;
            return prepared;
        }

        private async System.Threading.Tasks.Task<bool> BakeCell(
            RegionResource region, RegionCellResource cell, WorldHeightfield field)
        {
            if (GD.Load<PackedScene>(cell.ScenePath) is not { } source ||
                source.Instantiate() is not Node3D root)
            {
                Log.Error($"World bake: cell '{cell.Id}' cannot instance '{cell.ScenePath}'.");
                return false;
            }

            root.Name = cell.Id;
            root.Position = Vector3.Zero;
            WorldHeightfield view = cell.Presentation == null
                ? field
                : WorldTerrainMeshBuilder.ViewFor(field, cell.Presentation, cell.Center);
            WorldTerrainConform.Apply(root, view, cell.Center);
            WorldCellPresentation.Attach(root, region.EnvironmentProfile, cell.Presentation, view, cell.Center);
            WorldCellWater.Attach(root, cell.Presentation, view, cell.Center, FirstWater(region));
            WorldBiomeScatter.Attach(root, cell.Presentation, cell.BiomeScatter, view, cell.Center);
            AttachTraversalLinks(root, cell);

            // Only authored and deliberately baked additions belong to the PackedScene. Nodes
            // created by _Ready after this point retain no owner and are therefore reconstructed
            // exactly once when the prepared scene is instantiated at runtime.
            OwnUnownedDescendants(root, root);

            // Navigation parsing requires a live World3D. Runtime scripts see the same tree they see
            // in traversal probes, while CellNavBaker is explicitly suppressed to prevent a second bake.
            AddChild(root);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (Node node in Descendants(root))
            {
                if (node is NavigationRegion3D navigation && navigation.NavigationMesh != null)
                {
                    navigation.NavigationMesh = (NavigationMesh)navigation.NavigationMesh.Duplicate(true);
                    navigation.BakeNavigationMesh(false);
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Pack while the cell is still in the tree. Its presentation/water/scatter nodes own
            // disposable generated meshes and intentionally release them in _ExitTree; removing
            // first would serialize an empty terrain shell.
            var packed = new PackedScene();
            Error packedResult = packed.Pack(root);
            if (packedResult != Error.Ok)
            {
                RemoveChild(root);
                root.Free();
                Log.Error($"World bake: could not pack cell '{cell.Id}' ({packedResult}).");
                return false;
            }

            Error saved = ResourceSaver.Save(
                packed, WorldBakePaths.Cell(region.Id, cell.Id), ResourceSaver.SaverFlags.Compress);
            RemoveChild(root);
            root.Free();
            if (saved != Error.Ok)
            {
                Log.Error($"World bake: could not save cell '{cell.Id}' ({saved}).");
                return false;
            }
            Log.Info($"World bake: prepared cell '{cell.Id}'.");
            return true;
        }

        private static WorldWaterResource? FirstWater(RegionResource region)
        {
            foreach (RegionCellResource? cell in region.Cells)
            {
                if (cell?.Presentation == null)
                {
                    continue;
                }
                foreach (WorldWaterResource? water in cell.Presentation.Water)
                {
                    if (water != null)
                    {
                        return water;
                    }
                }
            }
            return null;
        }

        private static void AttachTraversalLinks(Node3D root, RegionCellResource cell)
        {
            if (cell.TraversalLinks.Count == 0)
            {
                return;
            }
            var host = new Node3D { Name = "PreparedTraversalLinks" };
            root.AddChild(host);
            foreach (WorldTraversalLinkResource? link in cell.TraversalLinks)
            {
                if (link == null)
                {
                    continue;
                }
                host.AddChild(new NavigationLink3D
                {
                    Name = string.IsNullOrEmpty(link.Id) ? link.Kind.ToString() : link.Id,
                    StartPosition = link.Start,
                    EndPosition = link.End,
                    Bidirectional = link.Bidirectional,
                    NavigationLayers = link.NavigationLayers,
                    TravelCost = link.TravelCost,
                });
            }
        }

        private static void OwnUnownedDescendants(Node node, Node owner)
        {
            foreach (Node child in node.GetChildren())
            {
                if (child.Owner == null)
                {
                    child.Owner = owner;
                }
                OwnUnownedDescendants(child, owner);
            }
        }

        private static System.Collections.Generic.IEnumerable<Node> Descendants(Node node)
        {
            foreach (Node child in node.GetChildren())
            {
                yield return child;
                foreach (Node nested in Descendants(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
