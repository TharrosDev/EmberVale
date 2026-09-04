using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Embervale.Core.Services;
using Embervale.Races;
using Embervale.Save;
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

    public static bool Requested() => HeadlessValidation.HasFlag(FlagArgument);

    /// <summary>
    /// Fire-and-forget: this drives real frames, and it ends the process itself. It is the entry
    /// point for a command-line mode, so there is no caller to hand a task back to.
    /// </summary>
    public static async void Run(ApplicationRoot root, SessionLifecycleCoordinator lifecycle)
    {
        Log.Info("=== lifecycle probe ===");
        Failures.Clear();

        await Frames(root, ReclaimFrames);

        int baselineSubscribers = EventBus.Instance?.TotalSubscriberCount() ?? 0;
        int baselineServices = ServiceLocator.Instance?.RegisteredCount ?? 0;
        int baselineOrphans = Orphans();
        Log.Info($"lifecycle: baseline — {baselineServices} service(s), {baselineSubscribers} subscription(s), " +
                 $"{baselineOrphans} orphan node(s).");

        for (int cycle = 1; cycle <= Cycles; cycle++)
        {
            string slot = $"lifecycle_probe_{cycle}";

            await RunNewGame(root, lifecycle, slot, cycle);
            await Teardown(root, lifecycle, $"cycle {cycle} new-game", baselineSubscribers, baselineServices, baselineOrphans);

            await RunLoad(root, lifecycle, slot, cycle);
            await Teardown(root, lifecycle, $"cycle {cycle} load", baselineSubscribers, baselineServices, baselineOrphans);
        }

        CleanUpProbeSlots();
        Report(root.GetTree(), baselineOrphans);
    }

    private static async Task RunNewGame(
        ApplicationRoot root, SessionLifecycleCoordinator lifecycle, string slot, int cycle)
    {
        lifecycle.StartNewGame(slot, CharacterProfile.Human);

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

        // The world is genuinely resident here: the gate only opens once the streamer has settled
        // and the physics server reports collision under the player.
        Check(session.WorldDirector.Streamer is { } streamer && streamer.IsSettled(),
            $"cycle {cycle} new-game: reached Playing with an unsettled streamer.");

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
        }
    }

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

    private static async Task<bool> WaitForPlaying(ApplicationRoot root)
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

    private static async Task Frames(ApplicationRoot root, int count)
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

    private static void Report(SceneTree tree, int baselineOrphans)
    {
        Log.Info($"lifecycle: {Cycles} new-game + load round trip(s); orphan nodes {Orphans()} " +
                 $"(baseline {baselineOrphans}); invariant violations {Invariant.Violations}.");

        if (Failures.Count == 0 && Invariant.Violations == 0)
        {
            Log.Info("lifecycle: PASS");
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
