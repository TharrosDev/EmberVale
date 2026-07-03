using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Audio;

/// <summary>
/// Environmental ambience (Phase 31D): a looping bed on the Ambience bus selected from weather + locale
/// + time of day and crossfaded on change. The pure <see cref="AmbienceSelection"/> resolves the cue
/// (weather &gt; town &gt; day/night); this director feeds it from EventBus (<see cref="WeatherChangedEvent"/>,
/// <see cref="TimeOfDayChangedEvent"/>) and a polled <see cref="SafeZones"/> membership for "in town"
/// (reusing the same safe-zone signal the music uses). Beds come from the shared <see cref="AudioLibrary"/>
/// — real CC0 field recordings when present under <c>assets/audio/ambience/</c>, else procedural washes.
///
/// Runs <see cref="Node.ProcessModeEnum.Always"/> so the bed keeps fading across a pause.
/// </summary>
public partial class AmbienceDirector : Node
{
    private const float FadeSeconds = 2.0f;
    private const float SilentDb = -60f;
    private const float TownPollSeconds = 0.5f;

    private AudioLibrary _library = null!;
    private AudioStreamPlayer _a = null!;
    private AudioStreamPlayer _b = null!;
    private bool _aActive = true;
    private WeatherType _weather = WeatherType.Clear;
    private DayPhase _phase = DayPhase.Day;
    private bool _inTown;
    private string? _current;
    private float _fade = 1f;
    private float _townTimer;

    private AudioStreamPlayer Active => _aActive ? _a : _b;
    private AudioStreamPlayer Idle => _aActive ? _b : _a;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _library = ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out AudioLibrary lib)
            ? lib
            : new AudioLibrary();

        _a = NewPlayer();
        _b = NewPlayer();
        AddChild(_a);
        AddChild(_b);

        EventBus.Instance?.Subscribe<WeatherChangedEvent>(OnWeather);
        EventBus.Instance?.Subscribe<TimeOfDayChangedEvent>(OnTime);

        Apply(AmbienceSelection.Resolve(_inTown, _weather, _phase), instant: true);
        Log.Info("AmbienceDirector ready (weather/locale/time beds).");
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<WeatherChangedEvent>(OnWeather);
        EventBus.Instance?.Unsubscribe<TimeOfDayChangedEvent>(OnTime);
    }

    public override void _Process(double delta)
    {
        PollTown((float)delta);
        AdvanceFade((float)delta);
    }

    private static AudioStreamPlayer NewPlayer() => new()
    {
        Bus = Embervale.Settings.AudioBuses.Ambience,
        VolumeDb = SilentDb,
    };

    private void OnWeather(WeatherChangedEvent e)
    {
        _weather = e.Current;
        Reevaluate();
    }

    private void OnTime(TimeOfDayChangedEvent e)
    {
        _phase = e.Phase;
        Reevaluate();
    }

    private void PollTown(float delta)
    {
        _townTimer -= delta;
        if (_townTimer > 0f)
        {
            return;
        }

        _townTimer = TownPollSeconds;
        bool inTown = ServiceLocator.Instance != null
            && ServiceLocator.Instance.TryGet(out PlayerCharacter player)
            && SafeZones.Contains(player.Body.GlobalPosition);
        if (inTown != _inTown)
        {
            _inTown = inTown;
            Reevaluate();
        }
    }

    private void Reevaluate()
    {
        string next = AmbienceSelection.Resolve(_inTown, _weather, _phase);
        if (_current != next)
        {
            Apply(next, instant: false);
        }
    }

    private void Apply(string cueId, bool instant)
    {
        if (!_library.TryGet(cueId, out AudioStream stream))
        {
            return;
        }

        _current = cueId;

        if (instant)
        {
            Active.Stream = stream;
            Active.VolumeDb = 0f;
            Active.Play();
            _fade = 1f;
            return;
        }

        AudioStreamPlayer incoming = Idle;
        incoming.Stream = stream;
        incoming.VolumeDb = SilentDb;
        incoming.Play();
        _aActive = !_aActive;
        _fade = 0f;
    }

    private void AdvanceFade(float delta)
    {
        if (_fade >= 1f)
        {
            return;
        }

        _fade = Mathf.Min(1f, _fade + delta / FadeSeconds);
        Active.VolumeDb = Mathf.Lerp(SilentDb, 0f, _fade);
        Idle.VolumeDb = Mathf.Lerp(0f, SilentDb, _fade);
        if (_fade >= 1f)
        {
            Idle.Stop();
        }
    }
}
