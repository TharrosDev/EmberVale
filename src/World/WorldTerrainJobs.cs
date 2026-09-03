using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Embervale.World;

/// <summary>
/// One cell's terrain as pure data: everything the generator can decide without touching Godot.
/// </summary>
/// <remarks>
/// ⚠️ <b>NOTHING IN HERE IS A GODOT OBJECT AND THAT IS THE POINT.</b> Arrays of structs can be built
/// on any thread; an <c>ArrayMesh</c>, a <c>ConcavePolygonShape3D</c> or a <c>Node</c> cannot, and a
/// worker that touches one is a crash that will happen on somebody else's machine and not on yours.
/// The split is the whole safety argument: <b>worker turns coordinates into data, main thread turns
/// data into resources.</b>
/// </remarks>
public sealed record WorldTerrainData(
    Vector3[] Vertices, Vector3[] Normals, Vector2[] Uvs, Vector2[] Uv2s, Color[] Colors,
    int[] Indices, Vector3[] CollisionFaces);

/// <summary>
/// Builds every cell's terrain data for a region on worker threads, ahead of the frame that needs it.
///
/// ⚠️ <b>WHY THIS EXISTS.</b> Real geography costs roughly an order of magnitude more per ground
/// sample than the two-octave field it replaced, and a cell's mesh plus its collision soup is well
/// over a hundred thousand of them. That work used to be cheap enough to do inline while
/// instantiating the cell; it is not any more, and inline it lands squarely on the frame the player
/// is waiting on. The arithmetic is pure and the field is immutable once built, so it parallelises
/// exactly.
///
/// ⚠️ <b>THE EPOCH IS NOT BOOKKEEPING.</b> Fast travel and a region change both re-target the
/// streamer while jobs from the old region are still running. Without a version stamp the first of
/// those jobs to finish would hand a mesh cut from the Ember Crown's heightfield to a cell of
/// Frostfang Reach, and it would look like a corrupt save rather than a race. A job whose epoch is
/// stale is dropped on completion and its result is never handed out.
///
/// ⚠️ <b>AND <see cref="Take"/> IS ALLOWED TO BLOCK.</b> It is called from the instantiation path,
/// which is behind the loading gate, and a cell that is somehow not ready yet must not be built from
/// half-computed data. In practice it never waits: the jobs start when the region is configured and
/// the cells arrive a frame apart after their scenes have loaded from disk.
/// </summary>
public sealed class WorldTerrainJobs
{
    private readonly ConcurrentDictionary<string, Task<WorldTerrainData?>> _jobs = new();
    private readonly int _epoch;
    private readonly CancellationTokenSource _cancel = new();

    private static int _currentEpoch;

    /// <summary>Started jobs, completed jobs, and jobs discarded because their region went away.
    /// Read by the performance overlay.</summary>
    public static int Started;
    public static int Completed;
    public static int Discarded;

    private WorldTerrainJobs(int epoch) => _epoch = epoch;

    /// <summary>Kicks a job per cell. Returns immediately; the caller keeps the handle and asks for
    /// each cell's data when it instantiates it.</summary>
    public static WorldTerrainJobs Start(RegionResource region, WorldHeightfield field)
    {
        var jobs = new WorldTerrainJobs(Interlocked.Increment(ref _currentEpoch));
        foreach (RegionCellResource? cell in region.Cells)
        {
            WorldCellPresentationResource? presentation = cell?.Presentation;
            if (cell == null || presentation == null || string.IsNullOrEmpty(cell.Id))
            {
                continue;
            }

            Vector3 origin = cell.Center;
            CancellationToken token = jobs._cancel.Token;
            Interlocked.Increment(ref Started);
            jobs._jobs[cell.Id] = Task.Run(
                () =>
                {
                    if (token.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref Discarded);
                        return null;
                    }

                    // The clipped view is built here rather than shared, because ForBounds allocates
                    // and a list being appended to from two threads is the one race this design is
                    // otherwise free of. The field it clips is immutable, so the clipping is safe.
                    WorldHeightfield view = WorldTerrainMeshBuilder.ViewFor(field, presentation, origin);
                    WorldTerrainData data = WorldTerrainMeshBuilder.BuildData(view, presentation, origin);
                    Interlocked.Increment(ref Completed);
                    return (WorldTerrainData?)data;
                },
                token);
        }

        return jobs;
    }

    /// <summary>This cell's terrain data, waiting for the worker if it has not finished. Null when
    /// the region moved on underneath it, or when the cell was never queued.</summary>
    public WorldTerrainData? Take(string cellId)
    {
        if (_epoch != Volatile.Read(ref _currentEpoch) || !_jobs.TryRemove(cellId, out Task<WorldTerrainData?>? job))
        {
            return null;
        }

        try
        {
            return job.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Drops every outstanding job. Called when the streamer re-targets, so a region the
    /// player has left stops burning cores behind them.</summary>
    public void Cancel()
    {
        _cancel.Cancel();
        _jobs.Clear();
    }

    /// <summary>Jobs still in flight, for the performance overlay.</summary>
    public int Outstanding => _jobs.Count;
}
