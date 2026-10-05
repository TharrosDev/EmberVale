using System.Collections.Generic;
using Embervale.Backgrounds;
using Embervale.Core;
using Embervale.Corruption;
using Embervale.Dialogue;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Progression;
using Embervale.Races;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// <c>--validate</c> arm for <see cref="BackgroundResource"/>s (P7): ids, Loc keys, the kit-value and
/// delta caps from <see cref="BackgroundRules"/>, every perk/item/faction reference, and that each
/// flag a background sets is read by a hub dialogue (a flag nothing reads is a hook that does nothing).
/// The wayfarer must stay the no-op default. A separate class like <see cref="EnvironmentValidation"/>
/// so the shared <see cref="ContentValidator"/> takes one call line.
/// </summary>
public static class BackgroundValidator
{
    private const string Directory = "res://data/backgrounds";

    /// <summary>The flags backgrounds write, for the story-flag reader/writer cross-check.</summary>
    public static IEnumerable<string> WrittenFlags()
    {
        foreach (BackgroundResource background in BackgroundDatabase.All)
        {
            foreach (string flag in background.FlavorFlags)
            {
                yield return flag;
            }
        }
    }

    public static void Validate(List<string> issues)
    {
        CheckDuplicateIds(issues);

        HashSet<string> readFlags = FlagsReadByDialogue();
        HashSet<string>? locKeys = LocaleKeys();

        if (BackgroundDatabase.Get(BackgroundDatabase.DefaultId) is not { } wayfarer)
        {
            issues.Add($"the default background '{BackgroundDatabase.DefaultId}' is missing");
        }
        else if (wayfarer.StartingPerkId.Length > 0 || wayfarer.StartingItems.Count > 0 || wayfarer.StartingGold != 0 ||
                 wayfarer.FlavorFlags.Count > 0 || wayfarer.StatDeltas.Count > 0 || wayfarer.ReputationTweaks.Count > 0)
        {
            issues.Add($"'{wayfarer.Id}' is the no-op default and must grant nothing");
        }

        foreach (BackgroundResource background in BackgroundDatabase.All)
        {
            string id = background.Id;
            if (!BackgroundRules.IsBackgroundId(id))
            {
                issues.Add($"background '{id}' must start with '{BackgroundRules.IdPrefix}'");
            }

            RequireKey(background.NameKey, "NameKey", id, locKeys, issues);
            RequireKey(background.DescKey, "DescKey", id, locKeys, issues);

            if (!BackgroundRules.IsLeanBranch(background.LeanBranch))
            {
                issues.Add($"background '{id}' lean branch '{background.LeanBranch}' is not one of " +
                           string.Join(", ", BackgroundRules.LeanBranches));
            }
            else if (background.LeanBranch.Length > 0)
            {
                RequireKey($"background.lean.{background.LeanBranch}", "lean badge", id, locKeys, issues);
            }

            if (background.StartingPerkId.Length > 0)
            {
                if (PerkDatabase.Get(background.StartingPerkId) is not { } perk)
                {
                    issues.Add($"background '{id}' starting perk '{background.StartingPerkId}' does not exist");
                }
                else if (perk.MinCorruptionTier != CorruptionTier.Untainted)
                {
                    issues.Add($"background '{id}' starting perk '{perk.Id}' is corruption-gated; a background grants a plain perk");
                }
            }

            foreach (RaceStatDelta delta in background.StatDeltaList())
            {
                if (!BackgroundRules.StatDeltaWithinCap(delta.Amount))
                {
                    issues.Add($"background '{id}' stat delta {delta.Stat} {delta.Amount} exceeds +/-{BackgroundRules.MaxStatDelta}");
                }
            }

            foreach (RaceReputationTweak tweak in background.ReputationTweakList())
            {
                if (FactionDatabase.Get(tweak.FactionId) == null)
                {
                    issues.Add($"background '{id}' reputation tweak references unknown faction '{tweak.FactionId}'");
                }

                if (!BackgroundRules.ReputationWithinCap(tweak.Amount))
                {
                    issues.Add($"background '{id}' reputation tweak {tweak.Amount} exceeds +/-{BackgroundRules.MaxReputationTweak}");
                }
            }

            ValidateKit(background, issues);

            foreach (string flag in background.FlavorFlags)
            {
                if (!flag.StartsWith(BackgroundRules.FlagPrefix, System.StringComparison.Ordinal))
                {
                    issues.Add($"background '{id}' flag '{flag}' must start with '{BackgroundRules.FlagPrefix}'");
                }
                else if (!readFlags.Contains(flag))
                {
                    issues.Add($"background '{id}' sets flag '{flag}', which no dialogue reads (add a HasFlag line on a hub)");
                }
            }
        }
    }

    private static void ValidateKit(BackgroundResource background, List<string> issues)
    {
        string id = background.Id;
        if (background.StartingGold < 0)
        {
            issues.Add($"background '{id}' has negative starting gold");
        }

        var kit = new List<(int UnitValue, int Count)>();
        foreach (string entry in background.StartingItems)
        {
            if (!BackgroundRules.TryParseItem(entry, out string itemId, out int count))
            {
                issues.Add($"background '{id}' kit entry '{entry}' is not '<item id>[:<count >= 1>]'");
            }
            else if (ItemDatabase.Get(itemId) is not { } item)
            {
                issues.Add($"background '{id}' kit references unknown item '{itemId}'");
            }
            else
            {
                kit.Add((item.Value, count));
            }
        }

        int value = BackgroundRules.KitValue(background.StartingGold, kit);
        if (!BackgroundRules.KitWithinCap(value))
        {
            issues.Add($"background '{id}' kit is worth {value}, over the {BackgroundRules.MaxKitValue} cap");
        }
    }

    private static void RequireKey(string key, string what, string id, HashSet<string>? locKeys, List<string> issues)
    {
        if (key.Length == 0)
        {
            issues.Add($"background '{id}' {what} is empty");
        }
        else if (locKeys != null && !locKeys.Contains(key))
        {
            issues.Add($"background '{id}' {what} '{key}' is not a key in data/locale/strings.csv");
        }
    }

    private static HashSet<string> FlagsReadByDialogue()
    {
        var read = new HashSet<string>();
        foreach (DialogueResource dialogue in DialogueDatabase.All)
        {
            foreach (DialogueStartVariant variant in dialogue.StartVariantList())
            {
                AddIfHasFlag(read, variant.Condition, variant.ConditionArg);
            }

            foreach (DialogueNode node in dialogue.NodeList())
            {
                foreach (DialogueChoice choice in node.ChoiceList())
                {
                    AddIfHasFlag(read, choice.Condition, choice.ConditionArg);
                    AddIfHasFlag(read, choice.Condition2, choice.Condition2Arg);
                }
            }
        }

        return read;

        static void AddIfHasFlag(HashSet<string> set, DialogueCondition condition, string arg)
        {
            if (condition == DialogueCondition.HasFlag && arg.Length > 0)
            {
                set.Add(arg);
            }
        }
    }

    private static HashSet<string>? LocaleKeys()
    {
        const string path = "res://data/locale/strings.csv";
        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return null;
        }

        return LocCatalog.Parse(file.GetAsText()).TryGetValue(Loc.DefaultLocale, out Dictionary<string, string>? messages)
            ? new HashSet<string>(messages.Keys)
            : null;
    }

    /// <summary>The database keeps the last duplicate and hides the other, so scan the files themselves.</summary>
    private static void CheckDuplicateIds(List<string> issues)
    {
        if (!DirAccess.DirExistsAbsolute(Directory))
        {
            issues.Add($"background directory '{Directory}' is missing");
            return;
        }

        var seen = new Dictionary<string, string>();
        foreach (string file in DirAccess.GetFilesAt(Directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres") || ResidentResources.Load<BackgroundResource>($"{Directory}/{name}") is not { } resource)
            {
                continue;
            }

            if (seen.TryGetValue(resource.Id, out string? first))
            {
                issues.Add($"background id '{resource.Id}' is duplicated (in {first} and {name})");
            }
            else
            {
                seen[resource.Id] = name;
            }
        }
    }
}
