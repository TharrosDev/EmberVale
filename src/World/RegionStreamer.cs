using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Godot;

namespace Embervale.World;

/// <summary>
/// Streams a region's sub-cells (Phase 25B): instances each <see cref="RegionCellResource"/>'s scene
/// when the player comes within its <see cref="RegionCellResource.LoadRadius"/> and frees it when
/// they leave (plus <see cref="UnloadMargin"/> of hysteresis). Pure load/unload logic lives in
/// <see cref="StreamDecision"/>; this node owns the per-frame instancing budget so a multi-cell wave
/// never hitches, and publishes <see cref="RegionCellLoadedEvent"/>/<see cref="RegionCellUnloadedEvent"/>
/// (the seam Phase 25D's persistence hooks).
///
/// Pausable (default process mode), so streaming halts while the game is paused. The procedural
/// sandbox is the always-loaded base — only the region's authored <see cref="RegionResource.Cells"/>
/// are streamed.
/// </summary>
public sealed partial class RegionStreamer : Node3D
{
    /// <summary>Extra distance beyond a cell's load radius before it unloads (hysteresis, metres).</summary>
    public const float UnloadMargin = 10f;

    /// <summary>Max cells instanced per frame, so a wave of loads spreads across frames (no hitch).</summary>
    private const int LoadsPerFrame = 1;

    private readonly List<RegionCellResource> _cells = new();
    private readonly Dictionary<string, Node3D> _loaded = new();
    private readonly List<RegionCellResource> _pending = new();
    private readonly HashSet<string> _pendingIds = new();

    /// <summary>Caches the region's cells; the streamer manages exactly these.</summary>
    public void Configure(RegionResource? region)
    {
        _cells.Clear();
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

    public override void _Process(double delta)
    {
        if (_cells.Count == 0 || ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player))
        {
            return;
        }

        Vector3 origin = player.GlobalPosition;
        foreach (RegionCellResource cell in _cells)
        {
            float distance = PlanarDistance(origin, cell.Center);
            bool isLoaded = _loaded.ContainsKey(cell.Id);
            switch (StreamDecision.Decide(distance, cell.LoadRadius, UnloadMargin, isLoaded))
            {
                case StreamAction.Load:
                    Enqueue(cell);
                    break;
                case StreamAction.Unload:
                    Unload(cell.Id);
                    break;
            }
        }

        DrainLoadQueue();
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return Mathf.Sqrt((dx * dx) + (dz * dz));
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
