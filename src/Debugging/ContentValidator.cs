using System.Collections.Generic;
using System.Text;
using Embervale.Companions;
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
using Embervale.Player;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Races;
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
        ValidateRaces(issues);
        ValidateLocale(issues);
        ValidateCompanions(issues);
        ValidateAIProfiles(issues);
        ValidateEnemyArchetypes(issues);
        ValidateBestiary(issues);
        ValidateResourcePaths(issues);
    }

    /// <summary>The bestiary (Phase 34G) is checked in <b>both directions</b>, which no other domain
    /// does. Forwards is the usual thing: an entry must name a real creature and its keys must
    /// resolve. Backwards is the check that earns its keep — every registered enemy template must
    /// <em>have</em> an entry. That is exactly the class of bug that let 34E ship two archetypes with
    /// no encounter and nobody notice until a full playthrough: content that exists but nothing can
    /// reach. Here it is a build-time failure instead.</summary>
    private static void ValidateBestiary(List<string> issues)
    {
        foreach (BestiaryEntryResource entry in BestiaryDatabase.All)
        {
            string id = entry.Id;
            RequireEnemy(id, $"bestiary entry '{id}'", issues);

            if (string.IsNullOrEmpty(entry.LoreKey) || !Loc.Has(entry.LoreKey))
            {
                issues.Add($"bestiary entry '{id}' lore key '{entry.LoreKey}' is missing from the locale catalogue");
            }

            // The name key is optional — it overrides the archetype's, and only the bespoke
            // creatures (goblin, Iron King, Ashen Acolyte) have no archetype to fall back on.
            if (!string.IsNullOrEmpty(entry.NameKey) && !Loc.Has(entry.NameKey))
            {
                issues.Add($"bestiary entry '{id}' name key '{entry.NameKey}' is missing from the locale catalogue");
            }
            else if (string.IsNullOrEmpty(entry.NameKey) && EnemyArchetypeDatabase.Get(id) == null)
            {
                issues.Add($"bestiary entry '{id}' has no name key and no archetype to take one from");
            }

            if (entry.KillsToKnow < 1)
            {
                issues.Add($"bestiary entry '{id}' needs at least one kill to reveal ({entry.KillsToKnow})");
            }
        }

        foreach (string templateId in EnemyTemplateRegistry.TemplateIds)
        {
            if (!BestiaryDatabase.IsRegistered(templateId))
            {
                issues.Add($"enemy template '{templateId}' has no bestiary entry — it would be uncatalogued in-game");
            }
        }
    }

    /// <summary>Humanoid archetypes became authored content in Phase 34B. They are entirely data, so
    /// every cross-reference here is one a typo would silently degrade: a missing attribute set makes
    /// a soldier fight with default stats, an unknown AI profile drops it to a plain brute, and a
    /// missing name key leaks a raw <c>enemy.x.name</c> onto the nameplate.</summary>
    private static void ValidateEnemyArchetypes(List<string> issues)
    {
        foreach (EnemyArchetypeResource archetype in EnemyArchetypeDatabase.All)
        {
            string id = archetype.Id;

            if (string.IsNullOrEmpty(archetype.NameKey) || !Loc.Has(archetype.NameKey))
            {
                issues.Add($"enemy archetype '{id}' name key '{archetype.NameKey}' is missing from the locale catalogue");
            }

            RequirePath(archetype.AttributesPath, $"enemy archetype '{id}' attributes", issues);
            RequirePath(archetype.WeaponPath, $"enemy archetype '{id}' weapon", issues);
            RequirePath(archetype.LootTablePath, $"enemy archetype '{id}' loot table", issues);

            if (!AIProfileDatabase.IsRegistered(archetype.AiProfileId))
            {
                issues.Add($"enemy archetype '{id}' references unknown AI profile '{archetype.AiProfileId}'");
            }

            if (!string.IsNullOrEmpty(archetype.FactionId) && FactionDatabase.Get(archetype.FactionId) == null)
            {
                issues.Add($"enemy archetype '{id}' references unknown faction '{archetype.FactionId}'");
            }

            foreach (string spellId in archetype.KnownSpellIds)
            {
                if (SpellDatabase.Get(spellId) == null)
                {
                    issues.Add($"enemy archetype '{id}' knows unknown spell '{spellId}'");
                }
            }

            if (archetype.XpValue < 0)
            {
                issues.Add($"enemy archetype '{id}' has a negative XP value");
            }

            ValidateHitZones(archetype, issues);

            // A breath (35C) is cast through the ordinary spellcasting path, so an id that is not in
            // the creature's loadout is a component holding a spell it can never select — silent in
            // play, and indistinguishable from "the dragon just isn't breathing yet".
            if (archetype.BreathSpellId.Length > 0)
            {
                if (SpellDatabase.Get(archetype.BreathSpellId) is not { } breath)
                {
                    issues.Add($"enemy archetype '{id}' breathes unknown spell '{archetype.BreathSpellId}'");
                }
                else if (breath.Delivery != SpellDelivery.Cone)
                {
                    issues.Add($"enemy archetype '{id}' breathes '{breath.Id}', which is not a Cone delivery");
                }

                if (!archetype.KnownSpellIds.Contains(archetype.BreathSpellId))
                {
                    issues.Add($"enemy archetype '{id}' breathes '{archetype.BreathSpellId}' but does not know it");
                }
            }

            // 35F: a creature that talks. An unknown id gives it a mute DialogueComponent, which in
            // play looks exactly like a creature that was never meant to speak.
            if (archetype.DialogueId.Length > 0 && DialogueDatabase.Get(archetype.DialogueId) == null)
            {
                issues.Add($"enemy archetype '{id}' references unknown dialogue '{archetype.DialogueId}'");
            }
        }
    }

    /// <summary>Hit zones (Phase 35A) are geometry authored as numbers, and the failure modes are all
    /// silent in play: a zero radius makes a zone unhittable, a zero multiplier makes it a free
    /// surface to stand on, and a duplicate id gives two zones the same name in the scene tree so the
    /// second one is the only one anybody ever finds when debugging.</summary>
    private static void ValidateHitZones(EnemyArchetypeResource archetype, List<string> issues)
    {
        var seen = new HashSet<string>();
        foreach (HitZoneResource zone in archetype.HitZones)
        {
            if (zone == null)
            {
                issues.Add($"enemy archetype '{archetype.Id}' has a null hit zone");
                continue;
            }

            if (string.IsNullOrEmpty(zone.Id))
            {
                issues.Add($"enemy archetype '{archetype.Id}' has a hit zone with no id");
            }
            else if (!seen.Add(zone.Id))
            {
                issues.Add($"enemy archetype '{archetype.Id}' has duplicate hit zone id '{zone.Id}'");
            }

            if (zone.Radius <= 0f)
            {
                issues.Add($"enemy archetype '{archetype.Id}' hit zone '{zone.Id}' has radius {zone.Radius} — unhittable");
            }

            if (zone.DamageMultiplier <= 0f)
            {
                issues.Add(
                    $"enemy archetype '{archetype.Id}' hit zone '{zone.Id}' has multiplier " +
                    $"{zone.DamageMultiplier} — it would absorb hits for free");
            }
        }

        if (archetype.DirectionalMelee && archetype.HitZones.Count == 0)
        {
            issues.Add($"enemy archetype '{archetype.Id}' has directional melee but no hit zones to justify it");
        }
    }

    /// <summary>AI profiles became authored content in Phase 34A. A profile with an incoherent band
    /// (kite distance past the standoff range, standoff inside weapon reach) doesn't crash — it just
    /// produces an enemy that jitters or refuses to close, which is far harder to spot in play than
    /// in a report.</summary>
    private static void ValidateAIProfiles(List<string> issues)
    {
        foreach (AIProfileResource profile in AIProfileDatabase.All)
        {
            string id = profile.Id;
            if (profile.VisionRange <= 0f)
            {
                issues.Add($"ai profile '{id}' has a non-positive vision range; it would never notice the player");
            }

            if (profile.AttackRange <= 0f)
            {
                issues.Add($"ai profile '{id}' has a non-positive attack range");
            }

            if (profile.IsStandoff && profile.KiteDistance >= profile.StandoffRange)
            {
                issues.Add($"ai profile '{id}' kites at {profile.KiteDistance} but only reaches {profile.StandoffRange}; the band is inverted");
            }

            if (profile.StandoffRange > 0f && !profile.IsStandoff)
            {
                issues.Add($"ai profile '{id}' has a standoff range inside its attack range; it will close to melee anyway");
            }

            if (profile.IsShielded && profile.BlockRecovery <= 0f)
            {
                issues.Add($"ai profile '{id}' blocks with no recovery window; it would never lower its guard to attack");
            }

            if (profile.RetreatHealthFraction is < 0f or > 1f)
            {
                issues.Add($"ai profile '{id}' retreat fraction {profile.RetreatHealthFraction} is outside 0..1");
            }

            // A negative territory (35D) reads as "leashed at once": ShouldBreakOff treats <= 0 as
            // "no leash", so the intent silently inverts into the opposite of what was authored.
            if (profile.TerritoryRadius < 0f)
            {
                issues.Add($"ai profile '{id}' has a negative territory radius ({profile.TerritoryRadius})");
            }

            ValidateFlight(profile, issues);
        }
    }

    /// <summary>Flight tuning (Phase 35B). Every failure here is silent in play rather than loud: a
    /// zero climb speed leaves a dragon stuck in a take-off it never completes, a zero airborne
    /// duration makes it flicker up and straight back down, and hover numbers on a profile with no
    /// takeoff range are tuning somebody wrote for a creature that will never leave the ground.</summary>
    private static void ValidateFlight(AIProfileResource profile, List<string> issues)
    {
        string id = profile.Id;
        if (profile.TakeoffRange <= 0f)
        {
            if (profile.HoverAltitude > 0f || profile.AirborneDuration > 0f || profile.ClimbSpeed > 0f)
            {
                issues.Add($"ai profile '{id}' has flight tuning but no takeoff range; it will never leave the ground");
            }

            return;
        }

        if (profile.HoverAltitude <= 0f)
        {
            issues.Add($"ai profile '{id}' takes off to a hover altitude of {profile.HoverAltitude}");
        }

        if (profile.ClimbSpeed <= 0f)
        {
            issues.Add($"ai profile '{id}' flies with a climb speed of {profile.ClimbSpeed}; it would never reach altitude");
        }

        if (profile.AirborneDuration <= 0f)
        {
            issues.Add($"ai profile '{id}' stays airborne for {profile.AirborneDuration}s; it would land the frame it took off");
        }

        if (profile.TakeoffRange > profile.VisionRange)
        {
            issues.Add(
                $"ai profile '{id}' takes off beyond {profile.TakeoffRange}m but only sees {profile.VisionRange}m; " +
                "it can never be in combat with a target that far away, so it would only ever fly on the ground timer");
        }
    }

    /// <summary>Companions became authored content in Phase 32C, so they get the same treatment as
    /// every other database: a name the UI can actually show, build paths that resolve (a missing
    /// attribute set silently degrades a companion to default stats), a sane loyalty range, and
    /// quest/dialogue cross-references that exist.</summary>
    private static void ValidateCompanions(List<string> issues)
    {
        foreach (CompanionResource companion in CompanionDatabase.All)
        {
            string id = companion.Id;
            if (string.IsNullOrEmpty(companion.NameKey) || !Loc.Has(companion.NameKey))
            {
                issues.Add($"companion '{id}' name key '{companion.NameKey}' is missing from the locale catalogue");
            }

            if (!string.IsNullOrEmpty(companion.TitleKey) && !Loc.Has(companion.TitleKey))
            {
                issues.Add($"companion '{id}' title key '{companion.TitleKey}' is missing from the locale catalogue");
            }

            RequirePath(companion.AttributesPath, $"companion '{id}' attributes", issues);
            RequirePath(companion.WeaponPath, $"companion '{id}' weapon", issues);

            if (!string.IsNullOrEmpty(companion.FactionId) && FactionDatabase.Get(companion.FactionId) == null)
            {
                issues.Add($"companion '{id}' references unknown faction '{companion.FactionId}'");
            }

            foreach (string spellId in companion.KnownSpellIds)
            {
                if (SpellDatabase.Get(spellId) == null)
                {
                    issues.Add($"companion '{id}' knows unknown spell '{spellId}'");
                }
            }

            if (companion.StartingLoyalty < CompanionLoyalty.Min || companion.StartingLoyalty > CompanionLoyalty.Max)
            {
                issues.Add($"companion '{id}' StartingLoyalty {companion.StartingLoyalty} is outside 0-100");
            }

            RequireQuestIfSet(companion.RecruitQuestId, $"companion '{id}' recruit quest", issues);
            RequireQuestIfSet(companion.LoyaltyQuestId, $"companion '{id}' loyalty quest", issues);

            if (!string.IsNullOrEmpty(companion.DialogueId) && DialogueDatabase.Get(companion.DialogueId) == null)
            {
                issues.Add($"companion '{id}' references unknown dialogue '{companion.DialogueId}'");
            }

            if (!CompanionRegistry.IsRegistered(id))
            {
                issues.Add($"companion '{id}' is authored but not registered (the registry seeds from the database)");
            }
        }
    }

    private static void RequirePath(string path, string context, List<string> issues)
    {
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path))
        {
            issues.Add($"{context} resource missing: {path}");
        }
    }

    private static void RequireQuestIfSet(string questId, string context, List<string> issues)
    {
        if (!string.IsNullOrEmpty(questId) && QuestDatabase.Get(questId) == null)
        {
            issues.Add($"{context} references unknown quest '{questId}'");
        }
    }

    /// <summary>Stat blocks and weapons have no database (the factories load them by literal path), so a
    /// missing/typo'd path silently degrades to defaults at runtime. Assert the critical ones resolve.</summary>
    private static void ValidateResourcePaths(List<string> issues)
    {
        (string Label, string Path)[] critical =
        {
            ("player attributes", PlayerFactory.PlayerAttributesPath),
            ("player weapon", PlayerFactory.StartingWeaponPath),
            ("player progression", PlayerFactory.ProgressionPath),
            ("goblin attributes", EnemyFactory.AttributesPath),
            ("goblin weapon", EnemyFactory.WeaponPath),
            ("goblin loot", EnemyFactory.LootTablePath),
            ("iron king attributes", BossFactory.AttributesPath),
            ("iron king weapon", BossFactory.WeaponPath),
        };

        foreach ((string label, string path) in critical)
        {
            if (!ResourceLoader.Exists(path))
            {
                issues.Add($"{label} resource missing: {path}");
            }
        }
    }

    private static void CollectGraphIssues(List<string> issues)
    {
        ValidateDialogueReachability(issues);
        ValidateStoryFlags(issues);
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
        CheckDuplicateIds<RaceResource>("res://data/races", "race", r => r.Id, issues);
        CheckDuplicateIds<AIProfileResource>("res://data/ai_profiles", "ai profile", r => r.Id, issues);
        CheckDuplicateIds<EnemyArchetypeResource>("res://data/enemies", "enemy archetype", r => r.Id, issues);
        CheckDuplicateIds<BestiaryEntryResource>("res://data/bestiary", "bestiary entry", r => r.Id, issues);
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

    private static void ValidateRaces(List<string> issues)
    {
        foreach (RaceResource race in RaceDatabase.All)
        {
            foreach (string perkId in race.InnatePerkIds)
            {
                if (PerkDatabase.Get(perkId) == null)
                {
                    issues.Add($"race '{race.Id}' references unknown innate perk '{perkId}'");
                }
            }

            foreach (string spellId in race.InnateSpellIds)
            {
                if (SpellDatabase.Get(spellId) == null)
                {
                    issues.Add($"race '{race.Id}' references unknown innate spell '{spellId}'");
                }
            }

            foreach (RaceReputationTweak tweak in race.ReputationTweakList())
            {
                if (FactionDatabase.Get(tweak.FactionId) == null)
                {
                    issues.Add($"race '{race.Id}' reputation tweak references unknown faction '{tweak.FactionId}'");
                }
            }
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

            if (!string.IsNullOrEmpty(quest.FactionRewardId) &&
                FactionDatabase.Get(quest.FactionRewardId) == null)
            {
                issues.Add($"quest '{quest.Id}' rewards unknown faction '{quest.FactionRewardId}'");
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

                    // 35F: a mistyped taught spell is the whole reward for a boss fight, silently gone.
                    if (choice.Effect == DialogueEffect.LearnSpell && SpellDatabase.Get(choice.EffectArg) == null)
                    {
                        issues.Add($"dialogue '{dialogue.Id}' LearnSpell effect references unknown spell '{choice.EffectArg}'");
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

            // Cone geometry (Phase 35C). A zero angle or reach resolves to a cone that contains
            // nothing — the spell casts, costs mana, plays its flash, and never hits.
            if (spell.Delivery == SpellDelivery.Cone)
            {
                if (spell.ConeAngleDegrees is <= 0f or >= 360f)
                {
                    issues.Add($"cone spell '{spell.Id}' has an angle of {spell.ConeAngleDegrees}°; it must be within (0, 360)");
                }

                if (spell.ImpactRadius <= 0f)
                {
                    issues.Add($"cone spell '{spell.Id}' has no impact radius; that is the cone's length, so it would reach nothing");
                }
            }
            else if (spell.ConeAngleDegrees > 0f)
            {
                issues.Add($"spell '{spell.Id}' has a cone angle but is not a Cone delivery; the angle will never be read");
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

            // A chance authored outside 0..1 has no other symptom: 50 silently makes every spawn
            // Ashen forever, and a negative one silently disables the encounter's corruption.
            if (encounter.CorruptionChance is < 0f or > 1f)
            {
                issues.Add($"encounter '{encounter.Id}' has a corruption chance outside 0..1: {encounter.CorruptionChance}");
            }

            // A misspelled region id (Phase 34.5B) silently narrows the encounter to nowhere: it is
            // never eligible, and the only symptom is a creature that stops appearing.
            foreach (string regionId in encounter.RegionIds)
            {
                if (RegionDatabase.Get(regionId) == null)
                {
                    issues.Add($"encounter '{encounter.Id}' references unknown region '{regionId}'");
                }
            }
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

            // A misspelled region id (Phase 35G) silently narrows the event to nowhere, exactly as it
            // does for an encounter: it is never eligible, and the only symptom is an event that
            // stops appearing.
            foreach (string regionId in worldEvent.RegionIds)
            {
                if (RegionDatabase.Get(regionId) == null)
                {
                    issues.Add($"event '{worldEvent.Id}' references unknown region '{regionId}'");
                }
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

    /// <summary>
    /// Story flags are the one id family with no database behind them, so nothing has ever checked
    /// them: a mistyped <c>HasFlag</c> arg is a gate that never opens, silently and permanently, and
    /// a mistyped <c>SetFlag</c> is a rank the hold never grants. Phase 34.5C's rank chain rests on
    /// them, so this closes the hole the only way a registry-less id can be closed — cross-reference
    /// the readers against the writers. A flag nothing writes is the typo that matters; the reverse
    /// (a flag set but never read) is legitimate, since flags are also a record of what happened.
    /// </summary>
    /// <summary>
    /// Adds story flags raised by scene-authored components to the "something writes this" set.
    /// <see cref="Enemies.LairSpawnComponent.DefeatFlagId"/> (Phase 35F) is the first flag writer that
    /// lives in a <c>.tscn</c> rather than in a dialogue effect or a code constant, so without this the
    /// flag audit reports every one of them as "nothing ever sets it" — a false failure that would push
    /// the next author to delete a working gate.
    ///
    /// Scanning the scene text rather than instantiating the scenes keeps this cheap and side-effect
    /// free: the validator runs headless and must not build actors to answer a content question.
    /// ponytail: one regex over the scene tree; if a second scene-authored flag writer appears, add its
    /// property name to the pattern rather than a second walk.
    /// </summary>
    private static void CollectSceneAuthoredFlags(HashSet<string> written)
    {
        foreach (string path in ScenePaths("res://scenes"))
        {
            using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(file.GetAsText(), "DefeatFlagId = \"([^\"]+)\""))
            {
                written.Add(match.Groups[1].Value);
            }
        }
    }

    /// <summary>Every <c>.tscn</c> at or below a directory.</summary>
    private static IEnumerable<string> ScenePaths(string directory)
    {
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            yield break;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            if (file.EndsWith(".tscn", System.StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{directory}/{file}";
            }
        }

        foreach (string sub in DirAccess.GetDirectoriesAt(directory))
        {
            foreach (string nested in ScenePaths($"{directory}/{sub}"))
            {
                yield return nested;
            }
        }
    }

    private static void ValidateStoryFlags(List<string> issues)
    {
        var written = new HashSet<string>
        {
            Enemies.BossEncounterDirector.DefeatedFlag,
            Narrative.SliceDirector.CompletedFlag,
            Narrative.SliceDirector.AbsorbedFlag,
        };

        CollectSceneAuthoredFlags(written);

        foreach (DialogueResource dialogue in DialogueDatabase.All)
        {
            foreach (DialogueNode node in dialogue.NodeList())
            {
                foreach (DialogueChoice choice in node.ChoiceList())
                {
                    if (choice.Effect is DialogueEffect.SetFlag or DialogueEffect.ClearFlag &&
                        !string.IsNullOrEmpty(choice.EffectArg))
                    {
                        written.Add(choice.EffectArg);
                    }
                }
            }
        }

        foreach (DialogueResource dialogue in DialogueDatabase.All)
        {
            foreach (DialogueNode node in dialogue.NodeList())
            {
                foreach (DialogueChoice choice in node.ChoiceList())
                {
                    if (choice.Condition is DialogueCondition.HasFlag or DialogueCondition.MissingFlag &&
                        !string.IsNullOrEmpty(choice.ConditionArg) && !written.Contains(choice.ConditionArg))
                    {
                        issues.Add($"dialogue '{dialogue.Id}' reads flag '{choice.ConditionArg}', which nothing ever sets");
                    }
                }
            }
        }

        foreach (RegionResource region in RegionDatabase.All)
        {
            if (!string.IsNullOrEmpty(region.UnlockFlagId) && !written.Contains(region.UnlockFlagId))
            {
                issues.Add($"region '{region.Id}' unlocks on flag '{region.UnlockFlagId}', which nothing ever sets");
            }
        }
    }
}
