using Embervale.Settings;
using Godot;

namespace Embervale.Audio;

/// <summary>
/// Creates the game's mixer buses at runtime (Phase 31A) from the <see cref="AudioBuses"/> constants —
/// the same names the <see cref="Settings"/> volume fields drive — so the bus graph and the settings
/// have a single source of truth and can never drift. Master (index 0) always exists; the rest route
/// their output to Master. Idempotent: safe to call more than once.
///
/// Must run <b>before</b> the first <c>SettingsService.Apply()</c> so that pass sets every bus volume
/// (it silently skips buses that don't exist yet).
/// </summary>
public static class AudioBusLayout
{
    // Every bus except Master, which the engine always provides at index 0.
    private static readonly string[] SubBuses =
    {
        AudioBuses.Music,
        AudioBuses.Sfx,
        AudioBuses.Ambience,
        AudioBuses.Ui,
        AudioBuses.Voice,
    };

    /// <summary>Ensures each named bus exists and sends to Master. Idempotent.</summary>
    public static void Ensure()
    {
        foreach (string bus in SubBuses)
        {
            if (AudioServer.GetBusIndex(bus) >= 0)
            {
                continue;
            }

            int index = AudioServer.BusCount;
            AudioServer.AddBus(index);
            AudioServer.SetBusName(index, bus);
            AudioServer.SetBusSend(index, AudioBuses.Master);
        }

        EnsureSfxLimiter();
    }

    /// <summary>The ceiling the SFX bus is held under (dBFS). Just below full scale, so the limiter
    /// only ever acts on a peak that would otherwise have clipped.</summary>
    private const float SfxCeilingDb = -1f;

    /// <summary>
    /// Puts a hard limiter on the SFX bus, once. The spell cues are each mastered to -3 dBFS, and
    /// the loudest of them play up to 9 dB over that and on top of one another (a charged blast, its
    /// impacts and a thunderclap in the same instant), which sums past full scale and clips at the
    /// output. The limiter holds the bus under <see cref="SfxCeilingDb"/> and does nothing at all to
    /// anything quieter, so the mix and the volume slider mean what they did.
    /// </summary>
    private static void EnsureSfxLimiter()
    {
        int index = AudioServer.GetBusIndex(AudioBuses.Sfx);
        if (index < 0 || FindEffect<AudioEffectHardLimiter>(index) >= 0)
        {
            return;
        }

        AudioServer.AddBusEffect(index, new AudioEffectHardLimiter { CeilingDb = SfxCeilingDb });
    }

    /// <summary>The position of the first effect of a type on a bus, or -1.</summary>
    private static int FindEffect<T>(int bus)
        where T : AudioEffect
    {
        for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is T)
            {
                return i;
            }
        }

        return -1;
    }

    // The world's own buses. Music, the UI and voices stay clear.
    private static readonly string[] DuckedBuses = { AudioBuses.Sfx, AudioBuses.Ambience };

    private const float DuckCutoffHz = 1400f;

    /// <summary>
    /// Muffles the world's sound (SFX and ambience) behind a low-pass while a menu has the world
    /// paused, and clears it again. Driven by <c>UiAudio</c> from <c>UiState.WorldPaused</c>.
    /// The filter is added the first time it is needed, ahead of anything else on the bus (the SFX
    /// limiter stays last, so it still has the final say), and switched on and off after that;
    /// volumes are untouched, so the settings sliders mean what they did.
    /// </summary>
    public static void SetMenuDuck(bool ducked)
    {
        foreach (string bus in DuckedBuses)
        {
            int index = AudioServer.GetBusIndex(bus);
            if (index < 0)
            {
                continue;
            }

            int filter = FindEffect<AudioEffectLowPassFilter>(index);
            if (filter < 0)
            {
                if (!ducked)
                {
                    continue;
                }

                filter = 0;
                AudioServer.AddBusEffect(index, new AudioEffectLowPassFilter { CutoffHz = DuckCutoffHz }, filter);
            }

            AudioServer.SetBusEffectEnabled(index, filter, ducked);
        }
    }
}
