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
        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].AddThemeColorOverride("font_color", i == Current ? UiTheme.Accent : UiTheme.Dim);
        }
    }
}
