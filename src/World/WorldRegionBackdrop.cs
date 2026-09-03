using Godot;

namespace Embervale.World;

/// <summary>
/// The land beyond the playable lattice: one non-colliding mesh that continues the ground outward
/// into mountains and closes the horizon.
///
/// ⚠️ <b>IT WAS TWENTY-SIX CONES ON A CIRCLE AND EVERY WIDE SHOT IN THE REPOSITORY SHOWED IT.</b>
/// A <c>CylinderMesh</c> with a 0.08 top radius and seven radial segments is a grey pyramid, and a
/// ring of them at one radius is a ring of grey pyramids — visibly regular, visibly separate from
/// the ground they stood behind, and visibly a fence. Worse, it did not solve the problem it was
/// there for: the terrain still ENDED at the lattice edge, so an elevated vista looked out over a
/// sheer clipped drop into fog with the pyramids floating past it.
///
/// This builds a <b>picture frame of real terrain</b> instead — a coarse grid over everything
/// outside the cell lattice, ramping up out of the playable edge into ridged mountain relief. It is
/// still ONE draw call and still has no collision, so the performance contract is unchanged; what
/// it buys is a horizon that is made of the same substance as the ground, occludes properly, takes
/// the sun the same way, and hides the region's edge inside its own foothills.
///
/// ⚠️ <b>IT SAMPLES THE REGION HEIGHTFIELD, SO THE JOIN IS NOT A JOIN.</b> The field is a pure
/// function of world X/Z and is defined outside the cells as well as inside them, so the frame is
/// literally the same surface continued — it agrees with the playable ground at the rim by
/// construction, exactly the way two abutting cells do. It is held 0.6 m under so the two meshes
/// never z-fight, and the mountains are added on top of that as distance grows.
/// </summary>
public sealed partial class WorldRegionBackdrop : MultiMeshInstance3D
{
    /// <summary>How far under the playable mesh the frame is held, to stop the two z-fighting.</summary>
    private const float InnerDrop = 0.6f;

    /// <summary>Grid divisions across the whole framed area. 64 is ~11 m cells on a 700 m frame,
    /// which is finer than the horizon needs and a third of the samples 88 cost.</summary>
    private const int Divisions = 64;

    /// <summary>
    /// How far outside the lattice the frame still samples the REAL region field.
    ///
    /// ⚠️ <b>THIS IS A LOAD-TIME BUDGET, NOT A LOOK DECISION.</b> <see cref="WorldHeightfield.Height"/>
    /// on the unclipped region field walks every landform, road and yard in the realm — a hundred and
    /// fifty of them — and the frame samples it five times per vertex. Doing that over the whole
    /// 700 m frame put three seconds on every region entry for ground the player is four hundred
    /// metres from. Inside this margin the two surfaces must agree exactly, so the real field is
    /// used; outside it the authored geography has faded to nothing anyway and the noise alone is
    /// indistinguishable.
    /// </summary>
    private const float FieldMargin = 45f;

    public override void _ExitTree()
    {
        if (Multimesh is not { } multiMesh)
        {
            return;
        }

        Mesh? mesh = multiMesh.Mesh;
        Material? material = mesh?.GetSurfaceCount() > 0 ? mesh.SurfaceGetMaterial(0) : null;
        Multimesh = null;
        multiMesh.Dispose();
        material?.Dispose();
        mesh?.Dispose();
    }

    public static WorldRegionBackdrop Create(
        WorldEnvironmentProfileResource profile, RegionResource region, WorldHeightfield field)
    {
        (float minX, float minZ, float maxX, float maxZ) = LatticeBounds(region);
        float reach = Mathf.Max(120f, profile.BackdropRadius);
        float outerMinX = minX - reach;
        float outerMinZ = minZ - reach;
        float outerMaxX = maxX + reach;
        float outerMaxZ = maxZ + reach;

        float stepX = (outerMaxX - outerMinX) / Divisions;
        float stepZ = (outerMaxZ - outerMinZ) / Divisions;
        int row = Divisions + 1;

        var heights = new float[row * row];
        var inside = new bool[row * row];
        for (int z = 0; z <= Divisions; z++)
        {
            float worldZ = outerMinZ + (z * stepZ);
            for (int x = 0; x <= Divisions; x++)
            {
                float worldX = outerMinX + (x * stepX);
                int index = (z * row) + x;
                // ⚠️ THE HOLE IS ONE GRID CELL SMALLER THAN THE LATTICE, ON PURPOSE. Cutting it at
                // the exact lattice rectangle leaves up to a full grid step of nothing between the
                // playable terrain's edge and the frame's first quad — a bright slot of open sky
                // running down the region boundary, which is what every overview shot showed. Held
                // in by a step, the frame passes UNDER the outer cells and the slot cannot exist.
                inside[index] = worldX > minX + stepX && worldX < maxX - stepX &&
                                worldZ > minZ + stepZ && worldZ < maxZ - stepZ;
                heights[index] = Elevation(profile, field, worldX, worldZ, minX, minZ, maxX, maxZ, reach);
            }
        }

        var vertices = new System.Collections.Generic.List<Vector3>(row * row);
        var normals = new System.Collections.Generic.List<Vector3>(row * row);
        var colors = new System.Collections.Generic.List<Color>(row * row);
        var indices = new System.Collections.Generic.List<int>();
        var vertexIndex = new int[row * row];

        for (int i = 0; i < vertexIndex.Length; i++)
        {
            vertexIndex[i] = -1;
        }

        for (int z = 0; z <= Divisions; z++)
        {
            float worldZ = outerMinZ + (z * stepZ);
            for (int x = 0; x <= Divisions; x++)
            {
                int index = (z * row) + x;
                if (inside[index])
                {
                    continue;
                }

                float worldX = outerMinX + (x * stepX);
                float here = heights[index];
                float left = Elevation(profile, field, worldX - stepX, worldZ, minX, minZ, maxX, maxZ, reach);
                float right = Elevation(profile, field, worldX + stepX, worldZ, minX, minZ, maxX, maxZ, reach);
                float back = Elevation(profile, field, worldX, worldZ - stepZ, minX, minZ, maxX, maxZ, reach);
                float forward = Elevation(profile, field, worldX, worldZ + stepZ, minX, minZ, maxX, maxZ, reach);

                vertexIndex[index] = vertices.Count;
                vertices.Add(new Vector3(worldX, here, worldZ));
                normals.Add(new Vector3(left - right, stepX + stepZ, back - forward).Normalized());
                // Aerial perspective baked into vertex colour: the further and higher the land, the
                // more it washes toward the sky. One multiply, and it is what turns a grey mass into
                // a range with depth in it.
                float haze = Mathf.Clamp(
                    (OutsideDistance(worldX, worldZ, minX, minZ, maxX, maxZ) / reach * 0.75f) +
                    (here / Mathf.Max(1f, profile.BackdropHeight) * 0.25f), 0f, 1f);
                colors.Add(new Color(1f - (haze * 0.28f), 1f - (haze * 0.22f), 1f - (haze * 0.12f)));
            }
        }

        for (int z = 0; z < Divisions; z++)
        {
            for (int x = 0; x < Divisions; x++)
            {
                int a = vertexIndex[(z * row) + x];
                int b = vertexIndex[(z * row) + x + 1];
                int c = vertexIndex[((z + 1) * row) + x];
                int d = vertexIndex[((z + 1) * row) + x + 1];
                if (a < 0 || b < 0 || c < 0 || d < 0)
                {
                    continue;
                }

                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
                indices.Add(b);
                indices.Add(d);
                indices.Add(c);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = profile.BackdropColor,
            Roughness = 1f,
            VertexColorUseAsAlbedo = true,
            // The frame is 700 m of very coarse triangles. Specular on it reads as facets.
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        });

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = 1,
        };
        multiMesh.SetInstanceTransform(0, Transform3D.Identity);

        return new WorldRegionBackdrop
        {
            Name = "DistantLandscape",
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>
    /// The lattice the frame surrounds, from the cells themselves rather than from
    /// <see cref="RegionResource.Bounds"/> — Bounds is the lattice plus an authored margin, and a
    /// frame that started at the margin would leave a moat of nothing around the playable edge.
    /// </summary>
    private static (float MinX, float MinZ, float MaxX, float MaxZ) LatticeBounds(RegionResource region)
    {
        float minX = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxZ = float.MinValue;
        foreach (RegionCellResource cell in region.Cells)
        {
            if (cell?.Presentation == null)
            {
                continue;
            }
            float halfWidth = cell.Presentation.Width * 0.5f;
            float halfDepth = cell.Presentation.Depth * 0.5f;
            minX = Mathf.Min(minX, cell.Center.X - halfWidth);
            maxX = Mathf.Max(maxX, cell.Center.X + halfWidth);
            minZ = Mathf.Min(minZ, cell.Center.Z - halfDepth);
            maxZ = Mathf.Max(maxZ, cell.Center.Z + halfDepth);
        }

        return minX > maxX
            ? (-100f, -100f, 100f, 100f)
            : (minX, minZ, maxX, maxZ);
    }

    private static float OutsideDistance(
        float x, float z, float minX, float minZ, float maxX, float maxZ)
    {
        float dx = Mathf.Max(Mathf.Max(minX - x, x - maxX), 0f);
        float dz = Mathf.Max(Mathf.Max(minZ - z, z - maxZ), 0f);
        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>
    /// Ridged noise, ramped by how far outside the lattice the sample is. Ridged rather than plain
    /// value noise because plain noise makes rolling hills at any amplitude, and the silhouette a
    /// horizon needs is crests and saddles.
    /// </summary>
    /// <summary>
    /// The region's own ground function continued outward, plus ridged mountain relief that ramps
    /// in with distance.
    ///
    /// ⚠️ <b>IT SAMPLES THE REAL HEIGHTFIELD AND THAT IS THE WHOLE JOIN.</b> The first cut used a
    /// constant drop below the lattice, which put a two-hundred-metre flat grey shelf around the
    /// realm — visible from every elevated vista and, from the Western Wilds, the largest object on
    /// screen. <see cref="WorldHeightfield"/> is defined everywhere, not only inside the cells, and
    /// its landform influence fades out on its own past the authored geography; so evaluating it
    /// out here makes the frame literally the same surface, agreeing at the rim to the millimetre.
    /// The 0.6 m dip keeps it just under the playable mesh so the two never z-fight.
    /// </summary>
    private static float Elevation(
        WorldEnvironmentProfileResource profile, WorldHeightfield field, float x, float z,
        float minX, float minZ, float maxX, float maxZ, float reach)
    {
        float outside = OutsideDistance(x, z, minX, minZ, maxX, maxZ);
        int seed = profile.TerrainSeed + 4177;
        float scale = 1f / Mathf.Max(40f, profile.BackdropRadius * 0.42f);
        float ridge = Ridged(seed, x * scale, z * scale);
        ridge = (ridge * 0.62f) + (Ridged(seed + 31, x * scale * 2.7f, z * scale * 2.7f) * 0.26f) +
                (WorldTerrainMath.ValueNoise(seed + 97, x * scale * 6.1f, z * scale * 6.1f) * 0.12f);

        // Climb over the first ~38% of the reach and then hold: relief that keeps rising to the far
        // edge reads as a bowl the player is standing at the bottom of.
        float ramp = Mathf.SmoothStep(0f, reach * 0.38f, outside);
        // ⚠️ BEYOND THE MARGIN THE HORIZON IS THE SAME GEOGRAPHY, JUST CHEAPER. This used to fall
        // back to two octaves of value noise, so the far country was flat wobble while the ground
        // the player stood on had mountain systems in it — a visible line at the lattice edge where
        // the realm stopped having shape. PreliminaryElevation is the generator's own macro pipeline
        // without the hydrology carve or the authored stamps: the same continents, the same ranges,
        // at a fraction of the cost and with nothing out there to carve or stamp anyway.
        float ground = outside <= FieldMargin
            ? field.Height(x, z)
            : WorldGenerator.PreliminaryElevation(field.Settings, x, z);
        return ground - InnerDrop + (profile.BackdropHeight * ridge * ramp);
    }

    private static float Ridged(int seed, float x, float z) =>
        1f - Mathf.Abs((WorldTerrainMath.ValueNoise(seed, x, z) * 2f) - 1f);
}
