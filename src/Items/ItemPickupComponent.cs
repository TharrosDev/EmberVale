using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Embervale.Loot;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Makes a world entity a collectable item. Interacting transfers the held item
/// into the instigator's <see cref="InventoryComponent"/> and despawns the pickup
/// once empty (or leaves the remainder if the inventory filled up, publishing an
/// <see cref="InventoryFullEvent"/> so the HUD can say why nothing happened).
/// </summary>
[GlobalClass]
public partial class ItemPickupComponent : InteractableComponent
{
    /// <summary>Least time between two <see cref="InventoryFullEvent"/>s from one pickup. The
    /// hold-to-collect sweep retries several times a second; one notice is enough.</summary>
    private const ulong FullNoticeIntervalMs = 2500;

    /// <summary>Template for an editor/mundane pickup; wrapped as a plain instance
    /// on first use. Ignored once <see cref="Instance"/> is set directly (loot).</summary>
    [Export] public ItemResource? Item { get; set; }
    [Export] public int Quantity { get; set; } = 1;

    /// <summary>The concrete instance carried by this pickup (rolled loot sets this
    /// directly; mundane pickups derive it from <see cref="Item"/>).</summary>
    public ItemInstance? Instance { get; set; }

    private ItemInstance? Resolved => Instance ??= Item != null ? ItemInstance.Plain(Item) : null;

    private ulong _lastFullNoticeMs;
    private bool _fullNoticeSent;

    /// <summary>The id of the item this pickup carries (for Collect-objective targeting), or empty.</summary>
    public string ItemId => Resolved?.TemplateId ?? string.Empty;

    /// <summary>
    /// True for the pickups collected just by walking over them: coin and plain crafting materials.
    /// Everything else waits for the player to choose it, because a pack slot is a decision and a
    /// coin is not.
    /// </summary>
    public bool IsAutoLoot => Resolved is { } instance && IsAutoLootItem(instance);

    /// <summary>The walk-over rule, split out so it reads the same wherever it is asked.</summary>
    public static bool IsAutoLootItem(ItemInstance instance)
    {
        if (instance.TemplateId == Embervale.Core.GameIds.Currency.Gold)
        {
            return true;
        }

        // Contraband is a material too, and carrying it is a choice with consequences at a toll:
        // it is never swept up for the player.
        return InventoryComponent.IsBagItem(instance)
            && !instance.Template.TradeTags.Contains(Embervale.Economy.TradeTags.Contraband);
    }

    public override string Prompt
    {
        get
        {
            ItemInstance? instance = Resolved;
            if (instance == null)
            {
                return Loc.T("interact.pickup");
            }

            return Quantity > 1
                ? Loc.TF("interact.pickup.many", instance.DisplayName, Quantity)
                : Loc.TF("interact.pickup.one", instance.DisplayName);
        }
    }

    public override bool Interact(IEntity instigator) => Collect(instigator, announceFull: true);

    /// <summary>
    /// Moves as much of the pickup as fits into <paramref name="instigator"/>'s inventory.
    /// </summary>
    /// <param name="instigator">Whoever is collecting.</param>
    /// <param name="announceFull">Publish an <see cref="InventoryFullEvent"/> when nothing fits. The
    /// walk-over sweep passes false: the player did not ask for that pickup, so a full pack is not
    /// news.</param>
    /// <returns>True when at least one unit was collected.</returns>
    public bool Collect(IEntity instigator, bool announceFull)
    {
        ItemInstance? instance = Resolved;
        if (instance == null)
        {
            return false;
        }

        InventoryComponent? inventory = instigator.GetComponent<InventoryComponent>();
        if (inventory == null)
        {
            return false;
        }

        int added = inventory.AddInstance(instance, Quantity);
        if (added <= 0)
        {
            if (announceFull)
            {
                AnnounceFull(instigator, instance);
            }

            return false;
        }

        EventBus.Instance?.Publish(new ItemPickedUpEvent(instigator, instance.Template, added));
        if (LootPresentation.IsAnnounced(instance.Rarity))
        {
            // A second, higher note over the ordinary pickup sound: rare loot is heard as well as seen.
            EventBus.Instance?.Publish(new SoundCueRequestedEvent(
                LootPresentation.ChimeCue, instigator.Body.GlobalPosition,
                LootPresentation.ChimeVolumeDb(instance.Rarity), LootPresentation.ChimePitch(instance.Rarity)));
        }

        Log.Info($"{instigator.DisplayName} picked up {instance.DisplayName} x{added}.");

        Quantity -= added;
        if (Quantity <= 0 && Entity != null)
        {
            ((Node)Entity.Body).QueueFree();
        }
        else if (announceFull)
        {
            // Part of the stack fit and the rest did not: that is a full pack too.
            AnnounceFull(instigator, instance);
        }

        return true;
    }

    private void AnnounceFull(IEntity instigator, ItemInstance instance)
    {
        ulong now = Time.GetTicksMsec();
        if (_fullNoticeSent && now - _lastFullNoticeMs < FullNoticeIntervalMs)
        {
            return;
        }

        _fullNoticeSent = true;
        _lastFullNoticeMs = now;
        EventBus.Instance?.Publish(new InventoryFullEvent(instigator, instance.Template, Quantity));
        Log.Info($"{instigator.DisplayName}'s inventory is full.");
    }
}
