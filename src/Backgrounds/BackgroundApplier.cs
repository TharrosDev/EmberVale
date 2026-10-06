using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Progression;
using Embervale.Races;

namespace Embervale.Backgrounds;

/// <summary>
/// Hands a freshly-created player their <see cref="BackgroundResource"/>'s one-time grants: the free
/// perk rank, the starting kit and gold, the story flags and the standing tweaks. New Game only,
/// called from <see cref="RaceComponent"/>; a load restores all of it from the Perks / Inventory /
/// StoryFlags / Reputation saves instead (replace semantics), and the stat deltas are re-derived from
/// the profile like a race's, so nothing here is saved twice.
/// </summary>
public static class BackgroundApplier
{
    public static void GrantStarting(IEntity owner, BackgroundResource background)
    {
        if (owner.GetComponent<PerksComponent>() is { } perks && PerkDatabase.Get(background.StartingPerkId) is { } perk)
        {
            perks.GrantFree(perk);
        }

        if (owner.GetComponent<InventoryComponent>() is { } pack)
        {
            foreach (string entry in background.StartingItems)
            {
                if (BackgroundRules.TryParseItem(entry, out string itemId, out int count) &&
                    ItemDatabase.Get(itemId) is { } item)
                {
                    pack.AddItem(item, count);
                }
            }

            if (background.StartingGold > 0 && ItemDatabase.Get(GameIds.Currency.Gold) is { } gold)
            {
                pack.AddItem(gold, background.StartingGold);
            }
        }

        if (owner.GetComponent<StoryFlagsComponent>() is { } flags)
        {
            foreach (string flag in background.FlavorFlags)
            {
                flags.Set(flag);
            }
        }

        if (owner.GetComponent<ReputationComponent>() is { } reputation)
        {
            foreach (RaceReputationTweak tweak in background.ReputationTweakList())
            {
                reputation.Add(tweak.FactionId, tweak.Amount);
            }
        }

        Log.Info($"Background '{background.Id}' granted.");
    }
}
