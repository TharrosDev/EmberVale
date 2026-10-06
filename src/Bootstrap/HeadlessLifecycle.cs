using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Embervale.Appearance;
using Embervale.Backgrounds;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Progression;
using Embervale.Races;
using Embervale.Save;
using Embervale.Stats;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// <c>godot --headless --path . -- --lifecycle</c> — drives the session and world lifecycle to
/// destruction, repeatedly, and fails the run if anything survives a teardown.
///
/// <para>This exists because unit tests structurally cannot answer the question it asks. The
/// lifecycle is Godot node lifetime: <c>_ExitTree</c> ordering, scope disposal, saveable
/// unregistration, event unsubscription and orphan reclamation. None of that runs under xUnit, and
/// all of it is what the 2026-09-03 overhaul changed.</para>
///
/// <para>Each cycle takes a session all the way into <c>Playing</c> — which means the region really
/// streamed and the loading gate really found collision under the player — saves it, destroys it,
/// loads it back, and destroys it again. Then it asserts that nothing is left: no session, no
/// surviving service registration, no surviving event subscription, no stranded <c>ISaveable</c>,
/// and no orphan nodes above the pool's parked working set.</para>
///
/// <para>Exit code 1 on any failed assertion, so it is a gate rather than a report.</para>
/// </summary>
public static class HeadlessLifecycle
{
    public const string FlagArgument = "--lifecycle";

    /// <summary>New Game / Load round trips to run. Three, because a leak that only shows on the
    /// second repetition is exactly the shape this is looking for, and one repetition cannot see it.</summary>
    private const int Cycles = 3;

    /// <summary>Frames to give a session to reach <c>Playing</c>. The loading gate's own cap is 30 s;
    /// this is generous against it and fails loudly rather than hanging.</summary>
    private const int LoadFrameBudget = 3000;

    /// <summary>Frames to let deferred frees actually run after a teardown. <c>QueueFree</c> reclaims
    /// at end of frame, so an orphan count read on the same frame is meaningless.</summary>
    private const int ReclaimFrames = 8;

    private static readonly List<string> Failures = new();

    /// <summary>The temp user directory this run created for itself, removed again at the end.</summary>
    private static string? _ownedUserDir;

    public static bool Requested() => HeadlessValidation.HasFlag(FlagArgument) || ReloadAuditRequested();

    private static bool ReloadAuditRequested()
    {
#if EMBERVALE_TOOLING
        return HeadlessValidation.HasFlag("--save-reload");
#else
        return false;
#endif
    }

    /// <summary>
    /// Fire-and-forget: this drives real frames, and it ends the process itself. It is the entry
    /// point for a command-line mode, so there is no caller to hand a task back to.
    /// </summary>
    public static async void Run(ApplicationRoot root, SessionLifecycleCoordinator lifecycle)
    {
        Log.Info("=== lifecycle probe ===");
        Failures.Clear();
        bool reloadAudit = ReloadAuditRequested();
        int cycles = reloadAudit ? 2 : Cycles;
        if (reloadAudit && !System.IO.Path.IsPathFullyQualified(OS.GetEnvironment("EMBERVALE_USER_DIR")))
        {
            Log.Error("save-reload requires an absolute isolated EMBERVALE_USER_DIR.");
            root.GetTree().Quit(1);
            return;
        }

        // After the check above on purpose: the reload audit must be handed its isolation, not
        // have this fallback grant it one.
        IsolateFromPlayerSaves();

        await Frames(root, ReclaimFrames);

        int baselineSubscribers = EventBus.Instance?.TotalSubscriberCount() ?? 0;
        int baselineServices = ServiceLocator.Instance?.RegisteredCount ?? 0;
        int baselineOrphans = Orphans();
        Log.Info($"lifecycle: baseline — {baselineServices} service(s), {baselineSubscribers} subscription(s), " +
                 $"{baselineOrphans} orphan node(s).");

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            string slot = $"lifecycle_probe_{cycle}";

            await RunNewGame(root, lifecycle, slot, cycle);
#if EMBERVALE_TOOLING
            if (reloadAudit)
            {
                await RunReloadAudit(root, lifecycle, slot, cycle);
                await Teardown(root, lifecycle, $"cycle {cycle} quick reload", baselineSubscribers, baselineServices, baselineOrphans);
                continue;
            }
#endif
            await Teardown(root, lifecycle, $"cycle {cycle} new-game", baselineSubscribers, baselineServices, baselineOrphans);

            await RunLoad(root, lifecycle, slot, cycle);
            await Teardown(root, lifecycle, $"cycle {cycle} load", baselineSubscribers, baselineServices, baselineOrphans);
        }

        CleanUpProbeSlots();
        Report(root.GetTree(), baselineOrphans, cycles, reloadAudit);
    }

    /// <summary>
    /// Keeps the gate off the developer's own saves. It maxes a character's level and builds and
    /// destroys sessions, and it used to do that with the real autosave ring live under it: the
    /// level-up autosaved, and <c>auto1..auto3</c> ended up holding a "Lifecycle Audit" character.
    ///
    /// Three independent guards, so that no single one being absent re-opens the hole:
    ///   * autosaves are off for the whole process (every session this gate builds gets a fresh
    ///     <see cref="AutosaveService"/>, so removing one node, as <c>StoryPlaythrough</c> does for
    ///     its single session, would not cover the next);
    ///   * when no <c>EMBERVALE_USER_DIR</c> isolates the run, it is pointed at a temp directory of
    ///     its own, so even the probe slots never touch the real save folder (tooling builds; an
    ///     export build ignores the variable and relies on the other two guards);
    ///   * save writes are forced inline, because the gate reads its files back on the next line.
    /// </summary>
    private static void IsolateFromPlayerSaves()
    {
        AutosaveService.Suppressed = true;
        SaveWriteQueue.ForceInline = true;

        if (System.IO.Path.IsPathFullyQualified(OS.GetEnvironment("EMBERVALE_USER_DIR")))
        {
            return;
        }

        string isolated = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "embervale-lifecycle", System.Environment.ProcessId.ToString());
        OS.SetEnvironment("EMBERVALE_USER_DIR", isolated);
        _ownedUserDir = isolated;
        Log.Info($"lifecycle: no isolated EMBERVALE_USER_DIR was given; saves for this run go to '{isolated}'.");
    }

    private static async Task RunNewGame(
        ApplicationRoot root, SessionLifecycleCoordinator lifecycle, string slot, int cycle)
    {
        lifecycle.StartNewGame(slot, AuditProfile());

        if (lifecycle.Session is not { } session)
        {
            Failures.Add($"cycle {cycle} new-game: no session was created.");
            return;
        }

        Check(session.Players.Player != null, $"cycle {cycle} new-game: the session built no player.");
        Check(session.Scope.Count > 0, $"cycle {cycle} new-game: the session scope holds nothing.");
        Check(session.World.Scope.Count > 0, $"cycle {cycle} new-game: the world scope holds nothing.");

        if (!await WaitForPlaying(root))
        {
            Failures.Add($"cycle {cycle} new-game: the world never reached Playing within {LoadFrameBudget} frames.");
            return;
        }

        CheckLandingReady(session, $"cycle {cycle} new-game");
        // Max level before the save, so the load half proves primaries re-derive from the restored level.
        session.Players.Player?.GetComponent<ProgressionComponent>()?.AddXp(1_000_000);
        CheckStatDerivation(session, $"cycle {cycle} new-game");
        CheckAuditCharacter(session, $"cycle {cycle} new-game");

        // Last, because it respecs and learns on the live player (and Verify below wipes it on purpose).
        PerksLifecycleProbe.Drive(session.Players.Player, Check);

        Check(SaveManager.Instance?.SaveGame(slot) == true, $"cycle {cycle} new-game: the session failed to save.");
    }

    private static async Task RunLoad(
        ApplicationRoot root, SessionLifecycleCoordinator lifecycle, string slot, int cycle)
    {
        if (SaveManager.Instance?.SaveExists(slot) != true)
        {
            Failures.Add($"cycle {cycle} load: the new-game half wrote no save to load.");
            return;
        }

        lifecycle.StartLoadedGame(slot);

        if (!lifecycle.HasSession)
        {
            Failures.Add($"cycle {cycle} load: no session was created.");
            return;
        }

        if (!await WaitForPlaying(root))
        {
            Failures.Add($"cycle {cycle} load: the world never reached Playing within {LoadFrameBudget} frames.");
            return;
        }
        CheckLandingReady(lifecycle.Session!, $"cycle {cycle} load");
        CheckStatDerivation(lifecycle.Session!, $"cycle {cycle} load");
        CheckAuditCharacter(lifecycle.Session!, $"cycle {cycle} load");
        PerksLifecycleProbe.Verify(lifecycle.Session!.Players.Player, Check);
    }

    /// <summary>
    /// The primaries are real: at the player's current level, Physical Power and Health equal their base
    /// plus per-level growth plus what the invested Strength / Vitality points buy (StatDerivation), and
    /// the level itself came back from the save. Run at max level on both halves of a round trip.
    /// </summary>
    private static void CheckStatDerivation(GameSession session, string label)
    {
        if (session.Players.Player is not { } player ||
            player.GetComponent<StatsComponent>() is not { } stats ||
            player.GetComponent<ProgressionComponent>() is not { Curve: { } curve } progression)
        {
            Failures.Add($"{label}: the player has no stats/progression to derive from.");
            return;
        }

        Check(player.GetComponent<StatDerivationComponent>() != null, $"{label}: the player has no StatDerivationComponent.");
        Check(progression.Level == curve.MaxLevel, $"{label}: expected level {curve.MaxLevel}, found {progression.Level}.");

        float levels = progression.Level - 1;
        Stat strength = stats.GetStat(StatType.Strength);
        Stat vitality = stats.GetStat(StatType.Vitality);
        PerksComponent? perks = player.GetComponent<PerksComponent>();
        float expectedPower = stats.GetStat(StatType.PhysicalPower).BaseValue + (curve.PhysicalPowerPerLevel * levels)
            + (0.8f * (strength.Value - strength.BaseValue)) + FlatPerkBonus(perks, StatType.PhysicalPower);
        float expectedHealth = stats.GetStat(StatType.Health).BaseValue + (curve.HealthPerLevel * levels)
            + (5f * (vitality.Value - vitality.BaseValue)) + FlatPerkBonus(perks, StatType.Health);
        Check(Mathf.Abs(stats.GetValue(StatType.PhysicalPower) - expectedPower) < 0.01f,
            $"{label}: Physical Power {stats.GetValue(StatType.PhysicalPower)} != derived {expectedPower}.");
        Check(Mathf.Abs(stats.GetValue(StatType.Health) - expectedHealth) < 0.01f,
            $"{label}: Max Health {stats.GetValue(StatType.Health)} != derived {expectedHealth}.");
        Check(strength.Value - strength.BaseValue > 0f, $"{label}: Strength did not grow with level.");
    }

    /// <summary>What the curve has added to a stat by the player's current level (per-level gain times levels gained).</summary>
    private static float LevelGrowth(ProgressionComponent? progression, StatType stat)
    {
        if (progression?.Curve is not { } curve)
        {
            return 0f;
        }

        float perLevel = 0f;
        foreach ((StatType gained, float gain) in curve.StatGains())
        {
            if (gained == stat)
            {
                perLevel += gain;
            }
        }

        return perLevel * (progression.Level - 1);
    }

    /// <summary>What the held perks add to a stat as Flat modifiers (the legacy single-stat effect), so the derivation
    /// check compares only primaries against the stat, not a Soldier's free Might or the perks probe's ranks.</summary>
    private static float FlatPerkBonus(PerksComponent? perks, StatType stat)
    {
        float total = 0f;
        foreach (PerkResource perk in PerkDatabase.All)
        {
            if (perk.Stat == stat && perk.ModifierType == ModifierType.Flat)
            {
                total += perk.ValueAtRank(perks?.RankOf(perk.Id) ?? 0);
            }
        }

        return total;
    }

    /// <summary>The non-default character every cycle plays: Umbral (Dexterity, an innate perk, a standing
    /// penalty) with the Soldier background (a perk, a shield, gold, a flag, a stat point, a standing bonus),
    /// so a regression in either the race or the background path shows as a changed number after a Load.</summary>
    private static CharacterProfile AuditProfile() => new()
    {
        RaceId = "race.umbral",
        CharacterName = "Lifecycle Audit",
        Background = AuditBackground,
        AppearanceOptionIds = AuditLook,
    };

    // Real options the Umbral offers, none of them a slot default, so the body must visibly differ from the unmodified model.
    private static readonly string[] AuditLook =
    [
        "appearance.skin.ashen", "appearance.hair.midnight", "appearance.eyes.violet", "appearance.ember.violet", "appearance.build.slim",
    ];

    private const string AuditBackground = "background.soldier";

    /// <summary>The chosen look reaches the body after New Game and again after Load: the shader carries the chosen
    /// tints (and not the unmodified references) and the Slim build narrows X/Z without changing height.</summary>
    private static void CheckAuditLook(Node3D player, string label)
    {
        Node3D? body = player.GetNodeOrNull<Node3D>("BodyMesh");
        ShaderMaterial? material = body == null ? null : PlayerAppearance.FindMaterial(body);
        if (body == null || material == null)
        {
            Failures.Add($"{label}: the player body has no appearance shader.");
            return;
        }

        Check(material.GetShaderParameter("skin_tint").AsColor().IsEqualApprox(AppearanceDatabase.Get("appearance.skin.ashen")!.Tint),
            $"{label}: the body skin tint is not the chosen Ashen.");
        Check(material.GetShaderParameter("hair_tint").AsColor().IsEqualApprox(AppearanceDatabase.Get("appearance.hair.midnight")!.Tint),
            $"{label}: the body hair tint is not the chosen Midnight.");
        Check(material.GetShaderParameter("eye_tint").AsColor().IsEqualApprox(AppearanceDatabase.Get("appearance.eyes.violet")!.Tint),
            $"{label}: the body eye tint is not the chosen Violet.");
        Check(material.GetShaderParameter("ember_tint").AsColor().IsEqualApprox(AppearanceDatabase.Get("appearance.ember.violet")!.Tint),
            $"{label}: the body ember tint is not the chosen Violet.");
        Check(Math.Abs(body.Scale.X - 0.92f) < 0.001f && Math.Abs(body.Scale.Z - 0.92f) < 0.001f && Math.Abs(body.Scale.Y - 1f) < 0.001f,
            $"{label}: the Slim build is not 0.92 wide and 1.0 tall (scale {body.Scale}).");
    }

    /// <summary>
    /// The audit character's identity and grants, asserted identically after New Game and after Load. The
    /// background kit is New Game only, so a load that re-granted it would read gold 20 and two shields; a
    /// load that dropped the profile would read no flag and no Strength point.
    /// </summary>
    private static void CheckAuditCharacter(GameSession session, string label)
    {
        BackgroundResource? background = BackgroundDatabase.Get(AuditBackground);
        RaceResource? race = RaceDatabase.Get("race.umbral");
        if (background == null || race == null || session.Players.Player is not { } player)
        {
            Failures.Add($"{label}: audit character, race or background is missing.");
            return;
        }

        Check(session.Profile.RaceId == "race.umbral" && session.Profile.Background == AuditBackground,
            $"{label}: the character profile did not round-trip (race '{session.Profile.RaceId}', background '{session.Profile.Background}').");

        CheckAuditLook(player, label);

        StatsComponent? stats = player.GetComponent<StatsComponent>();
        // The character may be at any level, and the curve grows the primaries per level: count only what is left over.
        ProgressionComponent? progression = player.GetComponent<ProgressionComponent>();
        Check(stats != null && Math.Abs(stats.GetStat(StatType.Strength).Value - stats.GetStat(StatType.Strength).BaseValue - LevelGrowth(progression, StatType.Strength) - 1f) < 0.001f,
            $"{label}: the background's Strength point was not applied exactly once.");
        Check(stats != null && Math.Abs(stats.GetStat(StatType.Dexterity).Value - stats.GetStat(StatType.Dexterity).BaseValue - LevelGrowth(progression, StatType.Dexterity) - 4f) < 0.001f,
            $"{label}: the race's Dexterity delta was not applied exactly once.");

        PerksComponent? perks = player.GetComponent<PerksComponent>();
        Check(perks?.FreeRankOf("perk.might") == 1, $"{label}: the Soldier's free perk is not at rank 1.");
        Check(perks?.RankOf("perk.precision") == 1, $"{label}: the Umbral innate perk is not at rank 1.");

        InventoryComponent? pack = player.GetComponent<InventoryComponent>();
        Check(pack?.CountOf("item.armor.round_shield") == 1, $"{label}: the Soldier's shield count is not exactly 1.");
        Check(pack?.CountOf(GameIds.Currency.Gold) == background.StartingGold,
            $"{label}: starting gold is not exactly {background.StartingGold}.");

        Check(player.GetComponent<StoryFlagsComponent>()?.Has("flag.background.soldier") == true,
            $"{label}: the background flag is not set.");

        int expectedStanding = (FactionDatabase.Get("faction.dawnwardens")?.DefaultReputation ?? 0) + 4;
        Check(player.GetComponent<ReputationComponent>()?.Get("faction.dawnwardens") == expectedStanding,
            $"{label}: the background's Dawnwarden standing is not {expectedStanding}.");
    }

    private static void CheckLandingReady(GameSession session, string label)
    {
        // LoadingCoordinator opens play once the landing cell has active collision. Distant
        // staged cells may still be streaming, so global IsSettled is not the gameplay contract.
        Check(session.Players.Player is { } player &&
              session.WorldDirector.Streamer is { } streamer && !streamer.HasFailedCells() &&
              streamer.IsPositionReady(player.GlobalPosition),
            $"{label}: reached Playing without a ready landing cell or with failed cells.");
    }

#if EMBERVALE_TOOLING
    private static async Task RunReloadAudit(ApplicationRoot root, SessionLifecycleCoordinator lifecycle, string slot, int cycle)
    {
        if (lifecycle.Session is not { } original || GameManager.Instance is not { IsPlaying: true })
        {
            Failures.Add($"reload cycle {cycle}: no playable session to rewind.");
            return;
        }
        SaveManager saves = SaveManager.Instance;
        ServiceScope priorScope = original.Scope;
        Check(original.DevTools != null, $"reload cycle {cycle}: quick-save/load input is absent from this build profile.");
        if (original.DevTools == null) { return; }
        if (BuildProfile.IsCapture)
        {
            Check(original.DevTools.GetChildCount() == 0,
                $"reload cycle {cycle}: capture mode constructed developer overlays beside quick-save/load input.");
        }
        const string authoredId = "ember_crown.guild_vault";
        if (cycle == 1)
        {
            // This authored cell actor uses the same TreeExiting removal ledger as death and
            // looted pickups. Removing it after the checkpoint used to survive an F9 overlay.
            IEntity? actor = null;
            for (int frame = 0; frame < LoadFrameBudget && actor == null; frame++)
            {
                actor = FindActor(original.World, authoredId);
                if (actor == null) { await Frames(root, 1); }
            }
            Check(actor != null, "F9 regression: authored guild vault never streamed in.");
            if (actor == null) { return; }
            Check(saves.SaveGame(slot), "F9 regression: checkpoint with authored actor failed to save.");
            actor.Body.QueueFree();
            await Frames(root, ReclaimFrames);
            Check(FindActor(original.World, authoredId) == null, "F9 regression: authored actor did not leave the scene.");
            Godot.Collections.Array removed = original.GetNode<CellPersistenceDirector>("CellPersistence").Save()["removed"].AsGodotArray();
            Check(removed.Contains(authoredId), "F9 regression: removal was not recorded by cell persistence.");
            Check(!lifecycle.RequestReload(original, slot + "_missing") && ReferenceEquals(lifecycle.Session, original),
                "F9 regression: absent checkpoint tore down the live session.");
            original.Profile.RaceId = "race.human";
            original.Profile.CharacterName = "Abandoned Timeline";
            original.Profile.Background = "background.hunter";
            original.Profile.AppearanceOptionIds = ["appearance.abandoned"];
            using var key = new InputEventKey { Keycode = Key.F9, Pressed = true };
            original.DevTools!._UnhandledKeyInput(key);
        }
        else
        {
            RewriteRegionAsV2Fixture(saves, slot, GameIds.Regions.FrostfangReach);
            GameManager.Instance.ChangeState(GameState.Paused);
            PauseMenu? menu = null;
            foreach (Node child in original.Ui.GetChildren()) { if (child is PauseMenu pause) { menu = pause; break; } }
            Check(menu?.RequestLoad() == true, "pause reload regression: the paused menu refused the checkpoint.");
        }

        Check(ReferenceEquals(lifecycle.Session, original), $"reload cycle {cycle}: input synchronously destroyed its owning session.");
        for (int repeat = 0; repeat < 20; repeat++)
        {
            Check(!lifecycle.RequestReload(original, slot), $"reload cycle {cycle}: duplicate pending request was accepted.");
        }
        await Frames(root, 2);
        Check(lifecycle.Session != null && !ReferenceEquals(lifecycle.Session, original), $"reload cycle {cycle}: deferred request did not create a fresh session.");
        if (!await WaitForPlaying(root) || lifecycle.Session is not { } restored)
        {
            Failures.Add($"reload cycle {cycle}: restored session never reached Playing.");
            return;
        }
        CheckLandingReady(restored, $"reload cycle {cycle}");
        Check(priorScope.Count == 0, $"reload cycle {cycle}: abandoned session services survived.");
        Check(restored.Profile.RaceId == "race.umbral" && restored.Profile.CharacterName == "Lifecycle Audit",
            $"reload cycle {cycle}: loaded character came from the abandoned session instead of the header.");
        Check(restored.Profile.Background == AuditBackground &&
              restored.Profile.AppearanceOptionIds.AsSpan().SequenceEqual(AuditLook),
            $"reload cycle {cycle}: background or appearance choices were lost.");
        Check(!lifecycle.RequestReload(original, slot), $"reload cycle {cycle}: a stale session could reload over its replacement.");

        if (cycle == 1)
        {
            IEntity? actor = null;
            for (int frame = 0; frame < LoadFrameBudget && actor == null; frame++)
            {
                actor = FindActor(restored.World, authoredId);
                if (actor == null) { await Frames(root, 1); }
            }
            Check(actor != null, "F9 regression: authored actor removed after the checkpoint was not rebuilt.");
        }
        else
        {
            Check(restored.CurrentRegionId == GameIds.Regions.FrostfangReach,
                "pause reload regression: migrated save retained the abandoned region.");
            Vector3 spawn = RegionDatabase.Get(GameIds.Regions.FrostfangReach)!.SpawnPoint;
            Vector3 landing = restored.Players.Player!.GlobalPosition;
            Check(Math.Abs(landing.X - spawn.X) < 1f && Math.Abs(landing.Z - spawn.Z) < 1f,
                "pause reload regression: migrated header did not land at its saved region's spawn point.");
            Check(lifecycle.RequestReload(restored, slot), "reload cancellation regression: request was not queued.");
            lifecycle.DestroySession();
            await Frames(root, ReclaimFrames);
            Check(!lifecycle.HasSession, "reload cancellation regression: deferred callback recreated a session after quit.");
        }
    }

    private static IEntity? FindActor(Node root, string persistentId)
    {
        if (root is IEntity actor && actor.PersistentId == persistentId && !root.IsQueuedForDeletion()) { return actor; }
        foreach (Node child in root.GetChildren())
        {
            if (FindActor(child, persistentId) is { } match) { return match; }
        }
        return null;
    }

    private static void RewriteRegionAsV2Fixture(SaveManager saves, string slot, string regionId)
    {
        string path = saves.SlotPath(slot);
        var envelope = Json.ParseString(FileAccess.GetFileAsString(path)).AsGodotDictionary();
        var header = envelope["header"].AsGodotDictionary();
        header["region_id"] = regionId;
        header["region"] = RegionDatabase.Get(regionId)!.DisplayName;
        envelope["version"] = 2;
        using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write)) { file.StoreString(Json.Stringify(envelope)); }
        string mirror = path.Substring(0, path.Length - "save.json".Length) + "header.json";
        using (FileAccess file = FileAccess.Open(mirror, FileAccess.ModeFlags.Write)) { file.StoreString(Json.Stringify(header)); }
    }
#endif

    /// <summary>
    /// What must be true after every teardown. Each of these was a real hazard before the overhaul:
    /// services outlived their owners in a process-wide dictionary, handlers accumulated across
    /// scene reloads, and there was no point at which "exactly one session" was enforceable at all.
    /// </summary>
    private static async Task Teardown(
        ApplicationRoot root,
        SessionLifecycleCoordinator lifecycle,
        string label,
        int baselineSubscribers,
        int baselineServices,
        int baselineOrphans)
    {
        lifecycle.DestroySession();
        await Frames(root, ReclaimFrames);

        Check(!lifecycle.HasSession, $"{label}: a session survived DestroySession.");

        int services = ServiceLocator.Instance?.RegisteredCount ?? 0;
        Check(services <= baselineServices,
            $"{label}: {services - baselineServices} service registration(s) survived teardown.");

        int subscribers = EventBus.Instance?.TotalSubscriberCount() ?? 0;
        Check(subscribers <= baselineSubscribers,
            $"{label}: {subscribers - baselineSubscribers} event subscription(s) survived teardown " +
            "(a duplicate subscription on the next session is the symptom).");

        int saveables = 0;
        foreach (string _ in SaveManager.Instance?.RegisteredSaveIds ?? Array.Empty<string>())
        {
            saveables++;
        }

        Check(saveables == 0, $"{label}: {saveables} ISaveable(s) survived teardown.");

        int orphans = Orphans();
        Check(orphans <= baselineOrphans,
            $"{label}: {orphans - baselineOrphans} node(s) were left detached but not freed.");
    }

    internal static async Task<bool> WaitForPlaying(ApplicationRoot root)
    {
        for (int frame = 0; frame < LoadFrameBudget; frame++)
        {
            if (GameManager.Instance is { IsPlaying: true })
            {
                return true;
            }

            await Frames(root, 1);
        }

        return false;
    }

    internal static async Task Frames(ApplicationRoot root, int count)
    {
        for (int i = 0; i < count; i++)
        {
            await root.ToSignal(root.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>Orphans, less the node pool's intentionally-detached working set — the same
    /// subtraction <c>WorldIntegrityChecker</c> makes, and for the same reason.</summary>
    private static int Orphans() =>
        (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) - NodePoolCensus.Parked;

    private static void CleanUpProbeSlots()
    {
        if (SaveManager.Instance is not { } saves)
        {
            return;
        }

        for (int cycle = 1; cycle <= Cycles; cycle++)
        {
            saves.DeleteSlot($"lifecycle_probe_{cycle}");
        }

        if (_ownedUserDir != null)
        {
            try
            {
                System.IO.Directory.Delete(_ownedUserDir, true);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Log.Warn($"lifecycle: could not remove the temp user directory '{_ownedUserDir}': {ex.Message}");
            }
        }
    }

    private static void Check(bool condition, string failure)
    {
        if (!condition)
        {
            Failures.Add(failure);
        }
    }

    private static void Report(SceneTree tree, int baselineOrphans, int cycles, bool reloadAudit)
    {
        string label = reloadAudit ? "save-reload" : "lifecycle";
        Log.Info($"{label}: {cycles} new-game + load round trip(s); orphan nodes {Orphans()} " +
                 $"(baseline {baselineOrphans}); invariant violations {Invariant.Violations}.");

        if (Failures.Count == 0 && Invariant.Violations == 0)
        {
            Log.Info($"{label}: PASS");
            tree.Quit(0);
            return;
        }

        foreach (string failure in Failures)
        {
            Log.Error($"lifecycle: {failure}");
        }

        if (Invariant.Violations > 0)
        {
            Log.Error($"lifecycle: {Invariant.Violations} invariant violation(s) were recorded during the run.");
        }

        Log.Error("lifecycle: FAIL");
        tree.Quit(1);
    }
}
