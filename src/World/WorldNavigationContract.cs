using System.Collections.Generic;
using Godot;

namespace Embervale.World;

public readonly record struct WorldNavigationAgentProfile(
    string Id, float Radius, float Height, float MaxSlopeDegrees, uint NavigationLayer);

/// <summary>Representative traversal envelopes validated by world authoring and probes.</summary>
public static class WorldNavigationContract
{
    public static readonly IReadOnlyList<WorldNavigationAgentProfile> Profiles = new[]
    {
        new WorldNavigationAgentProfile("player", 0.40f, 1.80f, 44f, 1u << 0),
        new WorldNavigationAgentProfile("humanoid", 0.45f, 1.85f, 42f, 1u << 0),
        new WorldNavigationAgentProfile("small_humanoid", 0.30f, 1.25f, 46f, 1u << 0),
        new WorldNavigationAgentProfile("large_enemy", 0.85f, 2.70f, 36f, 1u << 0),
    };

    public static bool RouteSupports(float width, float slopeRatio, WorldNavigationAgentProfile profile) =>
        width >= (profile.Radius * 2f) + 0.4f &&
        Mathf.RadToDeg(Mathf.Atan(slopeRatio)) <= profile.MaxSlopeDegrees;
}

public enum WorldTraversalKind
{
    Jump,
    Drop,
    Climb,
    Door,
    Gate,
    NarrowCrossing,
}

/// <summary>Explicit off-mesh capability link baked with a cell's navigation package.</summary>
[GlobalClass]
public partial class WorldTraversalLinkResource : Resource
{
    [Export] public string Id { get; set; } = string.Empty;
    [Export] public WorldTraversalKind Kind { get; set; }
    [Export] public Vector3 Start { get; set; }
    [Export] public Vector3 End { get; set; }
    [Export] public bool Bidirectional { get; set; } = true;
    [Export(PropertyHint.Layers3DNavigation)] public uint NavigationLayers { get; set; } = 1u;
    [Export(PropertyHint.Range, "0.1,20,0.1")] public float TravelCost { get; set; } = 1f;
}
