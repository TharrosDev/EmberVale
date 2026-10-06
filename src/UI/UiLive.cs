using Godot;

namespace Embervale.UI;

/// <summary>
/// Write-only-on-change helpers for controls a HUD tick touches every frame.
///
/// <c>AddThemeColorOverride</c> is not a cheap setter: every call raises a theme-changed notification
/// on the control, which makes a <see cref="Label"/> throw away its shaped text and queue a redraw
/// whether or not the colour moved. A widget that restates its colour each frame therefore re-shapes
/// its text each frame. These helpers ask the control what it already has first.
///
/// Kept out of <see cref="UiTheme"/> on purpose: that class is read by unit tests with no engine
/// running, and a <see cref="StringName"/> static cannot be constructed there.
/// </summary>
public static class UiLive
{
    private static readonly StringName FontColorName = "font_color";

    // Input actions polled from a per-frame tick, converted once. Handing Input a string converts it
    // to a StringName on every call, and each of those is a finalizable allocation.
    public static readonly StringName UiCancel = "ui_cancel";
    public static readonly StringName Pause = Core.GameInput.Pause;
    public static readonly StringName Interact = Core.GameInput.Interact;
    public static readonly StringName Attack = Core.GameInput.Attack;
    public static readonly StringName LookLeft = Core.GameInput.LookLeft;
    public static readonly StringName LookRight = Core.GameInput.LookRight;
    public static readonly StringName LookUp = Core.GameInput.LookUp;
    public static readonly StringName LookDown = Core.GameInput.LookDown;

    /// <summary>Sets the control's <c>font_color</c> override unless it already is <paramref name="color"/>.</summary>
    public static void FontColor(Control control, Color color)
    {
        if (control.HasThemeColorOverride(FontColorName) && control.GetThemeColor(FontColorName) == color)
        {
            return;
        }

        control.AddThemeColorOverride(FontColorName, color);
    }
}
