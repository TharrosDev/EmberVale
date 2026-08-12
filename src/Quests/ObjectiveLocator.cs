using System;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Items;
using Embervale.World;
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

    /// <summary>
    /// Group every conversable actor joins (41A), for <see cref="ObjectiveType.Talk"/>.
    ///
    /// ⚠️ <b>Joined by <c>DialogueComponent</c> itself, not by a factory, and that is the difference
    /// from the two groups above.</b> Enemies and pickups are spawned by code, so their factories add
    /// them — three enemy factories do it in three places. NPCs have <b>no factory at all</b>: all
    /// seventeen are authored directly as nodes in seven cell <c>.tscn</c> files. Adding the group
    /// there would mean editing seventeen scene stanzas and remembering it for every NPC ever
    /// authored afterwards, which is the kind of rule that holds for exactly as long as whoever wrote
    /// it is still reading. Self-registration covers the whole roster, now and later, in one line.
    /// </summary>
    public const string NpcGroup = "objective.npc";

    /// <summary>The world position of the nearest live target for this objective, or null when none
    /// is loaded (so the compass simply shows no objective marker that frame).</summary>
    public static Vector3? Locate(ObjectiveResource? objective, SceneTree? tree, Vector3 from)
    {
        if (objective == null || tree == null)
        {
            return null;
        }

        Vector3? live = string.IsNullOrEmpty(objective.TargetId) ? null : objective.Type switch
        {
            ObjectiveType.Kill => Nearest(tree, EnemyGroup, from,
                e => e.TemplateId == objective.TargetId),
            ObjectiveType.Collect => Nearest(tree, PickupGroup, from,
                e => e.GetComponent<ItemPickupComponent>()?.ItemId == objective.TargetId),
            ObjectiveType.Talk => Nearest(tree, NpcGroup, from,
                e => e.GetComponent<Dialogue.DialogueComponent>()?.DialogueId == objective.TargetId),

            // ⚠️ Reach resolves through MapService rather than by scanning actors, because a place is
            // not an actor — there is nothing in the tree to find. It is also the one type whose
            // TargetId IS a location id, so it needs no LocationId fallback and authoring one on a
            // Reach objective is a mistake the validator refuses.
            ObjectiveType.Reach => LocationPosition(objective.TargetId),
            _ => null,
        };

        // ⚠️ THE LIVE TARGET WINS, AND THE AUTHORED PLACE IS THE FALLBACK — NOT THE OTHER WAY ROUND
        // (39.5C). The dragon actually standing in front of you is a better answer than the roost it
        // is supposed to be in, and a Collect objective should point at the herb you can see rather
        // than at the meadow it usually grows in. The authored location is what the game knows when
        // it can see nothing.
        return live ?? LocationOf(objective);
    }

    /// <summary>
    /// Where the objective's authored <see cref="ObjectiveResource.LocationId"/> is, or null.
    ///
    /// ⚠️ <b>Null is the correct and common answer, and it must stay null.</b>
    /// <see cref="MapService.PositionOf"/> knows a position only for a location whose cell is
    /// resident or which the player has already found — so an objective across a region boundary
    /// resolves to nothing, by design (maintainer decision, 39.5C). The tracker names the place
    /// instead of drawing an arrow at a coordinate nobody measured; a position invented here would
    /// be the one thing 39.5A's invariant forbids, and it would point the compass at a lie.
    /// </summary>
    public static Vector3? LocationOf(ObjectiveResource? objective)
    {
        if (objective == null || string.IsNullOrEmpty(objective.LocationId))
        {
            return null;
        }

        return LocationPosition(objective.LocationId);
    }

    /// <summary>Where a <c>location.*</c> id is according to <see cref="MapService"/>, or null when
    /// the map cannot answer (its cell is not resident and no save remembered it).</summary>
    private static Vector3? LocationPosition(string locationId)
    {
        if (string.IsNullOrEmpty(locationId))
        {
            return null;
        }

        return ServiceLocator.Instance is { } locator && locator.TryGet(out MapService map)
            ? map.PositionOf(locationId)
            : null;
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
