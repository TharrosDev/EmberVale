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
    }

    // The world's own buses. Music, the UI and voices stay clear.
    private static readonly string[] DuckedBuses = { AudioBuses.Sfx, AudioBuses.Ambience };

    private const float DuckCutoffHz = 1400f;

    /// <summary>
    /// Muffles the world's sound (SFX and ambience) behind a low-pass while a menu has the world
    /// paused, and clears it again. Driven by <c>UiAudio</c> from <c>UiState.WorldPaused</c>.
    /// The filter is each bus's only effect, added the first time it is needed and switched on and
    /// off after that; volumes are untouched, so the settings sliders mean what they did.
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

            if (AudioServer.GetBusEffectCount(index) == 0)
            {
                if (!ducked)
                {
                    continue;
                }

                AudioServer.AddBusEffect(index, new AudioEffectLowPassFilter { CutoffHz = DuckCutoffHz });
            }

            AudioServer.SetBusEffectEnabled(index, 0, ducked);
        }
    }
}
