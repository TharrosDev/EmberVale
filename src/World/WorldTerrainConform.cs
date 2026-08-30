using Godot;

namespace Embervale.World;

/// <summary>
/// Drops every authored node in a cell onto the terrain under it (the 2026-08-29 geography
/// overhaul).
///
/// ⚠️ <b>THIS IS THE MIGRATION, AND IT IS WHY FIFTEEN SCENES DID NOT NEED REWRITING.</b> Every prop,
/// building, NPC, stall and pickup in the realm was authored against a floor whose top face was
/// exactly y = 0. Giving the world real elevation would otherwise have buried or floated all of
/// them. Instead a node's authored Y became its height <em>above the ground</em>, which is what an
/// author meant by it in every case, and this adds the ground back at load.
///
/// ⚠️ <b>A STRUCTURE WANTS A LEVEL PAD, NOT A CONFORM.</b> Only the node's own origin is sampled, so a
/// twelve-metre building on a hillside sinks one corner. Author a <see cref="WorldGroundAreaResource"/>
/// under every building cluster; that is what they are for, and it is also how a settlement gets its
/// raised ground.
///
/// Opt out by putting a node in the <c>terrain_absolute</c> group — for anything whose Y is a real
/// world height rather than a clearance (a flying thing, a ceiling, a water surface).
/// </summary>
public static class WorldTerrainConform
{
    public const string AbsoluteGroup = "terrain_absolute";

    /// <summary>
    /// Raises the direct children of <paramref name="cellRoot"/>, and of its <c>Nav</c> region, by
    /// the ground height under each. Call before the cell enters the tree; <paramref name="origin"/>
    /// is the cell's world <c>Center</c>, which has not been applied to child transforms yet.
    /// </summary>
    public static void Apply(Node3D cellRoot, WorldHeightfield field, Vector3 origin)
    {
        Conform(cellRoot, field, origin);
        if (cellRoot.GetNodeOrNull<NavigationRegion3D>("Nav") is { } nav)
        {
            Conform(nav, field, origin);
        }
    }

    private static void Conform(Node parent, WorldHeightfield field, Vector3 origin)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (child is not Node3D node || child is NavigationRegion3D || child is WorldCellPresentation ||
                child is WorldBiomeScatter || node.IsInGroup(AbsoluteGroup))
            {
                continue;
            }

            Vector3 local = node.Position;
            node.Position = new Vector3(
                local.X, local.Y + field.Height(origin.X + local.X, origin.Z + local.Z), local.Z);
        }
    }
}
