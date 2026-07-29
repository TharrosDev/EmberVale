using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Core.Services;
using Godot;

namespace Embervale.World;

/// <summary>
/// An interactable that triggers a hard region-to-region load (Phase 25C). On the player's
/// <c>E</c> raycast it publishes a <see cref="RegionTransitionRequestedEvent"/> for
/// <see cref="TargetRegionId"/>; the bootstrap performs the swap (unload current cells,
/// re-target the streamer, teleport the player to the new region's spawn, loading screen).
/// Mirrors <see cref="Embervale.Dialogue.DialogueComponent"/>: a trigger only publishes intent.
///
/// <b>Optionally gated (Phase 33D):</b> with a <see cref="RequiredFlagId"/> set, the portal is
/// hidden and inert until the player's story flags carry it. The vertical slice uses this to keep
/// the Frostfang door out of the starting square — the portals are placed at the region's spawn
/// point, so an ungated neighbour link puts the <em>next</em> region four metres in front of a
/// brand-new character. Hiding follows the <see cref="Companions.CompanionRecruiterComponent"/>
/// pattern: visibility <em>and</em> collision, remembering the authored layer rather than assuming
/// one, since an invisible body the interact ray still hits leaves a ghost prompt in the world.
/// </summary>
[GlobalClass]
public partial class RegionTransitionComponent : InteractableComponent
{
    /// <summary>Destination region id (a <c>region.*</c> key), resolved through the
    /// <see cref="RegionDatabase"/>.</summary>
    [Export] public string TargetRegionId { get; set; } = string.Empty;

    /// <summary>Story flag the player must carry before this portal exists for them. Empty = always
    /// open (the default, so every other region link is unaffected).</summary>
    [Export] public string RequiredFlagId { get; set; } = string.Empty;

    // Authored collision layers, remembered while hidden so revealing restores them exactly.
    private readonly Dictionary<CollisionObject3D, uint> _hiddenLayers = new();
    private bool _revealed = true;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        EventBus.Instance?.Subscribe<StoryFlagChangedEvent>(OnFlagChanged);
        EventBus.Instance?.Subscribe<Core.Events.GameLoadedEvent>(OnGameLoaded);
        Refresh();
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<StoryFlagChangedEvent>(OnFlagChanged);
        EventBus.Instance?.Unsubscribe<Core.Events.GameLoadedEvent>(OnGameLoaded);
        base.OnTeardown();
    }

    private void OnFlagChanged(StoryFlagChangedEvent e) => Refresh();

    // A load restores flags wholesale rather than one event at a time, so re-derive on load.
    private void OnGameLoaded(Core.Events.GameLoadedEvent e) => Refresh();

    /// <summary>Shows or hides the portal to match the player's flags.</summary>
    private void Refresh()
    {
        bool open = string.IsNullOrEmpty(RequiredFlagId) ||
            (ServiceLocator.Instance is { } sl && sl.TryGet(out PlayerCharacter player) &&
             player.GetComponent<StoryFlagsComponent>()?.Has(RequiredFlagId) == true);

        if (open == _revealed)
        {
            return;
        }

        _revealed = open;
        if (Entity?.Body is not Node3D body || !IsInstanceValid(body))
        {
            return;
        }

        body.Visible = open;
        foreach (Node child in body.GetChildren())
        {
            if (child is not CollisionObject3D collider)
            {
                continue;
            }

            if (open)
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

    public override string Prompt
    {
        get
        {
            string where = RegionDatabase.Get(TargetRegionId)?.DisplayName ?? "elsewhere";
            return Loc.TF("region.travel_prompt", where);
        }
    }

    public override void Interact(IEntity instigator)
    {
        if (!_revealed)
        {
            return;
        }

        if (RegionDatabase.Get(TargetRegionId) == null)
        {
            Log.Warn($"RegionTransitionComponent: unknown region id '{TargetRegionId}'.");
            return;
        }

        EventBus.Instance?.Publish(new RegionTransitionRequestedEvent(TargetRegionId));
    }
}
