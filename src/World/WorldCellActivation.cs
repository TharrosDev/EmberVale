using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>
/// Owns the staged fidelity of one resident prepared cell. Resource I/O/instancing is stage zero;
/// terrain collision, navigation and gameplay are enabled on separate subsequent frames.
/// </summary>
internal sealed class WorldCellActivation
{
    private readonly record struct CollisionState(CollisionObject3D Node, uint Layer, uint Mask);
    private readonly record struct VisualState(
        GeometryInstance3D Node, bool Visible, GeometryInstance3D.ShadowCastingSetting Shadow);

    private readonly List<CollisionState> _collisions = new();
    private readonly List<VisualState> _visuals = new();
    private readonly List<NavigationRegion3D> _navigation = new();

    public WorldCellActivation(Node3D root)
    {
        Root = root;
        Capture(root);
        Root.ProcessMode = Node.ProcessModeEnum.Disabled;
        ApplyPresentation(WorldStreamingTier.Backdrop);
        ApplyCollision(WorldStreamingTier.Unloaded);
        ApplyNavigation(WorldStreamingTier.Unloaded);
    }

    public Node3D Root { get; }
    public WorldStreamingTier Tier { get; private set; } = WorldStreamingTier.Unloaded;
    public WorldStreamingTier TargetTier { get; set; } = WorldStreamingTier.Unloaded;
    public int Stage { get; set; }
    public bool GameplayActive => Tier == WorldStreamingTier.Near;

    /// <summary>Runs one bounded activation unit. Returns true when the requested tier is complete.</summary>
    public bool Advance()
    {
        // Deactivation is ordered gameplay -> nav -> collision -> presentation. The persistence
        // event is published by the streamer before this first step.
        if (TargetTier < Tier)
        {
            Root.ProcessMode = Node.ProcessModeEnum.Disabled;
            ApplyNavigation(TargetTier);
            ApplyCollision(TargetTier);
            ApplyPresentation(TargetTier);
            Tier = TargetTier;
            Stage = 0;
            return true;
        }

        switch (Stage++)
        {
            case 0:
                ApplyPresentation(TargetTier);
                return false;
            case 1:
                ApplyCollision(TargetTier);
                return false;
            case 2:
                ApplyNavigation(TargetTier);
                return false;
            default:
                Root.ProcessMode = TargetTier == WorldStreamingTier.Near
                    ? Node.ProcessModeEnum.Inherit
                    : Node.ProcessModeEnum.Disabled;
                Tier = TargetTier;
                Stage = 0;
                return true;
        }
    }

    public bool HasTerrainCollision()
    {
        foreach (CollisionState state in _collisions)
        {
            if (state.Node.Name == "TerrainCollider" && state.Node.CollisionLayer != 0u)
            {
                return true;
            }
        }
        return false;
    }

    public bool HasNavigation()
    {
        foreach (NavigationRegion3D region in _navigation)
        {
            if (region.Enabled && region.NavigationMesh?.GetPolygonCount() > 0)
            {
                return true;
            }
        }
        return _navigation.Count == 0;
    }

    private void Capture(Node node)
    {
        if (node is CollisionObject3D collision)
        {
            _collisions.Add(new CollisionState(collision, collision.CollisionLayer, collision.CollisionMask));
        }
        if (node is GeometryInstance3D visual)
        {
            _visuals.Add(new VisualState(visual, visual.Visible, visual.CastShadow));
        }
        if (node is NavigationRegion3D navigation)
        {
            _navigation.Add(navigation);
        }
        foreach (Node child in node.GetChildren())
        {
            Capture(child);
        }
    }

    private void ApplyPresentation(WorldStreamingTier tier)
    {
        foreach (VisualState state in _visuals)
        {
            bool visible = state.Visible && tier switch
            {
                WorldStreamingTier.Unloaded => false,
                WorldStreamingTier.Backdrop => IsDistantRepresentation(state.Node),
                WorldStreamingTier.Far => !IsGameplayVisual(state.Node),
                _ => true,
            };
            state.Node.Visible = visible;
            state.Node.CastShadow = tier >= WorldStreamingTier.Mid
                ? state.Shadow
                : GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    private void ApplyCollision(WorldStreamingTier tier)
    {
        foreach (CollisionState state in _collisions)
        {
            bool enabled = tier == WorldStreamingTier.Near ||
                           (tier == WorldStreamingTier.Mid && state.Node.Name == "TerrainCollider");
            state.Node.CollisionLayer = enabled ? state.Layer : 0u;
            state.Node.CollisionMask = enabled ? state.Mask : 0u;
        }
    }

    private void ApplyNavigation(WorldStreamingTier tier)
    {
        foreach (NavigationRegion3D navigation in _navigation)
        {
            navigation.Enabled = tier == WorldStreamingTier.Near;
        }
    }

    private static bool IsGameplayVisual(Node node)
    {
        for (Node? current = node; current != null; current = current.GetParent())
        {
            if (current is CharacterBody3D || current.IsInGroup("world_gameplay"))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsDistantRepresentation(Node node)
    {
        string name = node.Name.ToString();
        for (Node? current = node; current != null; current = current.GetParent())
        {
            string currentName = current.Name.ToString();
            if (currentName is "WorldPresentation" or "PreparedBackdrop" ||
                currentName.Contains("Hlod", System.StringComparison.OrdinalIgnoreCase) ||
                current.IsInGroup("world_landmark"))
            {
                return true;
            }
        }
        return name is "SurfaceSkin" or "GeneratedWater";
    }
}
