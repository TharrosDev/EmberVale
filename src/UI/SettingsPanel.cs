using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The options menu: a sheet with six tabs (Graphics, Audio, Controls, Gameplay, Interface,
/// Accessibility), the active tab's options in a scrolling list, and a pane that says in plain
/// words what the focused option does. Each control reads and writes the live
/// <see cref="SettingsService"/>. Reachable from both shells (the title <see cref="MainMenu"/> and
/// the in-game <see cref="PauseMenu"/>), which hide themselves behind it and restore on Back.
///
/// Changes apply <b>live</b> (every control calls <see cref="SettingsService.Apply"/> on change);
/// they persist to disk on Back and on each discrete toggle/dropdown change, and on a slider's
/// drag-end (so dragging a volume doesn't thrash the file). The two sliders that resize the screen
/// itself, UI scale and text size, wait for the drag to end. Built through <see cref="UiTheme"/>;
/// runs with <see cref="Node.ProcessModeEnum.Always"/> so it works while the game is paused.
///
/// The whole sheet is rebuilt in place from a dirty flag (a tab switch, a reset, a text-size
/// change, the window resizing), keeping focus and scroll position. ⚠️ The option list must stay
/// the first <see cref="ScrollContainer"/> under this node: the screenshot harness scrolls it.
/// </summary>
public partial class SettingsPanel : CanvasLayer
{
    private SettingsService _settings = null!;
    private System.Action? _onBack;
    private Texture2D? _backdrop;

    private SettingsTab _tab;
    private Control? _root;
    private UiTabs _tabs = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _pane = null!;
    private UiLegend? _legend;
    private Viewport? _viewport;

    private bool _narrow;
    private bool? _narrowOverride;
    private bool _dirty;
    private bool _tabChanged;
    private int _restoreScroll;
    private int _restoreScrollFrames;

    /// <summary>Polled actions are ignored up to this frame: the key that was just captured as a
    /// binding, or that closed a dropdown, is still "just pressed" as far as the engine knows.</summary>
    private ulong _quietUntilFrame;

    private bool _popupOpen;

    /// <summary>Opens the panel as a child of <paramref name="parent"/>, invoking
    /// <paramref name="onBack"/> when the player backs out. No-op if no settings service exists.
    /// It opens on <paramref name="initialTab"/>, set before the panel enters the tree so the
    /// first build is already that tab. <paramref name="backdrop"/> is a painting to keep behind
    /// the sheet (the title's, when the title opened it).</summary>
    public static void Open(
        Node parent, System.Action? onBack = null, SettingsTab initialTab = SettingsTab.Graphics, Texture2D? backdrop = null)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out SettingsService settings))
        {
            Log.Warn("Settings requested but no SettingsService is registered.");
            onBack?.Invoke();
            return;
        }

        var panel = new SettingsPanel
        {
            _settings = settings,
            _onBack = onBack,
            _tab = initialTab,
            _backdrop = backdrop,
        };
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 13; // above the main menu (11), pause menu (10), and slot panel (12)
        UiState.Open(this);
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;

        Build();
        UiFocus.GrabFirst(_scroll); // gamepad/keyboard land on the first setting (30.5J)

        // The UI-scale setting changes the logical size of the view, and so can the window.
        _viewport = GetViewport();
        _viewport.SizeChanged += MarkDirty;
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
    }

    public override void _ExitTree()
    {
        if (_viewport != null)
        {
            _viewport.SizeChanged -= MarkDirty;
        }

        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        UiState.Close(this);
    }

    private void MarkDirty() => _dirty = true;

    public override void _Process(double delta)
    {
        if (_dirty)
        {
            _dirty = false;
            Rebuild();
        }

        // Two frames after a rebuild, once the new list has been laid out and has a range to scroll.
        if (_restoreScrollFrames > 0 && --_restoreScrollFrames == 0)
        {
            _scroll.ScrollVertical = _restoreScroll;
        }

        if (!_settings.BindingsApplied)
        {
            _settings.ApplyBindings();
        }

        if (_listening != null || _popupOpen || Engine.GetProcessFrames() <= _quietUntilFrame)
        {
            return;
        }

        // Esc or gamepad B backs out (matches the pause menu's feel); the PauseMenu suppresses its
        // own Esc while UiState.MenuOpen is set, so this can't also resume the game on the same press.
        bool cancel = Godot.Input.IsActionJustPressed(UiLive.Pause) ||
                      Godot.Input.IsActionJustPressed(UiLive.UiCancel);
        if (_prompt != null)
        {
            if (cancel)
            {
                UiAudio.Play(UiCue.Back);
                ClosePrompt();
            }

            return;
        }

        if (cancel)
        {
            UiAudio.Play(UiCue.Back);
            Back();
            return;
        }

        int step = (Godot.Input.IsActionJustPressed(UiLive.MenuTabNext) ? 1 : 0)
                   - (Godot.Input.IsActionJustPressed(UiLive.MenuTabPrev) ? 1 : 0);
        if (step != 0)
        {
            _tabs.Select((int)SettingsTabRules.Step(_tab, step));
        }
        else if (Godot.Input.IsActionJustPressed(UiLive.MenuSubPrev))
        {
            RevertFocused();
        }
        else if (Godot.Input.IsActionJustPressed(UiLive.MenuSubNext))
        {
            ClearFocusedBinding();
        }
    }

    // --- The sheet ----------------------------------------------------------

    private static readonly string[] TabKeys =
    {
        "settings.tab.graphics", "settings.tab.audio", "settings.tab.controls",
        "settings.tab.gameplay", "settings.tab.interface", "settings.tab.accessibility",
    };

    private void Build()
    {
        _rows.Clear();
        _graphicsSync.Clear();
        _bindingRows.Clear();
        _focusedRow = null;
        _shownRow = null;
        _focusedCell = null;
        _popupOpen = false;

        Vector2 view = GetViewport().GetVisibleRect().Size;
        _narrow = _narrowOverride ?? SettingsTabRules.Narrow(view.X);
        float width = Mathf.Min(view.X - (UiChromeRules.Gutter(view.X) * 2f), UiTheme.SettingsSheetMaxWidth);

        // A sheet, not a framed panel: the column sits on the scrim behind one lit rule. It fills
        // the height it is given (a sheet centres its column by default) so the list can scroll.
        (Control root, VBoxContainer col) = UiTheme.Sheet(width, 0.92f, centred: true);
        col.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        UiTheme.SheetOverPainting(root, _backdrop);
        _root = root;
        AddChild(root);
        MoveChild(root, 0); // under any open prompt and the legend

        // The legend is made again with the sheet: its captions, keycaps and plate are sized and
        // coloured when they are drawn, and a rebuild is what follows a text-size, font or contrast
        // change. Last child, so a prompt's scrim never dims the keys that answer the prompt.
        if (_legend != null)
        {
            RemoveChild(_legend);
            _legend.QueueFree();
        }

        _legend = new UiLegend();
        AddChild(_legend);

        col.AddChild(UiTheme.Title(Loc.T("settings.title")));

        _tabs = new UiTabs();
        foreach (string key in TabKeys)
        {
            _tabs.Add(Loc.T(key));
        }

        _tabs.Select((int)_tab);
        _tabs.TabChanged += OnTabChosen;
        col.AddChild(_tabs);
        FitTabs(width - UiTheme.SpaceLg - 1f); // the column is the sheet less its rule and the gap beside it

        BoxContainer split = _narrow ? new VBoxContainer() : new HBoxContainer();
        split.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        split.AddThemeConstantOverride("separation", _narrow ? UiTheme.SpaceSm : UiTheme.SpaceLg);
        col.AddChild(split);

        // The list takes whatever height the sheet leaves. Its floor is deliberately small: a floor
        // near the sheet's height pushes the footer off a 533 px logical viewport.
        (ScrollContainer scroll, VBoxContainer body) = UiTheme.ScrollList();
        scroll.CustomMinimumSize = new Vector2(0f, UiTheme.SettingsListMinHeight);
        _scroll = scroll;
        split.AddChild(scroll);

        split.AddChild(new ColorRect
        {
            Color = UiTheme.Rule,
            CustomMinimumSize = new Vector2(1f, 1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        _pane = new VBoxContainer { CustomMinimumSize = new Vector2(_narrow ? 0f : UiTheme.SettingsPaneWidth, 0f) };
        _pane.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        split.AddChild(_pane);

        switch (_tab)
        {
            case SettingsTab.Graphics: BuildGraphics(body); break;
            case SettingsTab.Audio: BuildAudio(body); break;
            case SettingsTab.Controls: BuildControls(body); break;
            case SettingsTab.Gameplay: BuildGameplay(body); break;
            case SettingsTab.Interface: BuildInterface(body); break;
            default: BuildAccessibility(body); break;
        }

        col.AddChild(BuildFooter());

        WireBindingGrid(); // focus neighbours are node paths, so only once the rows are in the tree
        ShowDescription(null);
        UpdateLegend();
    }

    /// <summary>
    /// Keeps the tab strip inside the sheet. The six names fit as they are at every size but the
    /// largest text on a handheld; there every tab but the active one gives up width in proportion
    /// to its own name and clips with an ellipsis (its tooltip keeps the whole name), instead of
    /// the strip pushing the sheet wider than the screen. The active tab is never clipped: a pad
    /// has no pointer for a tooltip, and stepping the strip then reads each name in turn.
    /// Measured, so only once the strip is in the tree.
    /// </summary>
    private void FitTabs(float room)
    {
        if (_tabs.GetCombinedMinimumSize().X <= room)
        {
            return;
        }

        int index = 0;
        foreach (Node child in _tabs.GetChildren())
        {
            if (child is Button tab && index++ != (int)_tab)
            {
                tab.SizeFlagsStretchRatio = Mathf.Max(1f, tab.GetCombinedMinimumSize().X);
                tab.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                tab.TooltipText = tab.Text;
                tab.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                tab.ClipText = true;
            }
        }
    }

    private Control BuildFooter()
    {
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Button back = UiTheme.Action(Loc.T("common.back"), UiCue.Back);
        back.Pressed += Back;
        footer.AddChild(back);
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        var resetTab = new RowInfo
        {
            Title = Loc.T("settings.reset_tab"),
            Description = Loc.T(_tab == SettingsTab.Controls ? "settings.reset_tab.desc_controls" : "settings.reset_tab.desc"),
        };
        Control tabButton = HoldButton(resetTab, () =>
        {
            KeepingHidden(() => _settings.ResetTab(_tab));
            Persist();
            MarkDirty();
        });
        Track(resetTab, tabButton);
        footer.AddChild(tabButton);

        var resetAll = new RowInfo
        {
            Title = Loc.T("settings.reset_all"),
            Description = Loc.T("settings.reset_all.desc"),
        };
        Control allButton = HoldButton(resetAll, () =>
        {
            KeepingHidden(_settings.ResetAllButBindingsAndAccessibility);
            Persist();
            MarkDirty();
        });
        Track(resetAll, allButton);
        footer.AddChild(allButton);
        return footer;
    }

    private void OnTabChosen(int index)
    {
        _tab = (SettingsTab)index;
        _tabChanged = true;
        UiAudio.Play(UiCue.Tab);
        MarkDirty();
    }

    /// <summary>Frees the sheet and builds it again for the settings as they are now, putting
    /// focus and the scroll position back where they were.</summary>
    private void Rebuild()
    {
        int[]? focus = _root != null ? UiFocus.PathOf(_root) : null;
        int scrolled = _scroll.ScrollVertical;
        bool tabChanged = _tabChanged;
        _tabChanged = false;

        // A new tab's list starts at its top, so focus that was somewhere down the old list goes
        // to the new one's first row rather than to whatever now sits at the same position.
        if (tabChanged && GetViewport()?.GuiGetFocusOwner() is { } owner && _scroll.IsAncestorOf(owner))
        {
            focus = null;
        }

        if (_root != null)
        {
            RemoveChild(_root);
            _root.QueueFree();
        }

        Build();

        // A prompt holds focus on purpose; the sheet under it takes it back when the prompt closes.
        if (_prompt == null)
        {
            if (focus != null)
            {
                UiFocus.Restore(_root!, focus);
            }
            else
            {
                UiFocus.GrabFirst(_scroll);
            }
        }

        _restoreScroll = tabChanged ? 0 : scrolled;
        _restoreScrollFrames = 2;
        if (tabChanged)
        {
            UiFx.FadeIn(_scroll, UiTheme.DurationTab);
        }
    }

    // --- Rows ---------------------------------------------------------------

    /// <summary>What the screen knows about one option: what to call it, what to say about it,
    /// whether it has been changed, and how to put it back.</summary>
    private sealed class RowInfo
    {
        public string Title = string.Empty;
        public string Description = string.Empty;

        /// <summary>Whether the option differs from its default. Null for a row with no default
        /// to go back to (a button).</summary>
        public System.Func<bool>? Changed;

        /// <summary>Puts the option back to its default in memory.</summary>
        public System.Action? Revert;

        /// <summary>Builds the pane's preview for this option, or null for none.</summary>
        public System.Func<Control?>? Preview;

        /// <summary>Redraws the preview on screen after the option moved.</summary>
        public System.Action? Live;

        public PanelContainer? Frame;
        public Control? Main;
        public Button? RevertButton;
    }

    private readonly List<RowInfo> _rows = new();
    private RowInfo? _focusedRow;
    private RowInfo? _shownRow;

    /// <summary>A row whose default is the default of the named <see cref="Settings.Settings"/> fields.</summary>
    private RowInfo Info(string title, string description, params string[] fields) => new()
    {
        Title = title,
        Description = description,
        Changed = () => _settings.Differs(fields),
        Revert = () => _settings.ResetFields(fields),
    };

    /// <summary>An engraved section rule, so a tab of twenty rows reads as groups and not as one
    /// undifferentiated column.</summary>
    private static void Section(VBoxContainer parent, string title, bool first = false)
    {
        parent.AddChild(UiTheme.SectionRule(title, first));
    }

    /// <summary>
    /// One setting: name on the left, control in a shared right-hand column, and a restore-default
    /// button that is only there once the setting has been changed. What the setting does is said
    /// in the description pane when the row takes focus (or the pointer).
    /// </summary>
    private Control Row(RowInfo info, Control control)
    {
        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        frame.AddThemeStyleboxOverride("panel", UiTheme.SettingsRowStyle(false));
        info.Frame = frame;

        // Every row is a full control tall, so a slider, a toggle and a dropdown share one pitch.
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight) };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        frame.AddChild(row);

        Label name = UiTheme.Body(info.Title);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(name);

        // Every control shares one right-hand column (37.5H), so thirty rows read as a column of
        // values and the widest dropdown cannot squeeze its label.
        var slot = new MarginContainer { CustomMinimumSize = new Vector2(UiTheme.SettingsControlColumn, 0f) };
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // A dropdown and a toggle are pressed anywhere in the row's height, not only on their glyph.
        // A slider is given the same height where it is built (SliderRow).
        if (control is BaseButton)
        {
            control.CustomMinimumSize = new Vector2(control.CustomMinimumSize.X, UiTheme.ControlHeight);
        }

        slot.AddChild(control);
        row.AddChild(slot);
        row.AddChild(RevertSlot(info));

        Track(info, control);
        frame.MouseEntered += () => ShowDescription(info);
        _rows.Add(info);
        return frame;
    }

    /// <summary>Ties every focusable control under <paramref name="root"/> to its row, so focus
    /// arriving lights the row and fills the description pane.</summary>
    private void Track(RowInfo info, Control root)
    {
        if (root.FocusMode != Control.FocusModeEnum.None)
        {
            info.Main ??= root;
            root.FocusEntered += () => OnRowFocus(info);
            root.FocusExited += () => OnRowBlur(info);
        }

        foreach (Node child in root.GetChildren())
        {
            if (child is Control control)
            {
                Track(info, control);
            }
        }
    }

    private void OnRowFocus(RowInfo info)
    {
        _focusedRow = info;
        info.Frame?.AddThemeStyleboxOverride("panel", UiTheme.SettingsRowStyle(true));
        ShowDescription(info);
        UpdateLegend();
    }

    private void OnRowBlur(RowInfo info)
    {
        info.Frame?.AddThemeStyleboxOverride("panel", UiTheme.SettingsRowStyle(false));
        if (_focusedRow == info)
        {
            _focusedRow = null;
        }
    }

    /// <summary>The fixed place at the end of a row for its restore-default button. The place is
    /// always there so the control column never moves; the button is only shown on a changed row,
    /// which makes the glyph itself the sign that the row differs from its default.</summary>
    private Control RevertSlot(RowInfo info)
    {
        var slot = new MarginContainer
        {
            CustomMinimumSize = new Vector2(UiTheme.ControlHeight, UiTheme.ControlHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (info.Changed == null || info.Revert == null)
        {
            return slot;
        }

        Button button = UiTheme.Action(string.Empty);
        button.TooltipText = Loc.T("settings.revert");
        button.Visible = info.Changed();
        button.Pressed += () => RevertRow(info);
        button.FocusEntered += () => OnRowFocus(info);
        button.FocusExited += () => OnRowBlur(info);
        button.AddChild(RevertGlyph());
        info.RevertButton = button;
        slot.AddChild(button);
        return slot;
    }

    /// <summary>A drawn turn-back arrow: three quarters of a ring with a head on its open end.</summary>
    private static Control RevertGlyph()
    {
        var glyph = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        glyph.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        glyph.Draw += () =>
        {
            const float Radius = 7f;
            const float Stroke = 2f;
            const float Head = 4f;
            float start = Mathf.DegToRad(-150f);
            Vector2 centre = glyph.Size * 0.5f;
            glyph.DrawArc(centre, Radius, start, Mathf.DegToRad(110f), 24, UiTheme.Text, Stroke, true);

            // The head sits on the arc's start and points back along it, against the sweep.
            var radial = new Vector2(Mathf.Cos(start), Mathf.Sin(start));
            var against = new Vector2(Mathf.Sin(start), -Mathf.Cos(start));
            Vector2 at = centre + (radial * Radius);
            glyph.DrawColoredPolygon(
                new[] { at + (against * (Head + 1f)), at + (radial * Head), at - (radial * Head) }, UiTheme.Text);
        };
        return glyph;
    }

    private void RevertRow(RowInfo info)
    {
        if (info.Revert == null || info.Changed?.Invoke() != true)
        {
            return;
        }

        // Onto the row's own control first: the button under the pointer is about to go, and the
        // rebuild puts focus back by position.
        if (info.Main != null && info.Main.IsVisibleInTree())
        {
            info.Main.GrabFocus();
        }

        info.Revert();
        UiAudio.Play(UiCue.Confirm);
        Persist();
        MarkDirty();
    }

    private void RevertFocused()
    {
        if (_focusedRow is { } row)
        {
            RevertRow(row);
        }
    }

    /// <summary>After a control moved its setting: shows or hides the row's restore button, moves
    /// its preview, and brings the legend up to date.</summary>
    private void Touched(RowInfo info)
    {
        if (info.RevertButton != null && info.Changed != null)
        {
            info.RevertButton.Visible = info.Changed();
        }

        info.Live?.Invoke();
        UpdateLegend();
    }

    private Control ToggleRow(RowInfo info, bool value, System.Action<bool> onChanged)
    {
        CheckButton toggle = UiTheme.Toggle(value);
        toggle.Toggled += pressed =>
        {
            onChanged(pressed);
            Touched(info);
        };
        return Row(info, toggle);
    }

    private Control DropdownRow(RowInfo info, string[] options, int selected, System.Action<int> onSelected)
    {
        OptionButton dropdown = Dropdown(options, selected);
        dropdown.ItemSelected += index =>
        {
            onSelected((int)index);
            Touched(info);
        };
        return Row(info, dropdown);
    }

    /// <summary>A dropdown that tells the panel while its list is open. The list is a window of its
    /// own, so the Esc that closes it and the Q or E typed over it would otherwise also reach this
    /// screen's polling and back out of the menu or switch its tab.</summary>
    private OptionButton Dropdown(string[] options, int selected)
    {
        OptionButton dropdown = UiTheme.Dropdown(options, selected);
        PopupMenu popup = dropdown.GetPopup();
        popup.AboutToPopup += () => _popupOpen = true;
        popup.PopupHide += () =>
        {
            _popupOpen = false;
            _quietUntilFrame = Engine.GetProcessFrames() + 1;
        };
        return dropdown;
    }

    private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

    private static string Decimal(float value) => $"{value:0.00}";

    private static string Whole(float value) => $"{Mathf.RoundToInt(value)}";

    /// <summary>
    /// A slider with a live readout. By default the setting applies while dragging and is written
    /// on release. With <paramref name="applyOnRelease"/> nothing is applied until the drag ends,
    /// and the sheet is then rebuilt: that is for the two sliders that resize the interface, where
    /// applying each tick moved the slider out from under the pointer that was dragging it. A step
    /// from the keyboard or a pad has no drag to wait for and applies at once.
    /// </summary>
    private Control SliderRow(RowInfo info, double min, double max, double step, float value,
        System.Action<float> assign, System.Func<float, string> format, bool applyOnRelease = false)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        HSlider slider = UiTheme.Slider(min, max, step, value,
            UiTheme.SettingsControlColumn - UiTheme.SettingsReadout - UiTheme.SpaceSm);
        slider.CustomMinimumSize = new Vector2(slider.CustomMinimumSize.X, UiTheme.ControlHeight); // a full control to grab
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        slider.Scrollable = false; // the wheel scrolls the list; it must not move a setting it passes over
        Label readout = UiTheme.Body(format(value), UiTheme.Dim);
        readout.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        readout.CustomMinimumSize = new Vector2(UiTheme.SettingsReadout, 0f);
        readout.HorizontalAlignment = HorizontalAlignment.Right;

        bool dragging = false;
        slider.DragStarted += () => dragging = true;
        slider.ValueChanged += v =>
        {
            assign((float)v);
            readout.Text = format((float)v);
            Touched(info);
            if (!applyOnRelease)
            {
                _settings.Apply(); // live
            }
            else if (!dragging)
            {
                Persist();
                MarkDirty();
            }
        };
        slider.DragEnded += changed =>
        {
            dragging = false;
            Persist();
            if (applyOnRelease && changed)
            {
                MarkDirty();
            }
        };
        box.AddChild(slider);
        box.AddChild(readout);
        return Row(info, box);
    }

    /// <summary>
    /// A button for something that cannot be undone, with its ring: hold it to confirm. With the
    /// holds-to-presses setting on there is no ring and a press asks first, with Cancel focused,
    /// so one stray press still changes nothing.
    /// </summary>
    private Control HoldButton(RowInfo info, System.Action onConfirmed)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        Button button = UiTheme.Action(info.Title);
        if (UiFx.HoldsToPresses)
        {
            button.Pressed += () => OpenConfirm(info.Title, info.Description, onConfirmed);
        }
        else
        {
            HoldRing ring = UiFx.HoldRing(onConfirmed);
            ring.Attach(button);
            box.AddChild(ring);
            button.TooltipText = Loc.T("settings.hold_hint");
            info.Description = Loc.TF("settings.hold_desc", info.Description);
        }

        box.AddChild(button);
        return box;
    }

    // --- Description pane ---------------------------------------------------

    private void ShowDescription(RowInfo? info)
    {
        if (info != null && info == _shownRow)
        {
            return;
        }

        _shownRow = info;
        UiTheme.ClearChildren(_pane);
        if (info == null)
        {
            _pane.AddChild(PaneText(Loc.T("settings.pane.hint"), UiTheme.Dim));
            return;
        }

        // The interface face, not the carved one: an option's name can run past three words.
        var title = new Label { Text = info.Title, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        UiTheme.ApplyType(title, UiTheme.FontRole.Interface, UiTheme.HeaderFontSize);
        title.AddThemeColorOverride("font_color", UiTheme.Accent);
        _pane.AddChild(title);
        _pane.AddChild(PaneText(info.Description, UiTheme.Text));

        // Folded under the list there is room for the words and not for a picture.
        if (!_narrow && info.Preview?.Invoke() is { } preview)
        {
            _pane.AddChild(preview);
        }
    }

    private Label PaneText(string text, Color color)
    {
        Label label = UiTheme.Body(text, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        if (_narrow)
        {
            // Three lines and an ellipsis: under the list the pane must not grow and push the
            // footer off a handheld screen.
            label.MaxLinesVisible = 3;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            int line = UiTheme.FontSize(UiTheme.BodyFontSize) + UiTheme.SpaceXs;
            label.CustomMinimumSize = new Vector2(0f, line * 2f);
        }

        return label;
    }

    // --- Legend -------------------------------------------------------------

    private void UpdateLegend()
    {
        var entries = new List<LegendEntry>();
        if (_listening != null)
        {
            entries.Add(new LegendEntry(GameInput.Pause, Loc.T("common.cancel")));
        }
        else if (_prompt != null)
        {
            entries.Add(new LegendEntry("ui_accept", Loc.T("settings.legend.select")));
            entries.Add(new LegendEntry("ui_cancel", Loc.T("common.cancel")));
        }
        else
        {
            entries.Add(new LegendEntry(GameInput.MenuTabPrev, Loc.T("settings.legend.tab"), GameInput.MenuTabNext));
            if (_focusedRow is { Changed: { } changed, Revert: not null } && changed())
            {
                entries.Add(new LegendEntry(GameInput.MenuSubPrev, Loc.T("settings.revert")));
            }

            if (_focusedCell != null)
            {
                entries.Add(new LegendEntry(GameInput.MenuSubNext, Loc.T("settings.bind.clear")));
            }

            entries.Add(new LegendEntry("ui_cancel", Loc.T("common.back")));
        }

        _legend?.Set(entries);
    }

    private void OnDeviceChanged(InputDeviceChangedEvent e)
    {
        // The listening prompt names its way out with a glyph, which is a snapshot of one device.
        if (_listening is { } listening)
        {
            ShowListeningPrompt(listening);
        }
    }

    // --- Apply / persist ----------------------------------------------------

    /// <summary>Applies the live settings to the engine and writes them to disk. Used by discrete
    /// changes (toggles/dropdowns) and slider drag-ends; live slider drags only <c>Apply</c>.</summary>
    private void Persist()
    {
        _settings.Apply();
        _settings.Save();
    }

    private void Back()
    {
        _settings.Save(); // catch any live-applied-but-not-yet-persisted slider drag
        System.Action? onBack = _onBack;
        QueueFree();
        onBack?.Invoke();
    }

    // --- Capture hooks ------------------------------------------------------

    /// <summary>Screenshot entry point: shows tab <paramref name="index"/> (strip order) at once.</summary>
    public void ShowTabForCapture(int index)
    {
        ClosePrompt();
        _tab = (SettingsTab)Mathf.Clamp(index, 0, SettingsTabRules.TabCount - 1);
        _tabChanged = true;
        _dirty = false;
        Rebuild();
    }

    /// <summary>Screenshot entry point: lays the sheet out in one column whatever the window's
    /// width (true), or by the width again (false).</summary>
    public void SetNarrowForCapture(bool narrow)
    {
        _narrowOverride = narrow ? true : null;
        _dirty = false;
        Rebuild();
    }

    public int TabForCapture => (int)_tab;

    public bool NarrowForCapture => _narrow;

    public bool ListeningForCapture => _listening != null;

    public bool PromptOpenForCapture => _prompt != null;
}
