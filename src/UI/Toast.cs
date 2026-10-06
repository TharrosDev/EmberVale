using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// A single transient notification. It rises into place while fading up (motion-gated), holds full
/// opacity for <see cref="Dwell"/> seconds, then fades out and frees itself. Built and stacked by
/// <see cref="Notifications"/>; a HUD plate (<see cref="UiTheme.HudPlateStyle"/>) whose one lit
/// edge is the colour of the thing announced.
///
/// Structurally a margin wrapper around the visible plate: the stack container owns this node's
/// position, so the rise animates the inner margins (+s top / −s bottom shifts the plate down by s
/// without changing the wrapper's layout size) instead of fighting the layout.
///
/// A second notice that says the same thing does not stack a second toast: <see cref="Bump"/> adds
/// to this one's count and starts its dwell again.
/// </summary>
public partial class Toast : MarginContainer
{
    /// <summary>Seconds held at full opacity, between the entrance and the fade
    /// (<see cref="ToastRules.Dwell(string, string?, float)"/>).</summary>
    public double Dwell { get; set; } = ToastRules.MinDwellSeconds;

    private readonly PanelContainer _chip = new();
    private double _age;
    private double _leaving = -1d; // < 0: not fading out yet
    private double _exitSeconds;

    // What was last written, so a toast holding still writes nothing.
    private float _alphaShown = -1f;
    private int _riseShown = int.MinValue;

    /// <summary>The semantic colour of the thing being announced (a level-up, a failed event, an
    /// autosave). Painted as the plate's left edge. Set before the toast enters the tree.</summary>
    public Color Accent { get; set; } = UiTheme.Accent;

    /// <summary>The "×3" label <see cref="Bump"/> writes, hidden while the count is one.</summary>
    public Label? CountLabel { get; set; }

    /// <summary>How many of the thing this toast stands for.</summary>
    public int Count { get; private set; } = 1;

    /// <summary>Where the key hint's glyph sits, and the action it shows, for a toast that has one.</summary>
    public Control? GlyphSlot { get; set; }

    public string? GlyphAction { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _chip.MouseFilter = MouseFilterEnum.Ignore;

        // A plate, not a Panel (37.5F): a transient line of text takes the HUD's light ground and one
        // lit edge, never a framed screen's brass and grain.
        _chip.AddThemeStyleboxOverride("panel", UiTheme.HudPlateStyle(Accent));
        AddChild(_chip);
        RefreshGlyph();
        Apply();
    }

    /// <summary>True once the toast has started its fade-out, so it no longer counts against the feed's room.</summary>
    public bool Expiring => _leaving >= 0d;

    /// <summary>Starts a short fade-out so the feed gets its room back soon, without the toast the
    /// player is reading disappearing between two frames. A toast already fading is left alone.</summary>
    public void Expedite() => Leave(UiTheme.DurationBase);

    /// <summary>Parents <paramref name="content"/> into the visible plate.</summary>
    public void AddContent(Control content) => _chip.AddChild(content);

    /// <summary>Sets the count to <paramref name="count"/> and starts the dwell again. False when the
    /// toast is already leaving: the caller shows a new one instead.</summary>
    public bool Bump(int count)
    {
        if (Expiring)
        {
            return false;
        }

        SetCount(count);
        _age = System.Math.Min(_age, UiTheme.Duration(UiTheme.DurationBase));
        return true;
    }

    /// <summary>Writes the count label. One shows no label: "×1" is noise.</summary>
    public void SetCount(int count)
    {
        Count = System.Math.Max(1, count);
        if (CountLabel != null)
        {
            CountLabel.Text = Loc.TF("hudc.toast.count", Count);
            CountLabel.Visible = Count > 1;
        }
    }

    /// <summary>Redraws the key hint for the device and bindings in use now. A glyph is a snapshot.</summary>
    public void RefreshGlyph()
    {
        if (GlyphSlot == null || string.IsNullOrEmpty(GlyphAction))
        {
            return;
        }

        UiTheme.ClearChildren(GlyphSlot);
        GlyphSlot.AddChild(UiGlyph.For(GlyphAction));
    }

    public override void _Process(double delta)
    {
        _age += delta;
        float enter = UiTheme.Duration(UiTheme.DurationBase);
        if (_leaving >= 0d)
        {
            _leaving += delta;
            if (_leaving >= _exitSeconds)
            {
                QueueFree();
                return;
            }
        }
        else if (_age >= enter + Dwell)
        {
            Leave(UiTheme.DurationSlow);
            if (_exitSeconds <= 0d)
            {
                QueueFree();
                return;
            }
        }

        Apply();
    }

    private void Leave(float seconds)
    {
        if (_leaving < 0d)
        {
            _leaving = 0d;
            _exitSeconds = UiTheme.Duration(seconds);
        }
    }

    /// <summary>Entrance: a short climb with an ease-out over DurationBase (no climb under reduced
    /// motion), fading up alongside. Exit: an ease-in fade.</summary>
    private void Apply()
    {
        float entrance = UiMotion.EaseOut(UiMotion.Progress((float)_age, UiTheme.Duration(UiTheme.DurationBase)));
        float alpha = _leaving >= 0d && _exitSeconds > 0d
            ? Mathf.Min(entrance, 1f - UiMotion.EaseIn(UiMotion.Progress((float)_leaving, (float)_exitSeconds)))
            : entrance;

        int rise = Mathf.RoundToInt(UiFx.RiseDistance * (1f - entrance));
        if (rise != _riseShown)
        {
            _riseShown = rise;
            AddThemeConstantOverride("margin_top", rise);
            AddThemeConstantOverride("margin_bottom", -rise);
        }

        alpha = Mathf.Clamp(alpha, 0f, 1f);
        if (alpha != _alphaShown)
        {
            _alphaShown = alpha;
            Modulate = new Color(1f, 1f, 1f, alpha);
        }
    }
}
