using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>Development-only cell boundary/tier overlay; hidden and idle by default.</summary>
internal sealed partial class WorldStreamingDebugDraw : MeshInstance3D
{
    private ImmediateMesh? _mesh;

    public void Rebuild(
        IReadOnlyList<RegionCellResource> cells,
        IReadOnlyDictionary<string, WorldStreamingTier> tiers)
    {
        _mesh?.Dispose();
        _mesh = new ImmediateMesh();
        Mesh = _mesh;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
        };
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        foreach (RegionCellResource cell in cells)
        {
            if (cell.Presentation == null)
            {
                continue;
            }
            float hx = cell.Presentation.Width * 0.5f;
            float hz = cell.Presentation.Depth * 0.5f;
            var corners = new[]
            {
                Point(cell.Center.X - hx, cell.Center.Z - hz),
                Point(cell.Center.X + hx, cell.Center.Z - hz),
                Point(cell.Center.X + hx, cell.Center.Z + hz),
                Point(cell.Center.X - hx, cell.Center.Z + hz),
            };
            _mesh.SurfaceSetColor(ColorFor(tiers.GetValueOrDefault(cell.Id)));
            for (int i = 0; i < 4; i++)
            {
                _mesh.SurfaceAddVertex(corners[i]);
                _mesh.SurfaceAddVertex(corners[(i + 1) % 4]);
            }
            Vector3 center = Point(cell.Center.X, cell.Center.Z);
            _mesh.SurfaceAddVertex(center);
            _mesh.SurfaceAddVertex(center + (Vector3.Up * (3f + (int)tiers.GetValueOrDefault(cell.Id))));
        }
        _mesh.SurfaceEnd();
    }

    public override void _ExitTree()
    {
        Mesh = null;
        _mesh?.Dispose();
        _mesh = null;
    }

    private static Vector3 Point(float x, float z) =>
        new(x, WorldGround.HeightAt(x, z) + 1.5f, z);

    private static Color ColorFor(WorldStreamingTier tier) => tier switch
    {
        WorldStreamingTier.Near => Colors.LimeGreen,
        WorldStreamingTier.Mid => Colors.Gold,
        WorldStreamingTier.Far => Colors.DeepSkyBlue,
        WorldStreamingTier.Backdrop => Colors.MediumPurple,
        _ => Colors.DimGray,
    };
}
