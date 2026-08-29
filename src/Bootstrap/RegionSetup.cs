using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// What a region needs done to it when it becomes the active one: its safe zones applied, its doors
/// placed, and — for the player standing at one of those doors — the toll resolved.
///
/// <b>Why these three and not the rest of the transition path.</b> The 2026-08-15 audit identified a
/// <c>RegionTransitionDirector</c> worth roughly 290 lines, covering these plus the streamer swap, the
/// fast-travel handler and the hard-load state machine. ⚠️ <b>That larger extraction was deliberately
/// NOT taken, because nothing in this project exercises a region transition automatically.</b>
/// <c>--play</c> boots into one region and streams its cells; it never walks a portal or takes a
/// fast-travel jump, and <c>--panelshots</c> opens the map without travelling. Moving the state
/// machine would mean shipping it verified by reading, and the audit's own rule is that a refactor
/// with no way to prove it is a refactor that waits for a session with a playthrough in it.
///
/// These three were taken because each is provably behaviour-preserving on its own: two are pure
/// construction against authored data, and the third is a decision function that already delegates
/// its arithmetic to <see cref="Embervale.Economy.TollFee"/>. None of them owns the transition's
/// state — <c>_currentRegionId</c>, <c>_loadingElapsed</c> and the streamer stay where they are.
/// </summary>
internal static class RegionSetup
{
    /// <summary>
    /// Replaces the resident safe zones with this region's (Phase 38M2): the region-wide circle plus
    /// one per authored cell. ⚠️ <b>Replace, never merge</b> — a region that inherited the previous
    /// one's zones would suppress encounters in places with nothing there, and the symptom is enemies
    /// quietly refusing to spawn. The same rule every <c>ISaveable.Load</c> follows, for the same
    /// reason.
    /// </summary>
    internal static void ApplySafeZones(RegionResource? region)
    {
        SafeZones.Set(region?.SafeZoneCenter ?? Vector3.Zero, region?.SafeZoneRadius ?? 0f);

        if (region == null)
        {
            return;
        }

        foreach (RegionCellResource cell in region.Cells)
        {
            if (cell != null)
            {
                SafeZones.Add(cell.Center, cell.SafeRadius);
            }
        }
    }

    /// <summary>Places a hard-transition portal for each of the region's neighbours (Phase 25C), a
    /// few metres in front of where the player enters. Frees any prior region's portals first so a
    /// transition swaps them out. Mirrors the other code-built actors: an Entity + mesh + collider +
    /// a <see cref="RegionTransitionComponent"/>. <paramref name="portals"/> is the caller's live
    /// list and is rewritten in place.</summary>
    internal static void RebuildPortals(Node root, List<Entity> portals, RegionResource? region)
    {
        foreach (Entity portal in portals)
        {
            if (GodotObject.IsInstanceValid(portal))
            {
                portal.QueueFree();
            }
        }

        portals.Clear();

        if (region == null)
        {
            return;
        }

        foreach (string neighbourId in region.Neighbours)
        {
            RegionResource? neighbour = RegionDatabase.Get(neighbourId);
            if (neighbour == null)
            {
                Log.Warn($"RebuildPortals: region '{region.Id}' lists unknown neighbour '{neighbourId}'.");
                continue;
            }

            // 38M2: a region can say where its doors stand. Empty means the original "a few metres in
            // front of the spawn", which is still right for a region with no gate of its own.
            var portal = new Entity
            {
                Name = $"Portal_{neighbourId}",
                DisplayName = neighbour.DisplayName,
                Position = region.PortalPoint != Vector3.Zero
                    ? region.PortalPoint
                    : region.SpawnPoint + new Vector3(0f, -1.2f, -4f),
            };

            portal.AddChild(new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1.3f },
                Position = new Vector3(0f, 1.6f, 0f),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.5f, 0.75f, 1f),
                    EmissionEnabled = true,
                    Emission = new Color(0.35f, 0.6f, 1f),
                },
            });

            var collider = new StaticBody3D { Name = "Collider" };
            collider.AddChild(new CollisionShape3D
            {
                Shape = new CylinderShape3D { Radius = 1.3f, Height = 3.2f },
                Position = new Vector3(0f, 1.6f, 0f),
            });
            portal.AddChild(collider);

            portal.AddChild(new RegionTransitionComponent
            {
                Name = "Transition",
                TargetRegionId = neighbourId,

                // The destination decides what unlocks it (33D), and as of 2026-08-28 NOTHING DOES:
                // both regions author an empty UnlockFlagId, so every portal is open from the start.
                // ⚠️ The older comment here said Frostfang "carries the Iron King's defeat flag",
                // which was wrong twice over — the flag it carried was quest.warband.heart's, and it
                // carries none at all now (maintainer direction; see the block above UnlockFlagId in
                // data/regions/FrostfangReach.tres). The mechanism stays: set a destination's
                // UnlockFlagId and its door hides and goes inert again with no code change.
                RequiredFlagId = neighbour.UnlockFlagId,
            });
            root.AddChild(portal);
            portals.Add(portal);
        }
    }

    /// <summary>
    /// Takes the road wardens' toll for entering <paramref name="destination"/> (Phase 38M), and
    /// answers whether the crossing may proceed.
    ///
    /// This is a shared static rather than logic on <see cref="RegionTransitionComponent"/> because it
    /// is the one place the portal and the <c>region</c> dev command both arrive — 38C's lesson from
    /// the travel fee, where gating the map screen alone would have left the console a free ride. It is
    /// deliberately <em>not</em> on the fast-travel path: that already pays <c>TravelFee</c>, and one
    /// journey does not pay two charges.
    ///
    /// Fails closed, like the travel fee: no gold, no crossing. The refusal is not toasted because
    /// <c>Notifications</c> has no generic message event — the portal's own prompt has already named
    /// the price and the shortfall, which is <c>ServiceComponent</c>'s rule that every refusal says
    /// itself where the player is already looking.
    /// </summary>
    internal static bool PayToll(PlayerCharacter? player, RegionResource destination)
    {
        if (destination.TollGold <= 0 || player == null)
        {
            return true;
        }

        StoryFlagsComponent? flags = player.GetComponent<StoryFlagsComponent>();
        InventoryComponent? purse = player.GetComponent<InventoryComponent>();

        switch (Economy.TollFee.Resolve(
            hasPermit: flags?.Has(destination.TollPermitFlagId) ?? false,
            hasPass: flags?.Has(destination.TollPassFlagId) ?? false,
            fee: destination.TollGold,
            goldHeld: purse?.CountOf(GameIds.Currency.Gold) ?? 0))
        {
            case Economy.TollOutcome.PassSpent:
                flags?.Clear(destination.TollPassFlagId);
                Log.Info($"Toll at '{destination.Id}' covered by a one-crossing pass.");
                return true;

            case Economy.TollOutcome.Charged:
                // The RemoveItem is still its own condition: chained into the Resolve above, a purse
                // that emptied between the prompt and the press would fall through to a free crossing.
                if (purse?.RemoveItem(GameIds.Currency.Gold, destination.TollGold) != true)
                {
                    Log.Warn($"Toll at '{destination.Id}' refused: {destination.TollGold} gold required.");
                    return false;
                }

                return true;

            case Economy.TollOutcome.CannotAfford:
                Log.Warn($"Toll at '{destination.Id}' refused: {destination.TollGold} gold required.");
                return false;

            default:
                return true; // Free, or a permit the player has already bought
        }
    }
}
