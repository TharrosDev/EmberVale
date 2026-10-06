using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Where spoken lines are captioned: a companion's remark, a boss's opening words.
///
/// Built with the session's shell (<c>UICompositionRoot.Subtitles</c>). A line is cut into pages
/// of at most two short lines (<see cref="SubtitleRules"/>), set bottom-centre above the hotbar
/// and above whatever the HUD's bottom-centre slot is showing, with the speaker's name over it.
/// Size, the plate's opacity and the name follow the player's subtitle settings; nothing is drawn
/// with subtitles off or the element hidden, and <see cref="TryShow"/> then answers false so the
/// caller can say the line some other way (a toast, the boss frame's own line). The same answer
/// is given while a menu or a conversation holds the world, and a caption already up when one
/// opens is hidden with its clock stopped until it closes: a line is never spent unseen.
///
/// Asleep while nothing is captioned: <see cref="Show"/> is the only thing that wakes it.
/// </summary>
public partial class SubtitleLayer : CanvasLayer
{
    /// <summary>Lines waiting behind the one on screen. A fourth is refused, so its caller says it
    /// another way: a caption that arrives long after its line was spoken is worse than none.</summary>
    private const int MaxQueued = 3;

    private readonly record struct Line(string? Speaker, string Text, float Seconds);

    private static SubtitleLayer? _current;

    private readonly Queue<Line> _queue = new();
    private List<string> _pages = new();

    private Control _root = null!;
    private PanelContainer _plate = null!;
    private StyleBoxFlat _ground = null!;
    private Label _speaker = null!;
    private Label _text = null!;

    private HudLayout? _layout;
    private bool _showing;
    private bool _held;
    private bool _speakerNames = true;
    private int _page;
    private int _lineLength;
    private double _lineSeconds;
    private double _pageLeft;
    private float _bottomShown = float.NaN;

    /// <summary>The page on screen, or null. For harness validation.</summary>
    public string? ShowingForCapture => _showing ? _text.Text : null;

    /// <summary>Whether the line on screen names its speaker. For harness validation.</summary>
    public bool SpeakerShownForCapture => _showing && _speaker.Visible;

    public override void _Ready()
    {
        // Ticks through a pause so it can tell when the menu holding it has closed.
        ProcessMode = ProcessModeEnum.Always;
        Layer = 5; // over the HUD, under the chapter banner (6)

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _ground = new StyleBoxFlat();
        _ground.SetCornerRadiusAll(UiTheme.RadiusSm);
        _ground.ContentMarginLeft = UiTheme.CompactPadX;
        _ground.ContentMarginRight = UiTheme.CompactPadX;
        _ground.ContentMarginTop = UiTheme.SpaceXs;
        _ground.ContentMarginBottom = UiTheme.SpaceXs;

        _plate = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _plate.AddThemeStyleboxOverride("panel", _ground);
        _plate.AnchorLeft = 0.5f;
        _plate.AnchorRight = 0.5f;
        _plate.AnchorTop = 1f;
        _plate.AnchorBottom = 1f;
        _plate.GrowHorizontal = Control.GrowDirection.Both;
        _plate.GrowVertical = Control.GrowDirection.Begin;
        _root.AddChild(_plate);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", UiTheme.LineGap);
        _plate.AddChild(column);

        _speaker = UiTheme.HudInk(UiTheme.Caption(string.Empty, UiTheme.Accent));
        _speaker.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_speaker);

        _text = UiTheme.HudInk(UiTheme.Body(string.Empty, UiTheme.Text));
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_text);

        EventBus.Instance?.Subscribe<SettingsAppliedEvent>(OnSettingsApplied);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
        ApplySettings();
        SetProcess(false);
    }

    public override void _EnterTree() => _current = this;

    public override void _ExitTree()
    {
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        EventBus.Instance?.Unsubscribe<SettingsAppliedEvent>(OnSettingsApplied);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
    }

    /// <summary>Whether a line would be captioned right now: subtitles are on and the element is
    /// not hidden.</summary>
    public static bool Enabled =>
        (Current()?.SubtitlesEnabled ?? true) &&
        GameHud.ElementMode(HudElement.Subtitles) != HudElementMode.Hidden;

    /// <summary>Whether a menu, a conversation or the pause state has the world stopped. A cinematic
    /// lock (the boss intro) does not: the line is part of what the player is held still to watch.</summary>
    private static bool Held =>
        UiState.WorldPaused || GameManager.Instance is { State: not GameState.Playing };

    /// <summary>Captions a line on the session's layer if there is one and it would be shown.
    /// False means the line was not captioned and the caller should say it another way.</summary>
    public static bool TryShow(string? speaker, string text, float seconds) =>
        _current is { } layer && IsInstanceValid(layer) && layer.Show(speaker, text, seconds);

    /// <summary>Captions one line for <paramref name="seconds"/> (its reading time when that is not
    /// positive). <paramref name="speaker"/> is the already-localised name, or null for a line nobody
    /// is credited with. A line arriving while another is up waits its turn. Returns whether the
    /// line was taken: not with subtitles off, the world held, or three lines already waiting.</summary>
    public bool Show(string? speaker, string text, float seconds)
    {
        if (!IsInsideTree() || !Enabled || Held || string.IsNullOrWhiteSpace(text) ||
            (_showing && _queue.Count >= MaxQueued))
        {
            return false;
        }

        var line = new Line(speaker, text, seconds > 0f ? seconds : (float)SubtitleRules.Seconds(text));
        if (!_showing)
        {
            Begin(line, arriving: true);
            return true;
        }

        _queue.Enqueue(line);
        return true;
    }

    /// <summary>Takes down whatever is showing, and whatever was waiting behind it.</summary>
    public void Dismiss()
    {
        _queue.Clear();
        _showing = false;
        SetHeld(false);
        SetProcess(false);
        UiFx.FadeOut(_plate, seconds: 0f);
    }

    private void Begin(Line line, bool arriving)
    {
        _pages = SubtitleRules.Pages(line.Text);
        _lineLength = 0;
        foreach (string page in _pages)
        {
            _lineLength += page.Length;
        }

        _lineSeconds = line.Seconds;
        _speaker.Text = line.Speaker ?? string.Empty;
        _speaker.Visible = _speakerNames && !string.IsNullOrEmpty(line.Speaker);
        _page = -1;
        _showing = true;
        SetProcess(true);
        NextPage();
        Place();
        if (arriving)
        {
            UiFx.FadeIn(_plate, UiTheme.DurationFast);
        }
    }

    private void NextPage()
    {
        _page++;
        if (_page < _pages.Count)
        {
            _text.Text = _pages[_page];
            _pageLeft = SubtitleRules.PageSeconds(_lineSeconds, _pages[_page].Length, _lineLength);
            return;
        }

        if (_queue.Count > 0)
        {
            Begin(_queue.Dequeue(), arriving: false);
            return;
        }

        _showing = false;
        SetProcess(false);
        UiFx.FadeOut(_plate);
    }

    public override void _Process(double delta)
    {
        // A line belongs to the session it was spoken in: the title screen and a loading screen
        // caption nothing.
        if (GameManager.Instance is { State: not (GameState.Playing or GameState.Paused) })
        {
            Dismiss();
            return;
        }

        // A menu over the caption: out of its way, and the page's time is not spent behind it.
        SetHeld(Held);
        if (_held)
        {
            return;
        }

        Place();
        _pageLeft -= delta;
        if (_pageLeft <= 0d)
        {
            NextPage();
        }
    }

    private void SetHeld(bool held)
    {
        if (held != _held)
        {
            _held = held;
            _root.Visible = !held;
        }
    }

    /// <summary>
    /// Sits the plate on the line the HUD keeps clear above the hotbar, or just above whatever the
    /// HUD's bottom-centre slot is showing (the interaction prompt, a tutorial hint) when that
    /// reaches higher. Written only when the answer moves.
    /// </summary>
    private void Place()
    {
        HudLayout? layout = Layout();
        float height = _root.Size.Y;
        float bottom = height - HudMetrics.ScreenClearance(
            HudLayout.BottomClearance, height, layout?.HudScale ?? 1f, layout?.SafeZone ?? 0f);
        if (layout != null && layout.BottomCenter.IsVisibleInTree())
        {
            bottom = Mathf.Min(bottom, layout.BottomCenter.GetGlobalRect().Position.Y - UiTheme.HudGap);
        }

        if (bottom != _bottomShown)
        {
            _bottomShown = bottom;
            _plate.OffsetTop = bottom - height;
            _plate.OffsetBottom = bottom - height;
        }
    }

    private HudLayout? Layout()
    {
        if (_layout != null && IsInstanceValid(_layout))
        {
            return _layout;
        }

        _layout = null;
        Node? parent = GetParent();
        int siblings = parent?.GetChildCount() ?? 0;
        for (int i = 0; i < siblings && _layout == null; i++)
        {
            if (parent!.GetChild(i) is GameHud hud)
            {
                _layout = hud.GetNodeOrNull<HudLayout>("Layout");
            }
        }

        return _layout;
    }

    private void OnSettingsApplied(SettingsAppliedEvent e)
    {
        ApplySettings();
        if (_showing && !Enabled)
        {
            Dismiss();
        }
    }

    // A caption from the timeline a load just abandoned.
    private void OnGameLoaded(GameLoadedEvent e) => Dismiss();

    /// <summary>The subtitle options: text size, the plate's opacity and whether names are shown.</summary>
    private void ApplySettings()
    {
        Settings.Settings? settings = Current();
        int size = SettingsMath.ClampSubtitleSize(settings?.SubtitleSize ?? 1);
        float ground = Mathf.Clamp(settings?.SubtitleBackground ?? 0.5f, 0f, 1f);
        _speakerNames = settings?.SubtitleSpeakerNames ?? true;

        UiTheme.ApplyType(_text, UiTheme.FontRole.Interface, SubtitleRules.FontToken(size));
        UiTheme.ApplyType(_speaker, UiTheme.FontRole.Interface, SubtitleRules.SpeakerFontToken(size));

        // High contrast makes a plate the player asked for opaque; it does not add one they turned off.
        _ground.BgColor = UiTheme.ScrimBg with { A = UiTheme.HighContrast && ground > 0f ? 1f : ground };
    }

    private static Settings.Settings? Current() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService service)
            ? service.Current
            : null;
}
