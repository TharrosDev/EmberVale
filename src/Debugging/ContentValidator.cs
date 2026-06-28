using System.Collections.Generic;
using System.Text;
using Embervale.Core.Diagnostics;
using Embervale.Crafting;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
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
/// and loot tables are non-empty. <see cref="RunAll"/> adds a graph-reachability battery —
/// dialogue orphans/dead-ends (via <see cref="DialogueGraphAnalysis"/>), quest completability,
/// and prerequisite cycles — surfaced through the <c>validate-all</c> console command.
/// </summary>
public static class ContentValidator
{
    private const string LootDirectory = "res://data/loot";

    /// <summary>
    /// Runs the cross-reference + structural checks (the boot/quick pass) and returns a
    /// human-readable summary. The bootstrap calls this; the <c>validate</c> console command
    /// mirrors it. For the heavier graph-reachability battery too, use <see cref="RunAll"/>.
    /// </summary>
    public static string Run()
    {
        var issues = new List<string>();
        CollectCoreIssues(issues);
        return Report(issues, "all content references resolve and content is well-formed");
    }

    /// <summary>
    /// Runs the full battery — the <see cref="Run"/> checks <em>plus</em> graph reachability
    /// (dialogue orphans/dead-ends, quest completability, prerequisite cycles). Surfaced via the
    /// <c>validate-all</c> console command and the headless validation path (Phase 22F).
    /// </summary>
    public static string RunAll()
    {
        RunAll(out string report);
        return report;
    }

    /// <summary>
    /// Full battery, exposing a clean pass/fail in addition to the summary — for the headless
    /// validation path, which exits non-zero when content is broken. Returns <c>true</c> when no
    /// issues were found.
    /// </summary>
    public static bool RunAll(out string report)
    {
        var issues = new List<string>();
        CollectCoreIssues(issues);
        CollectGraphIssues(issues);
        report = Report(issues, "all references resolve, content is well-formed, and graphs are reachable");
        return issues.Count == 0;
    }

    private static void CollectCoreIssues(List<string> issues)
    {
        ValidateDuplicateIds(issues);
        ValidateLootTables(issues);
        ValidateRecipes(issues);
        ValidateQuests(issues);
        ValidateDialogue(issues);
        ValidateSpells(issues);
        ValidateFactions(issues);
        ValidateEncounters(issues);
        ValidateWorldEvents(issues);
        ValidateRegions(issues);
        ValidateLocale(issues);
    }

    private static void CollectGraphIssues(List<string> issues)
    {
        ValidateDialogueReachability(issues);
        ValidateQuestCompletability(issues);
        ValidatePrerequisiteCycles(issues);
    }

    private static string Report(List<string> issues, string okSummary)
    {
        foreach (string issue in issues)
        {
            Invariant.Check(false, $"content: {issue}");
        }

        if (issues.Count == 0)
        {
            return $"ContentValidator: OK ({okSummary}).";
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
        CheckDuplicateIds<RegionResource>("res://data/regions", "region", r => r.Id, issues);
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

                    // Corruption-typed conditions/effects take an integer threshold/amount.
                    if (IsCorruptionCondition(choice.Condition) && !int.TryParse(choice.ConditionArg, out _))
                    {
                        issues.Add($"dialogue '{dialogue.Id}' corruption condition has non-numeric threshold '{choice.ConditionArg}'");
                    }

                    if (choice.Effect == DialogueEffect.AddCorruption && !int.TryParse(choice.EffectArg, out _))
                    {
                        issues.Add($"dialogue '{dialogue.Id}' AddCorruption effect has non-numeric amount '{choice.EffectArg}'");
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

    private static bool IsCorruptionCondition(DialogueCondition condition) => condition switch
    {
        DialogueCondition.CorruptionAtLeast => true,
        DialogueCondition.CorruptionBelow => true,
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

    private static void ValidateRegions(List<string> issues)
    {
        foreach (RegionResource region in RegionDatabase.All)
        {
            if (!string.IsNullOrEmpty(region.DefaultWeatherId) && WeatherDatabase.Get(region.DefaultWeatherId) == null)
            {
                issues.Add($"region '{region.Id}' has unknown default weather '{region.DefaultWeatherId}'");
            }

            foreach (string neighbour in region.Neighbours)
            {
                if (RegionDatabase.Get(neighbour) == null)
                {
                    issues.Add($"region '{region.Id}' links to unknown neighbour '{neighbour}'");
                }
            }

            // SpawnPoint is where every portal AND fast-travel node lands the player; outside the
            // region bounds drops them in the void (Phase 25.5F).
            if (!region.Bounds.HasPoint(region.SpawnPoint))
            {
                issues.Add($"region '{region.Id}' spawn point {region.SpawnPoint} is outside its bounds {region.Bounds}");
            }

            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell == null || string.IsNullOrEmpty(cell.ScenePath) || !ResourceLoader.Exists(cell.ScenePath))
                {
                    issues.Add($"region '{region.Id}' cell '{cell?.Id ?? "?"}' has a missing scene '{cell?.ScenePath}'");
                    continue;
                }

                if (!region.Bounds.HasPoint(cell.Center))
                {
                    issues.Add($"region '{region.Id}' cell '{cell.Id}' center {cell.Center} is outside region bounds");
                }
            }
        }
    }

    /// <summary>
    /// Audits the localization catalogue (Phase 25.5F): duplicate keys (the parser keeps the last,
    /// silently dropping a string) and keys with no default-locale value (the UI shows the raw key).
    /// ponytail: travel-node components live in cell <c>.tscn</c> scenes, not authored <c>.tres</c>,
    /// so their <c>RegionId</c> is validated at runtime on discovery — the scannable travel reference
    /// is the region <see cref="RegionResource.SpawnPoint"/>, gated above.
    /// </summary>
    private static void ValidateLocale(List<string> issues)
    {
        const string path = "res://data/locale/strings.csv";
        if (!FileAccess.FileExists(path))
        {
            return;
        }

        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            issues.Add($"locale catalogue '{path}' could not be read ({FileAccess.GetOpenError()})");
            return;
        }

        foreach (string issue in LocaleAudit.Audit(file.GetAsText(), Loc.DefaultLocale))
        {
            issues.Add($"locale: {issue}");
        }
    }

    // --- Graph reachability (RunAll only) -----------------------------------

    /// <summary>
    /// Projects each dialogue graph onto <see cref="DialogueGraphAnalysis"/> and reports orphan
    /// nodes (unreachable from the start) and dead-ends (reachable nodes that can never reach a
    /// conversation end). Dangling gotos / a missing start node are reported by
    /// <see cref="ValidateDialogue"/>, so a graph with no resolvable start is skipped here.
    /// </summary>
    private static void ValidateDialogueReachability(List<string> issues)
    {
        foreach (DialogueResource dialogue in DialogueDatabase.All)
        {
            DialogueNode? start = dialogue.StartNode();
            if (start == null)
            {
                continue;
            }

            var nodes = new List<DialogueGraphAnalysis.Node>();
            foreach (DialogueNode node in dialogue.NodeList())
            {
                var gotos = new List<string>();
                bool terminal = false;
                List<DialogueChoice> choices = node.ChoiceList();
                if (choices.Count == 0)
                {
                    terminal = true;
                }

                foreach (DialogueChoice choice in choices)
                {
                    if (string.IsNullOrEmpty(choice.Goto))
                    {
                        terminal = true;
                    }
                    else
                    {
                        gotos.Add(choice.Goto);
                    }
                }

                nodes.Add(new DialogueGraphAnalysis.Node(node.Id, gotos, terminal));
            }

            DialogueGraphAnalysis.Result result = DialogueGraphAnalysis.Analyze(start.Id, nodes);
            foreach (string id in result.Unreachable)
            {
                issues.Add($"dialogue '{dialogue.Id}' node '{id}' is unreachable from start '{start.Id}'");
            }

            foreach (string id in result.DeadEnds)
            {
                issues.Add($"dialogue '{dialogue.Id}' node '{id}' cannot reach a conversation end (dead-end loop)");
            }
        }
    }

    /// <summary>Flags quests that can never be completed: no objectives, or an objective whose
    /// <c>RequiredCount</c> is non-positive (it would never tick to done).</summary>
    private static void ValidateQuestCompletability(List<string> issues)
    {
        foreach (QuestResource quest in QuestDatabase.All)
        {
            List<ObjectiveResource> objectives = quest.ObjectiveList();
            if (objectives.Count == 0)
            {
                issues.Add($"quest '{quest.Id}' has no objectives (can never be completed)");
                continue;
            }

            foreach (ObjectiveResource objective in objectives)
            {
                if (objective.RequiredCount <= 0)
                {
                    issues.Add($"quest '{quest.Id}' objective '{objective.TargetId}' has a non-positive RequiredCount");
                }
            }
        }
    }

    /// <summary>Walks each quest's prerequisite chain and flags a cycle (a chain that revisits a
    /// quest), which would make every quest in the loop permanently unstartable. Unknown
    /// prerequisites are reported by <see cref="ValidateQuests"/>.</summary>
    private static void ValidatePrerequisiteCycles(List<string> issues)
    {
        foreach (QuestResource quest in QuestDatabase.All)
        {
            var visited = new HashSet<string>();
            string current = quest.Id;
            while (!string.IsNullOrEmpty(current) && visited.Add(current))
            {
                current = QuestDatabase.Get(current)?.PrerequisiteQuestId ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(current))
            {
                issues.Add($"quest '{quest.Id}' has a prerequisite cycle (revisits '{current}')");
            }
        }
    }
}
