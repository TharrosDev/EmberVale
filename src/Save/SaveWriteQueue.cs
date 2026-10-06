using System;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Save;

/// <summary>
/// The game's one <see cref="SaveWriteWorker"/>: every save file write (the envelope, the header
/// mirror, the thumbnail) goes through here, so the disk never stalls a frame and no two writes
/// interleave. Call it from the main thread only.
///
/// <b>The contract a caller keeps:</b> anything that is about to <em>read</em> a slot from disk, or
/// to end the process, calls <see cref="Flush"/> first. It costs nothing when the queue is idle.
/// <c>SaveManager</c> does it at the top of a load, a header read, a slot listing and a delete;
/// <see cref="AutosaveService"/> does it when the session goes away and when the window closes.
///
/// <b>Headless gates and tooling run inline.</b> <c>--lifecycle</c>, <c>--story</c> and the
/// save-audit probe read a file back on the line after the save and assert on the save's own
/// return value, so under a headless display, an <c>EMBERVALE_USER_DIR</c>, any <c>-- --flag</c>
/// run or <see cref="ForceInline"/> a write happens on the caller before the call returns, exactly
/// as it did before this queue existed.
/// </summary>
public static class SaveWriteQueue
{
    private static SaveWriteWorker? _worker;
    private static bool? _toolingRun;

    /// <summary>Forces every later write to run synchronously on the caller. A gate that runs
    /// windowed sets it; nothing ever needs to clear it, because a gate ends the process.</summary>
    public static bool ForceInline { get; set; }

    /// <summary>Whether writes run on the caller instead of the worker thread.</summary>
    public static bool RunsInline => ForceInline || (_toolingRun ??= DetectToolingRun());

    /// <summary>Whether a write is still queued or running, or a completion undelivered.</summary>
    public static bool HasPending => _worker is { HasPending: true };

    private static SaveWriteWorker Worker
    {
        get
        {
            _worker ??= new SaveWriteWorker(PostToMainThread);
            _worker.Inline = RunsInline;
            return _worker;
        }
    }

    /// <summary>
    /// Writes one save's files: <c>save.json</c>, then the header mirror, then the legacy flat
    /// file's removal (<see cref="SaveFiles.Commit"/>). Paths may be <c>user://</c> paths.
    /// <paramref name="keepPrevious"/> keeps the save being replaced as the slot's backup generation.
    ///
    /// Returns the outcome when the write ran inline (true = saved), or <b>null when it was
    /// queued</b>. Only a queued write calls <paramref name="onQueuedDone"/>, later and on the main
    /// thread, with whether the save landed; an inline caller already has its answer. Whatever the
    /// commit has to say is logged here, on the main thread, in both cases.
    /// </summary>
    public static bool? CommitSave(
        string slot,
        string savePath,
        string saveJson,
        string headerPath,
        string headerJson,
        string legacyPath,
        bool keepPrevious,
        Action<bool> onQueuedDone)
    {
        var commit = new SaveCommit(
            ProjectSettings.GlobalizePath(savePath),
            saveJson,
            ProjectSettings.GlobalizePath(headerPath),
            headerJson,
            ProjectSettings.GlobalizePath(legacyPath),
            keepPrevious);

        SaveWriteWorker worker = Worker;
        bool inline = worker.Inline;
        SaveCommitResult? result = null;
        worker.Enqueue(
            () =>
            {
                result = SaveFiles.Commit(commit);
                return result.Saved;
            },
            saved =>
            {
                Report(slot, result);

                // Only a queued write owes the caller a callback; an inline one returns its answer.
                if (!inline)
                {
                    onQueuedDone(saved);
                }
            });

        return inline ? result is { Saved: true } : null;
    }

    /// <summary>Queues an arbitrary file job behind the writes already queued (the thumbnail
    /// encoder uses it). <paramref name="onDone"/> runs on the main thread.</summary>
    public static void Enqueue(Func<bool> work, Action<bool>? onDone = null) => Worker.Enqueue(work, onDone);

    /// <summary>Blocks until every queued write is on disk and every completion has run. Cheap when
    /// idle. Main thread only.</summary>
    public static void Flush() => _worker?.Flush();

    private static void Report(string slot, SaveCommitResult? result)
    {
        if (result == null)
        {
            Log.Error($"Save slot '{slot}': the write job failed before it produced a result; previous save preserved.");
            return;
        }

        foreach (string error in result.Errors)
        {
            Log.Error($"Save slot '{slot}': {error}");
        }

        foreach (string warning in result.Warnings)
        {
            Log.Warn($"Save slot '{slot}': {warning}");
        }
    }

    private static void PostToMainThread(Action deliver) =>
        Dispatcher.SynchronizationContext.Post(static state => ((Action)state!)(), deliver);

    private static bool DetectToolingRun() =>
        DisplayServer.GetName() == "headless" ||
        !string.IsNullOrEmpty(OS.GetEnvironment("EMBERVALE_USER_DIR")) ||
        OS.GetCmdlineUserArgs().Length > 0;
}
