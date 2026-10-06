using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Crafting;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Player;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The loot feed: what the player just picked up, that the pack refused something, and that a recipe
/// was learned. Pure presentation over <see cref="ItemPickedUpEvent"/>, <see cref="PackFullEvent"/>
/// and <see cref="RecipeLearnedEvent"/>. None of the three is raised by a load, so a reload narrates
/// nothing.
///
/// Pickups are not toasted one event at a time. <see cref="LootFeedMerger"/> holds each item's line
/// for a moment and adds to it, so sweeping a pile reads "Iron Ore ×7" once instead of seven times.
/// The feed has no per-frame hook of its own in this file; a short timer flushes the merger, and
/// re-arms itself while anything is still being held.
/// </summary>
public partial class Notifications
{
    private readonly LootFeedMerger _loot = new();
    private bool _lootTimerArmed;

    private void SubscribeLoot(EventBus? bus)
    {
        bus?.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
        bus?.Subscribe<PackFullEvent>(OnPackFull);
        bus?.Subscribe<RecipeLearnedEvent>(OnRecipeLearned);
    }

    private void UnsubscribeLoot(EventBus bus)
    {
        bus.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
        bus.Unsubscribe<PackFullEvent>(OnPackFull);
        bus.Unsubscribe<RecipeLearnedEvent>(OnRecipeLearned);
    }

    private void OnItemPickedUp(ItemPickedUpEvent e)
    {
        if (!IsPlayer(e.Owner) || e.Item == null)
        {
            return;
        }

        _loot.Add(e.Item.Id, e.Item.DisplayName, (int)e.Item.Rarity, e.Quantity, Now());
        ArmLootTimer();
    }

    private void ArmLootTimer()
    {
        if (_lootTimerArmed || !IsInsideTree())
        {
            return;
        }

        // processAlways: the feed runs through a pause like the rest of this node, and a line
        // gathered just before a menu opened must not sit in the merger until the menu closes -
        // Push already holds toasts back while one is up.
        _lootTimerArmed = true;
        GetTree().CreateTimer(LootFeedMerger.Window, processAlways: true).Timeout += FlushLoot;
    }

    private void FlushLoot()
    {
        _lootTimerArmed = false;
        if (!IsInsideTree() || IsQueuedForDeletion())
        {
            return;
        }

        foreach (LootFeedLine line in _loot.Flush(Now()))
        {
            // The rarity is the toast's colour; the count is in the words, so the line still says
            // everything with the colours remapped.
            Push(
                line.Quantity > 1 ? Loc.TF("loot.picked_up_many", line.Name, line.Quantity) : Loc.TF("loot.picked_up", line.Name),
                UiTheme.RarityColor((ItemRarity)line.Rarity),
                line.Rarity >= (int)ItemRarity.Rare ? NoticeCategory.Reward : NoticeCategory.Minor);
        }

        if (_loot.HasPending)
        {
            ArmLootTimer();
        }
    }

    /// <summary>The pack turned something away. A warning, and coalesced by <see cref="Push"/>:
    /// pressing the key five times at one sword says it once with a count.</summary>
    private void OnPackFull(PackFullEvent e)
    {
        if (IsPlayer(e.Owner) && e.Item != null)
        {
            Push(Loc.TF("loot.pack_full", e.Item.DisplayName), UiTheme.Bad, NoticeCategory.Warning);
        }
    }

    private void OnRecipeLearned(RecipeLearnedEvent e)
    {
        if (IsPlayer(e.Crafter) && RecipeDatabase.Get(e.RecipeId) is { } recipe)
        {
            Push(Loc.TF("loot.recipe_learned", recipe.DisplayName), UiTheme.Accent, NoticeCategory.Reward);
        }
    }

    /// <summary>Companions and merchants carry packs too; the feed is the player's.</summary>
    private static bool IsPlayer(IEntity? owner) =>
        owner != null && ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) &&
        ReferenceEquals(player, owner);

    private static double Now() => Time.GetTicksMsec() / 1000d;
}
