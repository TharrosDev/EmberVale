using System.Collections.Generic;
using Embervale.Items;
using Embervale.Localization;

namespace Embervale.Debugging;

/// <summary>
/// The <c>--validate</c> arm for items, loot, crafting and their content (ics). One public entry,
/// <see cref="Collect"/>, called once from <see cref="ContentValidator"/>; the rules themselves live
/// in one partial file per lane so four lanes can add rules without touching the same file:
/// <c>ItemValidator.Items.cs</c>, <c>ItemValidator.Loot.cs</c>, <c>ItemValidator.Crafting.cs</c> and
/// <c>ItemValidator.Content.cs</c>. An issue is one plain sentence added to the list, naming the
/// offending id, exactly as <see cref="BackgroundValidator"/> reports them.
///
/// This file owns only the entry point and the reference checks on the two databases the
/// foundation introduced (sets and unique effects), so those are never unvalidated.
/// </summary>
public static partial class ItemValidator
{
    /// <summary>Appends every item-system content issue to <paramref name="issues"/>.</summary>
    public static void Collect(List<string> issues)
    {
        CollectSetAndUniqueReferences(issues);
        CollectItems(issues);
        CollectLoot(issues);
        CollectCrafting(issues);
        CollectContent(issues);
    }

    private static void CollectSetAndUniqueReferences(List<string> issues)
    {
        foreach (UniqueEffectResource effect in UniqueEffectDatabase.All)
        {
            string what = $"unique effect '{effect.Id}'";
            if (!effect.Id.StartsWith("unique."))
            {
                issues.Add($"{what} must start with 'unique.'");
            }

            RequireKey(effect.NameKey, "NameKey", what, issues);
            RequireKey(effect.DescriptionKey, "DescriptionKey", what, issues);
        }

        foreach (ItemSetResource set in ItemSetDatabase.All)
        {
            string what = $"item set '{set.Id}'";
            if (!set.Id.StartsWith("set."))
            {
                issues.Add($"{what} must start with 'set.'");
            }

            RequireKey(set.DisplayNameKey, "DisplayNameKey", what, issues);

            foreach (string pieceId in set.PieceIds)
            {
                if (ItemDatabase.Get(pieceId) is not { } piece)
                {
                    issues.Add($"{what} lists piece '{pieceId}', which is not an item");
                }
                else if (piece.SetId != set.Id)
                {
                    issues.Add($"{what} lists piece '{pieceId}', whose SetId is '{piece.SetId}'");
                }
            }

            foreach (ItemSetBonusResource bonus in set.Bonuses)
            {
                if (bonus == null)
                {
                    issues.Add($"{what} has an empty bonus row");
                    continue;
                }

                if (bonus.PiecesRequired < 2 || bonus.PiecesRequired > set.PieceIds.Count)
                {
                    issues.Add($"{what} has a bonus at {bonus.PiecesRequired} pieces, outside 2..{set.PieceIds.Count}");
                }

                if (!bonus.HasStat && !bonus.HasEffect)
                {
                    issues.Add($"{what} has a bonus at {bonus.PiecesRequired} pieces that grants nothing");
                }

                if (bonus.HasEffect && UniqueEffectDatabase.Get(bonus.EffectId) == null)
                {
                    issues.Add($"{what} bonus names unknown unique effect '{bonus.EffectId}'");
                }
            }
        }

        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            if (item.SetId.Length > 0)
            {
                if (ItemSetDatabase.Get(item.SetId) is not { } set)
                {
                    issues.Add($"item '{item.Id}' names unknown set '{item.SetId}'");
                }
                else if (!set.HasPiece(item.Id))
                {
                    issues.Add($"item '{item.Id}' names set '{item.SetId}', which does not list it as a piece");
                }
            }

            if (item.UniqueEffectId.Length > 0 && UniqueEffectDatabase.Get(item.UniqueEffectId) == null)
            {
                issues.Add($"item '{item.Id}' names unknown unique effect '{item.UniqueEffectId}'");
            }

            if (item.Tier < 0 || item.Tier > 6)
            {
                issues.Add($"item '{item.Id}' has tier {item.Tier}, outside 0..6");
            }

            if (item.ItemLevel < 0 || item.RequiredLevel < 0)
            {
                issues.Add($"item '{item.Id}' has a negative item level or required level");
            }
        }
    }

    /// <summary>Reports an empty locale key or one that is not in the catalogue. Shared by the lane
    /// files: <paramref name="owner"/> is the full subject, e.g. <c>"item 'item.x'"</c>.</summary>
    private static void RequireKey(string key, string what, string owner, List<string> issues)
    {
        if (key.Length == 0)
        {
            issues.Add($"{owner} {what} is empty");
        }
        else if (!Loc.Has(key))
        {
            issues.Add($"{owner} {what} '{key}' is not a key in data/locale/strings.csv");
        }
    }
}
