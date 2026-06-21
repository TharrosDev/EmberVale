using Godot;

namespace Embervale.UI;

/// <summary>
/// A single transient notification chip. It lives for <see cref="Life"/> seconds, holding
/// full opacity then fading out, and frees itself when done. Built and stacked by
/// <see cref="Notifications"/>; styled through <see cref="UiTheme"/>.
/// </summary>
public partial class Toast : PanelContainer
{
    public double Life { get; set; } = 4.0;

    private double _age;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", UiTheme.PanelStyle());
    }

    public override void _Process(double delta)
    {
        _age += delta;
        float t = (float)(_age / Life);
        if (t >= 1f)
        {
            QueueFree();
            return;
        }

        // Hold, then fade over the final 40% of the lifetime.
        float alpha = t < 0.6f ? 1f : 1f - ((t - 0.6f) / 0.4f);
        Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(alpha, 0f, 1f));
    }
}
