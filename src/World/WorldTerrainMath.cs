using System;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>
/// Engine-free, continuous heightfield math used by terrain mesh generation, collision, prop
/// conforming, scatter and tests.
///
/// ⚠️ <b>EVERY COORDINATE HERE IS WORLD SPACE, AND THAT IS THE WHOLE SEAM CONTRACT (the 2026-08-29
/// geography overhaul).</b> Until now the field faded to exactly zero in the outer 24% of every cell
/// so neighbours met at y = 0 without knowing about each other — which is precisely why the realm
/// read as a grid of flat rectangles with a dish in the middle of each. The field is now a single
/// continuous function of world X/Z shared by the whole region: two cells that abut sample the
/// identical function at the shared edge, so seams match by construction rather than by flattening.
/// A ridge authored in one cell runs into its neighbour, and that is the point.
///
/// The evaluation order is fixed and each stage may only see the one before it:
///   1. two octaves of value noise scaled by <c>relief</c> (metres) — the countryside wobble;
///   2. authored <see cref="Landform"/>s in order — hills, ridges, cuts, terraces, basins, cliffs;
///   3. authored <see cref="Path"/>s — a road grades between the base heights at its own endpoints,
///      so it climbs a hill instead of levelling it;
///   4. authored <see cref="GroundArea"/>s — yards, pads and floors, flat at a target elevation.
/// Stages 3 and 4 take the STRONGEST mask rather than accumulating, so overlapping authoring
/// softens once instead of digging a hole.
/// </summary>
public static class WorldTerrainMath
{
    /// <summary>What an authored landform does to the field it sits in.</summary>
    public enum LandformShape
    {
        /// <summary>Radial (elliptical) bump or hollow — hills, knolls, spoil banks, craters.</summary>
        Mound = 0,

        /// <summary>Swept along a segment — ridgelines, scarps, embankments, gullies, channels.</summary>
        Ridge = 1,
    }

    /// <summary>An authored route corridor in world X/Z. <see cref="StartHeight"/>/<see cref="EndHeight"/>
    /// are the base field sampled at the endpoints, precomputed once so grading never recurses.</summary>
    public readonly record struct Path(
        float StartX, float StartZ, float EndX, float EndZ, float Width, float Shoulder,
        float StartHeight = 0f, float EndHeight = 0f);

    /// <summary>An authored flat working surface in world X/Z at <see cref="Elevation"/>.</summary>
    public readonly record struct GroundArea(
        float X, float Z, float RadiusX, float RadiusZ, float Feather, float SurfaceBlend,
        float Elevation = 0f);

    /// <summary>
    /// One authored piece of geography in world X/Z.
    /// <para><b>Add vs set.</b> <see cref="Flatten"/> 0 adds <see cref="Height"/> on top of whatever
    /// is there (a hill on rolling ground); 1 replaces it with <see cref="Height"/> (a terrace, a pit
    /// floor, a shelf). Anything between is a partial levelling.</para>
    /// <para><b>Falloff</b> is the fraction of the extent spent on the transition: 0.9 is a soft
    /// hill, 0.12 is a cliff. Keep the resulting grade under ~40° where the player must walk and
    /// over ~50° where they must not — <c>CharacterBody3D</c>'s default floor angle is 45°, so a
    /// steep landform is the realm's honest, collider-free barrier.</para>
    /// </summary>
    public readonly record struct Landform(
        LandformShape Shape, float X, float Z, float EndX, float EndZ,
        float RadiusX, float RadiusZ, float Rotation, float Height, float Falloff, float Flatten,
        float Irregularity = 0f)
    {
        /// <summary>Half-extent of the authored shape. Also the wavelength <see cref="Irregularity"/>
        /// bends it at, so a big ridge gets big lobes rather than the same gravel as a knoll.</summary>
        public float Reach => MathF.Max(RadiusX, RadiusZ);

        /// <summary>⚠️ Culling half-extent, which is <see cref="Reach"/> GROWN BY THE WARP. A warped
        /// boundary can push past the authored radius, and a cull box drawn at the authored radius
        /// would clip the landform off at exactly the cells it was reaching into — the one artefact
        /// the world-space field exists to remove, reintroduced by an optimisation.</summary>
        public float Influence => Reach * (1f + Math.Clamp(Irregularity, 0f, 0.6f));

        public float MinX => MathF.Min(X, Shape == LandformShape.Ridge ? EndX : X) - Influence;
        public float MaxX => MathF.Max(X, Shape == LandformShape.Ridge ? EndX : X) + Influence;
        public float MinZ => MathF.Min(Z, Shape == LandformShape.Ridge ? EndZ : Z) - Influence;
        public float MaxZ => MathF.Max(Z, Shape == LandformShape.Ridge ? EndZ : Z) + Influence;
    }

    /// <summary>The countryside wobble alone: two octaves of world-space value noise, in metres.</summary>
    public static float BaseNoise(int seed, float worldX, float worldZ, float relief, float detailScale)
    {
        if (relief <= 0f)
        {
            return 0f;
        }

        float scale = MathF.Max(0.05f, detailScale);
        float macro = ValueNoise(seed, worldX * 0.0135f * scale, worldZ * 0.0135f * scale);
        float detail = ValueNoise(seed + 1709, worldX * 0.048f * scale, worldZ * 0.048f * scale);
        return (((macro - 0.5f) * 1.35f) + ((detail - 0.5f) * 0.28f)) * relief;
    }

    /// <summary>Noise plus every authored landform — the field a road or yard is levelled against.</summary>
    public static float BaseHeight(
        int seed, float worldX, float worldZ, float relief, float detailScale,
        IReadOnlyList<Landform>? landforms)
    {
        float height = BaseNoise(seed, worldX, worldZ, relief, detailScale);
        if (landforms == null)
        {
            return height;
        }

        for (int i = 0; i < landforms.Count; i++)
        {
            Landform form = landforms[i];
            float mask = LandformMask(form, worldX, worldZ);
            if (mask <= 0f)
            {
                continue;
            }

            float set = Math.Clamp(form.Flatten, 0f, 1f) * mask;
            height = ((height + (form.Height * mask * (1f - Math.Clamp(form.Flatten, 0f, 1f)))) * (1f - set)) +
                     (form.Height * set);
        }

        return height;
    }

    /// <summary>The finished ground height at a world point: noise, landforms, roads, then yards.</summary>
    public static float Height(
        int seed, float worldX, float worldZ, float relief, float detailScale,
        IReadOnlyList<Landform>? landforms = null,
        IReadOnlyList<Path>? paths = null,
        IReadOnlyList<GroundArea>? areas = null)
    {
        float height = BaseHeight(seed, worldX, worldZ, relief, detailScale, landforms);

        // ⚠️ ROADS: MASK-WEIGHTED, NOT WINNER-TAKES-ALL. Taking the strongest mask's target alone
        // looked simpler and put a step at every junction: two segments meeting at a shared point
        // both cover the ground around it, their masks differ by a hair, and whichever wins drags
        // the ground to ITS grade — a 1.8 m shelf two metres from the corner, which is what
        // ValidateRouteGrades kept reporting on cells with no landform anywhere near the road.
        // Squaring the weight keeps a road's own centreline dominant while blending corners.
        float pathMask = 0f;
        if (paths != null)
        {
            float weight = 0f;
            float weighted = 0f;
            for (int i = 0; i < paths.Count; i++)
            {
                Path path = paths[i];
                float halfWidth = MathF.Max(0.1f, path.Width * 0.5f);
                float feather = MathF.Max(0.1f, path.Shoulder);
                float raw = RawSegmentParameter(worldX, worldZ, path.StartX, path.StartZ, path.EndX, path.EndZ);
                float t = Math.Clamp(raw, 0f, 1f);
                float distance = DistanceToSegmentAt(worldX, worldZ, path, t);
                float mask = 1f - SmoothStep(halfWidth, halfWidth + feather, distance);
                if (mask <= 0f)
                {
                    continue;
                }

                // ⚠️ A ROAD'S SAY ENDS WHERE THE ROAD ENDS. Clamping t alone gives every segment a
                // half-width cap of its own end height projecting past it, so where one route hands
                // over to another with a different gradient the cap drags the ground flat for two or
                // three metres and then lets go — a 40-degree step in the middle of a 9-degree ramp,
                // authored by nobody. Fading the weight over the overshoot lets the continuing route
                // take the corner; at a real junction both share the point, so both targets agree
                // there and the taper changes nothing.
                float overshoot = MathF.Max(0f, MathF.Max(-raw, raw - 1f)) *
                                  MathF.Sqrt(((path.EndX - path.StartX) * (path.EndX - path.StartX)) +
                                             ((path.EndZ - path.StartZ) * (path.EndZ - path.StartZ)));
                float w = mask * mask * (1f - SmoothStep(0f, feather + halfWidth, overshoot));
                if (w <= 0f)
                {
                    continue;
                }
                weight += w;
                weighted += w * (path.StartHeight + ((path.EndHeight - path.StartHeight) * t));
                pathMask = MathF.Max(pathMask, mask);
            }
            if (weight > 0f)
            {
                // A road is a cut, not a suggestion: at full mask the centreline IS the graded line
                // between its own endpoints, so its gradient is arithmetic an author can predict.
                height += ((weighted / weight) - height) * pathMask;
            }
        }

        // ⚠️ AND A ROAD BEATS A YARD WHERE THEY OVERLAP. A ground area levels its footprint; applied
        // over a road it drags the carriageway to the yard's elevation over the area's feather, which
        // is a step in the middle of a route nobody authored and nobody can see. The yard still wins
        // everywhere the road is not, which is every part of it that matters.
        if (areas != null)
        {
            float best = 0f;
            float target = height;
            for (int i = 0; i < areas.Count; i++)
            {
                GroundArea area = areas[i];
                float mask = AreaMask(area, worldX, worldZ) * Math.Clamp(area.SurfaceBlend, 0f, 1f);
                if (mask > best)
                {
                    best = mask;
                    target = area.Elevation;
                }
            }
            height += (target - height) * best * (1f - pathMask);
        }

        return height;
    }

    public static float LandformMask(Landform form, float x, float z)
    {
        float falloff = Math.Clamp(form.Falloff, 0.01f, 1f);
        float normalized;
        if (form.Shape == LandformShape.Ridge)
        {
            float halfWidth = MathF.Max(0.1f, form.RadiusX);
            float t = SegmentParameter(x, z, form.X, form.Z, form.EndX, form.EndZ);
            float px = form.X + ((form.EndX - form.X) * t);
            float pz = form.Z + ((form.EndZ - form.Z) * t);
            normalized = MathF.Sqrt(((x - px) * (x - px)) + ((z - pz) * (z - pz))) / halfWidth;
        }
        else
        {
            float dx = x - form.X;
            float dz = z - form.Z;
            if (form.Rotation != 0f)
            {
                float cos = MathF.Cos(-form.Rotation);
                float sin = MathF.Sin(-form.Rotation);
                float rx = (dx * cos) - (dz * sin);
                dz = (dx * sin) + (dz * cos);
                dx = rx;
            }
            float radiusX = MathF.Max(0.1f, form.RadiusX);
            float radiusZ = MathF.Max(0.1f, form.RadiusZ);
            normalized = MathF.Sqrt(((dx * dx) / (radiusX * radiusX)) + ((dz * dz) / (radiusZ * radiusZ)));
        }

        // ⚠️ THE EDGE IS BENT HERE, AND ONLY THE EDGE. Warping the normalised radius moves the
        // landform's boundary without moving its centre, its height or its grade — so a hill stays
        // exactly as tall and as walkable as it was authored while ceasing to be an ellipse. Two
        // octaves, at a wavelength derived from the form's OWN size, so a 40 m knoll gets 40 m lobes
        // and a 90 m ridge gets 90 m ones rather than both getting the same gravel.
        //
        // ⚠️ It is deliberately seeded from the form's own position, not from a global seed: a
        // landform's shape must not change because a neighbouring one was edited, or every
        // regenerated .tres would move ground the author never touched.
        float irregularity = Math.Clamp(form.Irregularity, 0f, 0.6f);
        // ⚠️ THE EARLY-OUT IS NOT AN OPTIMISATION, IT IS THE DIFFERENCE BETWEEN A ONE-SECOND AND A
        // FOUR-SECOND REGION LOAD. Height() is evaluated something over a hundred thousand times per
        // cell — render vertices, the collision grid, every conformed prop, every scatter candidate,
        // every water vertex — and each call walks every landform reaching that cell. Two extra
        // noise octaves on all of them costs a hundred million samples across the realm. Only the
        // TRANSITION BAND can change its mask when the boundary moves: deep inside the form the mask
        // is 1 whatever the warp does, and well outside it is 0. Both are the common case.
        // ⚠️ The band is widened by 1.6x the irregularity, not 1x. The warp multiplies the radius by
        // up to (1 +/- 1.085 * irregularity), and PULLING a point in from outside needs more headroom
        // than pushing one out: 1 / (1 - 1.085 * 0.26) is about 1.39, not 1.26. A band drawn at 1x
        // would silently clip the outermost lobes of every warped landform in the realm.
        float band = irregularity * 1.6f;
        if (irregularity > 0f &&
            normalized > (1f - falloff) - band && normalized < 1f + band)
        {
            int seed = unchecked((int)(form.X * 37f) ^ ((int)(form.Z * 91f) << 8) ^ 0x5F3B);
            float wavelength = MathF.Max(6f, form.Reach * 0.55f);
            float warp = (ValueNoise(seed, x / wavelength, z / wavelength) - 0.5f) * 1.55f;
            warp += (ValueNoise(seed + 613, x / (wavelength * 0.36f), z / (wavelength * 0.36f)) - 0.5f) * 0.62f;
            normalized *= 1f + (warp * irregularity);
        }

        return 1f - SmoothStep(1f - falloff, 1f, normalized);
    }

    public static float PathMask(float x, float z, IReadOnlyList<Path>? paths)
    {
        if (paths == null)
        {
            return 0f;
        }

        float mask = 0f;
        foreach (Path path in paths)
        {
            float halfWidth = MathF.Max(0.1f, path.Width * 0.5f);
            float feather = MathF.Max(0.1f, path.Shoulder);
            float distance = DistanceToSegment(x, z, path.StartX, path.StartZ, path.EndX, path.EndZ);
            mask = MathF.Max(mask, 1f - SmoothStep(halfWidth, halfWidth + feather, distance));
        }
        return mask;
    }

    public static float GroundAreaMask(float x, float z, IReadOnlyList<GroundArea>? areas)
    {
        if (areas == null)
        {
            return 0f;
        }

        float mask = 0f;
        foreach (GroundArea area in areas)
        {
            mask = MathF.Max(mask, AreaMask(area, x, z) * Math.Clamp(area.SurfaceBlend, 0f, 1f));
        }
        return mask;
    }

    private static float AreaMask(GroundArea area, float x, float z)
    {
        float radiusX = MathF.Max(0.1f, area.RadiusX);
        float radiusZ = MathF.Max(0.1f, area.RadiusZ);
        float normalized = MathF.Sqrt(((x - area.X) * (x - area.X) / (radiusX * radiusX)) +
                                      ((z - area.Z) * (z - area.Z) / (radiusZ * radiusZ)));
        float feather = MathF.Max(0.01f, area.Feather / MathF.Max(radiusX, radiusZ));
        return 1f - SmoothStep(1f, 1f + feather, normalized);
    }

    public static bool InsidePath(float x, float z, Path path, float extra = 0f) =>
        DistanceToSegment(x, z, path.StartX, path.StartZ, path.EndX, path.EndZ) <
        MathF.Max(0f, (path.Width * 0.5f) + path.Shoulder + extra);

    public static bool InsideGroundArea(float x, float z, GroundArea area, float extra = 0f)
    {
        float radiusX = MathF.Max(0.1f, area.RadiusX + area.Feather + extra);
        float radiusZ = MathF.Max(0.1f, area.RadiusZ + area.Feather + extra);
        float dx = (x - area.X) / radiusX;
        float dz = (z - area.Z) / radiusZ;
        return (dx * dx) + (dz * dz) < 1f;
    }

    private static float SegmentParameter(float x, float z, float ax, float az, float bx, float bz) =>
        Math.Clamp(RawSegmentParameter(x, z, ax, az, bx, bz), 0f, 1f);

    /// <summary>The projection parameter <em>unclamped</em>, so a caller can tell how far past an
    /// end a point lies. Negative is before the start; over 1 is past the end.</summary>
    private static float RawSegmentParameter(float x, float z, float ax, float az, float bx, float bz)
    {
        float abx = bx - ax;
        float abz = bz - az;
        float lengthSquared = (abx * abx) + (abz * abz);
        if (lengthSquared <= 0.0001f)
        {
            return 0f;
        }
        return (((x - ax) * abx) + ((z - az) * abz)) / lengthSquared;
    }

    private static float DistanceToSegmentAt(float x, float z, Path path, float t)
    {
        float px = path.StartX + ((path.EndX - path.StartX) * t);
        float pz = path.StartZ + ((path.EndZ - path.StartZ) * t);
        float dx = x - px;
        float dz = z - pz;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    public static float DistanceToSegment(float x, float z, float ax, float az, float bx, float bz)
    {
        float t = SegmentParameter(x, z, ax, az, bx, bz);
        float px = ax + ((bx - ax) * t);
        float pz = az + ((bz - az) * t);
        float offsetX = x - px;
        float offsetZ = z - pz;
        return MathF.Sqrt((offsetX * offsetX) + (offsetZ * offsetZ));
    }

    public static float ValueNoise(int seed, float x, float z)
    {
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        float tx = SmoothCurve(x - x0);
        float tz = SmoothCurve(z - z0);
        float a = Unit2D(seed, x0, z0);
        float b = Unit2D(seed, x0 + 1, z0);
        float c = Unit2D(seed, x0, z0 + 1);
        float d = Unit2D(seed, x0 + 1, z0 + 1);
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), tz);
    }

    private static float Unit2D(int seed, int x, int z)
    {
        int index = unchecked((x * 73856093) ^ (z * 19349663));
        return WorldSceneryMath.Unit(seed, index);
    }

    private static float SmoothCurve(float value) => value * value * (3f - (2f * value));
    private static float Lerp(float a, float b, float amount) => a + ((b - a) * amount);

    private static float SmoothStep(float from, float to, float value)
    {
        if (to <= from)
        {
            return value >= to ? 1f : 0f;
        }
        float t = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return SmoothCurve(t);
    }
}
