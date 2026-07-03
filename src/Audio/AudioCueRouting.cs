using Embervale.Settings;

namespace Embervale.Audio;

/// <summary>
/// Pure routing for audio cue ids (Phase 31A): which mixer bus a cue plays on, and whether it is
/// positional (3D, attenuated by distance) or non-positional (2D, e.g. music/UI). Both are a pure
/// function of the cue-id prefix, so the whole game answers to one naming convention:
/// <list type="bullet">
///   <item><c>sfx.*</c>  → SFX bus, positional (world sounds: swings, impacts).</item>
///   <item><c>step.*</c> → SFX bus, positional (footsteps).</item>
///   <item><c>music.*</c>→ Music bus, 2D.</item>
///   <item><c>amb.*</c>  → Ambience bus, 2D.</item>
///   <item><c>ui.*</c>   → UI bus, 2D.</item>
///   <item><c>voice.*</c>→ Voice bus, 2D.</item>
/// </list>
/// Anything unmatched falls back to the Master bus, non-positional. Kept free of Godot types so it
/// unit-tests under <c>dotnet test</c>; the actual <c>AudioStreamWav</c> construction lives in
/// <see cref="ProceduralAudio"/>/<see cref="AudioLibrary"/>.
/// </summary>
public static class AudioCueRouting
{
    /// <summary>The mixer bus a cue id routes to, from its prefix (default: Master).</summary>
    public static string BusFor(string cueId)
    {
        if (HasPrefix(cueId, "sfx.") || HasPrefix(cueId, "step."))
        {
            return AudioBuses.Sfx;
        }

        if (HasPrefix(cueId, "music."))
        {
            return AudioBuses.Music;
        }

        if (HasPrefix(cueId, "amb."))
        {
            return AudioBuses.Ambience;
        }

        if (HasPrefix(cueId, "ui."))
        {
            return AudioBuses.Ui;
        }

        if (HasPrefix(cueId, "voice."))
        {
            return AudioBuses.Voice;
        }

        return AudioBuses.Master;
    }

    /// <summary>True for world sounds that should play in 3D at a position (SFX + footsteps).</summary>
    public static bool IsPositional(string cueId) =>
        HasPrefix(cueId, "sfx.") || HasPrefix(cueId, "step.");

    private static bool HasPrefix(string value, string prefix) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(prefix, System.StringComparison.Ordinal);
}
