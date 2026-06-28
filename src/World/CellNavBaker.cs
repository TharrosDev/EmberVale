using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.World;

/// <summary>
/// Bakes a streamed cell's navmesh at load time (Phase 27A). A greybox cell can't ship a pre-baked
/// <see cref="NavigationMesh"/> (the geometry is authored as plain meshes, not hand-tessellated
/// polygons), so the parent <see cref="NavigationRegion3D"/> bakes from the cell's mesh geometry the
/// moment the <see cref="RegionStreamer"/> instances it. The bake runs on a worker thread and applies
/// itself when finished; until then enemy <c>NavigationAgent3D</c>s simply fall back to straight-line
/// steering (see <c>EnemyAIComponent.NextPathPoint</c>), so there is never a hard dependency on the
/// bake having completed.
///
/// Attach this as a child of the cell's <see cref="NavigationRegion3D"/> in the cell scene.
/// </summary>
[GlobalClass]
public partial class CellNavBaker : Node
{
    public override void _Ready()
    {
        if (GetParent() is not NavigationRegion3D region)
        {
            Log.Warn($"{nameof(CellNavBaker)} must be a child of a NavigationRegion3D; skipping bake.");
            return;
        }

        if (region.NavigationMesh == null)
        {
            Log.Warn($"{nameof(CellNavBaker)}: region '{region.Name}' has no NavigationMesh; skipping bake.");
            return;
        }

        // ponytail: on-thread bake at cell load — fine for greybox cell sizes; revisit if a cell's
        // geometry grows large enough that the bake stalls a worker noticeably.
        region.BakeNavigationMesh();
    }
}
