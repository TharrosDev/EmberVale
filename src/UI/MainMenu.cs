using Embervale.Core;
using Embervale.Localization;
using Embervale.Save;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The title / main menu (Phase 24A): the first thing shown on launch, before any world is
/// built. <see cref="Bootstrap.GameShellController"/> boots into <see cref="GameState.MainMenu"/> and shows this
/// instead of constructing the sandbox. <b>Continue</b> resumes the most-recent save,
/// <b>New Game</b> and <b>Load Game</b> open the <see cref="SaveSlotPanel"/> to pick a slot (24C),
/// <b>Settings</b> and <b>Accessibility</b> open the <see cref="SettingsPanel"/> (24F),
/// <b>Credits</b> the <see cref="CreditsScreen"/>, and <b>Quit</b> asks before it exits.
///
/// There is no panel: a painting fills the screen (<see cref="TitleBackdrop"/>, one per act of
/// the newest save), a soft shade gathers on its right, and the menu is a frameless
/// <see cref="UiTheme.Sheet"/> column of words laid on it. The column is built again every time
/// the menu is shown, so what is enabled, the save named under Continue, the text size and the
/// painting all follow whatever the screen the player just left did.
///
/// On a person's first sight of it in a process the <see cref="BootSplash"/> comes first, and on
/// a true first run <see cref="FirstRunSetup"/> after that; a tool run gets neither.
/// </summary>
public partial class MainMenu : CanvasLayer
{
    /// <summary>Invoked with the chosen slot and the created character when the player starts a new game.</summary>
    public System.Action<string, Races.CharacterProfile>? NewCharacterRequested { get; set; }

    /// <summary>Invoked with the chosen slot when the player loads/continues a save.</summary>
    public System.Action<string>? LoadGameRequested { get; set; }

    /// <summary>A locale key for a message shown above the buttons when the menu opens: why the
    /// last session ended, when it ended on a failure. Null for an ordinary visit.</summary>
    public string? NoticeKey { get; init; }

    /// <summary>Where the rebuilt sheet goes among this layer's children: over the painting,
    /// under the legend and any prompt.</summary>
    private const int SheetIndex = 1;

    private TitleBackdrop _backdrop = null!;
    private UiLegend? _legend;
    private Control? _sheet;
    private VBoxContainer _entries = null!;
    private Label? _notice;
    private Button? _quit;
    private Control? _prompt;
    private string? _noticeText;
    private bool _firstRun;
    private bool _canContinue;

    // The newest save's act, kept against that save so its file is read once and not per visit.
    private string _actSlot = string.Empty;
    private double _actStamp;
    private int _act = 1;
    private bool _actPinned;

    public override void _Ready()
    {
        Layer = 11; // above the (not-yet-built) HUD and the pause menu
        // No world/player yet on the title screen, so make sure the cursor is free.
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        SetProcess(false); // only a prompt needs the tick (it polls for cancel), and it wakes it

        if (!string.IsNullOrEmpty(NoticeKey))
        {
            _noticeText = Loc.T(NoticeKey);
        }

        _backdrop = new TitleBackdrop();
        AddChild(_backdrop);
        BuildSheet();

        // Whenever a sub-screen (slots, creator, settings, credits) hands the screen back.
        VisibilityChanged += OnVisibilityChanged;

        _firstRun = FirstRunSetup.ConsumeWanted();
        if (BootSplash.Wanted)
        {
            Visible = false;
            BootSplash.Open(this, AfterSplash);
        }
        else
        {
            Present();
        }
    }

    private void AfterSplash()
    {
        if (_firstRun && FirstRunSetup.Open(this, () => Visible = true) != null)
        {
            return;
        }

        Visible = true;
    }

    private void OnVisibilityChanged()
    {
        if (!Visible)
        {
            return;
        }

        BuildSheet();
        Present();
    }

    /// <summary>The menu arriving: the painting's motion follows the settings as they are now,
    /// focus lands on the first live entry (Continue, when there is a save) and the entries come
    /// in one after another.</summary>
    private void Present()
    {
        _backdrop.Refresh();
        UiFocus.GrabFirst(_entries);
        UiFx.Stagger(_entries);
    }

    public override void _Process(double delta)
    {
        if (_prompt != null &&
            (Godot.Input.IsActionJustPressed(UiLive.UiCancel) || Godot.Input.IsActionJustPressed(UiLive.Pause)))
        {
            UiAudio.Play(UiCue.Back);
            ClosePrompt();
        }
    }

    // --- The sheet ----------------------------------------------------------

    private Control BuildVersion()
    {
        // In the legend's row, at the dark end of the shade.
        Label version = UiTheme.Caption(Loc.TF("title.version", ProjectSettings.GetSetting("application/config/version").AsString()));
        version.MouseFilter = Control.MouseFilterEnum.Ignore;
        version.HorizontalAlignment = HorizontalAlignment.Right;
        version.VerticalAlignment = VerticalAlignment.Center;
        version.AnchorLeft = 1f;
        version.AnchorRight = 1f;
        version.AnchorTop = 1f;
        version.AnchorBottom = 1f;
        version.GrowHorizontal = Control.GrowDirection.Begin;
        version.OffsetRight = -UiTheme.SpaceLg;
        version.OffsetTop = -(UiChromeRules.LegendHeight + UiChromeRules.ChromeMargin);
        version.OffsetBottom = -UiChromeRules.ChromeMargin;
        return version;
    }

    private void BuildSheet()
    {
        if (_sheet != null)
        {
            // Hidden now and freed at the end of the frame: a rebuild can start inside one of
            // this sheet's own button signals.
            _sheet.Visible = false;
            _sheet.QueueFree();
        }

        ClosePrompt(restoreFocus: false);

        // Continue needs a save it can read a header from; the browser only needs a file to exist,
        // because a save too damaged to list is exactly the one the player has to be shown.
        SaveSlotInfo? latest = MostRecent();
        _canContinue = latest != null;
        bool canBrowse = _canContinue || AnyBrowsableSlotHasAFile();
        if (!_actPinned)
        {
            _backdrop.SetAct(ActOf(latest));
        }

        Vector2 view = GetViewport().GetVisibleRect().Size;
        bool compact = view.Y < UiTheme.TitleShortHeight;
        float width = Mathf.Min(UiTheme.TitleSheetWidth, view.X - (UiChromeRules.Gutter(view.X) * 2f));

        (Control root, VBoxContainer col) = UiTheme.Sheet(width, 0f);
        UiTheme.SheetToRight(col, width);
        _sheet = root;
        AddChild(root);
        MoveChild(root, SheetIndex);

        // The paintings keep their calm space on the right, and that is where the words go. The
        // shade, the version line and the legend are made again with the column: all three follow
        // the contrast and text-size settings, which the screen the player just left may have moved.
        Control shade = UiTheme.Shade(new Vector2(0.4f, 0f), new Vector2(1f, 0f), 0.88f);
        root.AddChild(shade);
        root.MoveChild(shade, 0);
        root.AddChild(BuildVersion());

        if (_legend != null)
        {
            RemoveChild(_legend);
            _legend.QueueFree();
        }

        _legend = new UiLegend();
        AddChild(_legend);

        // A short view (a handheld) keeps the wordmark and gives the seal and the subtitle up, so
        // all seven entries stay on screen.
        if (!compact)
        {
            col.AddChild(new TextureRect
            {
                Texture = GD.Load<Texture2D>("res://assets/ui/emblems/embervale_seal.png"),
                CustomMinimumSize = new Vector2(UiTheme.TitleSealSize, UiTheme.TitleSealSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        // The title screen and the spellbook are the only two surfaces that get the shimmer (see
        // the ornament budget in UiOrnament). The label and the sweep are stacked in a Control
        // as tall as the wordmark so the sweep spans the title rather than the whole column.
        Label title = UiTheme.Display(Loc.T("menu.title"));
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var titleStack = new Control
        {
            CustomMinimumSize = new Vector2(0f, UiTheme.FontSize(UiTheme.DisplayFontSize) + UiTheme.SpaceSm),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        titleStack.AddChild(title);
        titleStack.AddChild(UiOrnament.InkShimmer(UiTheme.Accent, period: 8f, intensity: 0.45f));
        col.AddChild(titleStack);

        if (!compact)
        {
            Label subtitle = UiTheme.Prose(Loc.T("title.subtitle"), UiTheme.Dim);
            subtitle.HorizontalAlignment = HorizontalAlignment.Left;
            subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(subtitle);
        }

        col.AddChild(UiOrnament.EmberWipe());

        _notice = UiTheme.Prose(_noticeText ?? string.Empty, UiTheme.Bad);
        _notice.HorizontalAlignment = HorizontalAlignment.Left;
        _notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _notice.Visible = _noticeText != null;
        col.AddChild(_notice);

        _entries = new VBoxContainer();
        _entries.AddThemeConstantOverride("separation", compact ? 0 : UiTheme.Space2xs);
        col.AddChild(_entries);

        _entries.AddChild(ContinueEntry(latest));
        _entries.AddChild(Entry(Loc.T("menu.new_game"), () => OpenSlotPanel(SaveSlotPanel.Intent.New)));
        _entries.AddChild(Entry(Loc.T("menu.load_game"), canBrowse ? () => OpenSlotPanel(SaveSlotPanel.Intent.Load) : null));
        _entries.AddChild(Entry(Loc.T("menu.settings"), OpenSettings));
        _entries.AddChild(Entry(Loc.T("title.accessibility"), OpenAccessibility));
        _entries.AddChild(Entry(Loc.T("title.credits"), OpenCredits));
        _quit = Entry(Loc.T("menu.quit"), OpenQuitConfirm);
        _entries.AddChild(_quit);

        UpdateLegend();
    }

    /// <summary>
    /// Continue, with the save it would resume named under it: who, what level, where, and how
    /// long they have played. With no save it is there but unavailable, and the line under it
    /// says why, so the state is words as well as a greyed entry.
    /// </summary>
    private Control ContinueEntry(SaveSlotInfo? latest)
    {
        var block = new VBoxContainer();
        block.AddThemeConstantOverride("separation", 0);
        block.AddChild(Entry(Loc.T("menu.continue"), latest != null ? ContinueMostRecent : null, UiCue.Confirm));

        string text = Loc.T("menu.no_saves");
        if (latest != null)
        {
            (int hours, int minutes) = ShellFrontRules.Playtime(latest.PlaytimeSeconds);
            text = Loc.TF("title.continue_meta", latest.CharacterName, latest.Level, latest.Region, hours, minutes);
        }

        // One line: a long name gives way with an ellipsis rather than pushing the entries down.
        Label meta = UiTheme.Caption(text, latest != null ? UiTheme.Dim : UiTheme.Disabled);
        meta.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        meta.ClipText = true;
        meta.TooltipText = text;
        meta.MouseFilter = Control.MouseFilterEnum.Ignore;

        var inset = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        inset.AddThemeConstantOverride("margin_left", UiTheme.SpaceMd); // under the entry's word
        inset.AddThemeConstantOverride("margin_right", UiTheme.SpaceMd);
        inset.AddThemeConstantOverride("margin_bottom", UiTheme.Space2xs);
        inset.AddChild(meta);
        block.AddChild(inset);
        return block;
    }

    private static Button Entry(string text, System.Action? onPressed, UiCue cue = UiCue.Click)
    {
        Button button = UiTheme.TitleEntry(text, cue);
        if (onPressed == null)
        {
            // Only Continue and Load Game are ever disabled, and only for want of a save.
            button.Disabled = true;
            button.TooltipText = Loc.T("menu.no_saves");
        }
        else
        {
            button.Pressed += () => onPressed();
        }

        return button;
    }

    private void UpdateLegend()
    {
        _legend?.Set(_prompt != null
            ? new[]
            {
                new LegendEntry("ui_accept", Loc.T("title.legend.select")),
                new LegendEntry("ui_cancel", Loc.T("common.cancel")),
            }
            : new[] { new LegendEntry("ui_accept", Loc.T("title.legend.select")) });
    }

    /// <summary>Shows <paramref name="key"/>'s text above the buttons and brings the menu back if a
    /// sub-screen had hidden it: a refused or failed load lands the player here, and it must say why.</summary>
    public void ShowNotice(string key)
    {
        _noticeText = Loc.T(key);
        if (_notice != null && IsInstanceValid(_notice))
        {
            _notice.Text = _noticeText;
            _notice.Visible = true;
        }

        Visible = true;
    }

    // --- Saves --------------------------------------------------------------

    private static SaveSlotInfo? MostRecent()
    {
        if (SaveManager.Instance is not { } manager)
        {
            return null;
        }

        SaveSlotInfo? latest = null;
        foreach (SaveSlotInfo info in manager.ListSlots())
        {
            if (latest == null || info.TimestampUnix > latest.TimestampUnix)
            {
                latest = info;
            }
        }

        return latest;
    }

    private static bool AnyBrowsableSlotHasAFile()
    {
        if (SaveManager.Instance is not { } manager)
        {
            return false;
        }

        foreach (string slot in SaveSlotPanel.BrowsableSlots())
        {
            if (manager.SaveExists(slot))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The act the newest save has reached, which picks the painting. A header does not carry the
    /// chapter, so the save's own text is scanned for its chapter flags
    /// (<see cref="ShellFrontRules.ActFromSaveText"/>): once per save, not once per visit.
    /// </summary>
    private int ActOf(SaveSlotInfo? latest)
    {
        if (latest == null || SaveManager.Instance is not { } manager)
        {
            return 1;
        }

        if (latest.Slot == _actSlot && latest.TimestampUnix == _actStamp)
        {
            return _act;
        }

        string path = manager.SlotPath(latest.Slot);
        _actSlot = latest.Slot;
        _actStamp = latest.TimestampUnix;
        _act = ShellFrontRules.ActFromSaveText(FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : null);
        return _act;
    }

    private void ContinueMostRecent()
    {
        if (MostRecent() is { } latest)
        {
            LoadGameRequested?.Invoke(latest.Slot);
        }
    }

    // --- Sub-screens --------------------------------------------------------

    private void OpenSlotPanel(SaveSlotPanel.Intent mode)
    {
        var panel = new SaveSlotPanel();
        System.Action<string> chosen = mode == SaveSlotPanel.Intent.New
            ? OpenCreator
            : slot => LoadGameRequested?.Invoke(slot);

        // Hide the menu behind the panel; restore it if the player backs out.
        Visible = false;
        panel.Configure(mode, chosen, () => Visible = true);
        AddChild(panel);
        FadeInScreen(panel);
    }

    /// <summary>After a New-Game slot is picked, run the character creator (Phase 26D); confirm starts
    /// the game with the created profile, back returns to the title screen.</summary>
    private void OpenCreator(string slot)
    {
        var creator = new CharacterCreator();
        creator.Configure(
            profile => NewCharacterRequested?.Invoke(slot, profile),
            () => Visible = true);
        AddChild(creator);
        FadeInScreen(creator);
    }

    private void OpenSettings()
    {
        // Hide the menu behind the settings panel; restore it when the player backs out.
        Visible = false;
        SettingsPanel.Open(this, () => Visible = true);
        FadeInScreen(Newest<SettingsPanel>());
    }

    /// <summary>The same panel, opened on its Accessibility tab: the options a player may need
    /// before any other are one press from the title.</summary>
    private void OpenAccessibility()
    {
        Visible = false;
        SettingsPanel.Open(this, () => Visible = true);
        SettingsPanel? panel = Newest<SettingsPanel>();
        panel?.ShowTabForCapture((int)SettingsTab.Accessibility);
        FadeInScreen(panel);
    }

    private void OpenCredits()
    {
        Visible = false;
        CreditsScreen.Open(this, () => Visible = true);
    }

    /// <summary>The screen of type <typeparamref name="T"/> most recently opened over the menu.</summary>
    private T? Newest<T>() where T : Node
    {
        for (int i = GetChildCount() - 1; i >= 0; i--)
        {
            if (GetChild(i) is T found && !found.IsQueuedForDeletion())
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>A sub-screen arrives by fading in over the painting; it leaves at once, as every
    /// menu does. Each is its own layer, so it is the layer's surfaces that are faded.</summary>
    private static void FadeInScreen(Node? screen)
    {
        if (screen == null)
        {
            return;
        }

        foreach (Node child in screen.GetChildren())
        {
            if (child is Control { Visible: true } surface)
            {
                UiFx.FadeIn(surface);
            }
        }
    }

    // --- Quit ---------------------------------------------------------------

    /// <summary>Asks before leaving, with Cancel focused: one stray press on Quit changes nothing.</summary>
    private void OpenQuitConfirm()
    {
        if (_prompt != null)
        {
            return;
        }

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        ColorRect scrim = UiTheme.Scrim(0.6f);
        scrim.MouseFilter = Control.MouseFilterEnum.Stop;
        root.AddChild(scrim);

        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(centre);

        // A plate with one lit edge, the same prompt the settings screen asks with.
        float width = Mathf.Min(UiTheme.SettingsPromptWidth,
            GetViewport().GetVisibleRect().Size.X - (UiTheme.SpaceXl * 2f));
        PanelContainer plate = UiTheme.Card(UiTheme.RuleLit);
        centre.AddChild(plate);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0f) };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        plate.AddChild(col);

        var heading = new Label { Text = Loc.T("title.quit.heading"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        UiTheme.ApplyType(heading, UiTheme.FontRole.Interface, UiTheme.HeaderFontSize);
        heading.AddThemeColorOverride("font_color", UiTheme.Accent);
        col.AddChild(heading);

        Label body = UiTheme.Body(Loc.T("title.quit.body"));
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(body);

        HFlowContainer row = UiTheme.FlowRow();
        col.AddChild(row);
        Button cancel = UiTheme.Action(Loc.T("common.cancel"), UiCue.Back);
        cancel.Pressed += () => ClosePrompt();
        row.AddChild(cancel);
        Button quit = UiTheme.Action(Loc.T("title.quit.confirm"), UiCue.Confirm);
        quit.Pressed += () => GetTree().Quit();
        row.AddChild(quit);

        _prompt = root;
        AddChild(root);
        if (_legend != null)
        {
            MoveChild(_legend, -1); // it names this prompt's keys, so it stays above the scrim
        }

        // Focus stays inside the prompt: each button's neighbour is the other, and up and down go
        // nowhere. Paths, so only now that the buttons are in the tree.
        WirePair(cancel, quit);
        WirePair(quit, cancel);
        cancel.GrabFocus();

        UiFx.FadeIn(plate, UiTheme.DurationFast);
        UpdateLegend();
        SetProcess(true);
    }

    private static void WirePair(Button button, Button other)
    {
        NodePath to = button.GetPathTo(other);
        NodePath self = button.GetPathTo(button);
        button.FocusNeighborLeft = to;
        button.FocusNeighborRight = to;
        button.FocusPrevious = to;
        button.FocusNext = to;
        button.FocusNeighborTop = self;
        button.FocusNeighborBottom = self;
    }

    /// <summary>Closes the quit prompt, at once, and puts focus back on Quit.</summary>
    private void ClosePrompt(bool restoreFocus = true)
    {
        if (_prompt == null)
        {
            return;
        }

        _prompt.Visible = false;
        _prompt.QueueFree();
        _prompt = null;
        SetProcess(false);
        UpdateLegend();
        if (restoreFocus && _quit != null && IsInstanceValid(_quit) && _quit.IsVisibleInTree())
        {
            _quit.GrabFocus();
        }
    }

    // --- Capture hooks ------------------------------------------------------

    /// <summary>Deterministic screenshot entry point; follows the same settings path as the menu button.</summary>
    public void OpenSettingsForCapture() => OpenSettings();

    /// <summary>Screenshot entry point: the creator as a fresh New Game opens it, returned so a harness can drive its picks.</summary>
    public CharacterCreator OpenCreatorForCapture()
    {
        Visible = false;
        var creator = new CharacterCreator();
        creator.Configure(_ => { }, () => Visible = true);
        AddChild(creator);
        return creator;
    }

    /// <summary>Screenshot entry point: the boot splash as a first launch shows it, at rest.</summary>
    public BootSplash OpenSplashForCapture()
    {
        Visible = false;
        BootSplash splash = BootSplash.Open(this, () => Visible = true);
        splash.SettleForCapture();
        return splash;
    }

    /// <summary>Screenshot entry point: first-run setup as a first launch shows it, or null with
    /// no settings service to ask about.</summary>
    public FirstRunSetup? OpenFirstRunForCapture()
    {
        Visible = false;
        return FirstRunSetup.Open(this, () => Visible = true);
    }

    /// <summary>Screenshot entry point: the credits, returned so a harness can place the roll.</summary>
    public CreditsScreen OpenCreditsForCapture()
    {
        Visible = false;
        return CreditsScreen.Open(this, () => Visible = true);
    }

    /// <summary>Screenshot entry point: puts up the painting for <paramref name="act"/> and keeps
    /// it there whatever the newest save says.</summary>
    public void SetActForCapture(int act)
    {
        _actPinned = true;
        _backdrop.SetAct(act);
    }

    /// <summary>Screenshot entry point: ends the entries' arrival, so a capture taken a few
    /// frames after the menu is shown photographs the menu and not its entrance.</summary>
    public void SettleForCapture()
    {
        foreach (Node child in _entries.GetChildren())
        {
            if (child is Control entry)
            {
                UiFx.FadeIn(entry, 0f);
            }
        }
    }

    public void OpenQuitConfirmForCapture() => OpenQuitConfirm();

    public void CloseQuitConfirmForCapture() => ClosePrompt();

    public int ActForCapture => _backdrop.Act;

    public bool QuitConfirmOpenForCapture => _prompt != null;

    public bool ContinueEnabledForCapture => _canContinue;
}
