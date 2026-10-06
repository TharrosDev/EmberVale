using System;
using System.Collections.Generic;
using Embervale.Core.Events;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The footer legend: a fixed-height row of glyph and verb pairs saying what the buttons do on the
/// screen above it. <see cref="UiPanel"/> owns one per panel and fills it from its <c>Legend</c>;
/// a screen that is not a <c>UiPanel</c> builds one, anchors it and calls <see cref="Set"/>.
///
/// It sits in the gutter over the paused world, not on a panel, so it carries its own dark plate.
/// The glyphs are redrawn when the player changes device or rebinds
/// (<c>InputDeviceChangedEvent</c>, <c>InputBindingsChangedEvent</c>); nothing here ticks.
/// </summary>
public partial class UiLegend : HBoxContainer
{
    private readonly PanelContainer _plate;
    private readonly HBoxContainer _row;
    private LegendEntry[] _entries = Array.Empty<LegendEntry>();

    // A device flip while the legend is hidden: the glyphs are redrawn when it is next filled,
    // not for every closed panel each time the player touches the other device.
    private bool _stale;

    public UiLegend()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Alignment = AlignmentMode.Center;

        // Across the bottom of the view, one margin up from its edge. A panel's shell leaves this
        // row clear (UiTheme.ApplyScreenInset, UiChromeRules.BottomInset).
        AnchorLeft = 0f;
        AnchorRight = 1f;
        AnchorTop = 1f;
        AnchorBottom = 1f;
        OffsetLeft = UiTheme.SpaceLg;
        OffsetRight = -UiTheme.SpaceLg;
        OffsetTop = -(UiChromeRules.LegendHeight + UiChromeRules.ChromeMargin);
        OffsetBottom = -UiChromeRules.ChromeMargin;

        var ground = new StyleBoxFlat { BgColor = UiTheme.ScrimBg with { A = UiTheme.HighContrast ? 1f : 0.78f } };
        ground.SetCornerRadiusAll(UiTheme.RadiusSm);
        ground.ContentMarginLeft = UiTheme.SpaceMd;
        ground.ContentMarginRight = UiTheme.SpaceMd;

        _plate = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0f, UiChromeRules.LegendHeight),
            Visible = false,
        };
        _plate.AddThemeStyleboxOverride("panel", ground);
        AddChild(_plate);

        _row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _row.AddThemeConstantOverride("separation", UiTheme.SpaceLg);
        _plate.AddChild(_row);
    }

    public override void _EnterTree()
    {
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Subscribe<InputBindingsChangedEvent>(OnBindingsChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Unsubscribe<InputBindingsChangedEvent>(OnBindingsChanged);
    }

    /// <summary>Shows <paramref name="entries"/>, left to right. A no-op when they are the ones
    /// already shown, so a panel may call this after every rebuild.</summary>
    public void Set(IReadOnlyList<LegendEntry> entries)
    {
        if (!_stale && Same(entries))
        {
            return;
        }

        _entries = new LegendEntry[entries.Count];
        for (int i = 0; i < _entries.Length; i++)
        {
            _entries[i] = entries[i];
        }

        Redraw();
    }

    private bool Same(IReadOnlyList<LegendEntry> entries)
    {
        if (entries.Count != _entries.Length)
        {
            return false;
        }

        for (int i = 0; i < _entries.Length; i++)
        {
            if (entries[i] != _entries[i])
            {
                return false;
            }
        }

        return true;
    }

    private void OnDeviceChanged(InputDeviceChangedEvent e) => RedrawIfShown();

    private void OnBindingsChanged(InputBindingsChangedEvent e) => RedrawIfShown();

    private void RedrawIfShown()
    {
        if (IsVisibleInTree())
        {
            Redraw();
        }
        else
        {
            _stale = true;
        }
    }

    private void Redraw()
    {
        _stale = false;
        UiTheme.ClearChildren(_row);
        _plate.Visible = _entries.Length > 0;
        foreach (LegendEntry entry in _entries)
        {
            var pair = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            pair.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
            pair.AddChild(Centred(UiGlyph.For(entry.Action)));
            if (entry.SecondAction is { } second)
            {
                pair.AddChild(Centred(UiGlyph.For(second)));
            }

            Label verb = UiTheme.Caption(entry.Label, UiTheme.Text);
            verb.MouseFilter = MouseFilterEnum.Ignore;
            verb.VerticalAlignment = VerticalAlignment.Center;
            pair.AddChild(verb);
            _row.AddChild(pair);
        }
    }

    private static Control Centred(Control glyph)
    {
        glyph.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return glyph;
    }
}
