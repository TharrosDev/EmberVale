using System;

namespace Embervale.World;

/// <summary>The ground materials footsteps distinguish (Phase 31E). Extend as authored surfaces grow.</summary>
public enum SurfaceType
{
    Stone,
    Grass,
    Wood,
    Snow,
    Water,
}

/// <summary>
/// Pure mapping from a surface to its footstep cue id (Phase 31E). Godot-free so it unit-tests under
/// <c>dotnet test</c>; the <see cref="Player.FootstepComponent"/> gathers the evidence and asks here.
///
/// <para><b>Where the surface comes from (the 2026-09 upgrade), in order.</b> Declared water the
/// feet are standing in; a collider's explicit <c>surface</c> node-metadata tag
/// (<see cref="CueFromTag"/>); the generated terrain's own biome and slope at that point
/// (<see cref="FromTerrain"/>); the name of the collision shape or body that was hit
/// (<see cref="TagFromName"/>); and only then the generic stone step.</para>
///
/// <para>⚠️ <b>Nothing in the world carried a <c>surface</c> tag</b>, so before this every footstep
/// in the game was stone — on meadow, on snowfield, on a library's plank floor. The terrain and
/// name rules are what make the four recordings actually get used; the tag still wins wherever an
/// author sets one.</para>
/// </summary>
public static class Surfaces
{
    /// <summary>The footstep cue used when the ground is untagged or unrecognized.</summary>
    public const string DefaultCue = "step.stone";

    /// <summary>The cue for wading through declared water.</summary>
    public const string WaterCue = "step.water";

    /// <summary>Footstep cue id for a resolved <see cref="SurfaceType"/>.</summary>
    public static string CueId(SurfaceType surface) => surface switch
    {
        SurfaceType.Grass => "step.grass",
        SurfaceType.Wood => "step.wood",
        SurfaceType.Snow => "step.snow",
        SurfaceType.Water => WaterCue,
        _ => "step.stone",
    };

    /// <summary>Footstep cue id for a collider's <c>surface</c> tag (case-insensitive); default for
    /// null/unknown. Synonyms map to the nearest authored surface (concrete/rock → stone).</summary>
    public static string CueFromTag(string? tag) => tag?.Trim().ToLowerInvariant() switch
    {
        "grass" or "dirt" or "moss" or "mud" => "step.grass",
        "wood" or "plank" => "step.wood",
        "snow" or "ash" => "step.snow",
        "stone" or "concrete" or "rock" or "tile" => "step.stone",
        "water" or "shallows" => WaterCue,
        _ => DefaultCue,
    };

    /// <summary>
    /// The surface of generated terrain from the heightfield's own sample at the foot: snow above
    /// the snow line (the alpine weight), bare rock on anything steeper than
    /// <paramref name="rockSlope"/> (rise over run) or on exposed barren ground, and earth and grass
    /// everywhere else — which is what the painted ground layers show at the same point, because
    /// they are driven by the same weights.
    /// </summary>
    public static SurfaceType FromTerrain(float alpineWeight, float barrenWeight, float slope,
        float rockSlope = 1f)
    {
        if (alpineWeight >= 0.5f)
        {
            return SurfaceType.Snow;
        }

        return slope >= rockSlope || barrenWeight >= 0.5f ? SurfaceType.Stone : SurfaceType.Grass;
    }

    /// <summary>
    /// A surface tag guessed from a collider's node name, or null when the name says nothing. Every
    /// shipped building's walkable floor is the kit's <c>floor_wood</c> under a shape named
    /// <c>FloorShape</c>, which is why "floor" reads as wood; props name themselves (crates, carts,
    /// rocks, pillars) and the kit is consistent about it.
    /// </summary>
    public static string? TagFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string lower = name.ToLowerInvariant();
        if (ContainsAny(lower, "wood", "plank", "floor", "bridge", "dock", "jetty", "pier", "deck",
                "crate", "barrel", "cart", "bench", "table", "counter", "stall"))
        {
            return "wood";
        }

        if (ContainsAny(lower, "snow"))
        {
            return "snow";
        }

        if (ContainsAny(lower, "grass", "dirt", "moss", "mud", "hay"))
        {
            return "grass";
        }

        if (ContainsAny(lower, "rock", "stone", "crag", "cliff", "wall", "ruin", "pillar", "cobble",
                "brick", "step", "well", "waystone"))
        {
            return "stone";
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] words)
    {
        foreach (string word in words)
        {
            if (value.Contains(word, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
