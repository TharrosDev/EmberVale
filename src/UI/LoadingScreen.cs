using System.Collections.Generic;
using Embervale.Bootstrap;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The full-screen cover shown during a hard load. It reacts to <see cref="GameStateChangedEvent"/>
/// and is up only while the game is in <see cref="GameState.Loading"/>, covering the screen while
/// the bootstrap swaps regions and the streamer pulls in the destination cells. Runs with
/// <see cref="Node.ProcessModeEnum.Always"/> and sits above the rest of the UI.
///
/// What it shows beyond "Loading" comes from <see cref="LoadingProgressEvent"/>: the destination
/// realm's painting and name, and a thin line that fills as the gate's stages clear. Shown
/// without one (outside a session) it is the generic painting and a line that only says the game
/// is working. Under the name sits one tip or lore line (<see cref="LoadingCardRules"/>), chosen
/// when the cover goes up. Nothing here waits for the player: it lifts the moment play resumes.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
    /// <summary>How much of the line the fill gains a second while it catches up to a cleared stage.</summary>
    private const float FillPerSecond = 1.6f;

    /// <summary>Sweeps a second of the lit segment that stands for "working" with no stage known,
    /// and how much of the line that segment covers.</summary>
    private const float SweepPerSecond = 0.55f;
    private const float SweepSpan = 0.22f;

    private Control _root = null!;
    private TextureRect _art = null!;
    private MarginContainer _block = null!;
    private Label _name = null!;
    private Control _line = null!;
    private Label _kind = null!;
    private Label _card = null!;

    private string? _regionId;
    private string _painting = string.Empty;
    private string? _cardKey;
    private int _step = -1;
    private float _fill;
    private float _sweep;
    private int _loads;

    // Dismissal fade (30.5I): elapsed fade-out time; <0 while idle.
    private float _fadeAge = -1f;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 20; // above the pause menu (10) so a transition is never occluded
        Build();
        SetShown(GameManager.Instance?.State == GameState.Loading);
        EventBus.Instance?.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        EventBus.Instance?.Subscribe<LoadingProgressEvent>(OnProgress);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
        EventBus.Instance?.Unsubscribe<LoadingProgressEvent>(OnProgress);
    }

    private void Build()
    {
        // One root, so the dismissal fades the painting and the words as one thing, and the
        // pointer never reaches what is being loaded underneath.
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        ColorRect ground = UiTheme.Scrim(1f); // what shows if no painting could be loaded
        ground.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(ground);

        _art = UiTheme.Cover(null);
        _root.AddChild(_art);

        // The paintings keep their calm space lower right; the dark gathers there, under the words.
        _root.AddChild(UiTheme.Shade(new Vector2(0.2f, 1f), new Vector2(1f, 1f), 0.9f, radial: true));

        _block = new MarginContainer
        {
            AnchorLeft = 0.92f,
            AnchorRight = 0.92f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetBottom = -UiTheme.SpaceXl,
            GrowVertical = Control.GrowDirection.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(_block);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _block.AddChild(column);

        _name = UiTheme.Display(string.Empty, UiTheme.Text);
        _name.HorizontalAlignment = HorizontalAlignment.Left;
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_name);

        _line = new Control
        {
            CustomMinimumSize = new Vector2(0f, UiTheme.HighContrast ? UiTheme.LoadingLineHeight * 2f : UiTheme.LoadingLineHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _line.Draw += DrawLine;
        column.AddChild(_line);

        var card = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        card.AddThemeConstantOverride("separation", UiTheme.LineGap);
        column.AddChild(card);

        _kind = UiTheme.Caption(string.Empty, UiTheme.Accent);
        card.AddChild(_kind);
        _card = UiTheme.Prose(string.Empty, UiTheme.Text);
        _card.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        card.AddChild(_card);
    }

    private void OnGameStateChanged(GameStateChangedEvent e) => SetShown(e.Current == GameState.Loading);

    private void SetShown(bool visible)
    {
        if (!visible && _root.Visible && _fadeAge < 0f &&
            UiTheme.Duration(UiTheme.DurationSlow) > 0f)
        {
            // Fade the cover away to reveal the arrival (ease-in exit); showing stays instant
            // so the screen is covered the moment a transition starts.
            _fadeAge = 0f;
            SetProcess(true);
            return;
        }

        bool wasUp = _root.Visible && _fadeAge < 0f;
        _fadeAge = -1f;
        _root.Modulate = Colors.White;
        _root.Visible = visible;
        if (visible && !wasUp)
        {
            BeginLoad();
        }

        // Asleep whenever it is down: the line only moves, and the fade only runs, while it is up.
        SetProcess(visible);
    }

    /// <summary>The cover has just gone up: nothing is known yet about where the load is going.</summary>
    private void BeginLoad()
    {
        _regionId = null;
        _step = -1;
        _fill = 0f;
        _sweep = 0f;
        _loads++;

        Vector2 view = GetViewport().GetVisibleRect().Size;
        float width = Mathf.Min(UiTheme.LoadingBlockWidth, view.X - (UiTheme.SpaceLg * 2f));
        _block.OffsetLeft = -width;
        _block.OffsetRight = 0f;

        ShowRegion(null);
        ShowCard(LoadingCardRules.Pick(VisitedRegions(), (int)(Time.GetTicksMsec() % 100003UL) + _loads, _cardKey));
        _line.QueueRedraw();
    }

    private void OnProgress(LoadingProgressEvent e)
    {
        if (!_root.Visible || _fadeAge >= 0f)
        {
            return;
        }

        if (e.RegionId != _regionId)
        {
            ShowRegion(e.RegionId);
        }

        // A stage count that runs backwards is a second load opened on top of the first.
        if (e.Step < _step || UiTheme.Duration(UiTheme.DurationBase) <= 0f)
        {
            _fill = (float)e.Step / LoadingProgressEvent.Steps;
        }

        _step = e.Step;
        _line.QueueRedraw();
    }

    /// <summary>
    /// Puts up the painting and the name for a load into <paramref name="regionId"/>, or the
    /// generic pair for null. ⚠️ Both come from the destination and nothing else, which is what
    /// keeps the hidden realm off this screen until the player is walking into it.
    /// </summary>
    private void ShowRegion(string? regionId)
    {
        _regionId = regionId;
        _name.Text = (regionId != null ? RegionDatabase.Get(regionId)?.DisplayName : null) ?? Loc.T("loading.title");

        // A headless run draws nothing, so it never pays to read a painting off disk.
        string painting = LoadingCardRules.Painting(regionId);
        if (painting == _painting || DisplayServer.GetName() == "headless")
        {
            return;
        }

        _painting = painting;
        _art.Texture = UiTheme.Painting(painting);
    }

    private void ShowCard(string key)
    {
        _cardKey = key;
        _kind.Text = Loc.T(LoadingCardRules.IsLore(key) ? "loading.card.lore" : "loading.card.tip");
        _card.Text = Loc.T(key);
    }

    /// <summary>The realms this save has set foot in, or none outside a session.</summary>
    private static List<string> VisitedRegions()
    {
        var visited = new List<string>();
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out MapService map))
        {
            foreach (MapMarker marker in map.RegionMarkers())
            {
                visited.Add(marker.Id);
            }
        }

        return visited;
    }

    private void DrawLine()
    {
        Vector2 size = _line.Size;
        _line.DrawRect(new Rect2(Vector2.Zero, size), UiTheme.Rule);

        if (_step < 0)
        {
            // No stage known: a lit segment that crosses the line, or rests at its middle when
            // nothing is allowed to move.
            float span = size.X * SweepSpan;
            float at = UiTheme.MotionEnabled ? (_sweep * (size.X + span)) - span : (size.X - span) * 0.5f;
            float left = Mathf.Max(0f, at);
            float right = Mathf.Min(size.X, at + span);
            if (right > left)
            {
                _line.DrawRect(new Rect2(left, 0f, right - left, size.Y), UiTheme.Accent);
            }

            return;
        }

        float lit = size.X * Mathf.Clamp(_fill, 0f, 1f);
        if (lit <= 0f)
        {
            return;
        }

        // Ember gold, with the leading end still hot.
        float head = Mathf.Min(lit, UiTheme.SpaceSm);
        _line.DrawRect(new Rect2(0f, 0f, lit - head, size.Y), UiTheme.Accent);
        _line.DrawRect(new Rect2(lit - head, 0f, head, size.Y), UiTheme.AccentHot);
    }

    public override void _Process(double delta)
    {
        if (_fadeAge >= 0f)
        {
            _fadeAge += (float)delta;
            float alpha = 1f - UiMotion.EaseIn(UiMotion.Progress(_fadeAge, UiTheme.Duration(UiTheme.DurationSlow)));
            _root.Modulate = new Color(1f, 1f, 1f, alpha);
            if (alpha <= 0f)
            {
                SetShown(false);
            }

            return;
        }

        if (_step < 0)
        {
            if (UiTheme.MotionEnabled)
            {
                _sweep = Mathf.PosMod(_sweep + ((float)delta * SweepPerSecond), 1f);
                _line.QueueRedraw();
            }

            return;
        }

        float target = (float)_step / LoadingProgressEvent.Steps;
        if (_fill != target)
        {
            _fill = Mathf.MoveToward(_fill, target, (float)delta * FillPerSecond);
            _line.QueueRedraw();
        }
    }

    // --- Capture accessors --------------------------------------------------

    /// <summary>The region the cover is dressed for, or null for the generic one.</summary>
    public string? RegionForCapture => _regionId;

    /// <summary>The painting on screen, by file name.</summary>
    public string PaintingForCapture => _painting;

    /// <summary>Stages cleared, or -1 while progress is indeterminate.</summary>
    public int StepForCapture => _step;

    /// <summary>The tip or lore key under the name.</summary>
    public string? CardForCapture => _cardKey;

    /// <summary>Whether the cover is up and not fading away.</summary>
    public bool ShownForCapture => _root.Visible && _fadeAge < 0f;
}
