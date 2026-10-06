using Embervale.Audio;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The interface's own voice. An application-lifetime node (a child of <c>ApplicationRoot</c>), so
/// the title screen, the save list and the settings panel sound exactly like the menus inside a
/// session: before this every UI click went through the session's <c>AudioDirector</c>, and the
/// shell was silent because there was no session yet to own one.
///
/// It plays <see cref="UiCue"/>s on a fixed pool of voices on the UI bus (<see cref="Play"/>),
/// ticks when the player walks focus with a stick or the arrow keys, holds a quiet bed under the
/// title screen, and muffles the world's sound while a menu has it paused. What sounds and when is
/// decided by the pure <see cref="UiAudioRules"/>.
///
/// The voices are permanent children rather than pooled nodes: nothing is ever detached, so the
/// lifecycle probe's orphan count cannot see this node at all. Nothing in a session holds it;
/// callers go through the static <see cref="Play"/>, which is a no-op when there is no instance
/// (a tool mode, a unit test).
/// </summary>
public partial class UiAudio : Node
{
    private const float SilentDb = -60f;
    private const float BedFadeInSeconds = 2.0f;
    private const float BedFadeOutSeconds = 0.8f;
    private const string TitleBed = "music.title";

    private static UiAudio? _instance;

    private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[UiAudioRules.VoiceCount];
    private readonly bool[] _busy = new bool[UiAudioRules.VoiceCount];
    private readonly AudioLibrary _library = new();
    private StringName[] _navigation = System.Array.Empty<StringName>();
    private AudioStreamPlayer _bed = null!;
    private Tween? _bedFade;
    private bool _bedOn;
    private bool _ducked;
    private int _voice = -1;
    private UiCue? _last;
    private double _lastAt;
    private double _lastFocusAt;

    /// <summary>Plays <paramref name="cue"/>, unless a cue that means more has just sounded.
    /// Safe from anywhere and at any time; silent when the application has no audio node.</summary>
    public static void Play(UiCue cue, float pitch = 1f) => _instance?.PlayCue(cue, pitch);

    public override void _EnterTree() => _instance = this;

    public override void _Ready()
    {
        // Menus are open exactly when the tree is paused.
        ProcessMode = ProcessModeEnum.Always;

        // Shared with every session's AudioDirector and MusicDirector, which resolve it from here
        // instead of synthesising their own copy of the placeholders on each New Game and Load.
        ServiceScope.RegisterOwned(this, _library);

        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new AudioStreamPlayer { Name = $"Voice{i}", Bus = AudioBuses.Ui };
            AddChild(_voices[i]);
        }

        _bed = new AudioStreamPlayer { Name = "TitleBed", Bus = AudioBuses.Music, VolumeDb = SilentDb };
        AddChild(_bed);

        _navigation = new StringName[]
        {
            "ui_up", "ui_down", "ui_left", "ui_right", "ui_focus_next", "ui_focus_prev",
        };

        GetViewport().GuiFocusChanged += OnFocusChanged;
        UiState.Changed += OnUiStateChanged;
        EventBus.Instance?.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        SetBed(GameManager.Instance is { State: GameState.MainMenu });
    }

    public override void _ExitTree()
    {
        GetViewport().GuiFocusChanged -= OnFocusChanged;
        UiState.Changed -= OnUiStateChanged;
        EventBus.Instance?.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
        SetDuck(false);
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void PlayCue(UiCue cue, float pitch)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (!UiAudioRules.ShouldPlay(cue, now, _last, _lastAt)
            || !_library.TryGet(UiAudioRules.CueId(cue), out AudioStream stream))
        {
            return;
        }

        for (int i = 0; i < _voices.Length; i++)
        {
            _busy[i] = _voices[i].Playing;
        }

        _voice = UiAudioRules.NextVoice(_busy, _voice);
        AudioStreamPlayer voice = _voices[_voice];
        voice.Stream = stream;
        voice.PitchScale = pitch;
        voice.Play();

        if (cue != UiCue.HoldTick)
        {
            _last = cue;
            _lastAt = now;
        }
    }

    private void OnFocusChanged(Control node)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (!UiAudioRules.ShouldTickFocus(Navigating(), now, _lastFocusAt))
        {
            return;
        }

        _lastFocusAt = now;
        PlayCue(UiCue.Focus, 1f);
    }

    private bool Navigating()
    {
        foreach (StringName action in _navigation)
        {
            if (Input.IsActionPressed(action))
            {
                return true;
            }
        }

        return false;
    }

    // --- The world, muffled behind a menu -------------------------------------------------------

    // A menu that pauses the world, or the pause state itself (the pause menu is a state, not a
    // UiState owner).
    private void OnUiStateChanged() =>
        SetDuck(UiState.WorldPaused || GameManager.Instance is { State: GameState.Paused });

    private void SetDuck(bool ducked)
    {
        if (ducked == _ducked)
        {
            return;
        }

        _ducked = ducked;
        Audio.AudioBusLayout.SetMenuDuck(ducked);
    }

    // --- The title bed --------------------------------------------------------------------------

    private void OnGameStateChanged(GameStateChangedEvent e)
    {
        SetBed(e.Current == GameState.MainMenu);
        SetDuck(UiState.WorldPaused || e.Current == GameState.Paused);
    }

    /// <summary>The bed plays at the title and nowhere else: a session brings its own music. A
    /// headless run has nobody to hear it and gates that count what is playing.</summary>
    private void SetBed(bool on)
    {
        if (on == _bedOn || DisplayServer.GetName() == "headless")
        {
            return;
        }

        _bedOn = on;
        if (_bedFade != null && _bedFade.IsValid())
        {
            _bedFade.Kill();
        }

        if (on)
        {
            if (!_library.TryGet(TitleBed, out AudioStream stream))
            {
                return;
            }

            if (!_bed.Playing)
            {
                _bed.Stream = stream;
                _bed.VolumeDb = SilentDb;
                _bed.Play();
            }
        }
        else if (!_bed.Playing)
        {
            return;
        }

        // A fade in volume is not motion: reduced motion leaves it alone.
        _bedFade = CreateTween();
        _bedFade.SetPauseMode(Tween.TweenPauseMode.Process);
        _bedFade.SetIgnoreTimeScale(true);
        _bedFade.TweenProperty(_bed, "volume_db", on ? 0f : SilentDb, on ? BedFadeInSeconds : BedFadeOutSeconds);
        if (!on)
        {
            _bedFade.TweenCallback(Callable.From(_bed.Stop));
        }
    }
}
