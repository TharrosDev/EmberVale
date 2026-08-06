using System.Collections.Generic;
using System.Text;
using Embervale.Companions;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Crafting;
using Embervale.Dialogue;
using Embervale.Economy;
using Embervale.Enemies;
using Embervale.Housing;
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
        ValidateRecipeReachability(issues);
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
        ValidateBosses(issues);
        ValidateProperties(issues);
        ValidateItemTags(issues);
        ValidateShops(issues);
        ValidateServices(issues);
        ValidatePlaceables(issues);
        ValidateBestiary(issues);
        ValidateResourcePaths(issues);
        ValidateUiAssets(issues);
    }

    /// <summary>
    /// The UI's fonts and shaders (Phase 37.5D).
    ///
    /// These are content, and until this check they were the only content nothing could fail the
    /// build over. A missing font degrades silently to the engine default; a shader that fails to
    /// compile leaves an invisible decoration. Worse, three of the four UI shaders only instantiate
    /// when a specific screen is *opened*, so a broken one would not appear in a boot log, a
    /// `--play` run, or any automated check — only in play, on one screen, as "nothing is there".
    ///
    /// Loading each one is enough: Godot reports a parse or compile failure at load, and
    /// constructing the <c>ShaderMaterial</c> catches a shader that loaded but is unusable.
    /// </summary>
    private static void ValidateUiAssets(List<string> issues)
    {
        string[] fonts =
        {
            "res://assets/fonts/Cinzel-Variable.ttf",
            "res://assets/fonts/EBGaramond-Variable.ttf",
            "res://assets/fonts/EBGaramond-Italic-Variable.ttf",
            "res://assets/fonts/Inter-Variable.ttf",
        };

        foreach (string path in fonts)
        {
            if (!ResourceLoader.Exists(path) || GD.Load<FontFile>(path) is null)
            {
                issues.Add($"UI font '{path}' is missing or failed to import — the UI would fall back to the engine default.");
            }
        }

        string[] shaders =
        {
            "res://assets/shaders/ui/ui_grain.gdshader",
            "res://assets/shaders/ui/rune_circle.gdshader",
            "res://assets/shaders/ui/sigil_drift.gdshader",
            "res://assets/shaders/ui/ink_shimmer.gdshader",
        };

        foreach (string path in shaders)
        {
            if (!ResourceLoader.Exists(path) || GD.Load<Shader>(path) is not { } shader)
            {
                issues.Add($"UI shader '{path}' is missing.");
                continue;
            }

            // ⚠️ A null check is NOT enough, and the obvious version of this check is worthless:
            // GD.Load returns a perfectly non-null Shader for source that does not parse at all.
            // Godot prints the compile error and hands back the resource anyway, and there is no
            // public "did this compile" API. Verified by feeding it a file containing the words
            // "this is not glsl", which loaded fine and passed a null check.
            //
            // The uniform list is the seam that does work: a shader that failed to parse exposes
            // none. Every UI shader declares several by design, so an empty list means the source
            // did not compile. If a future UI shader genuinely has no uniforms it must be exempted
            // here explicitly rather than being allowed to weaken the check for the others.
            if (shader.GetShaderUniformList().Count == 0)
            {
                issues.Add($"UI shader '{path}' failed to compile (it exposes no uniforms).");
            }
        }
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
            // Loot is optional, and EnemyArchetypeFactory already treats it that way — it only
            // attaches a LootComponent for a non-empty path. Requiring one here contradicted the
            // factory; it went unnoticed only because every archetype happened to have a table until
            // the Iron King arrived, who drops nothing (Phase 28D's reward loop grants his relic).
            if (archetype.LootTablePath.Length > 0 && !ResourceLoader.Exists(archetype.LootTablePath))
            {
                issues.Add($"enemy archetype '{id}' loot table resource missing: {archetype.LootTablePath}");
            }

            // An *authored* model that fails to load falls back to a flat capsule, silently — and
            // for a boss that is worse than cosmetic, since BossController flares an emissive
            // material it can only find on the real model. An empty path is not an error: most
            // archetypes deliberately greybox (from their hit zones, or a tinted capsule).
            if (archetype.ModelPath.Length > 0 && !ResourceLoader.Exists(archetype.ModelPath))
            {
                issues.Add($"enemy archetype '{id}' model resource missing: {archetype.ModelPath}");
            }

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

    /// <summary>
    /// Boss fights became authored content in Phase 36A. Every failure here is silent at runtime and
    /// expensive in play: an unsorted phase table means a boss that never leaves stage one, a
    /// misspelled spell id means an ability set that grants nothing, and a <c>BossId</c> on a
    /// non-boss archetype is a fight structure that is simply never attached. Checked in <b>both
    /// directions</b>, the way the bestiary domain is.
    /// </summary>
    private static void ValidateBosses(List<string> issues)
    {
        foreach (BossResource boss in BossDatabase.All)
        {
            if (string.IsNullOrEmpty(boss.Id))
            {
                issues.Add("a boss has an empty id");
            }

            if (boss.Phases.Count == 0)
            {
                issues.Add($"boss '{boss.Id}' has no phases — it would never escalate");
                continue;
            }

            ValidatePhases(boss, issues);

            foreach (string spellId in boss.EnrageSpellIds)
            {
                if (SpellDatabase.Get(spellId) == null)
                {
                    issues.Add($"boss '{boss.Id}' enrage grants unknown spell '{spellId}'");
                }
            }

            ValidateEncounter(boss, issues);

            if (boss.EnrageSeconds < 0f)
            {
                issues.Add($"boss '{boss.Id}' has a negative enrage time ({boss.EnrageSeconds})");
            }
        }

        // The other direction: an archetype must name a boss that exists, and only a boss archetype
        // may name one at all — otherwise the reference is a no-op nothing would ever report.
        foreach (EnemyArchetypeResource archetype in EnemyArchetypeDatabase.All)
        {
            if (string.IsNullOrEmpty(archetype.BossId))
            {
                continue;
            }

            if (!archetype.IsBoss)
            {
                issues.Add(
                    $"enemy archetype '{archetype.Id}' names boss '{archetype.BossId}' but is not " +
                    "IsBoss — the fight structure would never be attached");
            }

            if (BossDatabase.Get(archetype.BossId) == null)
            {
                issues.Add($"enemy archetype '{archetype.Id}' names unknown boss '{archetype.BossId}'");
            }
        }
    }

    /// <summary>
    /// A boss's intro/defeat/reward config (36E). The load-bearing rule is the last one: a reward or
    /// a defeat conversation with no DefeatFlagId has nothing to record that it already happened, so
    /// it would pay out on every death. That is the shape of the bug 36E fixed, and this is what
    /// stops it being re-authored.
    /// </summary>
    private static void ValidateEncounter(BossResource boss, List<string> issues)
    {
        bool hasReward = boss.RewardItemId.Length > 0;
        bool hasDialogue = boss.DefeatDialogueId.Length > 0;

        if (hasReward && ItemDatabase.Get(boss.RewardItemId) == null)
        {
            issues.Add($"boss '{boss.Id}' rewards unknown item '{boss.RewardItemId}'");
        }

        if (hasDialogue && DialogueDatabase.Get(boss.DefeatDialogueId) == null)
        {
            issues.Add($"boss '{boss.Id}' names unknown defeat dialogue '{boss.DefeatDialogueId}'");
        }

        if ((hasReward || hasDialogue) && boss.DefeatFlagId.Length == 0)
        {
            issues.Add(
                $"boss '{boss.Id}' grants a reward or defeat conversation but sets no DefeatFlagId — " +
                "nothing would record that it already happened, so it would pay out on every death");
        }

        if (hasReward && boss.RewardQuantity <= 0)
        {
            issues.Add($"boss '{boss.Id}' rewards {boss.RewardQuantity}x '{boss.RewardItemId}'");
        }

        if (boss.IntroLockSeconds < 0f || boss.DefeatSlowSeconds < 0f)
        {
            issues.Add($"boss '{boss.Id}' has a negative intro or defeat duration");
        }

        if (boss.DefeatTimeScale <= 0f)
        {
            issues.Add(
                $"boss '{boss.Id}' has a defeat time scale of {boss.DefeatTimeScale} — the world " +
                "would stop rather than slow, and never restart");
        }
    }

    /// <summary>A phase table must open at full health and descend strictly, because
    /// <c>BossPhases.SelectPhase</c> trusts that ordering rather than re-sorting it every hit.</summary>
    private static void ValidatePhases(BossResource boss, List<string> issues)
    {
        if (!Mathf.IsEqualApprox(boss.Phases[0].HealthFraction, 1f))
        {
            issues.Add(
                $"boss '{boss.Id}' opens at {boss.Phases[0].HealthFraction:0.##} health rather than 1.0 — " +
                "an undamaged boss would start in no phase at all");
        }

        for (int i = 0; i < boss.Phases.Count; i++)
        {
            BossPhaseResource phase = boss.Phases[i];
            if (phase == null)
            {
                issues.Add($"boss '{boss.Id}' has a null phase at index {i}");
                continue;
            }

            if (phase.HealthFraction is <= 0f or > 1f)
            {
                issues.Add(
                    $"boss '{boss.Id}' phase {i + 1} has health fraction {phase.HealthFraction} — " +
                    "must be within (0, 1]");
            }

            if (i > 0 && phase.HealthFraction >= boss.Phases[i - 1].HealthFraction)
            {
                issues.Add(
                    $"boss '{boss.Id}' phase {i + 1} ({phase.HealthFraction:0.##}) does not fall below " +
                    $"phase {i} ({boss.Phases[i - 1].HealthFraction:0.##}) — phases must descend");
            }

            if (phase.WindupPoiseMultiplier <= 0f)
            {
                issues.Add(
                    $"boss '{boss.Id}' phase {i + 1} has a wind-up poise multiplier of " +
                    $"{phase.WindupPoiseMultiplier} — the phase could never be staggered mid-wind-up, " +
                    "which in play is indistinguishable from the interrupt being broken");
            }

            foreach (string spellId in phase.GrantSpellIds)
            {
                if (SpellDatabase.Get(spellId) == null)
                {
                    issues.Add($"boss '{boss.Id}' phase {i + 1} grants unknown spell '{spellId}'");
                }
            }

            if (!string.IsNullOrEmpty(phase.AiProfileId) && AIProfileDatabase.Get(phase.AiProfileId) == null)
            {
                issues.Add($"boss '{boss.Id}' phase {i + 1} names unknown AI profile '{phase.AiProfileId}'");
            }

            ValidateAddWaves(boss, phase, i + 1, issues);
        }
    }

    /// <summary>
    /// A boss's add waves (36D). Every failure here is silent in play and expensive: an unknown
    /// template spawns the registry's fallback goblin into a boss arena and reads as a design
    /// choice, and an uncapped repeating wave ends the fight by burying the player rather than by
    /// killing them.
    /// </summary>
    private static void ValidateAddWaves(
        BossResource boss, BossPhaseResource phase, int phaseNumber, List<string> issues)
    {
        for (int w = 0; w < phase.AddWaves.Count; w++)
        {
            BossAddWaveResource wave = phase.AddWaves[w];
            string where = $"boss '{boss.Id}' phase {phaseNumber} wave {w + 1}";

            if (wave == null)
            {
                issues.Add($"{where} is null");
                continue;
            }

            if (string.IsNullOrEmpty(wave.TemplateId))
            {
                issues.Add($"{where} names no enemy template");
            }
            else if (!EnemyTemplateRegistry.IsRegistered(wave.TemplateId))
            {
                issues.Add($"{where} names unregistered enemy template '{wave.TemplateId}'");
            }

            if (wave.Count <= 0)
            {
                issues.Add($"{where} summons {wave.Count} — a wave that brings nothing");
            }

            if (wave.RepeatSeconds < 0f)
            {
                issues.Add($"{where} has a negative repeat interval ({wave.RepeatSeconds})");
            }

            if (wave.RepeatSeconds > 0f && wave.MaxAlive <= 0)
            {
                issues.Add(
                    $"{where} repeats every {wave.RepeatSeconds:0.#}s with no MaxAlive cap — the " +
                    "phase would keep stacking adds until the player is buried rather than beaten");
            }

            if (wave.MaxAlive > 0 && wave.MaxAlive < wave.Count)
            {
                issues.Add(
                    $"{where} summons {wave.Count} but caps at {wave.MaxAlive} — the wave could " +
                    "never arrive in full");
            }

            if (wave.HealthMultiplier <= 0f)
            {
                issues.Add($"{where} has a health multiplier of {wave.HealthMultiplier}");
            }
        }
    }

    /// <summary>
    /// Holdings the player can claim (Phase 37A). The load-bearing rule is the last pair: a property
    /// that is neither sold nor earned is free the moment someone walks into the post, and one sold
    /// with no travel node is gold spent on a place the player cannot return to. Both look like
    /// content until someone plays them.
    /// </summary>
    private static void ValidateProperties(List<string> issues)
    {
        foreach (PropertyResource property in PropertyDatabase.All)
        {
            string id = property.Id;

            if (string.IsNullOrEmpty(property.NameKey) || !Loc.Has(property.NameKey))
            {
                issues.Add($"property '{id}' name key '{property.NameKey}' is missing from the locale catalogue");
            }

            if (RegionDatabase.Get(property.RegionId) == null)
            {
                issues.Add($"property '{id}' references unknown region '{property.RegionId}'");
            }

            if (property.RequiredQuestId.Length > 0 && QuestDatabase.Get(property.RequiredQuestId) == null)
            {
                issues.Add($"property '{id}' requires unknown quest '{property.RequiredQuestId}'");
            }

            if (property.PriceGold < 0)
            {
                issues.Add($"property '{id}' has a negative price ({property.PriceGold})");
            }

            if (property.PriceGold == 0 && property.RequiredQuestId.Length == 0)
            {
                issues.Add(
                    $"property '{id}' is neither sold nor earned — it would be claimed by the first " +
                    "player who walked into its deed post");
            }

            if (property.TravelNodeId.Length == 0)
            {
                issues.Add(
                    $"property '{id}' registers no travel node — the player would buy somewhere they " +
                    "then had no way back to");
            }

            ValidatePlacementArea(property, issues);
        }
    }

    /// <summary>
    /// A merchant's wares and spread (Phase 38A). The spread rule is the one that matters: a
    /// <c>SellFraction</c> at or above <c>BuyMarkup</c> is a money printer — buy a stack, sell it
    /// straight back, repeat. <see cref="Economy.ShopPricing"/> clamps so a hand-edited resource cannot
    /// actually do it, and this stops one being authored in the first place.
    ///
    /// A shop's <c>ShopId</c> lives on a <c>VendorComponent</c> in a <c>.tscn</c>, which this
    /// validator does not scan — so a mistyped one is invisible here and shows in game as a merchant
    /// with no prompt at all. Same blind spot as <c>PropertyStorageComponent.PropertyId</c>.
    /// </summary>
    /// <summary>
    /// Every item's trade tags are words <see cref="TradeTags"/> knows (Phase 38F). A typo here is the
    /// worst kind of silent: the item is not rejected by anything, it simply stops matching the merchant
    /// who was meant to buy it, and the symptom is one shop refusing one item with a straight face.
    ///
    /// An item with <em>no</em> tags is deliberately legal — every merchant takes it. That is the
    /// fail-open rule, not an oversight, so there is no "untagged item" issue here.
    /// </summary>
    private static void ValidateItemTags(List<string> issues)
    {
        foreach (KeyValuePair<string, ItemResource> entry in ItemDatabase.All)
        {
            ItemResource item = entry.Value;
            foreach (string tag in item.TagList())
            {
                if (!TradeTags.IsKnown(tag))
                {
                    issues.Add($"item '{item.Id}' carries unknown trade tag '{tag}' — it would never " +
                        "match a merchant's trade");
                }
            }
        }
    }

    private static void ValidateShops(List<string> issues)
    {
        foreach (ShopResource shop in ShopDatabase.All)
        {
            string id = shop.Id;

            if (string.IsNullOrEmpty(shop.NameKey) || !Loc.Has(shop.NameKey))
            {
                issues.Add($"shop '{id}' name key '{shop.NameKey}' is missing from the locale catalogue");
            }

            List<ShopStockEntry> stock = shop.StockList();
            if (stock.Count == 0 && shop.LeveledTable == null)
            {
                issues.Add($"shop '{id}' stocks nothing — its window would open empty");
            }

            foreach (ShopStockEntry entry in stock)
            {
                string itemId = entry.ItemId;
                RequireItem(itemId, $"shop '{id}' stock", issues);

                if (itemId == GameIds.Currency.Gold)
                {
                    issues.Add($"shop '{id}' stocks gold — the player would buy coins with coins");
                }
                else if (ItemDatabase.Get(itemId) is { Type: ItemType.Quest })
                {
                    issues.Add(
                        $"shop '{id}' stocks quest item '{itemId}' — a quest object must be found, " +
                        "not bought off a shelf");
                }

                if (entry.Quantity < 0)
                {
                    issues.Add($"shop '{id}' row '{itemId}' has a negative quantity ({entry.Quantity})");
                }
                else if (entry.Quantity > 0 && shop.RestockDays <= 0)
                {
                    issues.Add(
                        $"shop '{id}' row '{itemId}' holds {entry.Quantity} and the shop never restocks " +
                        "— the first player through the door empties it for the rest of the run");
                }
            }

            ValidateShopRestock(shop, issues);

            if (shop.BuyMarkup < 1f)
            {
                issues.Add(
                    $"shop '{id}' has a buy markup below 1 ({shop.BuyMarkup}) — it would sell below " +
                    "an item's own value");
            }

            if (shop.SellFraction <= 0f)
            {
                issues.Add($"shop '{id}' pays nothing when selling (sell fraction {shop.SellFraction})");
            }

            ValidateShopTrade(shop, issues);

            if (shop.FactionId.Length > 0 && FactionDatabase.Get(shop.FactionId) == null)
            {
                issues.Add(
                    $"shop '{id}' prices by unknown faction '{shop.FactionId}' — standing would have no " +
                    "effect and the discount would silently never apply");
            }

            if (shop.PurseGold < 0)
            {
                issues.Add($"shop '{id}' has a negative purse ({shop.PurseGold})");
            }
            else if (shop.PurseGold > 0 && shop.RestockDays <= 0)
            {
                issues.Add(
                    $"shop '{id}' carries {shop.PurseGold} gold and never restocks — the merchant is " +
                    "permanently out of money once the player has sold them that much");
            }

            // The clamp in ShopPricing.BuyPrice makes a discount safe (sell can never exceed buy), which
            // also means it silently swallows a markup too thin to discount. That is a content bug the
            // arithmetic cannot report, so it is reported here.
            if (shop.FactionId.Length > 0 &&
                ShopPricing.MarkupFor(shop.BuyMarkup, ReputationTier.Honored) <= 1f)
            {
                issues.Add(
                    $"shop '{id}' markup {shop.BuyMarkup} bottoms out at Honored — its best standings all " +
                    "pay the same price, so the reputation discount stops meaning anything");
            }

            if (shop.SellFraction >= shop.BuyMarkup)
            {
                issues.Add(
                    $"shop '{id}' pays at least as much as it charges (sell {shop.SellFraction} >= buy " +
                    $"{shop.BuyMarkup}) — that is an infinite gold loop");
            }
        }
    }

    /// <summary>
    /// The paid services (Phase 38D). Modelled on <see cref="ValidateShops"/>, and the two rules worth
    /// reading twice are the <c>UnlockFlagId</c> pairings — both are cases where the authored data is
    /// perfectly well-formed and the *economy* is broken.
    /// </summary>
    private static void ValidateServices(List<string> issues)
    {
        foreach (ServiceResource service in ServiceDatabase.All)
        {
            string id = service.Id;

            if (string.IsNullOrEmpty(service.NameKey) || !Loc.Has(service.NameKey))
            {
                issues.Add($"service '{id}' name key '{service.NameKey}' is missing from the locale catalogue");
            }

            if (service.PriceGold < 0)
            {
                issues.Add($"service '{id}' has a negative price ({service.PriceGold})");
            }

            if (service.FactionId.Length > 0 && FactionDatabase.Get(service.FactionId) == null)
            {
                issues.Add(
                    $"service '{id}' prices by unknown faction '{service.FactionId}' — standing would " +
                    "have no effect and the hostile refusal would never fire");
            }

            bool oneOff = service.Kind is ServiceKind.Bank or ServiceKind.Stable;
            if (oneOff && service.UnlockFlagId.Length == 0)
            {
                issues.Add(
                    $"service '{id}' is a one-off purchase with no unlock flag — nothing would record " +
                    "that it already happened, so it charges again on every interaction");
            }

            ValidateServiceKind(service, issues);
        }
    }

    /// <summary>The per-kind fields. Each kind reads exactly one group of the resource, so a value in the
    /// wrong group is silently ignored rather than wrong — which is why the rules below check the fields
    /// a kind <em>needs</em>, not the ones it leaves alone.</summary>
    private static void ValidateServiceKind(ServiceResource service, List<string> issues)
    {
        string id = service.Id;

        switch (service.Kind)
        {
            case ServiceKind.Trainer:
                if (service.TaughtRecipeIds.Count == 0 && service.XpReward <= 0)
                {
                    issues.Add(
                        $"service '{id}' is a trainer that teaches no recipes and grants no XP — it would " +
                        "take gold for nothing");
                }

                foreach (string recipeId in service.TaughtRecipeIds)
                {
                    if (string.IsNullOrEmpty(recipeId) || RecipeDatabase.Get(recipeId) == null)
                    {
                        issues.Add($"service '{id}' teaches unknown recipe '{recipeId}'");
                    }
                }

                // Without a flag, a trainer's "nothing left to teach" check is the recipe knowledge
                // itself — which XP does not have. An XP lesson with no flag is an infinite
                // gold-to-levels pump, and DESIGN §6 forbids buying the defining power outright.
                if (service.XpReward > 0 && service.UnlockFlagId.Length == 0)
                {
                    issues.Add(
                        $"service '{id}' grants {service.XpReward} XP with no unlock flag — it could be " +
                        "bought over and over, which turns gold into levels without limit");
                }

                break;

            case ServiceKind.Inn:
                if (service.RestHour < 0 || service.RestHour > 23)
                {
                    issues.Add($"service '{id}' rests to hour {service.RestHour}, outside 0..23");
                }

                if (service.UnlockFlagId.Length > 0)
                {
                    issues.Add(
                        $"service '{id}' is an inn with an unlock flag — a bed is bought every night, and " +
                        "a flag would make the first stay the only one that ever charged");
                }

                break;
        }
    }

    /// <summary>
    /// A shop's trade: which tags it buys, which it specialises in, and whether the spread survives the
    /// specialty premium (Phase 38F). Every failure here is well-formed data that reads in game as the
    /// feature being broken rather than as an authoring mistake.
    /// </summary>
    private static void ValidateShopTrade(ShopResource shop, List<string> issues)
    {
        string id = shop.Id;
        List<string> accepted = shop.AcceptedTagList();
        List<string> specialties = shop.SpecialtyList();

        foreach (string tag in accepted)
        {
            if (!TradeTags.IsKnown(tag))
            {
                issues.Add($"shop '{id}' accepts unknown trade tag '{tag}' — nothing carries it, so the " +
                    "list silently narrows instead of widening");
            }
        }

        foreach (string tag in specialties)
        {
            if (!TradeTags.IsKnown(tag))
            {
                issues.Add($"shop '{id}' specialises in unknown trade tag '{tag}' — the premium would " +
                    "never apply to anything");
            }

            // A specialist who refuses her own specialty is well-formed and incoherent: the row is
            // greyed out with a refusal that names the very trade it is refusing.
            if (accepted.Count > 0 && !accepted.Contains(tag))
            {
                issues.Add($"shop '{id}' specialises in '{tag}' but does not accept it — the premium " +
                    "sits behind a refusal");
            }
        }

        // The clamps in ShopPricing make the specialty premium incapable of inverting the spread, but
        // not incapable of closing it: sell == buy is frictionless churn rather than a money printer,
        // and it is invisible until a player notices they can round-trip an item for nothing. So the
        // widest possible sell is held against the narrowest possible buy, with room to spare.
        const float Margin = 1.25f;
        float widestSell = shop.SellFraction * ShopPricing.SpecialtySellBonus;
        float narrowestBuy = ShopPricing.MarkupFor(
            shop.BuyMarkup, Factions.ReputationTier.Allied, specialty: true);

        if (widestSell * Margin > narrowestBuy)
        {
            issues.Add($"shop '{id}' has too thin a spread once the specialty premium applies: it could " +
                $"pay {widestSell:0.00}x an item's value while charging as little as {narrowestBuy:0.00}x " +
                "for it — buying and selling back would cost the player almost nothing");
        }
    }

    /// <summary>
    /// A shop's restock clock and leveled pool (Phase 38B). Both halves fail the same way — silently,
    /// as a shop that simply never changes — so neither is safe to leave to play-testing.
    /// </summary>
    private static void ValidateShopRestock(ShopResource shop, List<string> issues)
    {
        string id = shop.Id;

        if (shop.RestockDays < 0)
        {
            issues.Add($"shop '{id}' has a negative restock interval ({shop.RestockDays} days)");
        }

        if (shop.LeveledTable is not { } table)
        {
            return;
        }

        if (table.Entries.Count == 0)
        {
            issues.Add(
                $"shop '{id}' has a leveled pool with no entries — it can only ever roll nothing, " +
                "which reads in game as the leveled stock being broken");
        }

        if (shop.RestockDays <= 0)
        {
            issues.Add(
                $"shop '{id}' has a leveled pool but never restocks — the pool is rolled *at* restock, " +
                "so it would roll once and be frozen for the whole run");
        }
    }

    /// <summary>
    /// A holding's buildable yard (Phase 37C). <c>PlacementRadius = 0</c> is legal and means the
    /// holding cannot be built in; what is not legal is a negative one, or an area centred somewhere
    /// the player cannot stand. The centre is <b>world</b> space, and a cell scene is authored at its
    /// own origin then moved to the cell's <c>Center</c> by the streamer — so the easy mistake is to
    /// copy a point straight out of a <c>.tscn</c> and land the yard a cell's width away from the
    /// house. Checking it against the region's bounds is what catches that.
    /// </summary>
    private static void ValidatePlacementArea(PropertyResource property, List<string> issues)
    {
        string id = property.Id;

        if (property.PlacementRadius < 0f)
        {
            issues.Add($"property '{id}' has a negative placement radius ({property.PlacementRadius})");
            return;
        }

        if (property.PlacementRadius <= 0f)
        {
            return; // a holding you may not build in — deliberate, and the common case
        }

        if (RegionDatabase.Get(property.RegionId) is not { } region)
        {
            return; // the unknown-region rule above already said so; don't say it twice
        }

        if (!region.Bounds.HasPoint(property.PlacementCenter))
        {
            issues.Add(
                $"property '{id}' centres its placement area at {property.PlacementCenter}, outside " +
                $"region '{region.Id}' bounds {region.Bounds} — the player could never stand in it");
        }
    }

    /// <summary>
    /// The kits the player sets down (Phase 37C). One rule, and it is the whole reason
    /// <see cref="PlaceableTemplates.Ids"/> exists as a plain set: the builders themselves are
    /// registered in <c>GameBootstrap.BuildWorld</c>, which <c>--validate</c> never runs, so without
    /// this a kit could craft, stack, carry and preview perfectly and then do nothing whatsoever when
    /// the player pressed the key — a failure with no error and no symptom but a wasted kit.
    /// </summary>
    private static void ValidatePlaceables(List<string> issues)
    {
        // Every template must actually build. The registry only stores a delegate, so "registered"
        // and "works" are different claims — this one builds each and throws it away.
        foreach (string templateId in PlaceableTemplates.Ids)
        {
            Node3D? built = PlaceableTemplates.Build(templateId, Vector3.Zero);
            if (built is not Entities.IEntity)
            {
                issues.Add(
                    $"placeable template '{templateId}' does not build an entity — " +
                    "PersistentSpawnDirector would discard it and the kit would be spent on nothing");
            }
            else if (built.GetNodeOrNull("Collider") == null)
            {
                issues.Add(
                    $"placeable template '{templateId}' builds without a collider — nothing could " +
                    "aim at it, so it could never be picked back up");
            }

            ValidateStandSlot(templateId, built, issues);

            built?.QueueFree();
        }

        foreach (KeyValuePair<string, ItemResource> entry in ItemDatabase.All)
        {
            if (entry.Value is not PlaceableItemResource placeable)
            {
                continue;
            }

            if (string.IsNullOrEmpty(placeable.TemplateId))
            {
                issues.Add($"placeable item '{placeable.Id}' names no template to build");
            }
            else if (!PlaceableTemplates.Ids.Contains(placeable.TemplateId))
            {
                issues.Add(
                    $"placeable item '{placeable.Id}' builds unregistered template " +
                    $"'{placeable.TemplateId}' — it would place nothing at all");
            }
        }
    }

    /// <summary>
    /// A display stand's one-slot inventory <b>is</b> its display and its whole persistence story
    /// (Phase 37D), so a stand built without one is a plinth that accepts nothing and remembers
    /// nothing — and a capacity other than 1 is a trophy case quietly acting as a chest. Neither
    /// throws: <c>Interact</c> just returns and the prompt goes quiet, which in play is
    /// indistinguishable from the stand not being finished.
    /// </summary>
    private static void ValidateStandSlot(string templateId, Node3D? built, List<string> issues)
    {
        if (built == null || built.GetNodeOrNull<Housing.TrophyStandComponent>("Display") == null)
        {
            return;
        }

        if (built.GetNodeOrNull<Items.InventoryComponent>("Inventory") is not { } slot)
        {
            issues.Add(
                $"placeable template '{templateId}' carries a trophy stand with no inventory — the " +
                "slot IS the display, so it would accept nothing and persist nothing");
        }
        else if (slot.Capacity != 1)
        {
            issues.Add(
                $"placeable template '{templateId}' gives its display stand capacity {slot.Capacity}; " +
                "a stand holds exactly one trophy");
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
        CheckDuplicateIds<BossResource>("res://data/bosses", "boss", r => r.Id, issues);
        CheckDuplicateIds<PropertyResource>("res://data/properties", "property", r => r.Id, issues);
        CheckDuplicateIds<ShopResource>("res://data/shops", "shop", r => r.Id, issues);
        CheckDuplicateIds<ServiceResource>("res://data/services", "service", r => r.Id, issues);
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

    /// <summary>
    /// Every recipe must be reachable. The bestiary has had a both-directions check since 34G; recipes
    /// never did, and because <c>CraftingComponent.Learn</c> has no caller anywhere (no tome, trainer,
    /// dialogue effect or quest reward — that seam is Phase 38's), the *only* way a player ever knows a
    /// recipe is <see cref="GameIds.Recipes.Starting"/>. A recipe outside that list is content nothing
    /// can reach, which is how <c>recipe.leather_vest</c> sat dead from Phase 15 to Phase 35.
    /// </summary>
    /// <summary>
    /// Recipe reachability — the <b>union</b> of the two paths that exist (Phase 38D). Until 38D there
    /// was only one: this check read "seeded in <c>GameIds.Recipes.Starting</c> or unreachable", because
    /// <c>CraftingComponent.Learn</c> had no caller in the entire game. A
    /// <see cref="Economy.ServiceKind.Trainer"/> is the second path, so the rule widens rather than
    /// relaxes — a recipe in neither still fails the build.
    ///
    /// The overlap is also an error, and that asymmetry is easy to miss: <c>PlayerFactory</c> seeds
    /// <c>Starting</c> unconditionally, so a recipe in both lists is a trainer charging for knowledge the
    /// player already walked in with.
    /// </summary>
    private static void ValidateRecipeReachability(List<string> issues)
    {
        var seeded = new HashSet<string>(GameIds.Recipes.Starting);
        var taught = new Dictionary<string, string>();

        foreach (ServiceResource service in ServiceDatabase.All)
        {
            if (service.Kind != ServiceKind.Trainer)
            {
                continue;
            }

            foreach (string recipeId in service.TaughtRecipeIds)
            {
                if (!string.IsNullOrEmpty(recipeId))
                {
                    taught[recipeId] = service.Id;
                }
            }
        }

        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            if (!seeded.Contains(recipe.Id) && !taught.ContainsKey(recipe.Id))
            {
                issues.Add(
                    $"recipe '{recipe.Id}' is never learnable — it must be listed in " +
                    "GameIds.Recipes.Starting or taught by a trainer service, or no player can ever craft it");
            }
        }

        foreach (string id in seeded)
        {
            if (RecipeDatabase.Get(id) == null)
            {
                issues.Add($"GameIds.Recipes.Starting seeds unknown recipe '{id}'");
            }

            if (taught.TryGetValue(id, out string? trainer))
            {
                issues.Add(
                    $"recipe '{id}' is both seeded and taught by service '{trainer}' — every player " +
                    "starts knowing it, so the trainer has nothing to sell");
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

                    // 38E: the first shop id anywhere the validator can see. The same typo on a
                    // VendorComponent.ShopId in a .tscn gives no prompt at all, because .tscn is not scanned.
                    if (choice.Effect == DialogueEffect.OpenShop && ShopDatabase.Get(choice.EffectArg) == null)
                    {
                        issues.Add($"dialogue '{dialogue.Id}' OpenShop effect references unknown shop '{choice.EffectArg}'");
                    }

                    // A shop choice with a Goto leaves the conversation open behind the vendor window, so
                    // closing the shop drops the player back into a dialogue they thought they had left.
                    if (choice.Effect == DialogueEffect.OpenShop && !string.IsNullOrEmpty(choice.Goto))
                    {
                        issues.Add($"dialogue '{dialogue.Id}' OpenShop choice must end the conversation but points at '{choice.Goto}'");
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

            // Range checks added in 38C. Until now a faction only decided hostility, and this pass only
            // cross-referenced its web; now standing sets prices, so a default outside the tier scale is
            // a shop that prices at a tier the player can never move off.
            if (faction.DefaultReputation < ReputationTiers.Min || faction.DefaultReputation > ReputationTiers.Max)
            {
                issues.Add(
                    $"faction '{faction.Id}' default standing {faction.DefaultReputation} is outside " +
                    $"{ReputationTiers.Min}..{ReputationTiers.Max}; it clamps on read, so the authored " +
                    "value is not the one the game uses");
            }

            if (faction.KillReputationPenalty < 0)
            {
                issues.Add(
                    $"faction '{faction.Id}' has a negative kill penalty " +
                    $"({faction.KillReputationPenalty}) — killing its members would improve standing");
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

                // A Kill objective has to name something the world can produce again. Resolving to a
                // registered template is not enough: a lair boss is a finite resource whose defeat is
                // permanent, so a quest taken after the kill can never be completed and never leaves the
                // journal. Phase 35F shipped exactly that through a green --validate.
                if (objective.Type == ObjectiveType.Kill && !quest.AllowsOneShotTarget &&
                    !string.IsNullOrEmpty(objective.TargetId) && !IsRepeatablySpawned(objective.TargetId))
                {
                    issues.Add(
                        $"quest '{quest.Id}' kills '{objective.TargetId}', which no encounter or world event spawns — " +
                        "it can only be killed once, so the quest is uncompletable if that happens first " +
                        "(set AllowsOneShotTarget once the offering dialogue gates on the target being alive)");
                }
            }
        }
    }

    /// <summary>Whether anything in the world can spawn this template more than once — an encounter or a
    /// world event. A lair spawner deliberately does not count: that is the finite case this exists to
    /// catch.</summary>
    private static bool IsRepeatablySpawned(string templateId)
    {
        foreach (EncounterResource encounter in EncounterDatabase.All)
        {
            if (encounter.EnemyTemplateId == templateId)
            {
                return true;
            }
        }

        foreach (WorldEventResource worldEvent in WorldEventDatabase.All)
        {
            if (worldEvent.Kind != WorldEventKind.Cache && worldEvent.EnemyTemplateId == templateId)
            {
                return true;
            }
        }

        return false;
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

    /// <summary>
    /// Story flags are the one id family with no database behind them, so nothing has ever checked
    /// them: a mistyped <c>HasFlag</c> arg is a gate that never opens, silently and permanently, and
    /// a mistyped <c>SetFlag</c> is a rank the hold never grants. Phase 34.5C's rank chain rests on
    /// them, so this closes the hole the only way a registry-less id can be closed — cross-reference
    /// the readers against the writers. A flag nothing writes is the typo that matters; the reverse
    /// (a flag set but never read) is legitimate, since flags are also a record of what happened.
    /// </summary>
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
