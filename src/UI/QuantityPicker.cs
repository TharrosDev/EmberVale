using System;
using Godot;

namespace Embervale.UI;

/// <summary>
/// "How many?" as one row: a step down, a slider, a step up and the reading. Used wherever part of
/// a stack is acted on (splitting it, selling some, storing some).
///
/// It reports through <paramref name="changed"/> and updates its own reading; the caller must NOT
/// rebuild in that callback. A rebuild frees this row, and a slider freed mid-drag drops the drag,
/// so the screen keeps the number and refreshes whatever depends on it in place.
///
/// Every part is focusable: on a pad, left and right on the slider change the amount, and the two
/// step buttons are there for a mouse and for exact single steps.
/// </summary>
public static class QuantityPicker
{
    public static HBoxContainer Build(int min, int max, int value, Action<int> changed)
    {
        max = Math.Max(min, max);
        value = Math.Clamp(value, min, max);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Button less = Step("−");
        HSlider slider = UiTheme.Slider(min, max, 1, value, 120f);
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Button more = Step("+");

        Label reading = UiTheme.Body($"{value} / {max}");
        reading.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        reading.HorizontalAlignment = HorizontalAlignment.Right;
        reading.CustomMinimumSize = new Vector2(64f, 0f);

        slider.ValueChanged += v =>
        {
            int now = (int)Math.Round(v);
            reading.Text = $"{now} / {max}";
            changed(now);
        };
        less.Pressed += () => slider.Value -= 1;
        more.Pressed += () => slider.Value += 1;

        row.AddChild(less);
        row.AddChild(slider);
        row.AddChild(more);
        row.AddChild(reading);
        return row;
    }

    private static Button Step(string symbol)
    {
        Button button = UiTheme.Action(symbol);
        button.Alignment = HorizontalAlignment.Center;
        button.CustomMinimumSize = new Vector2(UiTheme.ControlHeight, UiTheme.ControlHeight);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return button;
    }
}
