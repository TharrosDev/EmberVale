using System;
using Embervale.Entities;
using Embervale.Items;
using Godot;

namespace Embervale.Quests;

/// <summary>
/// Resolves a quest <see cref="ObjectiveResource"/> to a world position so the Phase 25F HUD compass
/// can point at it. Objectives are authored against an id, not a location, so the target is found
/// live by type: a Kill objective points at the nearest matching enemy, a Collect objective at the
/// nearest matching world pickup. Actors register themselves in the lookup groups
/// (<see cref="EnemyGroup"/>/<see cref="PickupGroup"/>) on spawn; this just scans them.
///
/// The switch is the extension seam — future objective types (Talk → nearest NPC, Reach → a POI)
/// add a branch here, not a new system.
/// </summary>
public static class ObjectiveLocator
{
    /// <summary>Group every targetable enemy joins on spawn (see <c>EnemyFactory</c>).</summary>
    public const string EnemyGroup = "objective.enemy";

    /// <summary>Group every world item pickup joins on spawn (see <c>ItemPickupFactory</c>).</summary>
    public const string PickupGroup = "objective.pickup";

    /// <summary>The world position of the nearest live target for this objective, or null when none
    /// is loaded (so the compass simply shows no objective marker that frame).</summary>
    public static Vector3? Locate(ObjectiveResource? objective, SceneTree? tree, Vector3 from)
    {
        if (objective == null || tree == null || string.IsNullOrEmpty(objective.TargetId))
        {
            return null;
        }

        return objective.Type switch
        {
            ObjectiveType.Kill => Nearest(tree, EnemyGroup, from,
                e => e.TemplateId == objective.TargetId),
            ObjectiveType.Collect => Nearest(tree, PickupGroup, from,
                e => e.GetComponent<ItemPickupComponent>()?.ItemId == objective.TargetId),
            _ => null,
        };
    }

    // ponytail: linear scan of the (small) group each call; the caller throttles re-resolution.
    // Swap to a spatial index only if a group ever grows large enough to show up in a profile.
    private static Vector3? Nearest(SceneTree tree, string group, Vector3 from, Func<IEntity, bool> match)
    {
        Vector3? best = null;
        float bestSq = float.MaxValue;

        foreach (Node node in tree.GetNodesInGroup(group))
        {
            if (node is not IEntity entity || !GodotObject.IsInstanceValid(node) || !match(entity))
            {
                continue;
            }

            Node3D body = entity.Body;
            if (!GodotObject.IsInstanceValid(body))
            {
                continue;
            }

            Vector3 p = body.GlobalPosition;
            float dx = p.X - from.X;
            float dz = p.Z - from.Z;
            float sq = (dx * dx) + (dz * dz);
            if (sq < bestSq)
            {
                bestSq = sq;
                best = p;
            }
        }

        return best;
    }
}
