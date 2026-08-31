using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Items;
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
        EntityNode.SetCollisionEnabled(body, open, _hiddenLayers);
    }

    public override string Prompt
    {
        get
        {
            RegionResource? destination = RegionDatabase.Get(TargetRegionId);
            string where = destination?.DisplayName ?? "elsewhere";

            if (destination == null || destination.TollGold <= 0)
            {
                return Loc.TF("region.travel_prompt", where);
            }

            // The price is quoted from the same TollFee.Resolve that GameBootstrap charges, so the
            // number at the gate and the number taken are one decision — 38C's finding, where the
            // travel fee's first draft resolved the region two different ways and would have shown a
            // price it did not charge.
            StoryFlagsComponent? flags = Player()?.GetComponent<StoryFlagsComponent>();
            int gold = Player()?.GetComponent<InventoryComponent>()?.CountOf(GameIds.Currency.Gold) ?? 0;

            return Economy.TollFee.Resolve(
                hasPermit: flags?.Has(destination.TollPermitFlagId) ?? false,
                hasPass: flags?.Has(destination.TollPassFlagId) ?? false,
                fee: destination.TollGold,
                goldHeld: gold) switch
            {
                Economy.TollOutcome.PermitHeld => Loc.TF("region.travel_prompt_permit", where),
                Economy.TollOutcome.PassSpent => Loc.TF("region.travel_prompt_pass", where),
                Economy.TollOutcome.CannotAfford =>
                    Loc.TF("region.travel_prompt_short", where, destination.TollGold, gold),
                _ => Loc.TF("region.travel_prompt_toll", where, destination.TollGold),
            };
        }
    }

    private static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    public override bool Interact(IEntity instigator)
    {
        if (!_revealed)
        {
            return false;
        }

        if (RegionDatabase.Get(TargetRegionId) == null)
        {
            Log.Warn($"RegionTransitionComponent: unknown region id '{TargetRegionId}'.");
            return false;
        }

        EventBus.Instance?.Publish(new RegionTransitionRequestedEvent(TargetRegionId));
        return true;
    }
}
