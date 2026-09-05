using System;
using Godot;

namespace Embervale.World;

/// <summary>
/// Committed, deterministic runtime data for one region. The expensive procedural field is sampled
/// by <c>tools/world_bake.py --bake</c>; gameplay loads this compact grid and the prepared cell
/// scenes instead of solving macro geography, hydrology, terrain, scatter and navigation again.
/// </summary>
[GlobalClass]
public partial class WorldPreparedRegionResource : Resource
{
    public const int CurrentSchema = 1;
    public const float MissingWater = -100000f;

    [Export] public int Schema { get; set; } = CurrentSchema;
    [Export] public string RegionId { get; set; } = string.Empty;
    [Export] public string SourceSignature { get; set; } = string.Empty;
    [Export] public float MinX { get; set; }
    [Export] public float MinZ { get; set; }
    [Export] public float SampleStep { get; set; } = 1f;
    [Export] public int Columns { get; set; }
    [Export] public int Rows { get; set; }
    [Export] public float[] Heights { get; set; } = Array.Empty<float>();
    [Export] public float[] GeneratedWaterSurfaces { get; set; } = Array.Empty<float>();
    [Export] public PackedScene? Backdrop { get; set; }

    public bool IsValidFor(RegionResource region) =>
        Schema == CurrentSchema && RegionId == region.Id && Columns >= 2 && Rows >= 2 &&
        SourceSignature.Length == 64 && SampleStep > 0f && Heights.Length == Columns * Rows &&
        GeneratedWaterSurfaces.Length == Heights.Length;

    public WorldHeightfield CreateRuntimeField(RegionResource region)
    {
        WorldGenerationSettings settings = region.GenerationProfile?.Settings() ?? new WorldGenerationSettings
        {
            Seed = region.EnvironmentProfile?.TerrainSeed ?? 3800,
            Version = 1,
            LocalRelief = region.EnvironmentProfile?.Relief ?? 1f,
            DetailScale = region.EnvironmentProfile?.DetailScale ?? 2.5f,
        };
        return new PreparedWorldHeightfield(settings, this);
    }

    /// <summary>Offline view: committed elevation/water plus the source field's biome and authored
    /// masks. Those presentation attributes are consumed into the prepared meshes/scatter and are
    /// not needed by normal runtime queries afterward.</summary>
    public WorldHeightfield CreateBakeField(RegionResource region, WorldHeightfield source) =>
        new PreparedWorldHeightfield(source.Settings, this, source);

    internal float SampleHeight(float x, float z) => Sample(Heights, x, z, 0f);

    internal float? SampleWater(float x, float z)
    {
        float value = Sample(GeneratedWaterSurfaces, x, z, MissingWater);
        return value <= MissingWater * 0.5f ? null : value;
    }

    internal bool HasWater(float minX, float minZ, float maxX, float maxZ)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - MinX) / SampleStep), 0, Columns - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - MinZ) / SampleStep), 0, Rows - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - MinX) / SampleStep), 0, Columns - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - MinZ) / SampleStep), 0, Rows - 1);
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (GeneratedWaterSurfaces[(z * Columns) + x] > MissingWater * 0.5f)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private float Sample(float[] values, float x, float z, float fallback)
    {
        if (Columns < 2 || Rows < 2 || SampleStep <= 0f || values.Length != Columns * Rows)
        {
            return fallback;
        }

        float gx = Mathf.Clamp((x - MinX) / SampleStep, 0f, Columns - 1f);
        float gz = Mathf.Clamp((z - MinZ) / SampleStep, 0f, Rows - 1f);
        int x0 = Mathf.FloorToInt(gx);
        int z0 = Mathf.FloorToInt(gz);
        int x1 = Mathf.Min(x0 + 1, Columns - 1);
        int z1 = Mathf.Min(z0 + 1, Rows - 1);
        float tx = gx - x0;
        float tz = gz - z0;

        float a = values[(z0 * Columns) + x0];
        float b = values[(z0 * Columns) + x1];
        float c = values[(z1 * Columns) + x0];
        float d = values[(z1 * Columns) + x1];
        if (a <= MissingWater * 0.5f || b <= MissingWater * 0.5f ||
            c <= MissingWater * 0.5f || d <= MissingWater * 0.5f)
        {
            // Water is discontinuous at a shore; interpolation across the sentinel would invent a
            // vertical wall. Use the nearest sample while height remains smoothly bilinear.
            return values[(Mathf.RoundToInt(gz) * Columns) + Mathf.RoundToInt(gx)];
        }
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }
}

/// <summary>Read-only runtime view over a baked region grid.</summary>
internal sealed class PreparedWorldHeightfield : WorldHeightfield
{
    private readonly WorldPreparedRegionResource _prepared;
    private readonly WorldHeightfield? _presentationSource;

    public PreparedWorldHeightfield(
        WorldGenerationSettings settings, WorldPreparedRegionResource prepared,
        WorldHeightfield? presentationSource = null)
        : base(settings, presentationSource?.Landforms, presentationSource?.Paths, presentationSource?.Areas)
    {
        _prepared = prepared;
        _presentationSource = presentationSource;
    }

    public override float GeneratedElevation(float worldX, float worldZ) => Height(worldX, worldZ);
    public override float BaseHeight(float worldX, float worldZ) => Height(worldX, worldZ);
    public override float Height(float worldX, float worldZ) => _prepared.SampleHeight(worldX, worldZ);
    public override float? GeneratedWaterSurface(float worldX, float worldZ) => _prepared.SampleWater(worldX, worldZ);
    public override bool MayHaveGeneratedWater(float minX, float minZ, float maxX, float maxZ) =>
        _prepared.HasWater(minX, minZ, maxX, maxZ);

    public override WorldSample Sample(float worldX, float worldZ)
    {
        WorldSample ground = RuntimeSample(worldX, worldZ);
        if (_presentationSource == null)
        {
            return ground;
        }
        WorldSample presentation = _presentationSource.Sample(worldX, worldZ);
        return presentation with
        {
            Elevation = ground.Elevation,
            NormalX = ground.NormalX,
            NormalY = ground.NormalY,
            NormalZ = ground.NormalZ,
            Slope = ground.Slope,
            Curvature = ground.Curvature,
            RoadInfluence = PathMask(worldX, worldZ),
            AuthoredInfluence = AreaMask(worldX, worldZ),
        };
    }

    public override WorldSample SampleEnvironment(float worldX, float worldZ) =>
        _presentationSource?.SampleEnvironment(worldX, worldZ) ?? RuntimeSample(worldX, worldZ);
    public override WorldHeightfield ForBounds(float minX, float minZ, float maxX, float maxZ, float margin = 24f) => this;

    private WorldSample RuntimeSample(float x, float z)
    {
        float here = Height(x, z);
        (float nx, float ny, float nz) = NormalAt(x, z);
        float slope = SlopeAt(x, z, here);
        float curvature = Height(x - 1f, z) + Height(x + 1f, z) +
                          Height(x, z - 1f) + Height(x, z + 1f) - (4f * here);
        return new WorldSample(
            here, here, nx, ny, nz, slope, curvature,
            0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
    }
}
