using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>
/// Owns the staged fidelity of one resident prepared cell. Resource I/O/instancing is stage zero;
/// terrain collision, navigation and gameplay are enabled on separate subsequent frames.
/// </summary>
internal sealed class WorldCellActivation
{
    private readonly record struct CollisionState(
        CollisionObject3D Node, uint Layer, uint Mask, bool Terrain);

    /// <summary><c>Gameplay</c> and <c>Distant</c> are what the Far and Backdrop tiers ask of a
    /// visual. Both are facts about where the node sits in the cell, so they are read once while the
    /// cell is captured instead of by walking every visual's ancestors on every tier change.</summary>
    private readonly record struct VisualState(
        GeometryInstance3D Node, bool Visible, GeometryInstance3D.ShadowCastingSetting Shadow,
        bool Gameplay, bool Distant);

    /// <summary>Nodes handled between two looks at the clock. Small enough that one slice is tens of
    /// microseconds, so an activation never overruns the streamer's budget by more than that.</summary>
    private const int SliceSize = 24;
    private const int FromEnd = int.MaxValue;

    private readonly List<CollisionState> _collisions = new();
    private readonly List<VisualState> _visuals = new();
    private readonly List<NavigationRegion3D> _navigation = new();
    private readonly List<Node3D> _transientActors = new();

    /// <summary>Where the phase in progress resumes; <see cref="FromEnd"/> when it has not begun.</summary>
    private int _cursor = FromEnd;

    public WorldCellActivation(Node3D root)
    {
        Root = root;
        Capture(root, gameplay: false, distant: false);
        Root.ProcessMode = Node.ProcessModeEnum.Disabled;
        ApplyPresentation(WorldStreamingTier.Backdrop, ulong.MaxValue);
        ApplyCollision(WorldStreamingTier.Unloaded, ulong.MaxValue);
        ApplyNavigation(WorldStreamingTier.Unloaded);
    }

    public Node3D Root { get; }
    public WorldStreamingTier Tier { get; private set; } = WorldStreamingTier.Unloaded;
    public WorldStreamingTier TargetTier { get; set; } = WorldStreamingTier.Unloaded;
    public int Stage { get; set; }
    public bool GameplayActive => Tier == WorldStreamingTier.Near;

    /// <summary>Aims the cell at a tier and restarts its staged activation from the first phase.
    /// Every phase writes absolute state, so abandoning one half-way is safe.</summary>
    public void Retarget(WorldStreamingTier target)
    {
        TargetTier = target;
        Stage = 0;
        _cursor = FromEnd;
    }

    /// <summary>Emergent actors are authored in world space and live only while this cell is Near.</summary>
    public void AddTransientActor(Node3D actor, Vector3 position)
    {
        // Initialization runs during AddChild. Put the detached actor in cell-local space first so
        // AI home positions and every component's OnInitialize observe the intended world point.
        actor.Position = Root.ToLocal(position);
        Root.AddChild(actor);
        _transientActors.Add(actor);
        actor.TreeExited += () => _transientActors.Remove(actor);
    }

    public void RetireTransientActors()
    {
        foreach (Node3D actor in _transientActors)
        {
            if (GodotObject.IsInstanceValid(actor))
            {
                actor.QueueFree();
            }
        }
        _transientActors.Clear();
    }

    /// <summary>
    /// Runs activation work until <paramref name="deadlineUsec"/> (a <see cref="Time.GetTicksUsec"/>
    /// value). Returns true when the requested tier is complete.
    ///
    /// Activation is presentation, collision, navigation, then gameplay, one phase per call so the
    /// streamer can interleave cells. Deactivation is gameplay, navigation, collision, then
    /// presentation and runs straight through; the persistence event is published by the streamer
    /// before its first step. A phase that runs out of time resumes where it stopped on the next
    /// call. A cell with a thousand visuals used to take its whole presentation pass in one frame
    /// whatever the budget said.
    /// </summary>
    public bool Advance(ulong deadlineUsec)
    {
        bool down = TargetTier < Tier;
        if (down && Stage == 0)
        {
            Root.ProcessMode = Node.ProcessModeEnum.Disabled;
        }

        while (Stage <= 2)
        {
            bool phaseComplete = (down ? 2 - Stage : Stage) switch
            {
                0 => ApplyPresentation(TargetTier, deadlineUsec),
                1 => ApplyCollision(TargetTier, deadlineUsec),
                _ => ApplyNavigation(TargetTier),
            };
            if (!phaseComplete)
            {
                return false;
            }
            Stage++;
            if (!down)
            {
                return false;
            }
        }

        Root.ProcessMode = TargetTier == WorldStreamingTier.Near
            ? Node.ProcessModeEnum.Inherit
            : Node.ProcessModeEnum.Disabled;
        Tier = TargetTier;
        Stage = 0;
        _cursor = FromEnd;
        return true;
    }

    /// <summary>One unbudgeted activation unit, for probes that complete a tier synchronously.</summary>
    public bool Advance() => Advance(ulong.MaxValue);

    public bool HasTerrainCollision()
    {
        foreach (CollisionState state in _collisions)
        {
            // Not pruned here: a phase in progress resumes by index, so only the phases remove.
            if (state.Terrain && GodotObject.IsInstanceValid(state.Node) &&
                state.Node.CollisionLayer != 0u)
            {
                return true;
            }
        }
        return false;
    }

    public bool HasNavigation()
    {
        for (int i = _navigation.Count - 1; i >= 0; i--)
        {
            NavigationRegion3D region = _navigation[i];
            if (!GodotObject.IsInstanceValid(region))
            {
                _navigation.RemoveAt(i);
                continue;
            }
            if (region.Enabled && region.NavigationMesh?.GetPolygonCount() > 0)
            {
                return true;
            }
        }
        return _navigation.Count == 0;
    }

    private void Capture(Node node, bool gameplay, bool distant)
    {
        // One name read per node, here, instead of one per ancestor per visual per tier change.
        string name = node.Name.ToString();
        gameplay |= node is CharacterBody3D || node.IsInGroup("world_gameplay");
        distant |= name is "WorldPresentation" or "PreparedBackdrop" ||
                   name.Contains("Hlod", System.StringComparison.OrdinalIgnoreCase) ||
                   node.IsInGroup("world_landmark");

        if (node is CollisionObject3D collision)
        {
            _collisions.Add(new CollisionState(
                collision, collision.CollisionLayer, collision.CollisionMask, name == "TerrainCollider"));
        }
        if (node is GeometryInstance3D visual)
        {
            _visuals.Add(new VisualState(
                visual, visual.Visible, visual.CastShadow, gameplay,
                distant || name is "SurfaceSkin" or "GeneratedWater"));
        }
        if (node is NavigationRegion3D navigation)
        {
            _navigation.Add(navigation);
        }
        int children = node.GetChildCount();
        for (int i = 0; i < children; i++)
        {
            Capture(node.GetChild(i), gameplay, distant);
        }
    }

    private bool ApplyPresentation(WorldStreamingTier tier, ulong deadlineUsec)
    {
        int handled = 0;
        for (int i = System.Math.Min(_cursor, _visuals.Count - 1); i >= 0; i--)
        {
            VisualState state = _visuals[i];
            // Death and persistence reconciliation can free descendants while the prepared cell
            // itself stays resident. Captured handles describe its initial contents, not a lifetime
            // guarantee; prune before any native property touches them.
            if (!GodotObject.IsInstanceValid(state.Node))
            {
                _visuals.RemoveAt(i);
            }
            else
            {
                state.Node.Visible = state.Visible && tier switch
                {
                    WorldStreamingTier.Unloaded => false,
                    WorldStreamingTier.Backdrop => state.Distant,
                    WorldStreamingTier.Far => !state.Gameplay,
                    _ => true,
                };
                state.Node.CastShadow = tier >= WorldStreamingTier.Mid
                    ? state.Shadow
                    : GeometryInstance3D.ShadowCastingSetting.Off;
            }

            if (++handled % SliceSize == 0 && i > 0 && Time.GetTicksUsec() >= deadlineUsec)
            {
                _cursor = i - 1;
                return false;
            }
        }
        _cursor = FromEnd;
        return true;
    }

    private bool ApplyCollision(WorldStreamingTier tier, ulong deadlineUsec)
    {
        int handled = 0;
        for (int i = System.Math.Min(_cursor, _collisions.Count - 1); i >= 0; i--)
        {
            CollisionState state = _collisions[i];
            if (!GodotObject.IsInstanceValid(state.Node))
            {
                _collisions.RemoveAt(i);
            }
            else
            {
                bool enabled = tier == WorldStreamingTier.Near ||
                               (tier == WorldStreamingTier.Mid && state.Terrain);
                state.Node.CollisionLayer = enabled ? state.Layer : 0u;
                state.Node.CollisionMask = enabled ? state.Mask : 0u;
            }

            if (++handled % SliceSize == 0 && i > 0 && Time.GetTicksUsec() >= deadlineUsec)
            {
                _cursor = i - 1;
                return false;
            }
        }
        _cursor = FromEnd;
        return true;
    }

    private bool ApplyNavigation(WorldStreamingTier tier)
    {
        for (int i = _navigation.Count - 1; i >= 0; i--)
        {
            NavigationRegion3D navigation = _navigation[i];
            if (!GodotObject.IsInstanceValid(navigation))
            {
                _navigation.RemoveAt(i);
                continue;
            }
            navigation.Enabled = tier == WorldStreamingTier.Near;
        }
        return true;
    }
}
