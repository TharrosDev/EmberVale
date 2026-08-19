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
using Embervale.Movement;
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
        ValidateQuestStringsAreKeys(issues);
        ValidateInteractIdsArePlaced(issues);
        ValidateDialogue(issues);
        ValidateSpells(issues);
        ValidateFactions(issues);
        ValidateEncounters(issues);
        ValidateWorldEvents(issues);
        ValidateRegions(issues);
        ValidateRaces(issues);
        ValidateLocale(issues);
        ValidateBreakdownKeys(issues);
        ValidateCompanions(issues);
        ValidateAIProfiles(issues);
        ValidateEnemyArchetypes(issues);
        ValidateBosses(issues);
        ValidateProperties(issues);
        ValidateItemTags(issues);
        ValidateShops(issues);
        ValidateCellTrade(issues);
        ValidateContrabandReachability(issues);
        ValidateEssentialsAreResident(issues);
        ValidateServices(issues);
        ValidateMount(issues);
        ValidateStepUp(issues);
        ValidateTolls(issues);
        ValidatePlaceables(issues);
        ValidateMapLocations(issues);
        ValidateSceneAuthoredIds(issues);
        ValidateBestiary(issues);
        ValidateResourcePaths(issues);
        ValidateUiAssets(issues);
    }

    /// <summary>
    /// Every line a price breakdown can print (Phase 38U).
    ///
    /// ⚠️ <b>It walks <see cref="PriceBreakdown.AllKeys"/> rather than the authored shops, and that is
    /// the whole reason it is worth writing.</b> A shock line, a glutted stack and a broker's cut are
    /// all unreachable at the town square — a rule that priced the realm's shops and checked the keys
    /// it happened to emit would pass while three tooltips showed a raw key to the one player who
    /// walked to the coast during a shortage. The declared set is the contract; the reachable set is
    /// today's accident.
    ///
    /// A missing row is invisible in every other check: <see cref="Loc.T"/> returns the key itself, so
    /// the tooltip renders <c>shop.line.glut</c> in the player's face and nothing logs a thing.
    /// </summary>
    private static void ValidateBreakdownKeys(List<string> issues)
    {
        foreach (string key in Economy.PriceBreakdown.AllKeys)
        {
            if (!Loc.Has(key))
            {
                issues.Add(
                    $"price breakdown line '{key}' is missing from the locale catalogue — the tooltip " +
                    "would show the raw key");
            }
        }
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

    /// <summary>
    /// Every contraband good has somewhere to go (Phase 38O) — 38M's toll-flag reachability rule in a
    /// different hat, and needed for the same reason.
    ///
    /// <see cref="TradeTags.Accepts"/> makes contraband the one tag that fails <em>closed</em>: an item
    /// wearing it is refused everywhere except a shop that names <c>contraband</c> explicitly. So a
    /// realm with no fence, or a fence whose accepted list loses the word to a typo, turns every
    /// contraband item into permanently unsellable loot — and the symptom the player sees is a merchant
    /// refusing something with a straight face, which reads as the feature being broken rather than as
    /// the data being wrong.
    ///
    /// Checked as a <b>union across every shop</b>, not per shop: one fence anywhere in the realm is
    /// enough, which is exactly the shape <c>ValidateRecipeReachability</c> and the toll-flag rule use.
    /// </summary>
    private static void ValidateContrabandReachability(List<string> issues)
    {
        var fences = new List<string>();
        foreach (ShopResource shop in ShopDatabase.All)
        {
            if (TradeTags.IsFence(shop.AcceptedTagList()))
            {
                fences.Add(shop.Id);
            }
        }

        if (fences.Count > 0)
        {
            return;
        }

        foreach (KeyValuePair<string, ItemResource> entry in ItemDatabase.All)
        {
            if (TradeTags.IsContraband(entry.Value.TagList()))
            {
                issues.Add($"item '{entry.Value.Id}' is contraband but no shop in the realm accepts " +
                    "'contraband' — it can never be sold to anyone, anywhere");
            }
        }
    }

    /// <summary>
    /// A consignment house is usable (Phase 38P). Four rules, each of which passes as well-formed data
    /// and reads in game as the broker being broken rather than misconfigured.
    ///
    /// ⚠️ Every one of them is gated on <see cref="ShopResource.IsConsignment"/>, so the whole block is
    /// inert for the twenty-two shops authored before 38P — the field arrives without touching one of
    /// them, the way <c>InvestmentTiers</c> did in 38I.
    ///
    /// ⚠️ They are kept separable (38L's finding, restated in 38O): each reads a different field, so a
    /// break in one cannot trip another and every negative test names exactly the rule under it.
    /// </summary>
    private static void ValidateShopConsignment(ShopResource shop, List<string> issues)
    {
        if (!shop.IsConsignment)
        {
            return;
        }

        string id = shop.Id;

        if (shop.ConsignDays < 1)
        {
            issues.Add(
                $"consignment shop '{id}' sells a listing in {shop.ConsignDays} days — a non-positive " +
                "period never matures (ShopStock.IsRestockDue), so the player could never collect");
        }

        // A broker who pays no better than a counter is content nobody would ever walk to. The realm's
        // most generous sell fraction is 0.62 (38N1); a broker is authored above every one of them.
        if (shop.ConsignFraction <= shop.SellFraction)
        {
            issues.Add(
                $"consignment shop '{id}' lists at {shop.ConsignFraction} and buys outright at " +
                $"{shop.SellFraction} — waiting days for the same money or less is never worth doing");
        }

        if (shop.ConsignCommission < 0f || shop.ConsignCommission >= 1f)
        {
            issues.Add(
                $"consignment shop '{id}' takes a commission of {shop.ConsignCommission} — outside " +
                "0..1 it either pays the player a bonus for listing or keeps the whole sale");
        }

        // She never owns the goods, so the two things a counter needs are meaningless on her: a purse
        // she would never spend, and a shelf she has nothing to put on it. Authored, they read as a
        // merchant whose Buy tab is silently empty and whose purse never moves.
        if (shop.PurseGold > 0 || shop.StockList().Count > 0)
        {
            issues.Add(
                $"consignment shop '{id}' authors a purse or stock — a broker fronts no money and " +
                "sells nothing of her own, so neither would ever be read");
        }
    }

    /// <summary>
    /// A fence's two-sided cost is coherent (Phase 38O). Three ways to author it wrong, and every one
    /// of them is well-formed data that reads in game as the penalty being broken rather than missing.
    ///
    /// ⚠️ The two rules are kept separable on purpose (38L's finding: a break that trips an earlier rule
    /// proves nothing about the one under test). A side is "authored" if <em>either</em> its faction or
    /// its delta is set, so clearing only the faction reaches the pairing rule, and clearing both
    /// reaches the completeness rule — one break, one message, each time.
    /// </summary>
    private static void ValidateShopContraband(ShopResource shop, List<string> issues)
    {
        string id = shop.Id;
        bool fence = TradeTags.IsFence(shop.AcceptedTagList());
        bool gain = !string.IsNullOrEmpty(shop.ContrabandFactionId) || shop.ContrabandDelta != 0;
        bool penalty = !string.IsNullOrEmpty(shop.ContrabandPenaltyFactionId) ||
            shop.ContrabandPenaltyDelta != 0;

        // A cost on a shop that will not take the goods can never fire — dead authoring that looks like
        // a working fence to anyone reading the resource.
        if (!fence && (gain || penalty))
        {
            issues.Add($"shop '{id}' authors a contraband standing cost but does not accept " +
                "'contraband' — nothing can ever trigger it");
        }

        // Two-sided means two sides. One-sided fencing is either a free reputation faucet or a pure
        // punishment, and both are the mechanic silently becoming something else.
        if (fence && !(gain && penalty))
        {
            issues.Add($"shop '{id}' fences contraband but authors only " +
                $"{(gain ? "the standing it gains" : penalty ? "the standing it costs" : "neither side")}" +
                " — the sale has to please somebody and offend somebody");
        }

        CheckContrabandSide(id, shop.ContrabandFactionId, shop.ContrabandDelta, gained: true, issues);
        CheckContrabandSide(
            id, shop.ContrabandPenaltyFactionId, shop.ContrabandPenaltyDelta, gained: false, issues);
    }

    /// <summary>One half of a fence's cost: the faction has to exist, the pair has to be complete, and
    /// the sign has to match which half it is. A "penalty" that raises standing is the sort of thing
    /// that only shows up as a player farming reputation off a merchant.</summary>
    private static void CheckContrabandSide(
        string id, string factionId, int delta, bool gained, List<string> issues)
    {
        string side = gained ? "gains" : "costs";

        if (delta != 0 && string.IsNullOrEmpty(factionId))
        {
            issues.Add($"shop '{id}' {side} {delta} standing on a fenced sale with no faction named — " +
                "the change would be dropped on the floor");
            return;
        }

        if (string.IsNullOrEmpty(factionId))
        {
            return;
        }

        if (FactionDatabase.Get(factionId) == null)
        {
            issues.Add($"shop '{id}' {side} standing with unknown faction '{factionId}'");
        }

        if (delta == 0)
        {
            issues.Add($"shop '{id}' names a faction it {side} standing with but moves it by 0");
        }
        else if (gained == delta < 0)
        {
            issues.Add($"shop '{id}' {side} standing by {delta} — the sign is backwards for the side " +
                "it is authored on");
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

            // ⚠️ A broker is the one shop whose window is SUPPOSED to open with nothing on the left
            // (38P): she sells nothing of her own, and the pack side is the whole interface. Note the
            // exemption is here rather than in ValidateShopConsignment, because this is the rule that
            // has to know about the exception — a second rule saying "unless" would leave two
            // authorities on when an empty shelf is a defect.
            if (stock.Count == 0 && shop.LeveledTable == null && !shop.IsConsignment)
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
            ValidateShopInvestment(shop, issues);
            ValidateShopHours(shop, issues);

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
            ValidateShopContraband(shop, issues);
            ValidateShopConsignment(shop, issues);

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

            ValidateShopHaggle(shop, issues);
            ValidateShopCell(shop, issues);

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

        ValidateConfiscationIsRecoverable(issues);
        ValidateConsignmentIsPayable(issues);
        ValidateContracts(issues);
    }

    /// <summary>
    /// The caravan board's postings (Phase 38Q2). Four rules on each contract plus one on the pool.
    ///
    /// ⚠️ <b>The reward rule runs the OPPOSITE way to 38Q's.</b> A commission is refused for being too
    /// <em>cheap</em>, because materials-in and goods-out could be looped. A contract is refused for
    /// being too <em>poor</em>: it is a one-shot per rotation, so nothing can be looped, and a posting
    /// paying less than a merchant already pays is strictly worse than selling — correct, saved,
    /// validated and pointless, which is the imperceptibility failure that got 38G parked. The bound
    /// on a contract is <c>ContractLedger</c>, never the price.
    /// </summary>
    private static void ValidateContracts(List<string> issues)
    {
        foreach (ContractResource contract in ContractDatabase.All)
        {
            string id = contract.Id;

            if (ItemDatabase.Get(contract.ItemId) is not { } item)
            {
                issues.Add($"contract '{id}' wants unknown item '{contract.ItemId}'");
                continue;
            }

            // A posting that ate a quest item would strand a Collect objective with no way to recover
            // it — the same refusal ShopPricing.Sellable makes at every counter in the realm, and the
            // reason it is a rule rather than a convention.
            if (item.Type == ItemType.Quest)
            {
                issues.Add(
                    $"contract '{id}' wants quest item '{contract.ItemId}' — handing it over would " +
                    "strand a Collect objective with no way to get it back");
            }

            if (contract.Quantity <= 0)
            {
                issues.Add($"contract '{id}' wants {contract.Quantity} of '{contract.ItemId}' — nothing to deliver");
            }

            if (contract.RewardGold <= 0)
            {
                issues.Add(
                    $"contract '{id}' pays {contract.RewardGold} gold — a posting nobody is paid for is " +
                    "a shop counter that takes the goods");
            }

            if (contract.ReputationDelta != 0 && FactionDatabase.Get(contract.FactionId) == null)
            {
                issues.Add(
                    $"contract '{id}' pays {contract.ReputationDelta} standing to unknown faction " +
                    $"'{contract.FactionId}' — the reward would be paid to nobody");
            }

            // The load-bearing one. A contract that pays less than a merchant already pays is a longer
            // walk for less money, and the player would only ever discover that by doing it.
            //
            // ⚠️ Asked at PriceView.Peak since 38T: a supply shock moves what the best buyer pays for a
            // few days at a time, so a posting that beats the shelf today can be beaten by the shelf on
            // Thursday. This is 38G's carried warning — "the demand table is a floor under other
            // people's rules" — made mechanical, and it is the same rule asked a harder question rather
            // than a second rule about shocks that would drift from this one.
            EconomyReport.BestBuyers(item, item.TagList(), out Offer best, out _, PriceView.Peak);
            long shelf = (long)best.Price * Mathf.Max(1, contract.Quantity);
            if (best.Has && contract.RewardGold <= shelf)
            {
                issues.Add(
                    $"contract '{id}' pays {contract.RewardGold} for {contract.Quantity}x " +
                    $"'{contract.ItemId}', and '{best.Shop}' already pays {shelf} over the counter — " +
                    $"the posting is strictly worse than selling: raise it above {shelf}");
            }
        }

        ValidateBoardsHaveEnoughPostings(issues);
    }

    /// <summary>
    /// A board must have more postings to draw from than it has slots (Phase 38Q2), or a rotation has
    /// to show one contract on two of them.
    ///
    /// Checked across the pool rather than per contract, the shape 38D's recipe reachability and 38M's
    /// toll flags both use: no single contract is wrong, it is the <em>count</em> that is, and nothing
    /// about one resource can see that. <c>ContractRules.SlotContract</c> keeps a rotation distinct by
    /// construction, so this is not protecting the arithmetic — it is protecting the board from being
    /// authored with nothing to rotate.
    /// </summary>
    private static void ValidateBoardsHaveEnoughPostings(List<string> issues)
    {
        int pool = ContractDatabase.All.Count;

        foreach (ServiceResource service in ServiceDatabase.All)
        {
            if (service.Kind != ServiceKind.Contracts)
            {
                continue;
            }

            if (service.BoardSlots > pool)
            {
                issues.Add(
                    $"service '{service.Id}' posts {service.BoardSlots} contracts and only {pool} " +
                    "are authored — a rotation would have to show the same posting twice");
            }
        }
    }

    /// <summary>
    /// A realm that can seize contraband can also give it back (Phase 38O). The whole design rests on
    /// this: a fine is a decision the player can price, and a permanent seizure is a punishment they
    /// cannot — 38H's ruling against a hard cap, applied to property instead of to a payout.
    ///
    /// A union across every authored service, like the contraband-reachability rule and 38M's
    /// toll-flag one. Checked here rather than per resource because neither service is wrong on its
    /// own; it is the <em>absence</em> of the second one that breaks the promise, and nothing about
    /// the first can see that.
    /// </summary>
    private static void ValidateConfiscationIsRecoverable(List<string> issues)
    {
        var searches = new List<string>();
        bool redeems = false;

        foreach (ServiceResource service in ServiceDatabase.All)
        {
            if (service.Kind == ServiceKind.Search)
            {
                searches.Add(service.Id);
            }
            else if (service.Kind == ServiceKind.Redeem)
            {
                redeems = true;
            }
        }

        if (redeems)
        {
            return;
        }

        foreach (string id in searches)
        {
            issues.Add($"service '{id}' confiscates contraband but no impound counter exists to sell it " +
                "back — a seizure would be permanent, which is theft rather than a fine");
        }
    }

    /// <summary>
    /// A consignment listing can be paid out (Phase 38P) — 38O's recoverability rule in its second
    /// instance, and the shape generalises: <b>a system that takes something from the player must have
    /// the thing that gives it back authored somewhere in the realm</b>.
    ///
    /// A broker with no clerk anywhere is worse than a broken shop. She takes the goods, prices them
    /// generously, records the sale, and there is no counter in the world to hand the money over — so
    /// the player watches an item leave their pack and is paid for it never. Nothing about the shop
    /// resource can see that, which is why the rule lives out here as a union across services rather
    /// than beside the other four.
    /// </summary>
    private static void ValidateConsignmentIsPayable(List<string> issues)
    {
        bool collects = false;
        foreach (ServiceResource service in ServiceDatabase.All)
        {
            if (service.Kind == ServiceKind.Collect)
            {
                collects = true;
                break;
            }
        }

        if (collects)
        {
            return;
        }

        foreach (ShopResource shop in ShopDatabase.All)
        {
            if (shop.IsConsignment)
            {
                issues.Add($"shop '{shop.Id}' takes goods on consignment but no counter anywhere in the " +
                    "realm pays the earnings out — a listing would be an item given away for nothing");
            }
        }
    }

    /// <summary>
    /// The seam between what the navmesh lets an NPC climb and what a body can actually climb
    /// (Phase 39C).
    ///
    /// ⚠️ <b>This mismatch was live for the whole project and nothing could see it.</b> Every cell
    /// authors <c>agent_max_climb = 0.5</c>, and before 39C a <c>CharacterBody3D</c> climbed
    /// <c>floor_snap_length</c> — 0.1 m. So the navmesh happily baked NPC routes over ground the
    /// player could not follow them onto, and the only symptom was a townsman walking somewhere you
    /// could not. The cells were authored around it instead: <c>embermarket.tscn</c> deleted a 0.3 m
    /// dais over exactly this, and every ground slab in the realm is a collider-less skin.
    ///
    /// Now that <see cref="StepUp.MaxHeight"/> answers the same number, this keeps them answering it.
    /// The day someone raises a cell's <c>agent_max_climb</c> for a new piece of terrain, the bug
    /// comes back **silently** — a navmesh is not something anyone re-reads.
    ///
    /// ponytail: a regex over the scene text, exactly as <see cref="CollectSceneAuthoredFlags"/> does
    /// and for the same reason — the validator runs headless and must not instantiate cells to answer
    /// a content question.
    /// ⚠️ <b>Anchored to the start of a line</b>, because a <c>.tscn</c> header is prose in the same
    /// file: <c>embermarket.tscn</c> discusses <c>agent_max_climb</c> twice in comments, and an
    /// unanchored match reads those as settings — a cell would fail this gate over a sentence.
    /// </summary>
    private static void ValidateStepUp(List<string> issues)
    {
        foreach (string path in ScenePaths("res://scenes/regions/ember_crown"))
        {
            CheckAgentClimb(path, issues);
        }

        foreach (string path in ScenePaths("res://scenes/regions/frostfang_reach"))
        {
            CheckAgentClimb(path, issues);
        }
    }

    private static void CheckAgentClimb(string path, List<string> issues)
    {
        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return;
        }

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(
                     file.GetAsText(), @"(?m)^agent_max_climb = ([0-9.]+)"))
        {
            if (float.TryParse(
                    match.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float climb) &&
                climb > Movement.StepUp.MaxHeight)
            {
                issues.Add(
                    $"cell scene '{path}' bakes navigation with agent_max_climb {climb}, above the " +
                    $"{Movement.StepUp.MaxHeight} m a body can actually step — NPCs would be pathed " +
                    "onto ground the player cannot follow them onto");
            }
        }
    }

    /// <summary>
    /// The mount (Phase 39A), and specifically the seam between it and 38D.
    ///
    /// ⚠️ <b>Ownership is one string held in two files that never meet.</b> The stablemaster's
    /// <c>UnlockFlagId</c> is what the 400 gold buys; <see cref="MountComponent.OwnedFlagId"/> is
    /// what the whistle key reads. Nothing in the game dereferences both, so a rename on either side
    /// leaves a service that charges and a mount that never comes — and every test still passes,
    /// because each half is individually correct. That is exactly the failure a content gate is for.
    ///
    /// The model check is the same rule the enemy archetypes get, for the same reason: a missing
    /// model is silent, and here it would summon an invisible horse.
    /// ⚠️ <b>It catches a wrong path, not a deleted file</b> — proved by trying both. Moving the
    /// <c>.glb</c> out of the tree leaves <c>--validate</c> passing, because the <c>.import</c>
    /// sidecar and the cached <c>.scn</c> still satisfy <c>ResourceLoader.Exists</c>. The archetype
    /// rule this copies has always had that hole; naming it is cheaper than a fake proof.
    /// </summary>
    private static void ValidateMount(List<string> issues)
    {
        if (!ResourceLoader.Exists(MountComponent.MountModelPath))
        {
            issues.Add($"mount model resource missing: {MountComponent.MountModelPath}");
        }

        bool granted = false;
        foreach (ServiceResource service in ServiceDatabase.All)
        {
            if (service.Kind == ServiceKind.Stable)
            {
                granted |= service.UnlockFlagId == MountComponent.OwnedFlagId;
            }
        }

        if (!granted)
        {
            issues.Add(
                $"no stable service grants '{MountComponent.OwnedFlagId}', which is the flag " +
                "MountComponent reads as proof of ownership — the mount would be unreachable in play");
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

            case ServiceKind.Search:
                // A search the player can be too poor to undergo is a search that lets a broke smuggler
                // through, and ServiceRules refuses anything unaffordable before the verb ever runs.
                if (service.PriceGold != 0)
                {
                    issues.Add(
                        $"service '{id}' is a warden's search priced at {service.PriceGold} gold — a " +
                        "search is not sold, and an unaffordable one waves the contraband through");
                }

                if (service.UnlockFlagId.Length > 0 || service.GrantedFlagId.Length > 0)
                {
                    issues.Add(
                        $"service '{id}' is a search with a flag authored on it — nothing reads it, " +
                        "because a search is repeatable by nature and answers 'nothing to do' from the " +
                        "player's pack instead");
                }

                break;

            case ServiceKind.Redeem:
                // PriceGold is the per-unit fine here, not the bill (ContrabandLaw.Fine). Zero means a
                // free redemption, which makes every confiscation a brief inconvenience.
                if (service.PriceGold <= 0)
                {
                    issues.Add(
                        $"service '{id}' redeems impounded goods for nothing — the fine is the only cost " +
                        "confiscation has, so a free counter makes the search inert");
                }

                if (service.UnlockFlagId.Length > 0 || service.GrantedFlagId.Length > 0)
                {
                    issues.Add(
                        $"service '{id}' is an impound counter with a flag authored on it — nothing " +
                        "reads it, and a one-off receipt would make the second confiscation permanent");
                }

                break;

            case ServiceKind.Collect:
                // The broker's commission is the only cut consignment takes. A fee to be handed money
                // already owed is a second one, charged on the player's own gold, and ServiceRules
                // would refuse the payout outright to anyone too broke to pay it — so a player whose
                // only money is on the shelf could never get at it.
                if (service.PriceGold != 0)
                {
                    issues.Add(
                        $"service '{id}' charges {service.PriceGold} gold to collect consignment " +
                        "earnings — the commission is already taken, and a player with no coin but a " +
                        "full shelf could never afford to be paid");
                }

                if (service.UnlockFlagId.Length > 0 || service.GrantedFlagId.Length > 0)
                {
                    issues.Add(
                        $"service '{id}' is a consignment counter with a flag authored on it — nothing " +
                        "reads it, and a one-off receipt would make every later listing unpayable");
                }

                break;

            case ServiceKind.Appraise:
                // Third instance of 38O's priced-search rule, and the sharpest: ServiceRules refuses
                // an unaffordable service before the verb runs, so a fee here fails closed on the
                // player with an empty purse and a full pack — exactly the person who walked over to
                // ask what is worth carrying. An appraisal that only the rich can buy is not a sink,
                // it is a lock on the one screen that explains the economy.
                if (service.PriceGold != 0)
                {
                    issues.Add(
                        $"service '{id}' charges {service.PriceGold} gold to appraise — a valuation is " +
                        "the advice a broke player most needs, and ServiceRules would refuse it to them");
                }

                if (service.UnlockFlagId.Length > 0 || service.GrantedFlagId.Length > 0)
                {
                    issues.Add(
                        $"service '{id}' is an appraiser with a flag authored on it — nothing reads it, " +
                        "and a one-off receipt would make every later visit silently do nothing");
                }

                break;

            case ServiceKind.Passage:
                // A permit records itself in UnlockFlagId; a bribe grants a consumable pass in
                // GrantedFlagId. With neither, the gold is taken and the road stays shut — and the
                // player has no way to tell that from having been robbed.
                if (service.UnlockFlagId.Length == 0 && service.GrantedFlagId.Length == 0)
                {
                    issues.Add(
                        $"service '{id}' sells passage but grants no flag — it would take gold and open " +
                        "nothing, because a toll is only ever read through a flag");
                }

                // The standing cost is the honest half of a bribe's price. Authored without a faction
                // it lands nowhere, and the cheap crossing quietly becomes strictly better than the
                // permit rather than a trade.
                if (service.ReputationDelta != 0 && service.FactionId.Length == 0)
                {
                    issues.Add(
                        $"service '{id}' moves standing by {service.ReputationDelta} with no faction — " +
                        "the cost would be charged to nobody");
                }

                break;

            case ServiceKind.Commission:
                ValidateCommission(service, id, issues);
                break;

            case ServiceKind.Contracts:
                // 38O's priced-service rule, fourth instance and the plainest: the player is being
                // PAID at a board. Note this is the opposite ruling to Commission directly above,
                // which must be priced because it hands over goods — the two cases sit together so
                // nobody reading one applies it to the other.
                if (service.PriceGold != 0)
                {
                    issues.Add(
                        $"service '{id}' charges {service.PriceGold} gold to read a contract board — " +
                        "the board pays the player, and ServiceRules would refuse it to anyone too " +
                        "broke to be given work");
                }

                if (service.UnlockFlagId.Length > 0 || service.GrantedFlagId.Length > 0)
                {
                    issues.Add(
                        $"service '{id}' is a contract board with a flag authored on it — nothing " +
                        "reads it, and a one-off receipt would close the board after one rotation");
                }

                if (service.BoardSlots < 1)
                {
                    issues.Add($"service '{id}' posts {service.BoardSlots} contracts — its board would be empty");
                }

                if (service.RotationDays < 1)
                {
                    issues.Add(
                        $"service '{id}' rotates every {service.RotationDays} days — the rotation is the " +
                        "only deadline a contract has, so a board that never turns is a board that is " +
                        "finished once each posting is filled");
                }

                break;

            case ServiceKind.Wager:
                ValidateWager(service, id, issues);
                break;

            case ServiceKind.Mercenary:
                // 38Q's priced-service ruling, second instance and for its exact reason: this hands
                // over something — a sword that fights for you — rather than advice. And a free
                // companion is what DialogueEffect.RecruitCompanion already does, which is how Kael
                // joins. A free hire is therefore not a generous mercenary, it is a story recruit with
                // the story removed: the plumbing kept and the feature deleted.
                if (service.PriceGold <= 0)
                {
                    issues.Add(
                        $"service '{id}' hires a sword for {service.PriceGold} gold — the coin is the " +
                        "only thing separating a mercenary from RecruitCompanion, which already " +
                        "recruits for free when a story has earned it");
                }

                if (CompanionDatabase.Get(service.CompanionId) == null)
                {
                    issues.Add(
                        $"service '{id}' hires unknown companion '{service.CompanionId}' — the press " +
                        "would take nothing and produce nobody");
                }

                // The roster is the record and it persists. A flag beside it is a second record of the
                // same fact that a dismissal does not clear, so the hire would be permanently retired
                // by having once been made — the already-held test asks the roster for that reason.
                if (service.UnlockFlagId.Length > 0 || service.GrantedFlagId.Length > 0)
                {
                    issues.Add(
                        $"service '{id}' is a mercenary with a flag authored on it — the roster already " +
                        "records the hire, and a flag would survive a dismissal and retire her for good");
                }

                break;
        }
    }

    /// <summary>
    /// A gambling house (Phase 38R2). Five rules, and the last is the only thing in the battery
    /// standing between the economy and a **tap** rather than a broken reference.
    ///
    /// ⚠️ <b>A wager is the first price in the game that can pay the player MORE than it took</b>, and
    /// the <c>ShopPricing</c> clamps have nothing to say about it — it is not a spread over an item's
    /// value at all. What keeps it a sink is the authored expectation, so that is what is checked, and
    /// checked at the standing where it is worst.
    /// </summary>
    private static void ValidateWager(ServiceResource service, string id, List<string> issues)
    {
        if (service.PriceGold <= 0)
        {
            issues.Add(
                $"service '{id}' stakes {service.PriceGold} gold — a free throw at a paying table is a " +
                "gift with a delay, and the stake is the whole of what makes this a game");
        }

        if (service.WinPercent < 1 || service.WinPercent > 99)
        {
            issues.Add(
                $"service '{id}' wins {service.WinPercent}% of the time — a house that never pays and a " +
                "house that always does are both something other than a game");
        }

        if (service.PlaysPerDay < 1)
        {
            issues.Add(
                $"service '{id}' allows {service.PlaysPerDay} throws a day — the table would be shut, " +
                "and the daily allowance is the only bound this feature has");
        }

        if (service.PayoutGold <= service.PriceGold)
        {
            issues.Add(
                $"service '{id}' pays {service.PayoutGold} on a win against a {service.PriceGold} stake — " +
                "a win that returns less than it took is a loss with congratulations on it");
        }

        if (service.UnlockFlagId.Length > 0 || service.GrantedFlagId.Length > 0)
        {
            issues.Add(
                $"service '{id}' is a gambling house with a flag authored on it — nothing reads it, and " +
                "a one-off receipt would close the table after a single throw");
        }

        // ⚠️ THE LOAD-BEARING ONE, and it is 38Q's trap in a place with no clamps to fall back on.
        // PriceOf runs the stake through ShopPricing.ServicePrice, so an Allied player stakes 15% less
        // against a payout that does not move — a house authored as a sink at Neutral can be a printer
        // at the top of the ramp, and only the discounted stake shows it.
        int cheapestStake = ShopPricing.ServicePrice(service.PriceGold, ReputationTier.Allied);
        if (WagerRules.Exploitable(cheapestStake, service.WinPercent, service.PayoutGold))
        {
            issues.Add(
                $"service '{id}' pays {service.PayoutGold} at {service.WinPercent}% against a stake of " +
                $"{cheapestStake} at best standing — that expects to make the player money, so the " +
                "table is a tap bounded only by its throws a day; drop the payout below " +
                $"{cheapestStake * 100 / Mathf.Max(1, service.WinPercent)}");
        }
    }

    /// <summary>
    /// A master's commission counter (Phase 38Q). Four rules, and the last of them is the only thing
    /// in the battery guarding an <b>unbounded gold loop</b> rather than a broken reference.
    ///
    /// ⚠️ <b>The free-service rule that fired three times running does not apply here, and this comment
    /// is the reason nobody should "tidy" it into place.</b> 38O's search, 38P's collect counter and
    /// 38P2's appraiser are all forced free because <see cref="ServiceRules"/> refuses an unaffordable
    /// service before the verb runs, so a fee would fail closed on exactly the player who needs it. A
    /// commission is the opposite case: it <em>hands over goods</em>, so a free one is the realm's
    /// materials shop with the spread deleted.
    /// </summary>
    private static void ValidateCommission(ServiceResource service, string id, List<string> issues)
    {
        if (service.CommissionStation == CraftingStationType.Hand)
        {
            issues.Add(
                $"service '{id}' commissions at the Hand station — hand recipes craft anywhere, so the " +
                "counter would charge for something the player can do standing still");
        }

        if (service.PriceGold <= 0)
        {
            issues.Add(
                $"service '{id}' commissions for {service.PriceGold} gold — a master who charges no " +
                "labour hands out materials at his own cost, which is the shop spread deleted");
        }

        if (service.UnlockFlagId.Length > 0 || service.GrantedFlagId.Length > 0)
        {
            issues.Add(
                $"service '{id}' is a commission counter with a flag authored on it — nothing reads it, " +
                "and a one-off receipt would make every later order silently do nothing");
        }

        if (ShopDatabase.Get(service.MaterialsShopId) is not { } shop)
        {
            issues.Add(
                $"service '{id}' prices its materials from unknown shop '{service.MaterialsShopId}' — " +
                "without one there is nothing to charge for what the master supplies, which is the " +
                "whole of what he sells");
            return; // every rule below prices through that shop
        }

        var any = false;
        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            if (!CraftingComponent.StationAccepts(recipe.Station, service.CommissionStation) ||
                ItemDatabase.Get(recipe.OutputItemId) is not { } output)
            {
                continue;
            }

            any = true;

            // ⚠️ THE LOAD-BEARING ONE. Buy every material from the master, have him make the piece,
            // sell it — that is an unbounded loop with no cooldown, and unlike every earlier price in
            // the economy the ShopPricing clamps cannot close it: a commission spans two different
            // items, and crafting is meant to add value. Only the labour fee closes it.
            //
            // Checked at its cheapest: Allied standing takes 15% off the buy side (38C), and the sell
            // side does not move with standing at all, so this is the corner the loop opens at first.
            // ⚠️ 38G widened this corner twice over. The materials are priced at the master's OWN
            // settlement (cheapest where they are a surplus) while BestBuyers below now scans every
            // settlement for the keenest buyer — so the loop is asked across the whole demand band
            // rather than at one price. And 38S's haggle was missing from "cheapest standing" entirely.
            // ⚠️ 38T widens it once more, and in BOTH directions at once: the materials are quoted at
            // the cheapest a glut at the master's own settlement could ever make them, and the result at
            // the keenest a shortage anywhere could ever make a buyer. Neither end is a day the game
            // will necessarily roll — one shock runs at a cell at a time — but the loop has to be shut
            // on the worst pair of days rather than on a pairing nobody thought to simulate.
            int cost = EconomyReport.CommissionCost(
                recipe, shop, ReputationTier.Allied, pack: null, service.PriceGold,
                haggled: shop.HaggleChance > 0, view: PriceView.Trough);
            EconomyReport.BestBuyers(output, output.TagList(), out Offer best, out _, PriceView.Peak);

            if (best.Has && CommissionRules.Exploitable(cost, best.Price, recipe.OutputQuantity))
            {
                issues.Add(
                    $"service '{id}' commissions '{recipe.Id}' for {cost} gold at best standing, and " +
                    $"'{best.Shop}' pays {best.Price} x{recipe.OutputQuantity} for the result — buy the " +
                    "materials, commission it, sell it, repeat: raise the labour fee above " +
                    $"{best.Price * Mathf.Max(1, recipe.OutputQuantity)}");
            }
        }

        if (!any)
        {
            issues.Add(
                $"service '{id}' commissions at {service.CommissionStation} and no authored recipe uses " +
                "that station — its window would open empty");
        }
    }

    /// <summary>
    /// A tolled crossing must be crossable (Phase 38M). A toll is read entirely through two story
    /// flags, and story flags have no database — so a typo in either one is a road that charges every
    /// time and can never be papered, which from inside the game looks like a bug in the portal rather
    /// than a bad string.
    ///
    /// The check is reachability as a <b>union</b>, the same shape 38D gave taught recipes: some
    /// authored <see cref="ServiceKind.Passage"/> must grant each flag the region names. Nothing else
    /// in the battery can catch this — <c>.tscn</c> is not scanned, so placing the warden is not proof
    /// he sells the right paper.
    /// </summary>
    private static void ValidateTolls(List<string> issues)
    {
        var granted = new HashSet<string>();
        foreach (ServiceResource service in ServiceDatabase.All)
        {
            if (service.Kind != ServiceKind.Passage)
            {
                continue;
            }

            if (service.UnlockFlagId.Length > 0)
            {
                granted.Add(service.UnlockFlagId);
            }

            if (service.GrantedFlagId.Length > 0)
            {
                granted.Add(service.GrantedFlagId);
            }
        }

        foreach (RegionResource region in RegionDatabase.All)
        {
            if (region.TollGold <= 0)
            {
                continue; // an untolled road, which is every region but the Crossway
            }

            foreach ((string label, string flag) in new[]
            {
                ("permit", region.TollPermitFlagId),
                ("pass", region.TollPassFlagId),
            })
            {
                if (flag.Length == 0)
                {
                    issues.Add(
                        $"region '{region.Id}' charges a {region.TollGold} gold toll with no {label} flag — " +
                        "there would be no way past it but paying, every time");
                }
                else if (!granted.Contains(flag))
                {
                    issues.Add(
                        $"region '{region.Id}' toll {label} flag '{flag}' is granted by no passage " +
                        "service — nothing in the world can sell the player past this gate");
                }
            }
        }
    }

    /// <summary>
    /// A shop's trading hours and its merchant's visit cycle (Phase 38J). Every rule here rejects a
    /// shop the player cannot reach — open for a window nobody can plan around, or on a cycle that
    /// never comes round. A merchant who is simply never there looks exactly like a broken interactable
    /// from inside the game, which is what makes this worth checking at author time.
    /// </summary>
    private static void ValidateShopHours(ShopResource shop, List<string> issues)
    {
        string id = shop.Id;

        if (shop.OpenHour is < 0 or > 23 || shop.CloseHour is < 0 or > 23)
        {
            issues.Add(
                $"shop '{id}' trades {shop.OpenHour}:00–{shop.CloseHour}:00 — an hour outside 0–23 is " +
                "wrapped by the arithmetic, so the authored window is not the one the player meets");
        }
        else if (shop.OpenHour != shop.CloseHour &&
            ShopHours.OpenSpanHours(shop.OpenHour, shop.CloseHour) < ShopHours.MinimumOpenSpan)
        {
            issues.Add(
                $"shop '{id}' is open only {ShopHours.OpenSpanHours(shop.OpenHour, shop.CloseHour)}h a " +
                $"day ({shop.OpenHour}:00–{shop.CloseHour}:00) — below {ShopHours.MinimumOpenSpan}h a " +
                "player has to plan a day around the window, which reads as the shop being broken");
        }

        if (shop.VisitEveryDays < 0)
        {
            issues.Add($"shop '{id}' has a negative visit cycle ({shop.VisitEveryDays} days)");
        }
        else if (shop.VisitEveryDays == 1)
        {
            issues.Add(
                $"shop '{id}' visits every 1 day, which is a resident merchant — author 0 and say so, " +
                "or the offset below silently becomes the only day he is ever absent");
        }
        else if (shop.VisitEveryDays > ShopHours.MaxVisitGap)
        {
            issues.Add(
                $"shop '{id}' visits one day in {shop.VisitEveryDays} — more than " +
                $"{ShopHours.MaxVisitGap} in-game days between visits is a wall rather than a cadence");
        }

        if (shop.VisitEveryDays > 0 &&
            (shop.VisitDayOffset < 0 || shop.VisitDayOffset >= shop.VisitEveryDays))
        {
            issues.Add(
                $"shop '{id}' arrives on day {shop.VisitDayOffset} of a {shop.VisitEveryDays}-day " +
                "cycle — that position never comes round, so the merchant never appears at all");
        }
    }

    /// <summary>
    /// The one closure in 38J that is a hard gate rather than a wait: an attrition supply sold only by
    /// a merchant who may not be in town. Hours are a wait — the inn advances the clock — but a
    /// travelling merchant is a coin flip against the calendar, and a player out of potions cannot
    /// sleep their way to one.
    ///
    /// Scoped to <see cref="ItemType.Consumable"/> deliberately. It is the existing enum member that
    /// already means "the thing you run out of mid-fight", so the rule needs no new authored marker and
    /// no judgement call about what counts as essential.
    /// </summary>
    private static void ValidateEssentialsAreResident(List<string> issues)
    {
        var travelling = new Dictionary<string, string>();   // item id -> a travelling shop that sells it
        var resident = new HashSet<string>();

        foreach (ShopResource shop in ShopDatabase.All)
        {
            foreach (ShopStockEntry entry in shop.StockList())
            {
                if (ItemDatabase.Get(entry.ItemId) is not { Type: ItemType.Consumable })
                {
                    continue;
                }

                if (shop.VisitEveryDays > 0)
                {
                    travelling.TryAdd(entry.ItemId, shop.Id);
                }
                else
                {
                    resident.Add(entry.ItemId);
                }
            }
        }

        foreach ((string itemId, string shopId) in travelling)
        {
            if (!resident.Contains(itemId))
            {
                issues.Add(
                    $"consumable '{itemId}' is sold only by travelling shop '{shopId}' — a player out " +
                    "of supplies cannot wait out a merchant who is not in town, so that is a hard gate " +
                    "rather than a closing time");
            }
        }
    }

    /// <summary>
    /// A shop's stock gates and its investment ladder (Phase 38I). Every rule here rejects data that is
    /// well-formed and buys the player nothing — a stake that grants no purse and unlocks no row, a row
    /// gated above the ladder that exists, a standing gate on a shop with no faction to have standing
    /// with. None of them can be seen in game: the shelf simply never opens, which reads as the feature
    /// being broken rather than as an authoring mistake.
    /// </summary>
    private static void ValidateShopInvestment(ShopResource shop, List<string> issues)
    {
        string id = shop.Id;
        List<ShopInvestmentTier> tiers = shop.InvestmentTierList();
        List<ShopStockEntry> stock = shop.StockList();

        int previousCost = 0;
        bool anyPurseBonus = false;
        for (int i = 0; i < tiers.Count; i++)
        {
            ShopInvestmentTier tier = tiers[i];

            if (tier.Cost <= 0)
            {
                issues.Add(
                    $"shop '{id}' investment rung {i + 1} costs {tier.Cost} — a free stake is not a sink");
            }
            else if (tier.Cost <= previousCost)
            {
                issues.Add(
                    $"shop '{id}' investment rung {i + 1} costs {tier.Cost}, no more than the rung below " +
                    $"it ({previousCost}) — a ladder that stops climbing is a mispriced ladder");
            }

            previousCost = tier.Cost;

            if (tier.PurseBonus > 0)
            {
                anyPurseBonus = true;

                // The bonus is added to an authored purse at each restock. Both of these make it a
                // silent no-op, which is the class of bug 38C's "markup bottoms out at Honored" rule
                // exists for: the arithmetic is safe, the data means nothing.
                if (shop.PurseGold <= 0)
                {
                    issues.Add(
                        $"shop '{id}' investment rung {i + 1} adds {tier.PurseBonus}g to a purse that is " +
                        "already unlimited — the player would be paying to make a bottomless merchant finite");
                }

                if (shop.RestockDays <= 0)
                {
                    issues.Add(
                        $"shop '{id}' investment rung {i + 1} adds {tier.PurseBonus}g to a purse that never " +
                        "refills — the bonus lands at a restock this shop does not have");
                }
            }
        }

        int deepest = 0;
        bool allGated = stock.Count > 0;
        foreach (ShopStockEntry entry in stock)
        {
            deepest = System.Math.Max(deepest, entry.RequiredInvestment);
            allGated &= entry.IsGated;

            if (entry.RequiredInvestment > tiers.Count)
            {
                issues.Add(
                    $"shop '{id}' row '{entry.ItemId}' needs {entry.RequiredInvestment} investment rung(s) " +
                    $"and the shop sells {tiers.Count} — that stock can never be bought");
            }

            // The window falls back to Neutral when a shop authors no faction (the inverted fail-safe a
            // half-built world needs), so a gate above Neutral there is a shelf that never opens.
            if (entry.RequiredTier > ReputationTier.Neutral && shop.FactionId.Length == 0)
            {
                issues.Add(
                    $"shop '{id}' row '{entry.ItemId}' needs {entry.RequiredTier} standing and the shop " +
                    "prices by no faction — the player has no standing to earn, so it never unlocks");
            }
        }

        if (tiers.Count > 0 && !anyPurseBonus && deepest == 0)
        {
            issues.Add(
                $"shop '{id}' sells a stake that grants no purse and unlocks no row — the player would be " +
                "paying for nothing");
        }

        // The gated cousin of the "stocks nothing" rule: every row locked is a window that opens empty
        // for a player who has just walked in, which is exactly how the shop reads as broken.
        if (allGated && shop.LeveledTable == null)
        {
            issues.Add(
                $"shop '{id}' gates every one of its {stock.Count} row(s) — its window opens empty for a " +
                "new player, who has no way to learn there is anything behind the gates");
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
        //
        // ⚠️ 38S folds the day's haggle into both sides for a haggling merchant, and it is the first
        // multiplier in the arc that moves the SELL side — so this band tightens from a ratio of about
        // 0.52 to about 0.42 for those shops, and a spread authored at the old edge would now be
        // reported. That is the rule working: a merchant who can be talked down on both halves of the
        // spread is the one round trip in the game that could reach zero.
        const float Margin = 1.25f;
        bool haggles = shop.HaggleChance > 0;
        float widestSell = ShopPricing.SellFractionFor(
            shop.SellFraction, specialty: true, haggled: haggles);
        float narrowestBuy = ShopPricing.MarkupFor(
            shop.BuyMarkup, Factions.ReputationTier.Allied, specialty: true, haggled: haggles);

        if (widestSell * Margin > narrowestBuy)
        {
            issues.Add($"shop '{id}' has too thin a spread once the specialty premium applies: it could " +
                $"pay {widestSell:0.00}x an item's value while charging as little as {narrowestBuy:0.00}x " +
                "for it — buying and selling back would cost the player almost nothing");
        }
    }

    /// <summary>
    /// Where a counter stands, and therefore what things are worth at it (Phase 38G). One rule, and it
    /// exists because the failure is <b>silent and looks like balance</b>: a shop whose
    /// <c>CellId</c> resolves to nothing prices at the realm reference, which is a perfectly ordinary
    /// set of numbers. Nobody would think to check.
    /// </summary>
    private static void ValidateShopCell(ShopResource shop, List<string> issues)
    {
        if (shop.CellId.Length > 0 && World.RegionDatabase.Cell(shop.CellId) is null)
        {
            issues.Add(
                $"shop '{shop.Id}' stands in unknown cell '{shop.CellId}' — local surplus and demand " +
                "would silently not apply and its prices would read as the realm average");
        }
    }

    /// <summary>
    /// A settlement's surplus and demand (Phase 38G). Three rules over the cells rather than the shops,
    /// because the tags are authored on the place — and the third one is the sub-phase's own parking
    /// notice made mechanical: <b>an authored place nobody prices from is invisible.</b>
    /// </summary>
    private static void ValidateCellTrade(List<string> issues)
    {
        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource cell in region.Cells)
            {
                List<string> surplus = Tags(cell.Surplus);
                List<string> demand = Tags(cell.Demand);

                foreach (string tag in surplus)
                {
                    if (!TradeTags.IsKnown(tag))
                    {
                        issues.Add($"cell '{cell.Id}' has surplus in unknown trade tag '{tag}'");
                    }

                    if (demand.Contains(tag))
                    {
                        issues.Add(
                            $"cell '{cell.Id}' is both awash in and short of '{tag}' — the value would " +
                            "be resolved as a surplus and the demand half would silently do nothing");
                    }
                }

                foreach (string tag in demand)
                {
                    if (!TradeTags.IsKnown(tag))
                    {
                        issues.Add($"cell '{cell.Id}' has demand for unknown trade tag '{tag}'");
                    }
                }

                ValidateCellShocks(cell, surplus, demand, issues);

                // 38G's parking notice, mechanised: demand that no counter reads is a multiplier the
                // player can never meet — correct, validated and completely imperceptible.
                if ((surplus.Count > 0 || demand.Count > 0 || cell.ShockTags.Count > 0) && !AnyShopIn(cell.Id))
                {
                    issues.Add(
                        $"cell '{cell.Id}' authors surplus or demand and no shop stands in it — no " +
                        "price in the game reads those tags, so the settlement's economy is invisible");
                }
            }
        }
    }

    /// <summary>
    /// What a settlement's fortunes can turn on (Phase 38T). Three rules, and the second is the one
    /// worth having: <b>a shock that cannot invert the cell's authored lists is an event that changes no
    /// price</b>, and it fails silently — the notice appears on the board, the days count down, and
    /// every number in the game is the number it was yesterday. <c>SupplyShockRules.Roll</c> already
    /// refuses to roll one, so without this rule the authoring mistake is a candidate that simply never
    /// fires, which is indistinguishable from a run of quiet days.
    /// </summary>
    private static void ValidateCellShocks(
        RegionCellResource cell, List<string> surplus, List<string> demand, List<string> issues)
    {
        List<string> candidates = Tags(cell.ShockTags);
        var seen = new HashSet<string>();

        // A shocked cell is named on the caravan board, and Loc falls back to printing the key — so
        // without this the board posts "cell.ember_crown.emberdeep_mine is short of raw ore" and looks
        // like a bug in the board rather than a missing row in a CSV.
        if (candidates.Count > 0 && !Loc.Has($"cell.{cell.Id}"))
        {
            issues.Add(
                $"cell '{cell.Id}' can be shocked but has no 'cell.{cell.Id}' locale row — the caravan " +
                "board would post the raw key as the settlement's name");
        }

        foreach (string tag in candidates)
        {
            if (!TradeTags.IsKnown(tag))
            {
                issues.Add($"cell '{cell.Id}' can be shocked in unknown trade tag '{tag}'");
                continue;
            }

            if (!seen.Add(tag))
            {
                issues.Add(
                    $"cell '{cell.Id}' lists '{tag}' twice as a shock candidate — a fair would move it " +
                    "once and the duplicate would only weight the roll");
            }

            // A tag that is a surplus here can still become a shortage and vice versa; what cannot
            // happen is both, so a tag in neither list is always legal and a tag in one is legal for the
            // shock that inverts it. Only a tag the cell somehow authors in both is dead on arrival —
            // and that pairing is already refused above, so what is left to catch is the fair.
            if (surplus.Contains(tag) && demand.Contains(tag))
            {
                issues.Add(
                    $"cell '{cell.Id}' can be shocked in '{tag}', which it authors as both a surplus " +
                    "and a demand — no shock could invert that, so the candidate would never fire");
            }
        }

        // A fair floods every candidate into surplus at once, so a cell whose candidates are ALL already
        // a surplus can roll a fair that does nothing. Roll refuses it; this says so at author time.
        if (candidates.Count > 0 && AllIn(candidates, surplus))
        {
            issues.Add(
                $"cell '{cell.Id}' lists only goods it is already awash in as shock candidates — every " +
                "glut and every fair there would change no price at all");
        }
    }

    private static bool AllIn(List<string> tags, List<string> list)
    {
        foreach (string tag in tags)
        {
            if (!list.Contains(tag))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AnyShopIn(string cellId)
    {
        foreach (ShopResource shop in ShopDatabase.All)
        {
            if (shop.CellId == cellId)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> Tags(Godot.Collections.Array<string> tags)
    {
        var list = new List<string>();
        foreach (string tag in tags)
        {
            if (!string.IsNullOrEmpty(tag))
            {
                list.Add(tag);
            }
        }

        return list;
    }

    /// <summary>
    /// A merchant's willingness to be talked down (Phase 38S). Four rules, and three of them are about
    /// something the arithmetic cannot see: <c>ShopPricing</c>'s clamps make a haggle incapable of
    /// inverting the spread, so what is left to check is whether the authored risk can land and whether
    /// the authored discount is visible.
    ///
    /// ⚠️ <b>Every question here is asked at ALLIED, not Neutral</b> — 38R2's carried lesson. A haggle
    /// stacks with the standing ramp, so the cheapest the player can ever be charged is the only
    /// interesting case, and it is the one no play-test at Neutral would ever reach.
    /// </summary>
    private static void ValidateShopHaggle(ShopResource shop, List<string> issues)
    {
        string id = shop.Id;

        if (shop.HaggleChance < 0 || shop.HaggleChance > 100)
        {
            issues.Add($"shop '{id}' has a haggle chance of {shop.HaggleChance} — it is a percentage");
        }

        if (shop.HaggleChance <= 0)
        {
            return; // a merchant who does not negotiate; nothing below applies
        }

        // The wager's FactionId rule with the sign flipped (38R2): there, no faction meant the discount
        // could never apply; here it means the PENALTY can never land, so the player negotiates for free.
        if (shop.FactionId.Length == 0)
        {
            issues.Add(
                $"shop '{id}' haggles but prices by no faction — the standing penalty would land " +
                "nowhere, so the attempt costs the player nothing and is free money");
        }

        if (shop.HaggleDelta >= 0)
        {
            issues.Add(
                $"shop '{id}' haggles with a standing delta of {shop.HaggleDelta} — a failed " +
                "negotiation must cost something, or asking is strictly better than not asking");
        }

        // A broker never touches ShopPricing.SellFractionFor (38P): her rows are priced by
        // ConsignmentRules, so a haggle would move her buy side, silently miss her shelf, and read in
        // game as the discount half-working.
        if (shop.IsConsignment)
        {
            issues.Add(
                $"shop '{id}' is a consignment house and cannot be haggled — a broker's payout comes " +
                "from ConsignmentRules, which the deal never reaches");
        }

        // The 38C bottoming-out rule, asked with the deal struck. A thin markup discounted twice hits
        // BuyPrice's >= 1 clamp, and then the negotiation the player just won changes no number at all.
        if (ShopPricing.MarkupFor(shop.BuyMarkup, ReputationTier.Allied, haggled: true) <= 1f)
        {
            issues.Add(
                $"shop '{id}' markup {shop.BuyMarkup} bottoms out once a haggle and Allied standing " +
                "both apply — the negotiation the player won would change no price on the shelf");
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

    private static void RequireMapLocation(string id, string context, List<string> issues)
    {
        if (string.IsNullOrEmpty(id))
        {
            issues.Add($"{context} has an empty map location id");
        }
        else if (World.MapLocationDatabase.Get(id) == null)
        {
            issues.Add($"{context} references unknown map location '{id}'");
        }
    }

    private static void RequireDialogue(string id, string context, List<string> issues)
    {
        if (string.IsNullOrEmpty(id))
        {
            issues.Add($"{context} has an empty dialogue id");
        }
        else if (DialogueDatabase.Get(id) == null)
        {
            issues.Add($"{context} references unknown dialogue '{id}'");
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

    /// <summary>
    /// Every player-facing string on a quest must be a key that exists in the locale catalogue (41A).
    ///
    /// ⚠️ <b>THIS IS THE DEFECT CLASS THAT LOOKS CORRECT ON SCREEN AND IS WRONG IN THE FILE.</b>
    /// Twelve of the fourteen quests author keys (<c>Title = "quest.warband.bounty.title"</c>); two —
    /// <c>GatherIron</c> and the since-deleted <c>CullTheGoblins</c> — authored <b>literal English</b>.
    /// Nothing caught it for twenty-nine phases because <c>Loc.T</c> returns the key unchanged on a
    /// miss, so <c>Loc.T("Gather Iron")</c> renders "Gather Iron" and the quest looks perfect. It is
    /// wrong the day a second locale ships, and <c>quest.gather_iron</c> is <b>live</b> — the Elder
    /// hands it out. Same family as invariant 33: a value can be wrong and nothing will ever say so.
    ///
    /// Checking <em>presence in the catalogue</em> rather than "looks dotted" is what makes this worth
    /// having: it catches a mistyped key too, which is the failure that survives a rename.
    /// </summary>
    private static void ValidateQuestStringsAreKeys(List<string> issues)
    {
        HashSet<string>? keys = LocaleKeys();
        if (keys == null)
        {
            return;
        }

        foreach (QuestResource quest in QuestDatabase.All)
        {
            Require(quest.Title, "title", issues, keys, quest.Id);
            Require(quest.Summary, "summary", issues, keys, quest.Id);

            List<ObjectiveResource> objectives = quest.ObjectiveList();
            for (int i = 0; i < objectives.Count; i++)
            {
                // Empty is refused too, not merely unlocalized: ObjectiveResource.ShortLabel falls back
                // to a generic when Description is missing, so an unauthored objective would render as
                // the word "objective" to the player. Authoring the line is the rule.
                Require(objectives[i].Description, $"objective {i} description", issues, keys, quest.Id);
            }
        }

        static void Require(string value, string what, List<string> issues, HashSet<string> keys, string questId)
        {
            if (string.IsNullOrEmpty(value))
            {
                issues.Add($"quest '{questId}' {what} is empty (author a locale key)");
            }
            else if (!keys.Contains(value))
            {
                issues.Add($"quest '{questId}' {what} '{value}' is not a key in data/locale/strings.csv — " +
                           "quest text must be authored as a locale key, never as literal display text " +
                           "(Loc.T returns the key on a miss, so literal English renders correctly and " +
                           "breaks silently on the first non-English locale)");
            }
        }
    }

    /// <summary>The default-locale key set from the catalogue, or null when it cannot be read (which
    /// <see cref="ValidateLocale"/> already reports — no need to say it twice).</summary>
    private static HashSet<string>? LocaleKeys()
    {
        const string path = "res://data/locale/strings.csv";
        if (!FileAccess.FileExists(path))
        {
            return null;
        }

        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return null;
        }

        Dictionary<string, Dictionary<string, string>> byLocale = LocCatalog.Parse(file.GetAsText());
        return byLocale.TryGetValue(Loc.DefaultLocale, out Dictionary<string, string>? messages)
            ? new HashSet<string>(messages.Keys)
            : null;
    }

    /// <summary>Shortest deadline a timed quest may author (41C). A floor under the authoring
    /// mistake, not a balance number.</summary>
    private const int MinimumDeadlineSeconds = 30;

    /// <summary>Shortest hold a <see cref="ObjectiveType.Defend"/> objective may author (41B). Not a
    /// balance number — a floor under the authoring mistake of leaving RequiredCount at its default
    /// of 1 on a type whose count is measured in seconds.</summary>
    private const int MinimumHoldSeconds = 10;

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

                    // 41A. Both new types name a row in a database rather than a template, so a typo
                    // is an objective that can never advance — silently, because nothing in the game
                    // ever looks the id up until the player is standing in the right place.
                    case ObjectiveType.Reach:
                        RequireMapLocation(objective.TargetId, $"quest '{quest.Id}' reach objective", issues);

                        // ⚠️ A Reach objective's TargetId IS its destination, so a LocationId beside
                        // it is either redundant or a contradiction — and a contradiction would send
                        // the compass and the map to two different places (invariant 5: one surface
                        // owns each fact).
                        if (objective.LocationId.Length > 0)
                        {
                            issues.Add($"quest '{quest.Id}' reach objective sets both TargetId " +
                                       $"'{objective.TargetId}' and LocationId '{objective.LocationId}' — " +
                                       "a reach objective's target is already its destination, so leave " +
                                       "LocationId empty");
                        }

                        break;
                    case ObjectiveType.Talk:
                        RequireDialogue(objective.TargetId, $"quest '{quest.Id}' talk objective", issues);
                        break;

                    // 41B. An escort names a person AND a destination, and it is the one objective
                    // type that needs both — the mirror of Reach, which refuses the second. Without
                    // LocationId there is nothing to measure the escortee against, so the objective
                    // can never advance and nothing at runtime would ever say why.
                    case ObjectiveType.Escort:
                        if (Companions.CompanionDatabase.Get(objective.TargetId) == null)
                        {
                            issues.Add($"quest '{quest.Id}' escort objective names unknown companion " +
                                       $"'{objective.TargetId}'");
                        }

                        if (objective.LocationId.Length == 0)
                        {
                            issues.Add($"quest '{quest.Id}' escort objective for '{objective.TargetId}' " +
                                       "sets no LocationId — an escort has to name where the escortee is " +
                                       "being taken, or it can never complete");
                        }

                        break;

                    // 41B. A hold's target is a place, like Reach's — but its RequiredCount is
                    // SECONDS rather than a tally, which is the one thing about this type that reads
                    // wrong at a glance. An unset count is the authoring default of 1, i.e. a
                    // quarter-second hold that completes before the player has stopped walking.
                    // 41C. The interact id has no database behind it — it is authored on a node in a
                    // cell scene — so this arm only checks that the objective names one at all, and
                    // ValidateInteractIdsArePlaced does the cross-reference in both directions.
                    case ObjectiveType.Interact:
                        if (objective.TargetId.Length == 0)
                        {
                            issues.Add($"quest '{quest.Id}' interact objective names no InteractId");
                        }

                        break;

                    // 41C. A stealth objective is a CONDITION, not a target: it fails on any enemy
                    // engaging, from anywhere, so an authored target would be a promise the rule does
                    // not keep ("undetected by goblins" reads as scoped and is not).
                    case ObjectiveType.Stealth:
                        if (objective.TargetId.Length > 0)
                        {
                            issues.Add($"quest '{quest.Id}' stealth objective sets TargetId " +
                                       $"'{objective.TargetId}' — a stealth condition targets nothing " +
                                       "and is blown by any enemy engaging, so leave it empty");
                        }

                        break;

                    case ObjectiveType.Defend:
                        RequireMapLocation(objective.TargetId, $"quest '{quest.Id}' defend objective", issues);

                        if (objective.RequiredCount < MinimumHoldSeconds)
                        {
                            issues.Add($"quest '{quest.Id}' defend objective at '{objective.TargetId}' " +
                                       $"holds for {objective.RequiredCount}s — RequiredCount is seconds " +
                                       $"for a Defend objective, and anything under {MinimumHoldSeconds}s " +
                                       "completes before the player can notice it started");
                        }

                        break;
                }

                // ⚠️ THE QUEST ARM OF THE MAP-COVERAGE RULE (39.5C), PROMISED IN CLAUDE.md §1.
                //
                // "IF THE PLAYER CAN GO THERE, IT GOES ON THE MAP" has been a gate for shops,
                // services and properties since 39.5A, and quests were the one named exemption —
                // "a quest names a template id rather than a place (39.5B), so when quest-to-location
                // linking lands, this rule extends to quests and gets its own validator arm." The
                // linking has landed, so here is the arm. A destination that names a location the
                // database does not have is a marker the player is sent to and never finds.
                if (objective.LocationId.Length > 0 &&
                    World.MapLocationDatabase.Get(objective.LocationId) == null)
                {
                    issues.Add($"quest '{quest.Id}' objective points at unknown map location " +
                               $"'{objective.LocationId}'");
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

                    // 38R: the same two rules for the service route, plus one that is genuinely new.
                    if (choice.Effect == DialogueEffect.OpenService &&
                        ServiceDatabase.Get(choice.EffectArg) == null)
                    {
                        issues.Add($"dialogue '{dialogue.Id}' OpenService effect references unknown service '{choice.EffectArg}'");
                    }

                    if (choice.Effect == DialogueEffect.OpenService && !string.IsNullOrEmpty(choice.Goto))
                    {
                        issues.Add($"dialogue '{dialogue.Id}' OpenService choice must end the conversation but points at '{choice.Goto}'");
                    }

                    // ⚠️ The one that is not a copy. A Bank opens the *host entity's* inventory, and a
                    // conversation has no host entity — the vault it would open does not exist. The
                    // failure is silent at runtime (a log line and a press that does nothing), so it is
                    // refused in the data instead. A banker who talks keeps the ServiceComponent on his
                    // vault and points the player at it; he does not open it mid-sentence.
                    if (choice.Effect == DialogueEffect.OpenService &&
                        ServiceDatabase.Get(choice.EffectArg) is { Kind: ServiceKind.Bank } vaultless)
                    {
                        issues.Add(
                            $"dialogue '{dialogue.Id}' OpenService names '{vaultless.Id}', which is a Bank — " +
                            "a bank opens its own entity's inventory and a conversation has no entity, " +
                            "so the choice would do nothing");
                    }

                    // 38J: the shop-hours condition pair, checked the way the quest conditions are.
                    if (IsShopCondition(choice.Condition) && ShopDatabase.Get(choice.ConditionArg) == null)
                    {
                        issues.Add(
                            $"dialogue '{dialogue.Id}' {choice.Condition} condition references unknown " +
                            $"shop '{choice.ConditionArg}'");
                    }

                    // ⚠️ 38J's load-bearing rule. A trade choice on a shop that closes must be gated on
                    // that shop being open, or the player picks "let's trade" at midnight and *nothing
                    // happens* — the effect's backstop refuses silently, which is a dead choice rather
                    // than a refusal. The condition is also what lets the merchant say she is shut.
                    if (choice.Effect == DialogueEffect.OpenShop &&
                        ShopDatabase.Get(choice.EffectArg) is { } gated &&
                        gated.OpenHour != gated.CloseHour &&
                        (choice.Condition != DialogueCondition.ShopOpen ||
                            choice.ConditionArg != choice.EffectArg))
                    {
                        issues.Add(
                            $"dialogue '{dialogue.Id}' opens shop '{choice.EffectArg}', which keeps hours, " +
                            "without a ShopOpen condition naming it — outside those hours the choice is " +
                            "shown and does nothing");
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

    private static bool IsShopCondition(DialogueCondition condition) => condition switch
    {
        DialogueCondition.ShopOpen => true,
        DialogueCondition.ShopClosed => true,
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

            var seenCellIds = new HashSet<string>();
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell == null || string.IsNullOrEmpty(cell.ScenePath) || !ResourceLoader.Exists(cell.ScenePath))
                {
                    issues.Add($"region '{region.Id}' cell '{cell?.Id ?? "?"}' has a missing scene '{cell?.ScenePath}'");
                    continue;
                }

                // 38K: "the file is there" and "the file parses" are different claims, and a cell scene is
                // hand-authored text — a bad sub-resource id or an ext_resource pointing at nothing yields
                // a file that exists and a PackedScene that is null. The streamer logs that and carries on,
                // so the symptom is a district that simply never appears. Loading does NOT instantiate, so
                // no component _Ready runs here; this is the same "registered is not works" line 37C drew
                // for placeable templates.
                if (GD.Load<PackedScene>(cell.ScenePath) == null)
                {
                    issues.Add(
                        $"region '{region.Id}' cell '{cell.Id}' scene '{cell.ScenePath}' exists but does " +
                        "not load — the streamer would log it and carry on, and the cell would simply " +
                        "never appear");
                    continue;
                }

                if (!region.Bounds.HasPoint(cell.Center))
                {
                    issues.Add($"region '{region.Id}' cell '{cell.Id}' center {cell.Center} is outside region bounds");
                }

                // 38K. A cell nothing can walk in is authored exactly like a working one and says
                // nothing at runtime — every NPC and enemy just falls back to straight-line steering
                // through the scenery. Adding the Embermarket made these one typo away, so they are
                // checked rather than remembered.
                // ⚠️ The "load radius of 0 never loads" rule was deleted in 38M2 with the radius
                // itself: every cell of the active region is resident now, so there is no such state.
                if (cell.SafeRadius < 0f)
                {
                    issues.Add($"region '{region.Id}' cell '{cell.Id}' has a negative safe radius ({cell.SafeRadius})");
                }

                if (!seenCellIds.Add(cell.Id))
                {
                    issues.Add(
                        $"region '{region.Id}' has two cells with id '{cell.Id}' — the streamer keys its " +
                        "loaded set by id, so one of them can never be instanced");
                }

                // ponytail: there is deliberately NO "every cell scene declares a NavigationRegion3D"
                // rule. 38K wrote one and deleted it the same hour: a text scan cannot see through scene
                // inheritance, so the three Frostfang roosts — which inherit their Nav from RoostCell —
                // all reported as unnavigable, and the glacier legitimately has no navmesh because it is
                // scenery. A check that is wrong three times out of four teaches authors to ignore the
                // validator. Resolving inherited scenes properly needs a real PackedScene walk; do that
                // if a missing navmesh ever actually ships.
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

            // 41C. A deadline shorter than this is not pressure, it is a quest that fails while the
            // giver is still talking — and 0 is the authored default meaning "untimed", so only a
            // positive value is judged.
            if (quest.TimeLimitSeconds > 0f && quest.TimeLimitSeconds < MinimumDeadlineSeconds)
            {
                issues.Add($"quest '{quest.Id}' has a {quest.TimeLimitSeconds}s time limit — under " +
                           $"{MinimumDeadlineSeconds}s the quest can fail before the player has " +
                           "finished the conversation that started it (0 means untimed)");
            }

            // 41C. A Stealth objective is seeded already met, so a quest made only of stealth
            // conditions completes on the frame it starts, silently, with rewards.
            bool anythingToDo = false;
            foreach (ObjectiveResource objective in objectives)
            {
                if (objective.Type != ObjectiveType.Stealth)
                {
                    anythingToDo = true;
                    break;
                }
            }

            if (!anythingToDo)
            {
                issues.Add($"quest '{quest.Id}' has only stealth objectives — a stealth condition " +
                           "starts met, so the quest would complete the instant it is accepted");
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
    /// <summary>
    /// The world map's location catalogue (Phase 39.5A).
    ///
    /// ⚠️ <b>The last two rules scan <c>.tscn</c> text, and they are the reason this is worth
    /// writing.</b> IDS.md records an open hole for <c>shop.*</c> and <c>service.*</c>: those ids are
    /// referenced from scenes as well as from resources, <c>ContentValidator</c> does not scan
    /// scenes, and a typo in a <c>VendorComponent.ShopId</c> therefore gives no prompt at all rather
    /// than an error. A map location is referenced from a scene <em>by construction</em> — the scene
    /// is where its position lives — so shipping it with the same hole would mean a mistyped
    /// <c>LocationId</c> produced a marker that silently never appears, which is indistinguishable
    /// from a location the player has not discovered yet. So the seam is checked in both directions:
    /// every id a scene names must exist, and every authored location must be placed somewhere.
    ///
    /// The second direction is the brief's §54 (no orphan systems) enforced by a machine instead of
    /// by good intentions — <c>CraftingComponent.Learn</c> sat with zero callers from Phase 15 to
    /// Phase 35 precisely because nothing could fail over it.
    /// </summary>
    private static void ValidateMapLocations(List<string> issues)
    {
        foreach (World.MapLocationResource location in World.MapLocationDatabase.All)
        {
            string id = location.Id;

            if (string.IsNullOrEmpty(location.NameKey) || !Loc.Has(location.NameKey))
            {
                issues.Add($"map location '{id}' has no name in the locale catalogue " +
                           $"('{location.NameKey}') — the map would draw the raw key");
            }

            if (location.DescriptionKey.Length > 0 && !Loc.Has(location.DescriptionKey))
            {
                issues.Add($"map location '{id}' names a missing description key " +
                           $"'{location.DescriptionKey}'");
            }

            if (location.CellId.Length == 0 || World.RegionDatabase.Cell(location.CellId) == null)
            {
                issues.Add($"map location '{id}' names cell '{location.CellId}', which no region " +
                           "declares — its settlement breadcrumb would be blank");
            }

            if (location.ShopId.Length > 0 && Economy.ShopDatabase.Get(location.ShopId) == null)
            {
                issues.Add($"map location '{id}' names shop '{location.ShopId}', which does not exist");
            }

            if (location.ServiceId.Length > 0 && Economy.ServiceDatabase.Get(location.ServiceId) == null)
            {
                issues.Add($"map location '{id}' names service '{location.ServiceId}', which does not exist");
            }

            if (location.DialogueId.Length > 0 && Dialogue.DialogueDatabase.Get(location.DialogueId) == null)
            {
                issues.Add($"map location '{id}' names dialogue '{location.DialogueId}', which does not exist");
            }

            if (location.PropertyId.Length > 0 && Housing.PropertyDatabase.Get(location.PropertyId) == null)
            {
                issues.Add($"map location '{id}' names property '{location.PropertyId}', which does not exist");
            }
        }

        ValidateMapMarkersArePlaced(issues);
        ValidateEverythingIsOnTheMap(issues);
        ValidateMapTaxonomyIsNamed(issues);
        ValidateHudComputedKeys(issues);
    }

    /// <summary>
    /// Every locale key the HUD builds at runtime rather than reading from a resource (39.5B).
    ///
    /// ⚠️ <b>Same class of hole as <see cref="ValidateMapTaxonomyIsNamed"/>, one screen over.</b> The
    /// quest tracker's destination readout picks a compass point from a bearing and a unit from a
    /// magnitude — neither key is named by any <c>.tres</c>, so no database walk can reach them and a
    /// missing one would print <c>hud.compass.nw</c> at the player under the objective. Enumerating
    /// <see cref="UI.CompassMath.CardinalKeys"/> is what makes the declared set checkable; deriving
    /// the check from "the bearings a test happened to try" would be the reachable set again.
    /// </summary>
    private static void ValidateHudComputedKeys(List<string> issues)
    {
        foreach (string key in UI.CompassMath.CardinalKeys)
        {
            if (!Loc.Has(key))
            {
                issues.Add($"compass point '{key}' has no locale key — the compass strip and the " +
                           "quest tracker's bearing would each show the raw key");
            }
        }

        foreach (string key in new[]
                 {
                     "hud.unit.metres", "hud.unit.kilometres", "hud.quest.destination",

                     // 41C. The timed-quest countdown, built at the call site from two numbers, so no
                     // database walk can reach it — invariant 26's family, enumerated here.
                     "hud.quest.time_left",
                 })
        {
            if (!Loc.Has(key))
            {
                issues.Add($"HUD distance readout has no locale key '{key}'");
            }
        }

        // The clock's phase name, computed from the enum member the same way (39.5B). It shipped as a
        // hard-coded English literal from Phase 18 until the HUD audit found it.
        foreach (World.DayPhase phase in System.Enum.GetValues<World.DayPhase>())
        {
            string key = World.DayPhases.NameKey(phase);
            if (!Loc.Has(key))
            {
                issues.Add($"day phase '{phase}' has no locale key '{key}' — the HUD clock would " +
                           "show the raw key beside the time");
            }
        }
    }

    /// <summary>
    /// Every <see cref="World.MapCategory"/> and <see cref="World.MapGroup"/> has a locale key.
    ///
    /// ⚠️ <b>This closes a hole the rest of the map's validation could not see, and it shipped
    /// through it once.</b> Every other key on this screen is authored in a <c>.tres</c> and is
    /// therefore checkable by walking the database; a category name is <em>computed</em> from the
    /// enum member at runtime (<c>MapCategories.NameKey</c>), so adding a member is adding a key
    /// reference that no resource mentions. <c>Crafting</c> was added in 39.5A and its key was not,
    /// and nothing failed — <see cref="Loc.T"/> returns the key itself, so the filter row, the
    /// legend and the info panel would all have read <c>map.category.crafting</c> at the player.
    /// This is <c>ValidateBreakdownKeys</c>'s lesson applied to a second computed key set: <b>the
    /// declared set is the contract, the reachable set is today's accident.</b>
    /// </summary>
    private static void ValidateMapTaxonomyIsNamed(List<string> issues)
    {
        foreach (World.MapCategory category in System.Enum.GetValues<World.MapCategory>())
        {
            string key = World.MapCategories.NameKey(category);
            if (!Loc.Has(key))
            {
                issues.Add($"map category '{category}' has no locale key '{key}' — the filter row, " +
                           "the legend and the info panel would each show the raw key");
            }
        }

        foreach (World.MapGroup group in System.Enum.GetValues<World.MapGroup>())
        {
            string key = World.MapCategories.NameKey(group);
            if (!Loc.Has(key))
            {
                issues.Add($"map group '{group}' has no locale key '{key}'");
            }
        }
    }

    /// <summary>
    /// Every shop and every service must be findable on the world map (Phase 39.5A, at the
    /// maintainer's direction).
    ///
    /// ⚠️ <b>THIS IS THE RULE THAT KEEPS THE MAP FROM ROTTING, AND IT IS DELIBERATELY A GATE RATHER
    /// THAN A NOTE IN A DOC.</b> The map is only a world-readability system while it is complete;
    /// the first merchant who is authored without a pin makes it a system the player cannot trust,
    /// and after that they check the map less rather than more. A note asking authors to remember
    /// is exactly the mechanism that let <c>recipe.leather_vest</c> rot behind an uncalled
    /// <c>CraftingComponent.Learn</c> for twenty phases — nothing could fail over it.
    ///
    /// Coverage is 23/23 shops and 15/15 services as of 39.5A, so this rule ships already satisfied
    /// and can only be broken by adding something new. If a future shop genuinely should not appear
    /// on the map, that is a design conversation and an explicit exemption here — not a silent
    /// omission.
    /// </summary>
    private static void ValidateEverythingIsOnTheMap(List<string> issues)
    {
        var mappedShops = new HashSet<string>();
        var mappedServices = new HashSet<string>();
        var mappedProperties = new HashSet<string>();

        foreach (World.MapLocationResource location in World.MapLocationDatabase.All)
        {
            if (location.ShopId.Length > 0)
            {
                mappedShops.Add(location.ShopId);
            }

            if (location.ServiceId.Length > 0)
            {
                mappedServices.Add(location.ServiceId);
            }

            if (location.PropertyId.Length > 0)
            {
                mappedProperties.Add(location.PropertyId);
            }
        }

        foreach (Economy.ShopResource shop in Economy.ShopDatabase.All)
        {
            if (!mappedShops.Contains(shop.Id))
            {
                issues.Add($"shop '{shop.Id}' is not on the world map — no map location names it. " +
                           "Add one with tools/gen_map_locations.py (docs/RECIPES.md, 'a new map " +
                           "location'); a shop the map cannot show is a shop the player cannot find");
            }
        }

        foreach (Economy.ServiceResource service in Economy.ServiceDatabase.All)
        {
            if (!mappedServices.Contains(service.Id))
            {
                issues.Add($"service '{service.Id}' is not on the world map — no map location names " +
                           "it. Add one with tools/gen_map_locations.py (docs/RECIPES.md, 'a new map " +
                           "location'); a service the map cannot show is a service the player cannot find");
            }
        }

        // ⚠️ Properties joined this rule after a continuity audit found the realm's ONLY holding —
        // the player's own cottage — missing from the map entirely, while the rule that was supposed
        // to prevent exactly that shipped covering shops and services and nothing else. A coverage
        // rule that does not enumerate every kind of place is a coverage rule with a hole in it.
        foreach (Housing.PropertyResource property in Housing.PropertyDatabase.All)
        {
            if (!mappedProperties.Contains(property.Id))
            {
                issues.Add($"property '{property.Id}' is not on the world map — no map location names " +
                           "it. A holding the player owns and cannot find on their own map is the " +
                           "plainest possible failure of a world-readability system");
            }
        }
    }

    /// <summary>
    /// Content ids authored on components in <c>.tscn</c> scenes rather than in <c>.tres</c> resources
    /// (audit 2026-08-15).
    ///
    /// ⚠️ <b>This was the largest silent-failure surface in the content pipeline, and the code already
    /// knew.</b> <see cref="Economy.ServiceComponent"/> carries the note verbatim: <i>"ContentValidator
    /// does not scan .tscn files, so a mistyped ServiceId yields NO PROMPT AT ALL rather than an
    /// error — the same trap VendorComponent.ShopId and PropertyStorageComponent.PropertyId carry."</i>
    /// A database walk cannot reach these: the id is not in any resource, it is a string in a scene
    /// file, so every existing rule was blind to it by construction. The failure is a merchant who
    /// opens an empty shop, a keeper who offers nothing, or a chest that stores into a property that
    /// does not exist — all of which look like unfinished content rather than a typo.
    ///
    /// <see cref="ValidateMapMarkersArePlaced"/> proved the technique on <c>LocationId</c> in 39.5A;
    /// this is the same scan widened to the rest of the family, which is what that rule's own comment
    /// said should happen ("IDS.md records an open hole for shop.* and service.*").
    ///
    /// <b>Only ids with a database behind them are here.</b> Two scene-authored ids are deliberately
    /// out: <c>TemplateId</c> (48 values) resolves through <see cref="Save.PersistentActorRegistry"/>,
    /// whose builders are seeded at runtime by the bootstrap and are therefore empty in a headless
    /// validate — checking it would mean duplicating the registration list, and a second list to keep
    /// in step is what this audit spent its time removing. <c>TravelNodeComponent.RegionId</c> is
    /// validated at runtime on discovery, by an existing documented decision.
    ///
    /// An empty value is legal everywhere here — it is every one of these properties' default and
    /// means "none", not "unset by mistake".
    ///
    /// ponytail: one regex pass per scene over all eight names; adding a ninth is a row in the table,
    /// not another walk of the scene tree.
    /// </summary>
    private static void ValidateSceneAuthoredIds(List<string> issues)
    {
        // property name -> (does this id exist?, what to call it in the message)
        var rules = new Dictionary<string, (System.Func<string, bool> Exists, string Noun)>
        {
            ["ShopId"] = (id => Economy.ShopDatabase.Get(id) != null, "shop"),
            ["ServiceId"] = (id => Economy.ServiceDatabase.Get(id) != null, "service"),
            ["PropertyId"] = (id => Housing.PropertyDatabase.Get(id) != null, "property"),
            ["ScheduleId"] = (id => Npc.ScheduleDatabase.Get(id) != null, "schedule"),
            ["DialogueId"] = (id => Dialogue.DialogueDatabase.Get(id) != null, "dialogue"),
            ["FactionId"] = (id => Factions.FactionDatabase.Get(id) != null, "faction"),
            ["SpellId"] = (id => Magic.SpellDatabase.Get(id) != null, "spell"),
            ["CompanionId"] = (id => Companions.CompanionDatabase.Get(id) != null, "companion"),
        };

        // Anchored to the start of a line: a .tscn header is prose in the same file, and an
        // unanchored match would read ids out of the comments that discuss them.
        var pattern = new System.Text.RegularExpressions.Regex(
            $@"(?m)^({string.Join("|", rules.Keys)}) = ""([^""]*)""");

        foreach (string path in ScenePaths("res://scenes"))
        {
            using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match match in pattern.Matches(file.GetAsText()))
            {
                string property = match.Groups[1].Value;
                string id = match.Groups[2].Value;
                if (id.Length == 0)
                {
                    continue;
                }

                (System.Func<string, bool> exists, string noun) = rules[property];
                if (!exists(id))
                {
                    issues.Add($"scene '{path}' authors {property} = '{id}', which no {noun} declares — " +
                               $"the component would find nothing at runtime and fail silently");
                }
            }
        }
    }

    /// <summary>Both directions of the scene↔catalogue seam.</summary>
    private static void ValidateMapMarkersArePlaced(List<string> issues)
    {
        var placed = new HashSet<string>();

        foreach (string path in ScenePaths("res://scenes/regions"))
        {
            using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(
                         file.GetAsText(), @"(?m)^LocationId = ""([^""]*)"""))
            {
                string id = match.Groups[1].Value;
                placed.Add(id);

                if (World.MapLocationDatabase.Get(id) == null)
                {
                    issues.Add($"cell scene '{path}' places a MapLocationComponent for '{id}', " +
                               "which no map location declares — the marker would never appear");
                }
            }
        }

        foreach (World.MapLocationResource location in World.MapLocationDatabase.All)
        {
            if (!placed.Contains(location.Id))
            {
                issues.Add($"map location '{location.Id}' is authored but no cell scene places a " +
                           "MapLocationComponent for it — nothing gives it a position, so it can " +
                           "never be discovered or drawn");
            }
        }
    }

    /// <summary>
    /// Cross-references <see cref="ObjectiveType.Interact"/> targets against the ids actually
    /// authored on interactables in the cell scenes (41C), in <b>both</b> directions.
    ///
    /// ⚠️ <b>This is the second scene-authored id with no database behind it</b> — the first was
    /// <c>MapLocationComponent.LocationId</c>, and this is deliberately the same technique
    /// (<see cref="ValidateMapMarkersArePlaced"/>) rather than a new one. A quest naming an id no
    /// node carries is an objective that can never advance, and nothing at runtime would ever say so:
    /// the player simply uses the thing and watches nothing happen.
    ///
    /// ⚠️ <b>Duplicates fail too, and that is the half a one-directional check would miss.</b> Two
    /// nodes sharing an id both advance the same objective, so a "use the north brazier" errand would
    /// be completed by the south one — or by both at once, twice.
    /// </summary>
    private static void ValidateInteractIdsArePlaced(List<string> issues)
    {
        var placed = new Dictionary<string, string>();

        foreach (string path in ScenePaths("res://scenes"))
        {
            using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(
                         file.GetAsText(), @"(?m)^InteractId = ""([^""]+)"""))
            {
                string id = match.Groups[1].Value;
                if (placed.TryGetValue(id, out string? first))
                {
                    issues.Add($"InteractId '{id}' is authored twice — in '{first}' and in '{path}'. " +
                               "Two interactables sharing an id advance the same quest objective, so " +
                               "whichever the player reaches first is the one the quest meant");
                    continue;
                }

                placed[id] = path;
            }
        }

        foreach (QuestResource quest in QuestDatabase.All)
        {
            foreach (ObjectiveResource objective in quest.ObjectiveList())
            {
                if (objective.Type == ObjectiveType.Interact && objective.TargetId.Length > 0 &&
                    !placed.ContainsKey(objective.TargetId))
                {
                    issues.Add($"quest '{quest.Id}' interact objective targets '{objective.TargetId}', " +
                               "which no scene authors on an interactable — the player would use the " +
                               "thing and watch nothing happen");
                }
            }
        }
    }

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

        // 38I's stock gates are the third reader family, and the one with the quietest failure: a
        // mistyped flag on a shop row is a shelf that stays greyed forever, and the refusal it shows is
        // perfectly sensible prose.
        foreach (ShopResource shop in ShopDatabase.All)
        {
            foreach (ShopStockEntry entry in shop.StockList())
            {
                if (!string.IsNullOrEmpty(entry.RequiredFlagId) && !written.Contains(entry.RequiredFlagId))
                {
                    issues.Add(
                        $"shop '{shop.Id}' row '{entry.ItemId}' unlocks on flag '{entry.RequiredFlagId}', " +
                        "which nothing ever sets");
                }
            }
        }
    }
}
