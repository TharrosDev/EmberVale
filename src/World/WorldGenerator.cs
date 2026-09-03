using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>Immutable, engine-free art direction for one version of the world generator.</summary>
public sealed record WorldGenerationSettings
{
    public int Seed { get; init; } = 3800;
    public int Version { get; init; } = 2;
    public float BaseElevation { get; init; }
    public float MacroScale { get; init; } = 420f;
    public float MacroRelief { get; init; } = 7f;
    public float MountainScale { get; init; } = 230f;
    public float MountainPrevalence { get; init; } = 0.48f;
    public float MountainHeight { get; init; } = 14f;
    public float ValleyStrength { get; init; } = 5f;
    public float ErosionStrength { get; init; } = 0.65f;
    public float LocalRelief { get; init; } = 1.4f;
    public float DetailScale { get; init; } = 2.75f;
    public float Temperature { get; init; } = 0.58f;
    public float Moisture { get; init; } = 0.52f;
    public float SnowLine { get; init; } = 34f;
    public float HydrologyCellSize { get; init; } = 12f;
    public float RiverThreshold { get; init; } = 16f;
    public float RiverWidth { get; init; } = 3.2f;
    public float RiverDepth { get; init; } = 1.8f;

    /// <summary>
    /// How far, in metres, an authored road or yard calms the macro relief around itself.
    ///
    /// ⚠️ <b>THIS IS THE ONE PLACE AUTHORED CONTENT REACHES BACK INTO THE GENERATOR, AND WITHOUT IT
    /// THE REALM'S ROADS ARE LADDERS.</b> A road is authored as a straight run between two fixed
    /// points and grades linearly between the ground at each end; drop a real mountain flank across
    /// it and those two points end up twenty metres apart vertically over twenty metres of ground,
    /// which is a 45-degree road. Eighteen of the realm's authored routes failed the walkable-grade
    /// gate the first time the generator ran under them, and re-authoring every one of them would
    /// only have to be done again the next time a region profile was re-tuned.
    ///
    /// So the mountain and local-relief terms fade out along authored circulation, over this
    /// distance, while the continental tilt does NOT — settlements and roads still sit on the
    /// realm's broad slope and still climb it, they simply do not have a peak dropped on them.
    /// That is the same claim a real map makes: roads and towns are in the gentle country, because
    /// that is where people put them.
    ///
    /// ⚠️ It is deliberately wide. A narrow calm radius is a trench with a road at the bottom, which
    /// is the artefact this exists to avoid rather than one it is allowed to produce.
    /// </summary>
    public float RouteCalm { get; init; } = 45f;

    public int Salt(int subsystem) => unchecked(Seed ^ (Version * 1_000_003) ^ subsystem);

    public string Signature => FormattableString.Invariant(
        $"{Version}:{Seed}:{MacroScale:F2}:{MacroRelief:F2}:{MountainScale:F2}:{MountainPrevalence:F3}:{MountainHeight:F2}:{ValleyStrength:F2}:{ErosionStrength:F3}:{LocalRelief:F2}:{DetailScale:F2}:{Temperature:F3}:{Moisture:F3}:{SnowLine:F2}:{HydrologyCellSize:F2}:{RiverThreshold:F2}:{RiverWidth:F2}:{RiverDepth:F2}:{RouteCalm:F2}");
}

/// <summary>One complete deterministic query. Biome weights are ordered lowland, wetland,
/// alpine and exposed/barren and always sum to one.</summary>
public readonly record struct WorldSample(
    float Elevation, float BaseElevation, float NormalX, float NormalY, float NormalZ,
    float Slope, float Curvature, float Continentalness, float Mountain,
    float Erosion, float Valley, float Temperature, float Moisture,
    float LowlandWeight, float WetlandWeight, float AlpineWeight, float BarrenWeight,
    float RiverInfluence, float WaterProximity, float Wetness,
    float RoadInfluence, float AuthoredInfluence)
{
    public float BiomeWeightSum => LowlandWeight + WetlandWeight + AlpineWeight + BarrenWeight;
}

internal readonly record struct ProceduralSample(
    float Elevation, float UncarvedElevation, float Continentalness, float Mountain,
    float Erosion, float Valley, float Temperature, float Moisture,
    float Lowland, float Wetland, float Alpine, float Barren,
    float River, float WaterProximity, float Wetness, float? WaterSurface);

/// <summary>
/// Version-two geography pipeline. Frequencies have distinct jobs: warped continental forms,
/// mountain systems, valley/erosion response, rolling relief, then restrained micro detail.
/// All random fields use independent salts, so changing ecology or hydrology cannot move a ridge.
/// </summary>
internal static class WorldGenerator
{
    private const int ContinentalSalt = 0x11A31;
    private const int WarpSalt = 0x22B47;
    private const int MountainSalt = 0x33C59;
    private const int ErosionSalt = 0x44D61;
    private const int ValleySalt = 0x55E73;
    private const int ReliefSalt = 0x66F89;
    private const int TemperatureSalt = 0x77091;
    private const int MoistureSalt = 0x88103;

    public static ProceduralSample Sample(
        WorldGenerationSettings settings, WorldMacroField? macro, WorldHydrologyMap? hydrology,
        float x, float z, float calm = 0f)
    {
        Preliminary(settings, macro, x, z, calm, out float elevation, out float continental,
            out float mountain, out float erosion, out float valley, out float temperature,
            out float moisture);

        HydrologySample water = hydrology?.Sample(x, z) ?? default;
        float carved = elevation - (water.RiverInfluence * settings.RiverDepth);
        moisture = Clamp01(moisture + water.WaterProximity * 0.34f + water.RiverInfluence * 0.22f);
        float wetness = Clamp01((moisture - 0.45f) * 1.6f + water.WaterProximity * 0.7f);

        float alpine = SmoothStep(settings.SnowLine - 9f, settings.SnowLine + 5f, carved) *
                       SmoothStep(0.38f, 0.72f, mountain);
        // ⚠️ WETLAND NEEDS WATER, AND THE FLOOR HERE USED TO SAY OTHERWISE. A `max(0.35, proximity)`
        // term gives every damp, flat acre in the realm a third of a wetland weight whether or not
        // there is any water within sight of it, and it put a quarter of a temperate lowland realm
        // into fen. A fen is where drainage collects; the proximity term is the whole point of it,
        // so it keeps only a small floor for genuinely waterlogged ground the coarse drainage solve
        // never resolved a channel for.
        float wetland = SmoothStep(0.62f, 0.9f, moisture) *
                        (1f - SmoothStep(0.28f, 0.62f, mountain)) *
                        (0.12f + (0.88f * water.WaterProximity));
        float barren = Clamp01(SmoothStep(0.58f, 0.9f, mountain) * (1f - moisture * 0.45f) +
                                SmoothStep(settings.SnowLine + 8f, settings.SnowLine + 24f, carved));
        float lowland = MathF.Max(0.001f, 1f - MathF.Max(alpine, MathF.Max(wetland, barren)));
        Normalise(ref lowland, ref wetland, ref alpine, ref barren);

        float? waterSurface = water.WaterSurface == null ? null :
            MathF.Max(water.WaterSurface.Value, carved + water.RiverInfluence * settings.RiverDepth * 0.45f);
        return new ProceduralSample(carved, elevation, continental, mountain, erosion, valley,
            temperature, moisture, lowland, wetland, alpine, barren,
            water.RiverInfluence, water.WaterProximity, wetness, waterSurface);
    }

    public static float PreliminaryElevation(
        WorldGenerationSettings settings, float x, float z, WorldMacroField? macro = null,
        float calm = 0f)
    {
        Preliminary(settings, macro, x, z, calm, out float elevation,
            out _, out _, out _, out _, out _, out _);
        return elevation;
    }

    /// <summary>
    /// The fast half of the pipeline. The slow fields come from <paramref name="cache"/> when there
    /// is one; only the rolling and fine terms, which vary at the scale a vertex actually resolves,
    /// are evaluated here.
    /// </summary>
    private static void Preliminary(
        WorldGenerationSettings s, WorldMacroField? cache, float x, float z, float calm,
        out float elevation, out float continental, out float mountain, out float erosion,
        out float valley, out float temperature, out float moisture)
    {
        calm = Clamp01(calm);
        if (s.Version <= 1)
        {
            elevation = WorldTerrainMath.BaseNoise(s.Seed, x, z, s.LocalRelief, s.DetailScale);
            continental = 0.5f;
            mountain = 0f;
            erosion = 0.5f;
            valley = 0f;
            temperature = s.Temperature;
            moisture = s.Moisture;
            return;
        }

        MacroFields macro = cache != null ? cache.Sample(x, z) : MacroAt(s, x, z);
        continental = macro.Continentalness;
        mountain = macro.Mountain;
        erosion = macro.Erosion;
        valley = macro.Valley;

        float erosionResponse = 1f - Clamp01(erosion * s.ErosionStrength);

        // ⚠️ THE GAIN IS UNDONE FOR THESE TWO, AND ONLY FOR THESE TWO. FbmGain exists so that a
        // field COMPARED AGAINST A THRESHOLD reaches the range those thresholds are written for.
        // Rolling relief and micro detail are not compared against anything — they are multiplied by
        // LocalRelief and added to the ground in METRES — so spreading them does not fix a threshold,
        // it silently triples the realm's small-scale relief and makes LocalRelief mean something
        // other than what its name and its range hint say. It also put a sixty-centimetre crest on a
        // four-metre wavelength, which is finer than the collision lattice can resolve: the collider
        // interpolated straight over it and handed the player ground 59 cm from the ground it draws.
        // Dividing the gain back out is what makes "1.4 metres of local relief" mean 1.4 metres.
        float rolling = ((Fbm(s.Salt(ReliefSalt), macro.WarpedX / 92f, macro.WarpedZ / 92f, 3) - 0.5f) * 2f) / FbmGain;
        float fineScale = MathF.Max(0.25f, s.DetailScale);
        float fine = ((Fbm(s.Salt(ReliefSalt + 43), x * 0.045f * fineScale,
            z * 0.045f * fineScale, 2) - 0.5f) * 0.24f) / FbmGain;
        float depositionalCalm = 1f - (valley * 0.78f);

        // ⚠️ THE CONTINENTAL TERM IS NOT CALMED AND THAT IS THE WHOLE POINT. Fading everything
        // would sink a settlement to a flat disc at sea level wherever a road reaches it — the
        // "hard flattening around POIs" artefact by another route. Fading only the terms that vary
        // fast leaves the realm's broad slope running straight through the town, so a road still
        // climbs and a market still sits on a hillside; what it cannot do is meet a peak.
        //
        // ⚠️ A valley cut across an authored road is the same defect as a peak dropped on one, so it
        // fades just as hard. What survives at full calm is the continental tilt, which is the only
        // term slow enough that a road can climb it at a grade a person can walk.
        float mountainCalm = 1f - (calm * 0.97f);
        float valleyCalm = 1f - (calm * 0.94f);
        float reliefCalm = 1f - (calm * 0.8f);
        elevation = s.BaseElevation + (macro.ContinentShape * s.MacroRelief) +
                    (mountain * mountain * s.MountainHeight * (0.52f + (erosionResponse * 0.48f)) * mountainCalm) -
                    (valley * s.ValleyStrength * valleyCalm) +
                    ((rolling + fine) * s.LocalRelief * depositionalCalm * reliefCalm);

        temperature = Clamp01(s.Temperature + ((macro.TemperatureNoise - 0.5f) * 0.32f) -
            (MathF.Max(0f, elevation - (s.SnowLine * 0.35f)) * 0.006f));
        float rainShadow = mountain * SmoothStep(0.45f, 0.8f, macro.RainShadow);
        moisture = Clamp01(s.Moisture + ((macro.MoistureNoise - 0.5f) * 0.62f) -
            (rainShadow * 0.22f) + (valley * 0.16f));
    }

    /// <summary>The slow half: every field that varies over tens or hundreds of metres, plus the
    /// domain-warped coordinates the rolling term is read at. See <see cref="WorldMacroField"/>.</summary>
    internal readonly record struct MacroFields(
        float WarpedX, float WarpedZ, float Continentalness, float ContinentShape,
        float Mountain, float Erosion, float Valley,
        float TemperatureNoise, float MoistureNoise, float RainShadow);

    /// <summary>Evaluate the macro fields from noise. Called once per macro-grid cell when a region
    /// is built, and directly only where there is no cache — the distant backdrop, and tests.</summary>
    internal static MacroFields MacroAt(WorldGenerationSettings s, float x, float z)
    {
        float macroScale = MathF.Max(80f, s.MacroScale);
        float warpScale = macroScale * 0.72f;
        float warpX = (Fbm(s.Salt(WarpSalt), x / warpScale, z / warpScale, 3) - 0.5f) * macroScale * 0.22f;
        float warpZ = (Fbm(s.Salt(WarpSalt + 19), x / warpScale, z / warpScale, 3) - 0.5f) * macroScale * 0.22f;
        float wx = x + warpX;
        float wz = z + warpZ;

        float continental = Fbm(s.Salt(ContinentalSalt), wx / macroScale, wz / macroScale, 4);
        float continentShape = SignedCurve((continental - 0.5f) * 2f, 1.35f);

        float mountainScale = MathF.Max(60f, s.MountainScale);
        float ridge = RidgedFbm(s.Salt(MountainSalt), wx / mountainScale, wz / mountainScale, 4);
        // ⚠️ THE RESPONSE BAND HAS A FIXED WIDTH; THE DIAL MOVES IT, IT DOES NOT SQUEEZE IT.
        // This read SmoothStep(prevalence, 0.94, ridge) — a threshold against a CONSTANT upper
        // bound — so raising prevalence to make a realm less mountainous narrowed the ramp instead
        // of moving it. At 0.92 the band was 0.02 wide, which is a step function: mountain went 0 to
        // 1 across a couple of metres of ground and put twenty-five metres of elevation under a
        // thirty-metre road. Seventeen authored routes failed the 0.80 grade gate at once and every
        // one of them looked like a road-authoring problem rather than a smoothstep.
        // A fixed-width band means "fewer mountains" and "gentler mountains" stay separate ideas.
        // ⚠️ AND THE UPPER BOUND IS ALLOWED PAST 1. Clamping it looks harmless and is the same bug
        // wearing a smaller hat: at a prevalence of 0.86 a band clamped to 0.995 is 0.135 wide, not
        // 0.34, so the ramp steepens again exactly where a lowland realm sets its dial. Letting the
        // bound run past the field's own ceiling means a high prevalence produces mountains that are
        // both RARER and LOWER - which is what "this realm is not very mountainous" should mean, and
        // is the property WorldGeneratorFieldTests checks by comparing two settings of the dial.
        const float mountainBand = 0.34f;
        float potential = SmoothStep(
            s.MountainPrevalence, s.MountainPrevalence + mountainBand, ridge);
        float chain = SmoothStep(0.34f, 0.72f,
            Fbm(s.Salt(MountainSalt + 37), wx / (mountainScale * 1.9f), wz / (mountainScale * 1.9f), 3));
        float mountain = Clamp01(potential * (0.38f + (chain * 0.82f)));

        float erosion = Fbm(s.Salt(ErosionSalt), wx / (macroScale * 0.46f), wz / (macroScale * 0.46f), 3);
        float valley = MathF.Pow(1f - MathF.Abs((Fbm(s.Salt(ValleySalt), wx / (macroScale * 0.62f),
            wz / (macroScale * 0.62f), 3) * 2f) - 1f), 3f);

        return new MacroFields(
            wx, wz, continental, continentShape, mountain, erosion, valley,
            Fbm(s.Salt(TemperatureSalt), x / 520f, z / 520f, 3),
            Fbm(s.Salt(MoistureSalt), x / 280f, z / 280f, 4),
            Fbm(s.Salt(MoistureSalt + 23), (x - 80f) / 310f, z / 310f, 3));
    }

    private static float Fbm(int seed, float x, float z, int octaves)
    {
        float value = 0f;
        float amplitude = 0.55f;
        float total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            value += WorldTerrainMath.ValueNoise(unchecked(seed + i * 1013), x, z) * amplitude;
            total += amplitude;
            x = x * 2.03f + 13.17f;
            z = z * 2.01f - 9.73f;
            amplitude *= 0.48f;
        }
        return Clamp01(0.5f + ((value / total) - 0.5f) * FbmGain);
    }

    /// <summary>Spread of the averaged octaves back to the 0..1 range every threshold assumes.
    /// Measured, not guessed: the unnormalised field's 10th-to-90th percentile spanned about 0.29
    /// of the range where a uniform field spans 0.80, and <c>--worldgen</c> prints the deciles that
    /// say so. <c>WorldGeneratorTests</c> fails if the spread collapses again.</summary>
    private const float FbmGain = 2.35f;

    private static float RidgedFbm(int seed, float x, float z, int octaves)
    {
        float value = 0f;
        float amplitude = 0.58f;
        float total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            float n = 1f - MathF.Abs(WorldTerrainMath.ValueNoise(unchecked(seed + i * 1291), x, z) * 2f - 1f);
            value += n * n * amplitude;
            total += amplitude;
            x = x * 2.08f - 17.1f;
            z = z * 2.04f + 11.9f;
            amplitude *= 0.46f;
        }
        // Ridged noise is already skewed low — squaring the folded octave sends most of the field
        // toward zero — so it gets a gentler spread than Fbm and, unlike Fbm, is centred on its own
        // measured median rather than on 0.5. A mountain field that never crosses its threshold is
        // a realm with no mountains in it, and MountainPrevalence cannot fix what it cannot reach.
        return Clamp01((value / total) * RidgedGain);
    }

    /// <summary>See <see cref="FbmGain"/>. Ridged fractals bunch near their low end; this lifts the
    /// upper half into the range <c>MountainPrevalence</c> is authored against.</summary>
    private const float RidgedGain = 1.32f;

    private static float SignedCurve(float value, float power) =>
        MathF.CopySign(MathF.Pow(MathF.Abs(value), power), value);

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static float SmoothStep(float a, float b, float value)
    {
        float t = Clamp01((value - a) / MathF.Max(0.0001f, b - a));
        return t * t * (3f - 2f * t);
    }

    private static void Normalise(ref float a, ref float b, ref float c, ref float d)
    {
        float sum = MathF.Max(0.0001f, a + b + c + d);
        a /= sum; b /= sum; c /= sum; d /= sum;
    }
}

/// <summary>
/// The region's macro geography, solved once on a coarse grid and read back by bilinear
/// interpolation.
///
/// ⚠️ <b>THIS IS WHAT MAKES REAL GEOGRAPHY AFFORDABLE, AND WITHOUT IT THE REALM IS UNPLAYABLE
/// RATHER THAN MERELY SLOW.</b> The staged generator costs about thirty-eight noise evaluations per
/// sample where the two-octave field it replaced cost two, and every terrain vertex, every collision
/// face, every conformed prop, every scatter candidate and every water vertex asks for one — five
/// times over for anything that needs a normal. Wired up naively that took an Ember Crown region
/// load from 4.6 seconds to 14.3, which is not a slow load, it is a broken one.
///
/// The insight is the one the brief states outright: a two-metre terrain vertex should not
/// independently rediscover the watershed it belongs to. Continentalness varies over five hundred
/// metres, mountain systems over a hundred and fifty, erosion and valleys over a hundred, climate
/// over three hundred — none of them has anything to say at two-metre spacing. Only the rolling and
/// fine terms do, and those stay live, which is why the ground keeps its texture.
///
/// ⚠️ <b>THE GRID IS BUILT FROM THE REGION'S BOUNDS AND SHARED BY REFERENCE</b>, exactly like
/// <see cref="WorldHydrologyMap"/> and for exactly the same reason: two cells that abut must
/// interpolate the same four corners at their shared edge, or the seam moves. A per-cell cache would
/// be a per-cell world.
///
/// ⚠️ <b>The step is derived, not chosen.</b> The mountain field's fourth octave has a wavelength of
/// roughly <c>MountainScale / 9</c>, and sampling coarser than a third of that turns a ridge into a
/// staircase — the one artefact that would make this optimisation visible.
/// </summary>
internal sealed class WorldMacroField
{
    private readonly float _originX;
    private readonly float _originZ;
    private readonly float _step;
    private readonly int _width;
    private readonly int _height;
    private readonly WorldGenerator.MacroFields[] _cells;

    public long ApproximateBytes => (long)_cells.Length * 40L;

    private WorldMacroField(
        WorldGenerationSettings settings, float minX, float minZ, float maxX, float maxZ)
    {
        float finest = MathF.Min(
            MathF.Max(60f, settings.MountainScale), MathF.Max(80f, settings.MacroScale)) / 9f;
        _step = Math.Clamp(finest / 3f, 2.5f, 12f);
        _originX = (MathF.Floor(minX / _step) * _step) - _step;
        _originZ = (MathF.Floor(minZ / _step) * _step) - _step;
        _width = Math.Max(2, (int)MathF.Ceiling((maxX - _originX) / _step) + 3);
        _height = Math.Max(2, (int)MathF.Ceiling((maxZ - _originZ) / _step) + 3);
        _cells = new WorldGenerator.MacroFields[_width * _height];
        for (int z = 0; z < _height; z++)
        {
            for (int x = 0; x < _width; x++)
            {
                _cells[(z * _width) + x] = WorldGenerator.MacroAt(
                    settings, _originX + (x * _step), _originZ + (z * _step));
            }
        }
    }

    public static WorldMacroField Build(
        WorldGenerationSettings settings, float minX, float minZ, float maxX, float maxZ) =>
        new(settings, minX, minZ, maxX, maxZ);

    /// <summary>Bilinear read. Points outside the built rectangle clamp to its edge, which only the
    /// distant backdrop ever does — and out there the horizon is drawn from
    /// <see cref="WorldGenerator.PreliminaryElevation"/> with no cache at all.</summary>
    public WorldGenerator.MacroFields Sample(float x, float z)
    {
        float gx = (x - _originX) / _step;
        float gz = (z - _originZ) / _step;
        int x0 = Math.Clamp((int)MathF.Floor(gx), 0, _width - 1);
        int z0 = Math.Clamp((int)MathF.Floor(gz), 0, _height - 1);
        int x1 = Math.Min(x0 + 1, _width - 1);
        int z1 = Math.Min(z0 + 1, _height - 1);
        // ⚠️ SMOOTHSTEPPED WEIGHTS, NOT RAW ONES, AND THAT IS NOT A COSMETIC CHOICE.
        // Plain bilinear interpolation is continuous in VALUE but not in SLOPE: the gradient jumps
        // at every grid line, so a cached field reconstructed this way is a lattice of flat facets
        // meeting at creases. Multiplied by MountainHeight those creases became sixty-centimetre
        // tent-shaped ridges on a five-and-a-half-metre spacing - fine enough that the collision
        // lattice interpolated straight over them and handed the player ground 59 cm away from the
        // ground being drawn, and regular enough that they would eventually have read as a grid on
        // the hillsides. Smoothstepping the weights forces the derivative to zero at each node, so
        // the reconstruction is smooth everywhere and the cache stops being visible.
        float tx = SmoothCurve(Math.Clamp(gx - x0, 0f, 1f));
        float tz = SmoothCurve(Math.Clamp(gz - z0, 0f, 1f));

        WorldGenerator.MacroFields a = _cells[(z0 * _width) + x0];
        WorldGenerator.MacroFields b = _cells[(z0 * _width) + x1];
        WorldGenerator.MacroFields c = _cells[(z1 * _width) + x0];
        WorldGenerator.MacroFields d = _cells[(z1 * _width) + x1];

        return new WorldGenerator.MacroFields(
            Mix(a.WarpedX, b.WarpedX, c.WarpedX, d.WarpedX, tx, tz),
            Mix(a.WarpedZ, b.WarpedZ, c.WarpedZ, d.WarpedZ, tx, tz),
            Mix(a.Continentalness, b.Continentalness, c.Continentalness, d.Continentalness, tx, tz),
            Mix(a.ContinentShape, b.ContinentShape, c.ContinentShape, d.ContinentShape, tx, tz),
            Mix(a.Mountain, b.Mountain, c.Mountain, d.Mountain, tx, tz),
            Mix(a.Erosion, b.Erosion, c.Erosion, d.Erosion, tx, tz),
            Mix(a.Valley, b.Valley, c.Valley, d.Valley, tx, tz),
            Mix(a.TemperatureNoise, b.TemperatureNoise, c.TemperatureNoise, d.TemperatureNoise, tx, tz),
            Mix(a.MoistureNoise, b.MoistureNoise, c.MoistureNoise, d.MoistureNoise, tx, tz),
            Mix(a.RainShadow, b.RainShadow, c.RainShadow, d.RainShadow, tx, tz));
    }

    private static float SmoothCurve(float t) => t * t * (3f - (2f * t));

    private static float Mix(float p, float q, float r, float s, float tx, float tz)
    {
        float top = p + ((q - p) * tx);
        float bottom = r + ((s - r) * tx);
        return top + ((bottom - top) * tz);
    }
}

internal readonly record struct HydrologySample(
    float RiverInfluence, float WaterProximity, float? WaterSurface);

/// <summary>Region-scale cached D8 drainage. It is built once with a two-cell apron and shared by
/// all clipped heightfield views, so render chunks never rediscover their watershed.</summary>
internal sealed class WorldHydrologyMap
{
    private readonly float _originX;
    private readonly float _originZ;
    private readonly float _step;
    private readonly int _width;
    private readonly int _height;
    private readonly float[] _elevation;
    private readonly float[] _filled;
    private readonly float[] _flow;
    private readonly int[] _downstream;

    /// <summary>
    /// True where <see cref="Sample"/> could possibly find water: any grid cell within the two-cell
    /// reach that carries channel flow.
    ///
    /// ⚠️ <b>THIS IS AN EARLY-OUT, NOT AN OPTIMISATION AROUND THE EDGES.</b> Sample walks a
    /// five-by-five neighbourhood doing a point-to-segment distance and two smoothsteps per cell,
    /// and it is asked that question by every terrain vertex, every collision face, every prop and
    /// every scatter candidate in the realm. Rivers cover a little over one percent of either
    /// region, so more than ninety-eight percent of those twenty-five-cell walks existed to return
    /// zero. One array read now answers for them.
    /// </summary>
    private readonly bool[] _nearChannel;

    /// <summary>
    /// How many grid cells out <see cref="Sample"/> has to look, derived from the furthest a channel
    /// can still influence a point rather than fixed.
    ///
    /// ⚠️ <b>IT WAS A HARD-CODED TWO, AND THAT PUT A STEP IN THE WETLAND FIELD.</b> Water proximity
    /// falls off over the channel width plus twenty-two metres, so at a ten-metre grid a river could
    /// still be reaching a point two and a half cells away — and the scan stopped at two. Proximity
    /// therefore dropped from a smooth value to exactly zero at a grid line, moisture dropped with
    /// it, and the wetland weight jumped by six tenths across one metre of ground. That is a visible
    /// line on the ground drawn by the coordinate system, which is precisely the artefact generated
    /// biomes exist to remove; it took a continuity test to find it, because nothing about it looks
    /// wrong in the source.
    /// </summary>
    private readonly int _reach;
    private readonly WorldGenerationSettings _settings;

    public long ApproximateBytes =>
        ((long)(_elevation.Length + _filled.Length + _flow.Length + _downstream.Length) * sizeof(float)) +
        _nearChannel.Length;

    private readonly WorldMacroField? _macro;
    private readonly IReadOnlyList<WorldTerrainMath.Path>? _corridorPaths;
    private readonly IReadOnlyList<WorldTerrainMath.GroundArea>? _corridorAreas;

    private WorldHydrologyMap(
        WorldGenerationSettings settings, WorldMacroField? macro,
        IReadOnlyList<WorldTerrainMath.Path>? corridorPaths,
        IReadOnlyList<WorldTerrainMath.GroundArea>? corridorAreas,
        float minX, float minZ, float maxX, float maxZ)
    {
        _settings = settings;
        _macro = macro;
        _corridorPaths = corridorPaths;
        _corridorAreas = corridorAreas;
        _step = MathF.Max(6f, settings.HydrologyCellSize);
        _originX = MathF.Floor(minX / _step) * _step - _step * 2f;
        _originZ = MathF.Floor(minZ / _step) * _step - _step * 2f;
        _width = Math.Max(5, (int)MathF.Ceiling((maxX - _originX) / _step) + 3);
        _height = Math.Max(5, (int)MathF.Ceiling((maxZ - _originZ) / _step) + 3);
        int count = _width * _height;
        _elevation = new float[count];
        _filled = new float[count];
        _flow = new float[count];
        _downstream = new int[count];
        _nearChannel = new bool[count];
        // The widest a channel gets is RiverWidth * (0.75 + 2.2 * 0.55), and its influence fades out
        // over twice that plus the twenty-two metre proximity band. Round out, and keep a floor of
        // two so a coarse grid never scans less than the old behaviour.
        float influence = (settings.RiverWidth * 3.9f) + 22f;
        _reach = Math.Clamp((int)MathF.Ceiling(influence / _step), 2, 8);
        Build();
    }

    public static WorldHydrologyMap Build(
        WorldGenerationSettings settings, WorldMacroField? macro,
        IReadOnlyList<WorldTerrainMath.Path>? corridorPaths,
        IReadOnlyList<WorldTerrainMath.GroundArea>? corridorAreas,
        float minX, float minZ, float maxX, float maxZ) =>
        new(settings, macro, corridorPaths, corridorAreas, minX, minZ, maxX, maxZ);

    private void Build()
    {
        for (int z = 0; z < _height; z++)
        for (int x = 0; x < _width; x++)
        {
            int i = Index(x, z);
            // ⚠️ THE DRAINAGE SOLVES THE GROUND THE WORLD ACTUALLY HAS, CALM AND ALL.
            // Route calm lowers and gentles the terrain around authored circulation, so a solve run
            // on the un-calmed field is solving a different world: it finds spill heights and flow
            // directions for a landscape that does not exist, and then the water surface it derives
            // sits above ground that was calmed out from under it. That is not a subtle error - it
            // put the Fen Edge's cell centre under 3.8 metres of water and drowned three of its
            // route ends, on a causeway whose whole purpose is to be walked.
            float sampleX = _originX + (x * _step);
            float sampleZ = _originZ + (z * _step);
            float calm = _corridorPaths == null && _corridorAreas == null
                ? 0f
                : WorldTerrainMath.RouteCalm(
                    sampleX, sampleZ, _settings.RouteCalm, _corridorPaths, _corridorAreas);
            _elevation[i] = WorldGenerator.PreliminaryElevation(
                _settings, sampleX, sampleZ, _macro, calm);
            _filled[i] = _elevation[i];
            _flow[i] = 1f;
            _downstream[i] = -1;
        }

        // Cheap depression filling: lift interior sinks to their lowest spill neighbour. Multiple
        // passes are enough at this coarse scale and avoid rivers terminating in noise dimples.
        for (int pass = 0; pass < 8; pass++)
        for (int z = 1; z < _height - 1; z++)
        for (int x = 1; x < _width - 1; x++)
        {
            int i = Index(x, z);
            float spill = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dz != 0)
                    spill = MathF.Min(spill, _filled[Index(x + dx, z + dz)]);
            if (_filled[i] <= spill)
                _filled[i] = spill + 0.001f;
        }

        var order = new int[_filled.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) => _filled[b].CompareTo(_filled[a]));

        foreach (int i in order)
        {
            int x = i % _width;
            int z = i / _width;
            int best = -1;
            float bestDrop = 0f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx == 0 && dz == 0) || x + dx < 0 || x + dx >= _width || z + dz < 0 || z + dz >= _height)
                    continue;
                int n = Index(x + dx, z + dz);
                float distance = dx == 0 || dz == 0 ? 1f : 1.41421356f;
                float drop = (_filled[i] - _filled[n]) / distance;
                if (drop > bestDrop)
                {
                    bestDrop = drop;
                    best = n;
                }
            }
            _downstream[i] = best;
            if (best >= 0)
                _flow[best] += _flow[i];
        }

        for (int z = 0; z < _height; z++)
        for (int x = 0; x < _width; x++)
        {
            if (_downstream[Index(x, z)] < 0 || _flow[Index(x, z)] < _settings.RiverThreshold)
                continue;
            for (int dz = -_reach; dz <= _reach; dz++)
            for (int dx = -_reach; dx <= _reach; dx++)
            {
                int nx = x + dx;
                int nz = z + dz;
                if (nx >= 0 && nx < _width && nz >= 0 && nz < _height)
                    _nearChannel[Index(nx, nz)] = true;
            }
        }
    }

    /// <summary>Whether any grid cell inside the rectangle — plus the reach <see cref="Sample"/>
    /// uses — carries enough flow to be a channel.</summary>
    public bool HasChannel(float minX, float minZ, float maxX, float maxZ)
    {
        int x0 = Math.Clamp((int)MathF.Floor((minX - _originX) / _step), 0, _width - 1);
        int x1 = Math.Clamp((int)MathF.Ceiling((maxX - _originX) / _step), 0, _width - 1);
        int z0 = Math.Clamp((int)MathF.Floor((minZ - _originZ) / _step), 0, _height - 1);
        int z1 = Math.Clamp((int)MathF.Ceiling((maxZ - _originZ) / _step), 0, _height - 1);
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (_nearChannel[Index(x, z)])
                {
                    return true;
                }
            }
        }
        return false;
    }

    public HydrologySample Sample(float x, float z)
    {
        int cx = Math.Clamp((int)MathF.Floor((x - _originX) / _step), 0, _width - 1);
        int cz = Math.Clamp((int)MathF.Floor((z - _originZ) / _step), 0, _height - 1);
        if (!_nearChannel[Index(cx, cz)])
        {
            return default;
        }

        float river = 0f;
        float proximity = 0f;
        float? surface = null;
        for (int dz = -_reach; dz <= _reach; dz++)
        for (int dx = -_reach; dx <= _reach; dx++)
        {
            int gx = cx + dx;
            int gz = cz + dz;
            if (gx < 0 || gx >= _width || gz < 0 || gz >= _height) continue;
            int i = Index(gx, gz);
            int down = _downstream[i];
            if (down < 0 || _flow[i] < _settings.RiverThreshold) continue;
            float ax = _originX + gx * _step;
            float az = _originZ + gz * _step;
            float bx = _originX + (down % _width) * _step;
            float bz = _originZ + (down / _width) * _step;
            float distance = WorldTerrainMath.DistanceToSegment(x, z, ax, az, bx, bz);
            float width = _settings.RiverWidth * (0.75f + MathF.Min(2.2f, MathF.Sqrt(_flow[i] / _settings.RiverThreshold) * 0.55f));
            // ⚠️ A BANK IS WIDER THAN THE CHANNEL IT BELONGS TO, AND A NARROW ONE IS A TRENCH.
            // Fading the carve from the water's edge to twice its width put the whole of a river's
            // relief into about three metres of ground: believable enough in a screenshot and not
            // resolvable by anything that samples the world at two metres. The collision lattice
            // interpolated straight across it and put the ground it hands the player up to 59 cm
            // from the ground it draws them - more than they can step. Starting the fade inside the
            // channel and carrying it out to two and a half widths spreads the same depth over
            // roughly eight metres, which is both a gentler bank and one a collider can see.
            float influence = 1f - SmoothStep(width * 0.55f, width * 2.6f, distance);
            river = MathF.Max(river, influence);
            proximity = MathF.Max(proximity, 1f - SmoothStep(width, width + 22f, distance));
            if (influence > 0.04f)
            {
                float length = MathF.Max(0.001f, MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)));
                float t = Math.Clamp(((x - ax) * (bx - ax) + (z - az) * (bz - az)) / (length * length), 0f, 1f);
                float candidate = _elevation[i] + (_elevation[down] - _elevation[i]) * t - _settings.RiverDepth * 0.28f;
                surface = surface == null ? candidate : MathF.Max(surface.Value, candidate);
            }
        }
        return new HydrologySample(river, proximity, river > 0.32f ? surface : null);
    }

    private int Index(int x, int z) => z * _width + x;

    private static float SmoothStep(float a, float b, float value)
    {
        float t = Math.Clamp((value - a) / MathF.Max(0.0001f, b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
