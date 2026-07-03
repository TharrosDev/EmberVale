namespace Embervale.World;

/// <summary>The ground materials footsteps distinguish (Phase 31E). Extend as authored surfaces grow.</summary>
public enum SurfaceType
{
    Stone,
    Grass,
    Wood,
    Snow,
}

/// <summary>
/// Pure mapping from a surface to its footstep cue id (Phase 31E). A floor collider tags itself with a
/// <c>surface</c> node-metadata string (e.g. <c>"grass"</c>); untagged or unknown ground falls back to the
/// generic stone step, so footsteps always sound. Godot-free so it unit-tests under <c>dotnet test</c>;
/// the <see cref="Player.FootstepComponent"/> reads the metadata and calls <see cref="CueFromTag"/>.
/// </summary>
public static class Surfaces
{
    /// <summary>The footstep cue used when the ground is untagged or unrecognized.</summary>
    public const string DefaultCue = "step.stone";

    /// <summary>Footstep cue id for a resolved <see cref="SurfaceType"/>.</summary>
    public static string CueId(SurfaceType surface) => surface switch
    {
        SurfaceType.Grass => "step.grass",
        SurfaceType.Wood => "step.wood",
        SurfaceType.Snow => "step.snow",
        _ => "step.stone",
    };

    /// <summary>Footstep cue id for a collider's <c>surface</c> tag (case-insensitive); default for
    /// null/unknown. Synonyms map to the nearest authored surface (concrete/rock → stone).</summary>
    public static string CueFromTag(string? tag) => tag?.Trim().ToLowerInvariant() switch
    {
        "grass" or "dirt" or "moss" => "step.grass",
        "wood" or "plank" => "step.wood",
        "snow" or "ash" => "step.snow",
        "stone" or "concrete" or "rock" or "tile" => "step.stone",
        _ => DefaultCue,
    };
}
