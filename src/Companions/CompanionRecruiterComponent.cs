using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Godot;

namespace Embervale.Companions;

/// <summary>
/// Keeps the world NPC and the party member from being on screen at the same time (Phase 32E).
/// A recruitable companion exists twice: as the standing NPC you talk to, and as the actor that
/// walks behind you once recruited. This component sits on the NPC and hides it (and its collider,
/// so the interaction prompt goes with it) while its companion is in the party — then brings it back
/// when they are dismissed, so the same conversation can recruit them again.
///
/// The alternative — freeing the NPC on recruit — loses the authored placement and the dialogue hook
/// permanently, which is exactly what a player who dismisses a companion then wants back.
/// </summary>
[GlobalClass]
public partial class CompanionRecruiterComponent : EntityComponent
{
    /// <summary>The companion this NPC stands in for.</summary>
    [Export] public string CompanionId { get; set; } = string.Empty;

    // Authored collision layers, remembered while the NPC is hidden so showing restores them exactly.
    private readonly Dictionary<CollisionObject3D, uint> _hiddenLayers = new();

    protected override void OnInitialize()
    {
        EventBus.Instance?.Subscribe<CompanionRecruitedEvent>(OnRecruited);
        EventBus.Instance?.Subscribe<CompanionDismissedEvent>(OnDismissed);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
        Refresh();
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<CompanionRecruitedEvent>(OnRecruited);
        EventBus.Instance?.Unsubscribe<CompanionDismissedEvent>(OnDismissed);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
    }

    private void OnRecruited(CompanionRecruitedEvent e)
    {
        if (e.CompanionId == CompanionId)
        {
            SetPresent(false);
        }
    }

    private void OnDismissed(CompanionDismissedEvent e)
    {
        if (e.CompanionId == CompanionId)
        {
            SetPresent(true);
        }
    }

    // A load can restore a party this NPC's cell knows nothing about, so re-derive on load rather
    // than trusting the events that fired before the cell streamed in.
    private void OnGameLoaded(GameLoadedEvent e) => Refresh();

    private void Refresh()
    {
        bool recruited = ServiceLocator.Instance != null &&
            ServiceLocator.Instance.TryGet(out CompanionRoster roster) &&
            roster.IsRecruited(CompanionId);
        SetPresent(!recruited);
    }

    private void SetPresent(bool present)
    {
        if (Entity?.Body is not Node3D body || !IsInstanceValid(body))
        {
            return;
        }

        body.Visible = present;

        // Hiding a Node3D does not disable its collision, and an invisible body the player can still
        // raycast into would leave a ghost interaction prompt hanging in the street. The authored
        // layer is remembered on the way out so showing the NPC again restores it exactly.
        foreach (Node child in body.GetChildren())
        {
            if (child is not CollisionObject3D collider)
            {
                continue;
            }

            if (present)
            {
                if (_hiddenLayers.TryGetValue(collider, out uint layer))
                {
                    collider.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, layer);
                    _hiddenLayers.Remove(collider);
                }
            }
            else if (collider.CollisionLayer != 0u)
            {
                _hiddenLayers[collider] = collider.CollisionLayer;
                collider.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 0u);
            }
        }
    }
}
