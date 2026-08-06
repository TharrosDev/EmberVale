using System;
using System.Collections.Generic;
using Godot;

namespace Embervale.UI;

/// <summary>
/// A reusable tab strip (Phase 30.5F): themed buttons in a row, one active at a time, the
/// active tab highlighted in the accent colour. Panels add tabs once in their shell build and
/// react to <see cref="TabChanged"/> (typically by calling <c>MarkDirty</c>).
/// </summary>
public partial class UiTabs : HBoxContainer
{
    private readonly List<Button> _buttons = new();

    /// <summary>Raised with the new tab index after <see cref="Select"/>.</summary>
    public event Action<int>? TabChanged;

    /// <summary>The active tab index.</summary>
    public int Current { get; private set; }

    public UiTabs()
    {
        AddThemeConstantOverride("separation", UiTheme.SpaceXs);
    }

    /// <summary>Appends a tab; the first added starts active.</summary>
    public void Add(string label)
    {
        int index = _buttons.Count;
        Button button = UiTheme.Action(label);
        button.Pressed += () => Select(index);
        AddChild(button);
        _buttons.Add(button);
        Highlight();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _buttons.Count || index == Current)
        {
            return;
        }

        Current = index;
        Highlight();
        TabChanged?.Invoke(index);
    }

    private void Highlight()
    {
        // Swap the font colour, not Modulate — modulating the whole button multiplies the
        // already-dim font down below readable contrast (the 30.5K audit caught ~2.9:1).
        //
        // 37.5H adds the ember underline. Colour alone made the active tab a slightly brighter
        // button in a row of buttons, which is a weak signal at the top of a screen whose whole
        // job is telling you where you are — and it was the *only* signal, so it vanished
        // entirely under a colourblind setting. The rule is a second, non-colour channel.
        for (int i = 0; i < _buttons.Count; i++)
        {
            bool active = i == Current;
            _buttons[i].AddThemeColorOverride("font_color", active ? UiTheme.Accent : UiTheme.Dim);

            var box = new StyleBoxFlat
            {
                BgColor = active ? UiTheme.CardBg : new Color(0f, 0f, 0f, 0f),
                BorderColor = UiTheme.Accent,
            };
            box.SetBorderWidthAll(0);
            box.BorderWidthBottom = active ? 2 : 0;
            box.SetContentMarginAll(UiTheme.SpaceXs);
            box.ContentMarginLeft = UiTheme.SpaceMd;
            box.ContentMarginRight = UiTheme.SpaceMd;
            box.CornerRadiusTopLeft = UiTheme.RadiusSm;
            box.CornerRadiusTopRight = UiTheme.RadiusSm;
            _buttons[i].AddThemeStyleboxOverride("normal", box);
        }
    }
}
