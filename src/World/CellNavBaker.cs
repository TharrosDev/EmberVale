using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.World;

/// <summary>
/// Compatibility baker for source scenes and world-authoring debug mode. Production cell scenes
/// contain navigation prepared by <c>tools/world_bake.py</c>, and this node exits immediately when
/// polygons already exist. Runtime activation never rebuilds prepared navigation.
///
/// Attach this as a child of the cell's <see cref="NavigationRegion3D"/> in the cell scene.
/// </summary>
[GlobalClass]
public partial class CellNavBaker : Node
{
    /// <summary>The offline packer owns navigation while set; scene-authored bakers stay inert.</summary>
    public static bool RuntimeBakeSuppressed { get; set; }

    public override void _Ready()
    {
        if (RuntimeBakeSuppressed)
        {
            return;
        }
        // A cell root may finish configuring inherited geometry in its own _Ready (RoostCell, for
        // example, resizes the shared base floor). Child _Ready runs first, so baking immediately
        // captured the 90 m editor placeholder before an Ash Roost resized it to 100 m and left a
        // real 5 m navigation gap at its clan-hold seam. Defer one idle turn so authored runtime
        // geometry and collision are final before parsing them.
        Callable.From(Bake).CallDeferred();
    }

    private void Bake()
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

        // Production cell scenes already contain navigation polygons. Re-baking them would turn an
        // offline artifact back into a runtime workload and briefly remove navigation at activation.
        if (region.NavigationMesh.GetPolygonCount() > 0)
        {
            return;
        }

        // Inherited roost scenes share the base scene's NavigationMesh resource. Baking two of those
        // cells in the same resident region otherwise asks Godot to mutate one resource twice at the
        // same time ("NavigationMesh is already baking"). Each cell owns its bake result.
        region.NavigationMesh = (NavigationMesh)region.NavigationMesh.Duplicate(true);

        // ponytail: on-thread bake at cell load — fine for greybox cell sizes; revisit if a cell's
        // geometry grows large enough that the bake stalls a worker noticeably.
        region.BakeNavigationMesh();
    }
}
