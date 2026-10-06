using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Embervale.Save;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure half of the save write path: <see cref="SaveWriteWorker"/> (ordering, the
/// synchronous flush, completions handed back to the caller, inline mode) and
/// <see cref="SaveFiles"/> (the atomic write and the save / header-mirror sequence). The engine
/// wrapper that picks inline mode and posts to the main thread runs against Godot.
/// </summary>
public sealed class SaveWriteWorkerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "embervale-tests", Guid.NewGuid().ToString("N"));

    public SaveWriteWorkerTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private string In(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Jobs_RunInOrder_OffTheCallingThread()
    {
        var worker = new SaveWriteWorker(_ => { });
        int caller = Environment.CurrentManagedThreadId;
        var order = new List<int>();
        bool offThread = true;
        for (int i = 0; i < 20; i++)
        {
            int n = i;
            worker.Enqueue(() =>
            {
                offThread &= Environment.CurrentManagedThreadId != caller;
                Thread.Sleep(n % 3);
                lock (order)
                {
                    order.Add(n);
                }

                return true;
            });
        }

        worker.Flush();
        Assert.True(offThread);
        Assert.Equal(20, order.Count);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(i, order[i]);
        }

        Assert.False(worker.HasPending);
    }

    [Fact]
    public void Completions_AreDeliveredOnTheCallerThread_ExactlyOnce()
    {
        var posted = new List<Action>();
        var worker = new SaveWriteWorker(deliver =>
        {
            lock (posted)
            {
                posted.Add(deliver);
            }
        });

        int caller = Environment.CurrentManagedThreadId;
        int calls = 0;
        bool onCaller = false;
        bool result = false;
        worker.Enqueue(() => true, ok =>
        {
            calls++;
            onCaller = Environment.CurrentManagedThreadId == caller;
            result = ok;
        });

        worker.Flush();
        Assert.Equal(1, calls);
        Assert.True(onCaller);
        Assert.True(result);

        // The post the worker made arrives later, as it would on the next frame; it finds nothing left.
        Action[] late;
        lock (posted)
        {
            late = posted.ToArray();
        }

        foreach (Action deliver in late)
        {
            deliver();
        }

        Assert.Equal(1, calls);
    }

    [Fact]
    public void AThrowingJob_ReportsFalse_AndDoesNotStopTheLane()
    {
        var worker = new SaveWriteWorker(_ => { });
        bool? first = null;
        bool? second = null;
        worker.Enqueue(() => throw new InvalidOperationException("boom"), ok => first = ok);
        worker.Enqueue(() => true, ok => second = ok);
        worker.Flush();
        Assert.False(first);
        Assert.True(second);
    }

    [Fact]
    public void Flush_AlsoRunsWorkQueuedByACompletion()
    {
        var worker = new SaveWriteWorker(_ => { });
        bool followUpRan = false;
        worker.Enqueue(() => true, _ => worker.Enqueue(() =>
        {
            followUpRan = true;
            return true;
        }));

        worker.Flush();
        Assert.True(followUpRan);
        Assert.False(worker.HasPending);
    }

    [Fact]
    public void Inline_RunsOnTheCallerBeforeEnqueueReturns()
    {
        var worker = new SaveWriteWorker(_ => throw new InvalidOperationException("inline must not post")) { Inline = true };
        int caller = Environment.CurrentManagedThreadId;
        bool ranOnCaller = false;
        bool? done = null;
        bool ranInline = worker.Enqueue(
            () =>
            {
                ranOnCaller = Environment.CurrentManagedThreadId == caller;
                return false;
            },
            ok => done = ok);

        Assert.True(ranInline);
        Assert.True(ranOnCaller);
        Assert.False(done);
    }

    [Fact]
    public void WriteAtomic_ReplacesTheTarget_AndLeavesNoTempFile()
    {
        string path = In("save.json");
        Assert.True(SaveFiles.WriteAtomic(path, "one", out _));
        Assert.True(SaveFiles.WriteAtomic(path, "two", out _));
        Assert.Equal("two", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));

        byte[] raw = File.ReadAllBytes(path);
        Assert.Equal((byte)'t', raw[0]); // no byte-order mark: the engine's JSON reader gets plain UTF-8
    }

    [Fact]
    public void WriteAtomic_Failing_PreservesThePreviousFile()
    {
        string path = In("save.json");
        File.WriteAllText(path, "good");
        Directory.CreateDirectory(path + ".tmp"); // the staging path cannot be opened as a file

        Assert.False(SaveFiles.WriteAtomic(path, "bad", out string error));
        Assert.NotEqual(string.Empty, error);
        Assert.Equal("good", File.ReadAllText(path));
    }

    [Fact]
    public void Commit_WritesSaveAndHeader_AndDropsTheLegacyFile()
    {
        string legacy = In("slot1.json");
        File.WriteAllText(legacy, "old layout");
        var commit = new SaveCommit(
            In(Path.Combine("slot1", "save.json")), "{\"v\":3}", In(Path.Combine("slot1", "header.json")), "{\"h\":1}", legacy);

        SaveCommitResult result = SaveFiles.Commit(commit);

        Assert.True(result.Saved);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal("{\"v\":3}", File.ReadAllText(commit.SavePath));
        Assert.Equal("{\"h\":1}", File.ReadAllText(commit.HeaderPath));
        Assert.False(File.Exists(legacy));
    }

    [Fact]
    public void Commit_WhenTheSaveCannotBeWritten_FailsAndTouchesNothingElse()
    {
        string save = In(Path.Combine("slot1", "save.json"));
        string header = In(Path.Combine("slot1", "header.json"));
        Directory.CreateDirectory(In("slot1"));
        File.WriteAllText(save, "good save");
        File.WriteAllText(header, "good header");
        Directory.CreateDirectory(save + ".tmp");

        SaveCommitResult result = SaveFiles.Commit(new SaveCommit(save, "new save", header, "new header"));

        Assert.False(result.Saved);
        Assert.NotEmpty(result.Errors);
        Assert.Equal("good save", File.ReadAllText(save));
        Assert.Equal("good header", File.ReadAllText(header));
    }

    [Fact]
    public void Commit_WhenTheHeaderMirrorCannotBeWritten_RemovesTheStaleOne_AndStillSaves()
    {
        string save = In(Path.Combine("slot1", "save.json"));
        string header = In(Path.Combine("slot1", "header.json"));
        Directory.CreateDirectory(In("slot1"));
        File.WriteAllText(header, "previous save's header");
        Directory.CreateDirectory(header + ".tmp");

        SaveCommitResult result = SaveFiles.Commit(new SaveCommit(save, "new save", header, "new header"));

        Assert.True(result.Saved);
        Assert.NotEmpty(result.Warnings);
        Assert.False(File.Exists(header));
        Assert.Equal("new save", File.ReadAllText(save));
    }

    [Fact]
    public void WriteAtomic_KeepingThePrevious_LeavesItAsTheBackupGeneration()
    {
        string path = In("save.json");
        Assert.True(SaveFiles.WriteAtomic(path, "one", out _, keepPrevious: true));
        Assert.False(File.Exists(path + ".bak")); // nothing to keep on the first write

        Assert.True(SaveFiles.WriteAtomic(path, "two", out _, keepPrevious: true));
        Assert.True(SaveFiles.WriteAtomic(path, "three", out _, keepPrevious: true));
        Assert.Equal("three", File.ReadAllText(path));
        Assert.Equal("two", File.ReadAllText(path + ".bak")); // exactly one generation back
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Commit_NotKeepingThePrevious_LeavesAGoodBackupAlone()
    {
        string save = In(Path.Combine("slot1", "save.json"));
        string header = In(Path.Combine("slot1", "header.json"));
        Directory.CreateDirectory(In("slot1"));
        File.WriteAllText(save, "damaged");
        File.WriteAllText(save + ".bak", "good backup");

        // A damaged primary is replaced in place: rotating it would destroy the good generation.
        Assert.True(SaveFiles.Commit(new SaveCommit(save, "new save", header, "h")).Saved);
        Assert.Equal("good backup", File.ReadAllText(save + ".bak"));

        Assert.True(SaveFiles.Commit(new SaveCommit(save, "newer save", header, "h", KeepPrevious: true)).Saved);
        Assert.Equal("new save", File.ReadAllText(save + ".bak"));
        Assert.Equal("newer save", File.ReadAllText(save));
    }
}
