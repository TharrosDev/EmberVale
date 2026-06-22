using System.Collections.Generic;
using System.Text;
using Embervale.Core.Diagnostics;
using Embervale.Crafting;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Loot;
using Embervale.Magic;
using Embervale.Npc;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Boot-time (and on-demand) validation of authored content cross-references. The content
/// databases each guard their own ids, but nothing checked that a loot table referenced a
/// <em>real</em> item, a quest a <em>real</em> enemy, a spell a <em>real</em> status effect,
/// or a dialogue a <em>real</em> quest — those failed silently at runtime. As the content set
/// grows (Phase 21), a single typo could quietly disable a drop, a reward or a whole quest.
///
/// This pass resolves every cross-reference against the databases / the
/// <see cref="EnemyTemplateRegistry"/> and reports the breakages in one place, feeding the
/// shared <see cref="Invariant"/> counter. Run once from the bootstrap after the databases
/// load, and on demand via the <c>validate</c> dev-console command.
///
/// Beyond "references resolve", it also checks that content is <em>well-formed</em>: ids are
/// unique within their domain (the databases dedupe duplicates to a single last-write-wins
/// entry, so a duplicate id silently drops content — only a direct directory scan catches it)
/// and loot tables are non-empty. Graph reachability is a later pass (Phase 22E).
/// </summary>
public static class ContentValidator
{
    private const string LootDirectory = "res://data/loot";

    /// <summary>Runs every cross-reference and structural check; returns a human-readable summary.</summary>
    public static string Run()
    {
        var issues = new List<string>();

        ValidateDuplicateIds(issues);
        ValidateLootTables(issues);
        ValidateRecipes(issues);
        ValidateQuests(issues);
        ValidateDialogue(issues);
        ValidateSpells(issues);
        ValidateFactions(issues);
        ValidateEncounters(issues);
        ValidateWorldEvents(issues);

        foreach (string issue in issues)
        {
            Invariant.Check(false, $"content: {issue}");
        }

        if (issues.Count == 0)
        {
            return "ContentValidator: OK (all content references resolve and content is well-formed).";
        }

        var sb = new StringBuilder($"ContentValidator: {issues.Count} issue(s):\n");
        foreach (string issue in issues)
        {
            sb.Append("  • ").Append(issue).Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Scans every id-bearing content directory and flags duplicate ids within a domain. The
    /// databases dedupe on load (last-write-wins), so a duplicate would silently disable one of
    /// the colliding resources — invisible to a <c>.All</c> walk. We scan the files directly,
    /// mirroring <see cref="ValidateLootTables"/>. Per the ID registry (docs/IDS.md), ids are
    /// unique <em>within</em> a domain, so each directory is checked independently.
    /// </summary>
    private static void ValidateDuplicateIds(List<string> issues)
    {
        CheckDuplicateIds<ItemResource>("res://data/items", "item", r => r.Id, issues);
        CheckDuplicateIds<AffixDefinition>("res://data/affixes", "affix", r => r.Id, issues);
        CheckDuplicateIds<PerkResource>("res://data/perks", "perk", r => r.Id, issues);
        CheckDuplicateIds<QuestResource>("res://data/quests", "quest", r => r.Id, issues);
        CheckDuplicateIds<DialogueResource>("res://data/dialogue", "dialogue", r => r.Id, issues);
        CheckDuplicateIds<ScheduleResource>("res://data/schedules", "schedule", r => r.Id, issues);
        CheckDuplicateIds<SpellResource>("res://data/spells", "spell", r => r.Id, issues);
        CheckDuplicateIds<StatusEffectResource>("res://data/status_effects", "status", r => r.Id, issues);
        CheckDuplicateIds<WeatherResource>("res://data/weather", "weather", r => r.Id, issues);
        CheckDuplicateIds<EncounterResource>("res://data/encounters", "encounter", r => r.Id, issues);
        CheckDuplicateIds<CraftingRecipeResource>("res://data/recipes", "recipe", r => r.Id, issues);
        CheckDuplicateIds<FactionResource>("res://data/factions", "faction", r => r.Id, issues);
        CheckDuplicateIds<WorldEventResource>("res://data/world_events", "event", r => r.Id, issues);
    }

    /// <summary>Loads every <c>.tres</c> in <paramref name="directory"/> and reports empty or
    /// duplicate ids for the <paramref name="domain"/>.</summary>
    private static void CheckDuplicateIds<T>(
        string directory, string domain, System.Func<T, string> idOf, List<string> issues)
        where T : Resource
    {
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            return;
        }

        var seen = new Dictionary<string, string>();
        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var resource = GD.Load<T>($"{directory}/{name}");
            if (resource == null)
            {
                continue;
            }

            string id = idOf(resource);
            if (string.IsNullOrEmpty(id))
            {
                issues.Add($"{domain} '{name}' has an empty id");
            }
            else if (seen.TryGetValue(id, out string? firstFile))
            {
                issues.Add($"{domain} id '{id}' is duplicated (in {firstFile} and {name})");
            }
            else
            {
                seen[id] = name;
            }
        }
    }

    private static void RequireItem(string id, string context, List<string> issues)
    {
        if (string.IsNullOrEmpty(id))
        {
            issues.Add($"{context} has an empty item id");
        }
        else if (ItemDatabase.Get(id) == null)
        {
            issues.Add($"{context} references unknown item '{id}'");
        }
    }

    private static void RequireEnemy(string id, string context, List<string> issues)
    {
        if (string.IsNullOrEmpty(id))
        {
            issues.Add($"{context} has an empty enemy template id");
        }
        else if (!EnemyTemplateRegistry.IsRegistered(id))
        {
            issues.Add($"{context} references unregistered enemy template '{id}'");
        }
    }

    private static void ValidateLootTables(List<string> issues)
    {
        if (!DirAccess.DirExistsAbsolute(LootDirectory))
        {
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(LootDirectory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var table = GD.Load<LootTable>($"{LootDirectory}/{name}");
            if (table == null)
            {
                issues.Add($"loot table '{name}' failed to load");
                continue;
            }

            if (table.Entries.Count == 0 && table.GoldChance <= 0f)
            {
                issues.Add($"loot table '{name}' is empty (no entries and no gold)");
            }

            foreach (Variant element in table.Entries)
            {
                if (element.As<LootEntry>() is { } entry && !string.IsNullOrEmpty(entry.ItemId))
                {
                    RequireItem(entry.ItemId, $"loot '{name}'", issues);
                }
            }

            if (table.GoldChance > 0f)
            {
                RequireItem(table.GoldItemId, $"loot '{name}' gold", issues);
            }
        }
    }

    private static void ValidateRecipes(List<string> issues)
    {
        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            foreach (RecipeIngredient ingredient in recipe.IngredientList())
            {
                RequireItem(ingredient.ItemId, $"recipe '{recipe.Id}' ingredient", issues);
            }

            RequireItem(recipe.OutputItemId, $"recipe '{recipe.Id}' output", issues);
        }
    }

    private static void ValidateQuests(List<string> issues)
    {
        foreach (QuestResource quest in QuestDatabase.All)
        {
            foreach (ObjectiveResource objective in quest.ObjectiveList())
            {
                switch (objective.Type)
                {
                    case ObjectiveType.Kill:
                        RequireEnemy(objective.TargetId, $"quest '{quest.Id}' kill objective", issues);
                        break;
                    case ObjectiveType.Collect:
                        RequireItem(objective.TargetId, $"quest '{quest.Id}' collect objective", issues);
                        break;
                }
            }

            foreach (Variant element in quest.RewardItems)
            {
                if (element.As<QuestItemReward>() is { } reward)
                {
                    RequireItem(reward.ItemId, $"quest '{quest.Id}' reward", issues);
                }
            }

            if (quest.GoldReward > 0)
            {
                RequireItem(quest.GoldItemId, $"quest '{quest.Id}' gold reward", issues);
            }

            if (!string.IsNullOrEmpty(quest.PrerequisiteQuestId) &&
                QuestDatabase.Get(quest.PrerequisiteQuestId) == null)
            {
                issues.Add($"quest '{quest.Id}' requires unknown quest '{quest.PrerequisiteQuestId}'");
            }
        }
    }

    private static void ValidateDialogue(List<string> issues)
    {
        foreach (DialogueResource dialogue in DialogueDatabase.All)
        {
            if (dialogue.StartNode() == null)
            {
                issues.Add($"dialogue '{dialogue.Id}' has no start node '{dialogue.StartNodeId}'");
            }

            foreach (DialogueNode node in dialogue.NodeList())
            {
                foreach (DialogueChoice choice in node.ChoiceList())
                {
                    // A non-empty Goto must resolve to a real node.
                    if (!string.IsNullOrEmpty(choice.Goto) && dialogue.FindNode(choice.Goto) == null)
                    {
                        issues.Add($"dialogue '{dialogue.Id}' choice points at unknown node '{choice.Goto}'");
                    }

                    // Quest-typed conditions/effects must reference a real quest.
                    if (IsQuestCondition(choice.Condition) && !string.IsNullOrEmpty(choice.ConditionArg) &&
                        QuestDatabase.Get(choice.ConditionArg) == null)
                    {
                        issues.Add($"dialogue '{dialogue.Id}' condition references unknown quest '{choice.ConditionArg}'");
                    }

                    if (choice.Effect == DialogueEffect.StartQuest && QuestDatabase.Get(choice.EffectArg) == null)
                    {
                        issues.Add($"dialogue '{dialogue.Id}' StartQuest effect references unknown quest '{choice.EffectArg}'");
                    }
                }
            }
        }
    }

    private static bool IsQuestCondition(DialogueCondition condition) => condition switch
    {
        DialogueCondition.QuestAvailable => true,
        DialogueCondition.QuestActive => true,
        DialogueCondition.QuestCompleted => true,
        DialogueCondition.QuestNotStarted => true,
        _ => false,
    };

    private static void ValidateSpells(List<string> issues)
    {
        foreach (SpellResource spell in SpellDatabase.All)
        {
            if (!string.IsNullOrEmpty(spell.StatusEffectId) &&
                StatusEffectDatabase.Get(spell.StatusEffectId) == null)
            {
                issues.Add($"spell '{spell.Id}' references unknown status effect '{spell.StatusEffectId}'");
            }
        }
    }

    private static void ValidateFactions(List<string> issues)
    {
        foreach (FactionResource faction in FactionDatabase.All)
        {
            foreach (string enemy in faction.Enemies)
            {
                if (FactionDatabase.Get(enemy) == null)
                {
                    issues.Add($"faction '{faction.Id}' lists unknown enemy faction '{enemy}'");
                }
            }

            foreach (string ally in faction.Allies)
            {
                if (FactionDatabase.Get(ally) == null)
                {
                    issues.Add($"faction '{faction.Id}' lists unknown ally faction '{ally}'");
                }
            }
        }
    }

    private static void ValidateEncounters(List<string> issues)
    {
        foreach (EncounterResource encounter in EncounterDatabase.All)
        {
            RequireEnemy(encounter.EnemyTemplateId, $"encounter '{encounter.Id}'", issues);
        }
    }

    private static void ValidateWorldEvents(List<string> issues)
    {
        foreach (WorldEventResource worldEvent in WorldEventDatabase.All)
        {
            switch (worldEvent.Kind)
            {
                case WorldEventKind.Cache:
                    RequireItem(worldEvent.CacheItemId, $"event '{worldEvent.Id}' cache", issues);
                    break;
                default:
                    RequireEnemy(worldEvent.EnemyTemplateId, $"event '{worldEvent.Id}'", issues);
                    break;
            }

            if (!string.IsNullOrEmpty(worldEvent.RewardItemId))
            {
                RequireItem(worldEvent.RewardItemId, $"event '{worldEvent.Id}' reward", issues);
            }

            if (!string.IsNullOrEmpty(worldEvent.FactionRewardId) &&
                FactionDatabase.Get(worldEvent.FactionRewardId) == null)
            {
                issues.Add($"event '{worldEvent.Id}' rewards unknown faction '{worldEvent.FactionRewardId}'");
            }
        }
    }
}
