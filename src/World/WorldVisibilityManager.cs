using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>
/// Coarse world-visibility manager above engine frustum/LOD culling. Gameplay roots stay resident;
/// only cosmetic biome batches are disabled beyond the region's authored visibility distance.
/// Detailed and HLOD instances inside each batch cross-fade through GeometryInstance ranges.
/// </summary>
public sealed partial class WorldVisibilityManager : Node
{
    private readonly Dictionary<string, WorldBiomeScatter> _scatter = new();
    private WorldPerformanceBudgetResource? _budget;
    private double _timer;

    public int VisibleScatterCells { get; private set; }

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

        float limitSquared = _budget.BiomeCullDistance * _budget.BiomeCullDistance;
        int visible = 0;
        foreach (WorldBiomeScatter scatter in _scatter.Values)
        {
            if (!IsInstanceValid(scatter))
            {
                continue;
            }
            bool show = camera.GlobalPosition.DistanceSquaredTo(scatter.GlobalPosition) <= limitSquared;
            scatter.Visible = show;
            if (show)
            {
                visible++;
            }
        }
        VisibleScatterCells = visible;
    }
}
