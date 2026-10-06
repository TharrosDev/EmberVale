using System;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The hub strip: the five screens of the in-game hub as fixed tabs in the top gutter of each
/// panel that opts in (<c>UiPanel.Hub</c>), with the two inputs that walk them at either end.
///
/// Every hub panel draws its own copy with its own tab lit. The copies are identical in every
/// other respect, so stepping from one screen to the next changes the frame under the strip and
/// leaves the strip where it was. Tabs are pressed with the mouse or walked with
/// <c>menu_tab_prev</c>/<c>menu_tab_next</c> (polled by <see cref="UiPanel"/>); they take no
/// focus, so a d-pad never wanders out of the panel into the strip.
/// </summary>
public partial class HubStrip : HBoxContainer
{
    private const int TabWidth = 112;

    private readonly Control _prev;
    private readonly Control _next;

    public HubStrip(HubTab current, Action<HubTab> go)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Alignment = AlignmentMode.Center;
        AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        AnchorLeft = 0f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = UiTheme.SpaceLg;
        OffsetRight = -UiTheme.SpaceLg;

        _prev = Slot();
        AddChild(_prev);

        // The strip sits over the paused world, not on a panel, so it carries its own ground.
        var ground = new StyleBoxFlat { BgColor = UiTheme.HighContrast ? UiTheme.ScrimBg : UiTheme.ScrimHub };
        ground.SetCornerRadiusAll(UiTheme.RadiusSm);
        ground.BorderColor = UiTheme.Rule;
        ground.BorderWidthBottom = 1;
        var plate = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", ground);
        AddChild(plate);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 0);
        plate.AddChild(row);
        row.AddChild(Tab(Loc.T("ui.hub.character"), HubTab.Character, current, go));
        row.AddChild(Tab(Loc.T("ui.hub.spellbook"), HubTab.Spellbook, current, go));
        row.AddChild(Tab(Loc.T("ui.hub.journal"), HubTab.Journal, current, go));
        row.AddChild(Tab(Loc.T("ui.hub.map"), HubTab.Map, current, go));
        row.AddChild(Tab(Loc.T("ui.hub.bestiary"), HubTab.Bestiary, current, go));

        _next = Slot();
        AddChild(_next);
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

    /// <summary>Centres the strip in the top gutter for the current viewport and redraws the two
    /// end glyphs. Called by the panel each time it opens: the UI scale, the device and the
    /// bindings can all have changed since.</summary>
    public void Refresh()
    {
        Vector2 view = GetViewportRect().Size;
        int height = UiChromeRules.HubHeight(view.Y);
        int inset = UiChromeRules.TopInset(UiChromeRules.Gutter(view.X), hub: true, view.Y);
        OffsetTop = (inset - height) * 0.5f;
        OffsetBottom = OffsetTop + height;

        Fill(_prev, GameInput.MenuTabPrev);
        Fill(_next, GameInput.MenuTabNext);
    }

    private void OnDeviceChanged(InputDeviceChangedEvent e) => RefreshIfShown();

    private void OnBindingsChanged(InputBindingsChangedEvent e) => RefreshIfShown();

    private void RefreshIfShown()
    {
        if (IsVisibleInTree())
        {
            Refresh();
        }
    }

    private static void Fill(Control slot, string action)
    {
        UiTheme.ClearChildren(slot);
        slot.AddChild(UiGlyph.For(action));
    }

    private static Control Slot() => new CenterContainer
    {
        MouseFilter = MouseFilterEnum.Ignore,
        CustomMinimumSize = new Vector2(TabWidth * 0.5f, 0f),
    };

    private static Button Tab(string text, HubTab tab, HubTab current, Action<HubTab> go)
    {
        bool active = tab == current;
        var button = new Button
        {
            Text = text,
            FocusMode = FocusModeEnum.None,
            Disabled = active, // the lit tab is where the player already is
            CustomMinimumSize = new Vector2(TabWidth, 0f),
            SizeFlagsVertical = SizeFlags.Fill,
        };
        UiTheme.ApplyType(button, UiTheme.FontRole.Display, UiTheme.BodyFontSize);

        // The carved capitals stop growing at header size: five tabs have to share a handheld's width.
        button.AddThemeFontSizeOverride("font_size",
            Mathf.Min(UiTheme.FontSize(UiTheme.BodyFontSize), UiTheme.HeaderFontSize));
        button.AddThemeColorOverride("font_color", UiTheme.Dim);
        button.AddThemeColorOverride("font_hover_color", UiTheme.Text);
        button.AddThemeColorOverride("font_pressed_color", UiTheme.Text);
        button.AddThemeColorOverride("font_disabled_color", UiTheme.Accent);

        // Colour is not the only channel: the open screen's tab also carries the ember underline.
        button.AddThemeStyleboxOverride("normal", Face(lit: false, hover: false));
        button.AddThemeStyleboxOverride("hover", Face(lit: false, hover: true));
        button.AddThemeStyleboxOverride("pressed", Face(lit: false, hover: true));
        button.AddThemeStyleboxOverride("disabled", Face(lit: true, hover: false));
        button.Pressed += () => go(tab);
        return button;
    }

    private static StyleBoxFlat Face(bool lit, bool hover)
    {
        var box = new StyleBoxFlat
        {
            BgColor = lit ? UiTheme.CardBg : hover ? UiTheme.ButtonFaceHover : new Color(0f, 0f, 0f, 0f),
            BorderColor = UiTheme.Accent,
        };
        box.SetBorderWidthAll(0);
        box.BorderWidthBottom = lit ? (UiTheme.HighContrast ? 3 : 2) : 0;
        box.ContentMarginLeft = UiTheme.SpaceSm;
        box.ContentMarginRight = UiTheme.SpaceSm;
        return box;
    }
}
