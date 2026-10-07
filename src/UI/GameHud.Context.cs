using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>The contextual readouts of <see cref="GameHud"/>: clock and weather, the world-event banner,
/// the interaction prompt and its keycaps, and the lock-on reticle.</summary>
public partial class GameHud
{
    private Label _context = null!;
    private HudIcon _phaseGlyph = null!;
    private Label _phaseText = null!;
    private Label _weatherText = null!;

    private PanelContainer _bannerPanel = null!;
    private Label _bannerText = null!;
    private Label _bannerTimer = null!;

    private PanelContainer _promptPanel = null!;
    private Control _promptGlyph = null!;
    private Label _promptText = null!;
    private Label _promptNoun = null!;
    private Label _promptHold = null!;

    // A glyph is a snapshot of a binding on a device (UiGlyph), so each is redrawn when either moves.
    private bool _promptGlyphStale = true;
    private bool _spellGlyphStale = true;

    // As _vitalsQuiet: the tick after an invalidation restates everything, and that is not news.
    private bool _contextQuiet = true;

    private Control _lockReticle = null!;

    // -1 = nothing shown yet, -2 = the "no clock" empty state, otherwise the DayPhase.
    private int _phaseShown = -1;
    private int _hourShown = -1;
    private bool _weatherKnown;
    private WeatherResource? _weatherShown;

    private WorldEvent? _bannerShown;
    private int _bannerProgress = int.MinValue;
    private int _bannerRequired = int.MinValue;
    private float _bannerSecondsShown = float.NaN;
    private int _bannerHot = -1;

    private string? _promptShown;
    private string? _promptNounShown;
    private int _promptHoldShown = -1;

    private void InvalidateContextShown()
    {
        _promptNounShown = null;
        _promptHoldShown = -1;
        _promptGlyphStale = true;
        _spellGlyphStale = true;
        _contextQuiet = true;
        _phaseShown = -1;
        _hourShown = -1;
        _weatherKnown = false;
        _bannerShown = null;
        _bannerSecondsShown = float.NaN;
        _bannerHot = -1;
        _promptShown = null;
    }

    /// <summary>
    /// The world clock, its phase and the weather (§25–§27).
    ///
    /// ⚠️ <b>This was one `Body` label reading "10:00  (Day)   ·   Fog".</b> Three unrelated facts in
    /// one string at one weight, so nothing in it could be read at a glance — and the phase came from
    /// a hard-coded English literal (see <see cref="DayPhases.NameKey"/>). §26 asks for the time to be
    /// communicated subtly rather than spelled out, so the phase is now a **glyph** carrying the
    /// dawn/day/dusk/night state, the hour is the thing you actually read, and the weather is
    /// subordinate — the hierarchy §3 asks for, inside one small widget.
    /// </summary>
    private void BuildContext()
    {
        // One line of inked text on a shade. It sat bare on the world, and this is the corner of the
        // screen that is sky: bone text and a gold sun over a bright noon were not there. The shade is
        // the thinnest ground the HUD has and carries no edge, so it is still a line and not a box.
        PanelContainer panel = Ignore(UiTheme.HudShade());
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _layout.TopLeft.AddChild(panel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        // Shape AND colour carry the phase, so it survives ColorVision (§40) — and it is never the
        // only channel, because the phase name is on the same row.
        _phaseGlyph = HudIcon.Create(UiIcon.Kind.Sun, HudCoreMetrics.IconSize, UiTheme.Accent);
        _phaseGlyph.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(_phaseGlyph);

        _context = UiTheme.HudInk(UiTheme.Body("", UiTheme.Text));
        _context.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_context);

        // The phase and the weather are subordinate by size, not by being dimmer: Dim over a
        // translucent ground over the sky is the pairing that could not be read.
        _phaseText = UiTheme.HudInk(UiTheme.Caption("", UiTheme.Text));
        _phaseText.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_phaseText);

        _weatherText = UiTheme.HudInk(UiTheme.Caption("", UiTheme.Text));
        _weatherText.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_weatherText);

        panel.AddChild(row);
    }

    /// <summary>The phase's glyph and tint. Warm at midday, cold at night, ember at the edges of the
    /// day — the same palette language the rest of the UI uses.</summary>
    private static (UiIcon.Kind Icon, Color Tint) PhaseMark(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => (UiIcon.Kind.Sun, UiTheme.Accent),
        DayPhase.Day => (UiIcon.Kind.Sun, UiTheme.Accent),
        DayPhase.Dusk => (UiIcon.Kind.Moon, UiTheme.AccentHot),
        _ => (UiIcon.Kind.Moon, UiTheme.ArcaneSilver),
    };

    private void BuildBanner()
    {
        _bannerPanel = Ignore(UiTheme.HudPlate(UiTheme.AccentHot));
        _bannerPanel.Visible = false;
        _bannerPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _layout.TopCenter.AddChild(_bannerPanel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        row.AddChild(UiIcon.Create(UiIcon.Kind.Warning, HudCoreMetrics.IconSize, UiTheme.Accent));
        _bannerText = UiTheme.HudInk(UiTheme.Body("", UiTheme.Accent));
        row.AddChild(_bannerText);
        _bannerTimer = UiTheme.HudInk(UiTheme.Body("", UiTheme.Dim));
        row.AddChild(_bannerTimer);
        _bannerPanel.AddChild(row);
    }

    /// <summary>The held lock-on mark (Phase 29H), tracked onto the locked target's screen position:
    /// the same small keylined dot <see cref="LockOnCueLayer"/> closes its ring onto
    /// (<see cref="UiTheme.DrawLockDot"/>). It was the waypoint pin, which said "go here" on the one
    /// thing the player is already fighting.</summary>
    private void BuildLockReticle()
    {
        var reticle = new Control
        {
            Name = "LockReticle",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(HudCoreMetrics.ReticleSize, HudCoreMetrics.ReticleSize),
        };
        reticle.Draw += () => UiTheme.DrawLockDot(reticle, reticle.Size * 0.5f);
        _lockReticle = reticle;
        _layout.Overlay.AddChild(_lockReticle);
    }

    /// <summary>Redraw the prompt's and the spell row's glyphs when the player switches between
    /// keyboard and gamepad (30.5J): "E" becomes the pad's own button shape, live, no rebuild.</summary>
    private void OnInputDeviceChanged(InputDeviceChangedEvent e) => MarkGlyphsStale();

    private void OnInputBindingsChanged(InputBindingsChangedEvent e) => MarkGlyphsStale();

    // A glyph that does not follow the device or the binding is worse than none: it confidently
    // names a key the player's controller does not have (39.5B).
    private void MarkGlyphsStale()
    {
        _promptGlyphStale = true;
        _spellGlyphStale = true;
    }

    /// <summary>Puts <paramref name="action"/>'s glyph for the device in hand into <paramref name="host"/>,
    /// replacing the one that was there.</summary>
    private static void SetGlyph(Control host, string action)
    {
        foreach (Node child in host.GetChildren())
        {
            host.RemoveChild(child);
            child.QueueFree();
        }

        host.AddChild(UiGlyph.For(action));
    }

    /// <summary>
    /// The interaction prompt: the input's glyph, then what it does and to what.
    ///
    /// The phrase is one localized string ("Loot Iron chest"), because word order is the
    /// translator's. Where it ends with the name of the thing aimed at, that name is set apart as
    /// the noun (<see cref="PromptRules.Split"/>); where it does not, the phrase is shown whole.
    /// Picking something up can also be held to gather everything nearby, which nothing told the
    /// player before: the second line says so, only for a pickup.
    /// </summary>
    private void BuildPrompt()
    {
        _promptPanel = Ignore(UiTheme.HudPlate(UiTheme.Accent));
        _promptPanel.Visible = false;
        _promptPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _layout.BottomCenter.AddChild(_promptPanel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        _promptGlyph = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddChild(_promptGlyph);

        var lines = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        lines.AddThemeConstantOverride("separation", 0);
        row.AddChild(lines);

        var phrase = new HBoxContainer();
        phrase.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        _promptText = UiTheme.HudInk(UiTheme.Body("", UiTheme.Text));
        phrase.AddChild(_promptText);
        _promptNoun = UiTheme.HudInk(UiTheme.Body("", UiTheme.Accent));
        _promptNoun.Visible = false;
        phrase.AddChild(_promptNoun);
        lines.AddChild(phrase);

        _promptHold = UiTheme.HudInk(UiTheme.Caption(Loc.T("hud.prompt.hold_gather"), UiTheme.Dim));
        _promptHold.Visible = false;
        lines.AddChild(_promptHold);

        _promptPanel.AddChild(row);

        // GameHud's own subscriptions do not include a rebind; this one is taken and returned here.
        EventBus.Instance?.Subscribe<InputBindingsChangedEvent>(OnInputBindingsChanged);
        TreeExiting += () => EventBus.Instance?.Unsubscribe<InputBindingsChangedEvent>(OnInputBindingsChanged);
    }

    private void UpdateContext()
    {
        if (_clock is not { } clock || !IsInstanceValid(clock))
        {
            // Graceful empty state (§53): the widget goes away rather than showing an empty frame or
            // a placeholder time the player might believe.
            if (_phaseShown != -2)
            {
                _phaseShown = -2;
                _hourShown = -1;
                _weatherKnown = false;
                _phaseGlyph.Visible = false;
                _context.Text = string.Empty;
                _phaseText.Text = string.Empty;
                _weatherText.Text = string.Empty;
            }

            return;
        }

        // The phase changes four times a day and the hour twenty-four: neither is restated per frame.
        DayPhase phase = clock.Phase;
        if ((int)phase != _phaseShown)
        {
            _phaseShown = (int)phase;
            (UiIcon.Kind icon, Color tint) = PhaseMark(phase);
            _phaseGlyph.Visible = true;
            _phaseGlyph.Texture = UiIcon.Texture(icon);
            _phaseGlyph.Tint = UiTheme.Adapt(tint);
            _phaseText.Text = Loc.T(DayPhases.NameKey(phase));
            NoteClockChanged();
        }

        int hour = clock.Hour;
        if (hour != _hourShown)
        {
            _hourShown = hour;
            _context.Text = clock.Clock();
        }

        WeatherResource? current = _weather is { } weather && IsInstanceValid(weather) ? weather.Current : null;
        if (!_weatherKnown || !ReferenceEquals(current, _weatherShown))
        {
            _weatherKnown = true;
            _weatherShown = current;
            _weatherText.Visible = current != null;
            if (current != null)
            {
                _weatherText.Text = $"· {current.DisplayName}";
            }

            NoteClockChanged();
        }

        _contextQuiet = false;
    }

    /// <summary>A Dynamic clock comes up when the day turns a phase or the weather changes, which is
    /// when the player would look at it. The hour ticking over is not news.</summary>
    private void NoteClockChanged()
    {
        if (!_contextQuiet)
        {
            MarkChanged(HudElement.Clock);
        }
    }

    private void UpdateBanner()
    {
        if (_worldEvents is { } director && IsInstanceValid(director) && director.Active is { } worldEvent)
        {
            // The line is the event's name and its progress count; it is rebuilt when either moves.
            if (!ReferenceEquals(worldEvent, _bannerShown) || worldEvent.Progress != _bannerProgress ||
                worldEvent.Required != _bannerRequired)
            {
                _bannerShown = worldEvent;
                _bannerProgress = worldEvent.Progress;
                _bannerRequired = worldEvent.Required;
                _bannerSecondsShown = float.NaN;
                _bannerHot = -1;
                _bannerText.Text = $"{worldEvent.Name} — {worldEvent.ObjectiveLabel()}";
            }

            // Separate countdown that heats to ember orange in the final seconds (urgency read).
            _bannerTimer.Visible = worldEvent.IsTimed;
            if (worldEvent.IsTimed)
            {
                float seconds = (float)System.Math.Round(worldEvent.TimeLeft, System.MidpointRounding.AwayFromZero);
                if (seconds != _bannerSecondsShown)
                {
                    _bannerSecondsShown = seconds;
                    _bannerTimer.Text = $"{worldEvent.TimeLeft:0}s";
                }

                int hot = worldEvent.TimeLeft <= 10f ? 1 : 0;
                if (hot != _bannerHot)
                {
                    _bannerHot = hot;
                    UiLive.FontColor(_bannerTimer, hot == 1 ? UiTheme.AccentHot : UiTheme.Dim);
                }
            }

            _bannerPanel.Visible = true;
        }
        else
        {
            _bannerShown = null;
            _bannerPanel.Visible = false;
        }
    }

    private void UpdateFocus()
    {
        InteractionSensor? focusSensor = _player?.GetComponent<InteractionSensor>();

        // The locked-on target (Phase 29H) takes nameplate priority over the aimed-at focus, and is
        // reticled at its projected screen position.
        IEntity? locked = _player?.GetComponent<Combat.LockOnComponent>()?.Target;
        UpdateLockReticle(locked);
        IEntity? focus = (locked is Node lockNode && IsInstanceValid(lockNode)) ? locked : focusSensor?.FocusedEntity;

        // Nameplate for an aimed-at damageable that isn't the player. The widget owns the
        // validity guard, the snap-on-target-change and the disposition tint (37.5B).
        _nameplate.Show(Shows(HudElement.TargetPlate) ? focus : null, _player);

        // Interaction prompt for an aimed-at interactable.
        string? prompt = focusSensor?.FocusPrompt;
        bool prompting = !string.IsNullOrEmpty(prompt);

        // Aiming at something is what a Dynamic crosshair, and a Dynamic target plate, are for.
        if (prompting || focus != null)
        {
            MarkChanged(HudElement.Crosshair);
            MarkChanged(HudElement.TargetPlate);
        }

        if (prompting && Shows(HudElement.Prompts))
        {
            string? noun = focusSensor!.FocusedEntity?.DisplayName;
            if (prompt != _promptShown || noun != _promptNounShown)
            {
                _promptShown = prompt;
                _promptNounShown = noun;
                (string verb, string thing) = PromptRules.Split(prompt!, noun);
                _promptText.Text = verb;
                _promptNoun.Text = thing;
                _promptNoun.Visible = thing.Length > 0;
            }

            int hold = focusSensor.FocusedInteractable is Embervale.Items.ItemPickupComponent ? 1 : 0;
            if (hold != _promptHoldShown)
            {
                _promptHoldShown = hold;
                _promptHold.Visible = hold == 1;
            }

            if (_promptGlyphStale)
            {
                _promptGlyphStale = false;
                SetGlyph(_promptGlyph, GameInput.Interact);
            }

            _promptPanel.Visible = true;
        }
        else
        {
            _promptPanel.Visible = false;
        }
    }

    /// <summary>Tracks the lock-on reticle onto the target's body, hiding it when there's no lock, the
    /// target is gone, or it's behind the camera.</summary>
    private void UpdateLockReticle(IEntity? locked)
    {
        if (locked is Node node && IsInstanceValid(node) && locked.Body is Node3D body &&
            GetViewport().GetCamera3D() is { } camera)
        {
            // The same point the lock itself holds and the acquire ring closes onto.
            Vector3 head = Combat.BodyMetrics.AimPoint(body);
            if (!camera.IsPositionBehind(head))
            {
                _lockReticle.Position = camera.UnprojectPosition(head) - (_lockReticle.Size / 2f);

                // A slow breathe so the lock reads as live, not a painted marker (30.5E).
                float alpha = UiTheme.MotionEnabled
                    ? 0.8f + (0.2f * Mathf.Sin(Time.GetTicksMsec() / 250f))
                    : 1f;
                _lockReticle.Modulate = new Color(1f, 1f, 1f, alpha);
                _lockReticle.Visible = true;
                return;
            }
        }

        _lockReticle.Visible = false;
    }
}
