using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>
/// Coarse world-visibility manager above engine frustum/LOD culling. Gameplay roots stay resident;
/// only cosmetic biome batches are disabled beyond the region's authored visibility distance.
/// Detailed and HLOD instances inside each batch cross-fade through GeometryInstance ranges.
///
/// It is also the one place the scatter learns about <see cref="WorldQualityScale"/>: the cull
/// distance is multiplied by the draw distance, and every resident cell's tiles are rescaled when
/// the scale changes or when a cell arrives while it is not 1.
/// </summary>
public sealed partial class WorldVisibilityManager : Node
{
    private readonly Dictionary<string, WorldBiomeScatter> _scatter = new();
    private WorldPerformanceBudgetResource? _budget;
    private double _timer;

    public int VisibleScatterCells { get; private set; }

    public override void _EnterTree() => WorldQualityScale.Changed += OnQualityChanged;

    public override void _ExitTree() => WorldQualityScale.Changed -= OnQualityChanged;

    public void Configure(WorldPerformanceBudgetResource? budget)
    {
        _budget = budget;
        _scatter.Clear();
        _timer = 0d;
    }

    public void RecordCellLoaded(string cellId, WorldBiomeScatter? scatter)
    {
        if (scatter != null)
        {
            _scatter[cellId] = scatter;
            // A no-op at the default scale, so a default-quality load does no per-tile work here.
            scatter.ApplyQuality();
        }
    }

    public void RecordCellUnloaded(string cellId) => _scatter.Remove(cellId);

    public override void _Process(double delta)
    {
        if (_budget == null)
        {
            return;
        }

        _timer += delta;
        if (_timer < _budget.VisibilityUpdateInterval)
        {
            return;
        }
        _timer = 0d;

        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            return;
        }

        float limit = _budget.BiomeCullDistance * WorldQualityScale.DrawDistance;
        float limitSquared = limit * limit;
        Vector3 eye = camera.GlobalPosition;
        int visible = 0;
        foreach (WorldBiomeScatter scatter in _scatter.Values)
        {
            if (!IsInstanceValid(scatter))
            {
                continue;
            }
            bool show = eye.DistanceSquaredTo(scatter.GlobalPosition) <= limitSquared;
            scatter.SetShown(show);
            if (show)
            {
                visible++;
            }
        }
        VisibleScatterCells = visible;
    }

    private void OnQualityChanged()
    {
        foreach (WorldBiomeScatter scatter in _scatter.Values)
        {
            if (IsInstanceValid(scatter))
            {
                scatter.ApplyQuality();
            }
        }
        // Re-run the coarse cull on the next frame instead of up to an interval later.
        _timer = double.MaxValue;
    }
}
