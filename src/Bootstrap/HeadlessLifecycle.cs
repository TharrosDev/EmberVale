using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Races;
using Embervale.Save;
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

    private static async Task RunNewGame(
        ApplicationRoot root, SessionLifecycleCoordinator lifecycle, string slot, int cycle)
    {
        CharacterProfile profile = ReloadAuditRequested()
            ? new CharacterProfile { RaceId = "race.umbral", CharacterName = "Reload Audit",
                Background = "A traveller from the Reach", AppearanceOptionIds = ["appearance.audit_one", "appearance.audit_two"] }
            : CharacterProfile.Human;
        lifecycle.StartNewGame(slot, profile);

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
        PerksLifecycleProbe.Verify(lifecycle.Session!.Players.Player, Check);
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
            original.Profile.Background = "Abandoned background";
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
        Check(restored.Profile.RaceId == "race.umbral" && restored.Profile.CharacterName == "Reload Audit",
            $"reload cycle {cycle}: loaded character came from the abandoned session instead of the header.");
        Check(restored.Profile.Background == "A traveller from the Reach" &&
              restored.Profile.AppearanceOptionIds is ["appearance.audit_one", "appearance.audit_two"],
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
