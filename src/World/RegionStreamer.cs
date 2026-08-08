using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Godot;

namespace Embervale.World;

/// <summary>
/// Brings in a region's sub-cells (Phase 25B, rewritten 38M2): instances every
/// <see cref="RegionCellResource"/>'s scene on entering the region and keeps them all resident until
/// the region changes. It still owns the per-frame instancing budget so a multi-cell wave never
/// hitches, and still publishes <see cref="RegionCellLoadedEvent"/>/<see cref="RegionCellUnloadedEvent"/>
/// (the seam Phase 25D's persistence hooks).
///
/// <b>Distance streaming is gone (maintainer direction, 38M2).</b> Until now a cell loaded when the
/// player came within its <c>LoadRadius</c> and was freed when they left it plus a hysteresis margin,
/// with the rule in a pure <c>StreamDecision</c>. A region is five cells and the largest is a
/// thousand nodes, so residency costs nothing worth the seams it bought: an NPC's routine walking a
/// cell that is not loaded, a district popping in as the player crests the road, and a whole class of
/// "it only happens when you approach from the north" bug. The unload path survives only in
/// <see cref="UnloadAll"/>, which is what a region transition calls.
///
/// ⚠️ <b>Both regions cannot be resident at once</b> and this is not the code's fault: Frostfang's
/// <c>dragon_roost</c> (25, 0, -20) and <c>ancient_aerie</c> (25, 0, -110) share coordinate space
/// with the Ember Crown's <c>arena</c> (55, 0, -10) and <c>wilds_north</c> (0, 0, -65). Two regions
/// loaded together would be two worlds inside each other. Whole-realm residency needs the world
/// re-laid-out first, which is a Phase 44 (region design) decision and not this node's.
///
/// Pausable (default process mode), so loading halts while the game is paused. The procedural
/// sandbox is the always-loaded base — only the region's authored <see cref="RegionResource.Cells"/>
/// are managed here.
/// </summary>
public sealed partial class RegionStreamer : Node3D
{
    /// <summary>Max cells instanced per frame, so a wave of loads spreads across frames (no hitch).
    /// Five cells is five frames, and the loading screen already waits on <see cref="IsSettled"/>.</summary>
    private const int LoadsPerFrame = 1;

    private readonly List<RegionCellResource> _cells = new();
    private readonly Dictionary<string, Node3D> _loaded = new();
    private readonly List<RegionCellResource> _pending = new();
    private readonly HashSet<string> _pendingIds = new();

    /// <summary>The region currently being streamed, or empty before the first <see cref="Configure"/>.
    /// The streamer is re-configured at both places the active region changes (world build and each
    /// hard transition), so this is the cheapest honest answer to "where is the player" for systems
    /// that need it — the <see cref="EncounterDirector"/>'s region gate reads it.</summary>
    public string ActiveRegionId { get; private set; } = string.Empty;

    /// <summary>Caches the region's cells; the streamer manages exactly these.</summary>
    public void Configure(RegionResource? region)
    {
        _cells.Clear();
        ActiveRegionId = region?.Id ?? string.Empty;
        if (region == null)
        {
            return;
        }

        foreach (RegionCellResource cell in region.Cells)
        {
            if (cell != null)
            {
                _cells.Add(cell);
            }
        }
    }

    /// <summary>Frees every loaded cell and clears the pending queue (Phase 25C hard transitions).
    /// Call before <see cref="Configure"/> when re-targeting to a new region, or the old region's
    /// loaded cells orphan in the loaded set and never unload.</summary>
    public void UnloadAll()
    {
        foreach (string cellId in new List<string>(_loaded.Keys))
        {
            Unload(cellId);
        }

        _pending.Clear();
        _pendingIds.Clear();
    }

    /// <summary>True when nothing is queued and every one of the region's cells is loaded — the world
    /// has finished coming in. The bootstrap gates the post-transition loading screen on this (Phase
    /// 25.5B) instead of a fixed delay, so the screen holds exactly as long as the cells need and no
    /// longer. Since 38M2 that means <em>all</em> of them rather than the ones near the landing point,
    /// which is a few extra frames and the whole point of the change.</summary>
    public bool IsSettled() => _pending.Count == 0 && _loaded.Count == _cells.Count;

    public override void _Process(double delta)
    {
        // No player lookup and no distance test any more: every cell of the active region belongs in
        // the tree, so the only question left is whether it is there yet.
        foreach (RegionCellResource cell in _cells)
        {
            if (!_loaded.ContainsKey(cell.Id))
            {
                Enqueue(cell);
            }
        }

        DrainLoadQueue();
    }

    private void Enqueue(RegionCellResource cell)
    {
        if (_pendingIds.Add(cell.Id))
        {
            _pending.Add(cell);
        }
    }

    /// <summary>Instances up to <see cref="LoadsPerFrame"/> queued cells this frame.</summary>
    private void DrainLoadQueue()
    {
        int budget = LoadsPerFrame;
        while (budget > 0 && _pending.Count > 0)
        {
            RegionCellResource cell = _pending[0];
            _pending.RemoveAt(0);
            _pendingIds.Remove(cell.Id);

            if (_loaded.ContainsKey(cell.Id))
            {
                continue; // already loaded since it was queued
            }

            Load(cell);
            budget--;
        }
    }

    private void Load(RegionCellResource cell)
    {
        if (string.IsNullOrEmpty(cell.ScenePath))
        {
            return;
        }

        var scene = GD.Load<PackedScene>(cell.ScenePath);
        if (scene?.Instantiate() is not Node3D root)
        {
            Log.Warn($"RegionStreamer: cell '{cell.Id}' scene '{cell.ScenePath}' failed to instance.");
            return;
        }

        root.Name = cell.Id;
        root.Position = cell.Center;
        AddChild(root);
        _loaded[cell.Id] = root;

        Log.Info($"RegionStreamer: loaded cell '{cell.Id}'.");
        EventBus.Instance?.Publish(new RegionCellLoadedEvent(cell.Id, root));
    }

    private void Unload(string cellId)
    {
        if (!_loaded.TryGetValue(cellId, out Node3D? root))
        {
            return;
        }

        // Announce before freeing so 25D persistence can capture cell state.
        EventBus.Instance?.Publish(new RegionCellUnloadedEvent(cellId));
        _loaded.Remove(cellId);
        root.QueueFree();
        Log.Info($"RegionStreamer: unloaded cell '{cellId}'.");
    }
}
