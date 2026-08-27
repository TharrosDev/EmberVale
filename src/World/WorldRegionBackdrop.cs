using Godot;

namespace Embervale.World;

/// <summary>One-draw-call, non-playable macro landscape outside a region's authored cell lattice.</summary>
public sealed partial class WorldRegionBackdrop : MultiMeshInstance3D
{
    public static WorldRegionBackdrop Create(WorldEnvironmentProfileResource profile)
    {
        var mountain = new CylinderMesh
        {
            TopRadius = 0.08f,
            BottomRadius = 1f,
            Height = 1f,
            RadialSegments = 7,
            Rings = 2,
            Material = new StandardMaterial3D
            {
                AlbedoColor = profile.BackdropColor,
                Roughness = 1f,
            },
        };
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mountain,
            InstanceCount = profile.BackdropCount,
        };

        for (int i = 0; i < profile.BackdropCount; i++)
        {
            float angle = Mathf.Tau * i / profile.BackdropCount;
            float radial = profile.BackdropRadius * (0.90f + (WorldSceneryMath.Unit(701, i) * 0.18f));
            float height = WorldSceneryMath.RidgeHeight(811, i, profile.BackdropHeight);
            float width = height * (0.48f + (WorldSceneryMath.Unit(919, i) * 0.20f));
            var basis = Basis.Identity.Scaled(new Vector3(width, height, width * 0.78f));
            Vector3 position = profile.BackdropCenter + new Vector3(Mathf.Cos(angle) * radial, height * 0.5f - 1f,
                Mathf.Sin(angle) * radial);
            multiMesh.SetInstanceTransform(i, new Transform3D(basis, position));
        }

        return new WorldRegionBackdrop
        {
            Name = "DistantLandscape",
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = profile.BackdropRadius * 2.2f,
        };
    }
}
