using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Embervale.Save;

/// <summary>
/// One background lane for save I/O. Jobs run <b>one at a time, in the order they were queued</b>,
/// off the calling thread; each job's completion callback is handed back to the caller's thread.
/// It holds no Godot type (the engine-facing wrapper is <see cref="SaveWriteQueue"/>), so the
/// ordering, the flush and the marshalling are unit-tested.
///
/// Three properties the save system leans on:
///   * <b>serial</b>: two writes can never interleave, so a slot's <c>save.json</c>, its header
///     mirror and its thumbnail land in the order the save produced them;
///   * <b><see cref="Flush"/> is synchronous</b>: it returns only when every queued job has run
///     <em>and</em> every completion has been delivered, which is what a load, a slot listing and
///     a quit need before they touch the disk;
///   * <b><see cref="Inline"/> removes the thread entirely</b>: headless gates and tooling read
///     files back on the very next line, so there a job runs on the caller before Enqueue returns.
/// </summary>
public sealed class SaveWriteWorker
{
    private readonly object _gate = new();
    private readonly ConcurrentQueue<Action> _completions = new();
    private readonly Action<Action> _postToCaller;
    private Task _tail = Task.CompletedTask;
    private int _queued;

    /// <param name="postToCaller">Schedules its argument to run later on the caller's thread (the
    /// main thread in the game). It is handed <see cref="DeliverCompletions"/>.</param>
    public SaveWriteWorker(Action<Action> postToCaller)
    {
        _postToCaller = postToCaller ?? throw new ArgumentNullException(nameof(postToCaller));
    }

    /// <summary>When true, a job runs synchronously on the caller and its completion is invoked
    /// before <see cref="Enqueue"/> returns.</summary>
    public bool Inline { get; set; }

    /// <summary>Whether any job is still queued or running, or any completion undelivered.</summary>
    public bool HasPending => Volatile.Read(ref _queued) > 0 || !_completions.IsEmpty;

    /// <summary>
    /// Queues <paramref name="work"/>. Its result (false also when it throws) is passed to
    /// <paramref name="onDone"/> on the caller's thread. Returns true when the job already ran
    /// (inline), false when it was queued.
    /// </summary>
    public bool Enqueue(Func<bool> work, Action<bool>? onDone = null)
    {
        if (work == null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        if (Inline)
        {
            // Anything queued before the mode changed still goes first.
            Flush();
            bool ok = Run(work);
            onDone?.Invoke(ok);
            return true;
        }

        Interlocked.Increment(ref _queued);
        lock (_gate)
        {
            _tail = _tail.ContinueWith(
                _ =>
                {
                    bool ok = Run(work);
                    if (onDone != null)
                    {
                        _completions.Enqueue(() => onDone(ok));
                    }

                    Interlocked.Decrement(ref _queued);
                    try
                    {
                        _postToCaller(DeliverCompletions);
                    }
                    catch (Exception)
                    {
                        // The caller's loop is gone (shutdown). Flush still delivers what is queued.
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }

        return false;
    }

    /// <summary>
    /// Blocks until every queued job has finished and every completion has been delivered,
    /// including work a completion queued in turn. Call it from the caller's thread only.
    /// </summary>
    public void Flush()
    {
        while (true)
        {
            Task tail;
            lock (_gate)
            {
                tail = _tail;
            }

            tail.Wait();
            DeliverCompletions();

            lock (_gate)
            {
                if (ReferenceEquals(tail, _tail) && _completions.IsEmpty)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Runs the completions of finished jobs. Caller's thread only; safe to call when
    /// there are none, and each completion runs exactly once however many callers race to it.</summary>
    public void DeliverCompletions()
    {
        while (_completions.TryDequeue(out Action? completion))
        {
            completion();
        }
    }

    private static bool Run(Func<bool> work)
    {
        try
        {
            return work();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
