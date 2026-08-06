using Godot;

namespace Embervale.UI;

/// <summary>
/// A single transient notification chip. It slides in from the right while fading up (30.5I,
/// motion-gated), lives for <see cref="Life"/> seconds holding full opacity, then fades out
/// and frees itself. Built and stacked by <see cref="Notifications"/>; styled through
/// <see cref="UiTheme"/>.
///
/// Structurally a margin wrapper around the visible panel chip: the stack container owns this
/// node's position, so the slide animates the inner margins (+s left / −s right shifts the
/// chip right by s without changing the wrapper's layout size) instead of fighting the layout.
/// </summary>
public partial class Toast : MarginContainer
{
    public double Life { get; set; } = 4.0;

    /// <summary>Horizontal slide-in distance (px at reference scale).</summary>
    private const float SlideDistance = 24f;

    private readonly PanelContainer _chip = new();
    private double _age;

    /// <summary>The semantic colour of the thing being announced (a level-up, a failed event, an
    /// autosave). Painted as the chip's left spine. Set before the toast enters the tree.</summary>
    public Color Accent { get; set; } = UiTheme.Accent;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _chip.MouseFilter = MouseFilterEnum.Ignore;

        // A Card, not a Panel (37.5F). Toasts had been built from PanelStyle(), so after 37.5A each
        // transient chip carried a 2 px brass rule, an engraved shadow and its own grain
        // ShaderMaterial - a full framed screen's worth of chrome for one line of text that lives
        // four seconds. This is the third instance of the same pattern (status chips in 37.5B, save
        // rows in this same phase): a small widget that reused Panel() as a generic box.
        _chip.AddThemeStyleboxOverride("panel", UiTheme.CardStyle(Accent));
        AddChild(_chip);
        ApplySlide(UiTheme.Duration(UiTheme.DurationBase) > 0f ? SlideDistance : 0f);
    }

    /// <summary>Parents <paramref name="content"/> into the visible chip.</summary>
    public void AddContent(Control content) => _chip.AddChild(content);

    public override void _Process(double delta)
    {
        _age += delta;
        float t = (float)(_age / Life);
        if (t >= 1f)
        {
            QueueFree();
            return;
        }

        // Entrance: slide in from the right with an ease-out over DurationBase (collapses to
        // no slide under reduced motion), fading up alongside.
        float entrance = UiMotion.EaseOut(UiMotion.Progress((float)_age, UiTheme.Duration(UiTheme.DurationBase)));
        ApplySlide(SlideDistance * (1f - entrance));

        // Fade up with the entrance; hold; then fade out over the final 40% of the lifetime.
        float alpha = t < 0.6f ? entrance : 1f - ((t - 0.6f) / 0.4f);
        Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(alpha, 0f, 1f));
    }

    private void ApplySlide(float amount)
    {
        AddThemeConstantOverride("margin_left", (int)amount);
        AddThemeConstantOverride("margin_right", -(int)amount);
    }
}
