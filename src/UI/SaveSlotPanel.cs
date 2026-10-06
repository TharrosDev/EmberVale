using System;
using System.Collections.Generic;
using Embervale.Corruption;
using Embervale.Localization;
using Embervale.Save;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The save-slot browser (Phase 24C): a modal list of slots the player picks from to start a new
/// game, load a save, or (from the pause menu) save into a slot. Each filled slot shows a
/// screenshot thumbnail, how it was made, whose it is and where they stood, their level and
/// corruption, how long they played and when they left. Opened by the <see cref="MainMenu"/> and
/// the <see cref="PauseMenu"/> in one of three <see cref="Intent"/>s. Built in code through
/// <see cref="UiTheme"/>: a frameless sheet, like the settings screen.
///
/// <para>Slots are read with <see cref="SaveManager.InspectSlot"/>, so a save that is corrupt or was
/// written by a newer build is a row with a badge and an explanation rather than an "Empty" the
/// player would start a new game over without knowing. Such a row cannot be loaded.</para>
///
/// <para>Deleting a save and writing over one cannot be undone, so both are held
/// (<see cref="HoldRing"/>); with the holds-to-presses setting on, a press asks first instead.
/// Loading over unsaved progress asks too. A thumbnail is decoded when its row is first on
/// screen, one a frame, and kept for as long as the browser is open.</para>
/// </summary>
public partial class SaveSlotPanel : CanvasLayer
{
    public enum Intent
    {
        /// <summary>Choosing a slot to start a fresh game (empty slots act; filled ask to overwrite).</summary>
        New,

        /// <summary>Choosing an existing save to load: manual, quick and autosaves, newest first.</summary>
        Load,

        /// <summary>Choosing a manual slot to save the running game into (filled ask to overwrite).</summary>
        Save,
    }

    /// <summary>The buttons of one row, for the focus wiring: what the row does, and its Delete.</summary>
    private readonly record struct RowButtons(Button Primary, Button? Secondary);

    /// <summary>Marks a button that has to be held, so the legend can say so while it has focus.</summary>
    private const string HoldMeta = "slot_hold";

    /// <summary>How long the capture harness's hold takes: long enough that the frame it
    /// photographs half a second later is mid-hold on any machine.</summary>
    private const float CaptureHoldSeconds = 2f;

    private Intent _mode;
    private Action<string>? _onChosen;
    private Action? _onBack;
    private string? _loadWarning;
    private string? _currentSlot;

    // Inspected once per open and again after a delete: InspectSlot parses the whole save.
    private readonly List<SaveSlotInfo> _entries = new();

    // Decoded screenshots by slot, null for a slot with none. Kept for the life of the panel, so a
    // refresh after a delete does not read every PNG again.
    private readonly Dictionary<string, Texture2D?> _thumbnails = new();
    private readonly List<(string Slot, TextureRect Rect)> _undecoded = new();
    private readonly List<RowButtons> _rows = new();
    private readonly Dictionary<string, HoldRing> _deleteRings = new();

    private ScrollContainer _scroll = null!;
    private VBoxContainer _list = null!;
    private Button _back = null!;
    private UiLegend _legend = null!;
    private SessionPrompt? _prompt;
    private Viewport? _viewport;
    private HoldRing? _captureRing;
    private bool _narrow;

    /// <param name="loadWarning">Already-localized text shown above the list in <see cref="Intent.Load"/>;
    /// when set, loading a row asks first (the running game has unsaved progress).</param>
    /// <param name="currentSlot">The running session's own slot, marked in <see cref="Intent.Save"/>.</param>
    public void Configure(
        Intent mode, Action<string> onChosen, Action onBack, string? loadWarning = null, string? currentSlot = null)
    {
        _mode = mode;
        _onChosen = onChosen;
        _onBack = onBack;
        _loadWarning = loadWarning;
        _currentSlot = currentSlot;
    }

    /// <summary>Every slot the load browser looks in: the manual roster, the quick slot, the autosave ring.</summary>
    public static IEnumerable<string> BrowsableSlots()
    {
        foreach (string slot in SaveManager.ManualSlots)
        {
            yield return slot;
        }

        yield return SaveManager.QuickSlot;

        foreach (string slot in SaveManager.AutoSlots)
        {
            yield return slot;
        }
    }

    /// <summary>What the player calls a slot: "Slot 2", "Quick Save", "Autosave 1". A slot outside
    /// the rosters (a dev or probe slot) is shown by its id.</summary>
    public static string SlotLabel(string slot)
    {
        if (slot == SaveManager.QuickSlot)
        {
            return Loc.T("slots.quick");
        }

        for (int i = 0; i < SaveManager.AutoSlots.Count; i++)
        {
            if (SaveManager.AutoSlots[i] == slot)
            {
                return Loc.TF("slots.autosave", i + 1);
            }
        }

        for (int i = 0; i < SaveManager.ManualSlots.Count; i++)
        {
            if (SaveManager.ManualSlots[i] == slot)
            {
                return Loc.TF("slots.slot", i + 1);
            }
        }

        return slot;
    }

    public override void _Ready()
    {
        Layer = 12; // above the main menu and the pause menu
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        Build();

        // The legend names what the focused button does, and a held one is not a pressed one.
        _viewport = GetViewport();
        _viewport.GuiFocusChanged += OnFocusChanged;
    }

    public override void _ExitTree()
    {
        if (_viewport != null)
        {
            _viewport.GuiFocusChanged -= OnFocusChanged;
        }
    }

    private void Build()
    {
        Vector2 view = GetViewport().GetVisibleRect().Size;
        _narrow = view.X < UiChromeRules.NarrowWidth;
        float width = Mathf.Min(view.X - (UiChromeRules.Gutter(view.X) * 2f), UiTheme.SessionSheetMaxWidth);

        // A sheet, not a framed panel: the column sits on the scrim behind one lit rule. It fills
        // the height it is given (a sheet centres its column by default) so the list can scroll.
        (Control root, VBoxContainer col) = UiTheme.Sheet(width, 0.92f, centred: true);
        col.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        AddChild(root);

        string titleKey = _mode switch
        {
            Intent.New => "slots.title.new",
            Intent.Save => "slots.title.save",
            _ => "slots.title.load",
        };
        col.AddChild(UiTheme.Title(Loc.T(titleKey)));
        col.AddChild(UiOrnament.EmberWipe());

        if (_mode == Intent.Load && !string.IsNullOrEmpty(_loadWarning))
        {
            Label warning = UiTheme.Caption(_loadWarning, UiTheme.Bad);
            warning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(warning);
        }

        // The list scrolls inside the sheet: with three manual slots and the autosave ring it is
        // taller than a 720 px window. Its floor is small so the footer stays on a handheld screen.
        (_scroll, _list) = UiTheme.ScrollList();
        _scroll.CustomMinimumSize = new Vector2(0f, UiTheme.SlotListMinHeight);
        col.AddChild(_scroll);

        col.AddChild(UiTheme.SessionRule());

        var footer = new HBoxContainer();
        _back = UiTheme.Action(Loc.T("common.back"), UiCue.Back);
        _back.Pressed += Back;
        footer.AddChild(_back);
        col.AddChild(footer);

        _legend = new UiLegend();
        AddChild(_legend);

        Inspect();
        RefreshList();
        UpdateLegend();
    }

    public override void _Process(double delta)
    {
        DecodeOneThumbnail();

        // Esc / gamepad B backs out (30.5J), matching the settings panel. An open question takes
        // the press first, so cancel always undoes the nearest thing.
        if (!Godot.Input.IsActionJustPressed(UiLive.UiCancel))
        {
            return;
        }

        UiAudio.Play(UiCue.Back);
        if (_prompt is { IsOpen: true })
        {
            _prompt.Close();
            _prompt = null;
            return;
        }

        Back();
    }

    private void Back()
    {
        _onBack?.Invoke();
        QueueFree();
    }

    /// <summary>Reads every slot this mode shows. New and Save list the three player slots in
    /// order, empty ones included; Load lists whatever exists, newest first.</summary>
    private void Inspect()
    {
        _entries.Clear();
        if (_mode == Intent.Load)
        {
            var found = new List<SaveSlotInfo>();
            foreach (string slot in BrowsableSlots())
            {
                found.Add(InspectOne(slot));
            }

            _entries.AddRange(SaveSlotPolicy.NewestFirst(found));
            return;
        }

        foreach (string slot in SaveSlotPolicy.PlayerManualSlots)
        {
            _entries.Add(InspectOne(slot));
        }
    }

    private static SaveSlotInfo InspectOne(string slot) =>
        SaveManager.Instance?.InspectSlot(slot) ?? new SaveSlotInfo { Slot = slot, Health = SaveHealth.Missing };

    private void RefreshList()
    {
        UiTheme.ClearChildren(_list);
        _undecoded.Clear();
        _rows.Clear();
        _deleteRings.Clear();
        _captureRing = null;

        if (_entries.Count == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("slots.none"), UiTheme.Disabled));
        }

        foreach (SaveSlotInfo info in _entries)
        {
            _list.AddChild(BuildRow(info));
        }

        WireRows();

        // Refresh frees the row the gamepad/keyboard focus sat on (after a delete) - re-land on the
        // first row once the new tree exists (30.5J), or on Back when no row can act.
        Callable.From(() =>
        {
            if (IsInstanceValid(this) && !IsQueuedForDeletion() && _prompt is not { IsOpen: true } &&
                !UiFocus.GrabFirst(_list))
            {
                _back.GrabFocus();
            }
        }).CallDeferred();
    }

    /// <summary>
    /// Up and down walk a column of the rows, left and right the two buttons of one. The engine's
    /// own search is by distance, and from a row's Delete the nearest thing above is as often the
    /// next row's Load. A row whose first button cannot be pressed (a damaged save's Load) is
    /// entered at its Delete. Paths, so only once the rows are in the tree.
    /// </summary>
    private void WireRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            RowButtons row = _rows[i];
            Link(row.Primary, i, secondary: false);
            if (row.Secondary is { } secondary)
            {
                Link(secondary, i, secondary: true);
                row.Primary.FocusNeighborRight = row.Primary.GetPathTo(secondary);
                secondary.FocusNeighborLeft = secondary.GetPathTo(row.Primary);
            }
        }

        // Never left pointing at a row that has been freed: the last save may just have been deleted.
        _back.FocusNeighborTop = _rows.Count > 0
            ? _back.GetPathTo(Entry(_rows[^1], secondary: false))
            : new NodePath();
    }

    private void Link(Button button, int row, bool secondary)
    {
        if (row > 0)
        {
            button.FocusNeighborTop = button.GetPathTo(Entry(_rows[row - 1], secondary));
        }

        Control below = row < _rows.Count - 1 ? Entry(_rows[row + 1], secondary) : _back;
        button.FocusNeighborBottom = button.GetPathTo(below);
    }

    /// <summary>The button focus lands on when it enters a row from above or below.</summary>
    private static Button Entry(RowButtons row, bool secondary) =>
        row.Secondary is { } other && (secondary || row.Primary.Disabled) ? other : row.Primary;

    private Control BuildRow(SaveSlotInfo info)
    {
        // A Card, not a Panel (37.5F): a list of six slots must not be six brass frames.
        //
        // The spine carries corruption: a save's tier is the one thing about it that is a *state*
        // rather than a statistic, and it is what a returning player is orienting on. A save that
        // cannot be loaded outranks that: its spine is the warning colour. Neither is said by
        // colour alone: the row carries the tier's mark, pips and name, or a badge.
        bool filled = info.Health != SaveHealth.Missing;
        bool loadable = info.Health == SaveHealth.Ok;
        CorruptionTier tier = loadable ? ShellSessionRules.TierOf(info.CorruptionTier) : CorruptionTier.Untainted;
        bool corrupted = tier > CorruptionTier.Untainted;
        Color spine = !filled ? UiTheme.Disabled
            : !loadable ? UiTheme.Bad
            : corrupted ? UiTheme.CorruptionText
            : UiTheme.Accent;

        PanelContainer rowPanel = UiTheme.Compact(UiTheme.Card(spine));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        rowPanel.AddChild(row);

        row.AddChild(BuildThumbnail(info.Slot, filled));

        var text = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        text.AddThemeConstantOverride("separation", UiTheme.LineGap);
        row.AddChild(text);

        text.AddChild(BuildHeading(info, filled, loadable));

        if (!filled)
        {
            text.AddChild(UiTheme.Caption(Loc.T("slots.row.empty"), UiTheme.Disabled));
        }
        else
        {
            if (loadable)
            {
                text.AddChild(BuildFacts(info, tier, corrupted));
            }

            Label caption = UiTheme.Caption(Describe(info), loadable ? null : UiTheme.Bad);
            caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.AddChild(caption);
        }

        row.AddChild(BuildActions(info));
        return rowPanel;
    }

    /// <summary>The row's first line: how the save was made (a mark and the word, in the load
    /// browser, where the three kinds are mixed), what the slot is called, and what is wrong with it.</summary>
    private Control BuildHeading(SaveSlotInfo info, bool filled, bool loadable)
    {
        HFlowContainer head = UiTheme.FlowRow();

        var name = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        name.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        if (_mode == Intent.Load && filled)
        {
            name.AddChild(new SessionGlyph(KindShape(info.Kind), UiTheme.Dim, UiTheme.SlotGlyphSize));
        }

        // The player's own label can be any length; a slot's default name is two words.
        bool labelled = info.DisplayName.Length > 0;
        Label title = UiTheme.Body(labelled ? info.DisplayName : SlotLabel(info.Slot), filled ? UiTheme.Text : UiTheme.Disabled);
        UiTheme.ApplyType(title, labelled ? UiTheme.FontRole.Interface : UiTheme.FontRole.Display, UiTheme.BodyFontSize);
        name.AddChild(title);
        head.AddChild(name);

        if (_mode == Intent.Load && filled)
        {
            Label kind = UiTheme.Caption(Loc.T(KindKey(info.Kind)));
            kind.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            head.AddChild(kind);
        }
        else if (_mode == Intent.Save && filled && info.Slot == _currentSlot)
        {
            head.AddChild(UiTheme.Chip(Loc.T("slots.current"), UiTheme.Accent));
        }

        if (filled && !loadable)
        {
            head.AddChild(UiTheme.Chip(
                Loc.T(info.Health == SaveHealth.Newer ? "slots.badge.newer" : "slots.badge.corrupt"), UiTheme.Bad));
        }

        // The newest save in the slot is damaged and a load will read the one before it: said on
        // the row, in words, before the player commits to it.
        if (loadable && info.RecoveredFromBackup)
        {
            head.AddChild(UiTheme.Chip(Loc.T("slots.badge.backup"), UiTheme.Bad));
        }

        return head;
    }

    /// <summary>The row's second line: whose save, where they stood, their level and corruption.
    /// It wraps when the column is narrow. A save header carries no chapter or mission, so the
    /// region is the place the row names.</summary>
    private static Control BuildFacts(SaveSlotInfo info, CorruptionTier tier, bool corrupted)
    {
        HFlowContainer facts = UiTheme.FlowRow();

        Label name = UiTheme.Body(info.CharacterName, UiTheme.Text);
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        facts.AddChild(name);

        Label region = UiTheme.Body(info.Region, UiTheme.Accent);
        region.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        facts.AddChild(region);

        facts.AddChild(UiTheme.Chip(Loc.TF("slots.level", info.Level), UiTheme.Text));

        Color tint = corrupted ? UiTheme.CorruptionText : UiTheme.Dim;
        var corruption = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        corruption.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        corruption.AddChild(new SessionGlyph(SessionGlyph.Shape.Corruption, tint, UiTheme.SlotGlyphSize));
        corruption.AddChild(new SessionGlyph(
            SessionGlyph.Shape.Pips, tint, UiTheme.SlotGlyphSize,
            ShellSessionRules.CorruptionPips(tier), ShellSessionRules.CorruptionPipCount));
        corruption.AddChild(UiTheme.Caption(CorruptionTiers.DisplayName(tier), tint));
        facts.AddChild(corruption);
        return facts;
    }

    private static string KindKey(SaveKind kind) => kind switch
    {
        SaveKind.Quick => "slots.kind.quick",
        SaveKind.Auto => "slots.kind.auto",
        _ => "slots.kind.manual",
    };

    private static SessionGlyph.Shape KindShape(SaveKind kind) => kind switch
    {
        SaveKind.Quick => SessionGlyph.Shape.Quick,
        SaveKind.Auto => SessionGlyph.Shape.Auto,
        _ => SessionGlyph.Shape.Manual,
    };

    // --- Thumbnails -----------------------------------------------------------

    private Control BuildThumbnail(string slot, bool filled)
    {
        // A well, so a save with no screenshot (and an empty slot) shows a cut frame, not a hole.
        float scale = _narrow ? 0.8f : 1f;
        PanelContainer well = UiTheme.Well();
        well.CustomMinimumSize = new Vector2(UiTheme.SlotThumbWidth, UiTheme.SlotThumbHeight) * scale;
        well.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        well.MouseFilter = Control.MouseFilterEnum.Ignore;

        var rect = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        well.AddChild(rect);

        if (!filled)
        {
            return well;
        }

        if (_thumbnails.TryGetValue(slot, out Texture2D? known))
        {
            rect.Texture = known;
        }
        else
        {
            _undecoded.Add((slot, rect));
        }

        return well;
    }

    /// <summary>
    /// Decodes at most one screenshot a frame, and only for a row that is on screen: the list
    /// used to read and decode every slot's PNG inside the open, and again on every refresh.
    /// A row scrolled or focused into view is decoded the frame after it arrives.
    /// </summary>
    private void DecodeOneThumbnail()
    {
        if (_undecoded.Count == 0)
        {
            return;
        }

        Rect2 window = _scroll.GetGlobalRect();
        for (int i = 0; i < _undecoded.Count; i++)
        {
            (string slot, TextureRect rect) = _undecoded[i];
            if (!IsInstanceValid(rect) || !rect.IsInsideTree())
            {
                _undecoded.RemoveAt(i--);
                continue;
            }

            if (!rect.GetGlobalRect().Intersects(window))
            {
                continue;
            }

            _undecoded.RemoveAt(i);
            rect.Texture = Thumbnail(slot);
            return;
        }
    }

    private Texture2D? Thumbnail(string slot)
    {
        if (_thumbnails.TryGetValue(slot, out Texture2D? cached))
        {
            return cached;
        }

        Texture2D? texture = null;
        if (SaveManager.Instance is { } manager)
        {
            string path = manager.ScreenshotPath(slot);
            if (FileAccess.FileExists(path) && Image.LoadFromFile(path) is { } image && !image.IsEmpty())
            {
                texture = ImageTexture.CreateFromImage(image);
            }
        }

        _thumbnails[slot] = texture;
        return texture;
    }

    // --- Actions --------------------------------------------------------------

    /// <summary>A button in a slot row: a full control tall and wide enough that Load and Delete are
    /// equal targets rather than two different-sized words.</summary>
    private static Button RowAction(string text, UiCue cue = UiCue.Click)
    {
        Button button = UiTheme.Action(text, cue);
        button.CustomMinimumSize = new Vector2(UiTheme.SlotActionWidth, UiTheme.ControlHeight);
        return button;
    }

    private Control BuildActions(SaveSlotInfo info)
    {
        string slot = info.Slot;
        bool filled = info.Health != SaveHealth.Missing;
        string label = info.DisplayName.Length > 0 ? info.DisplayName : SlotLabel(slot);
        var box = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        box.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Button primary;
        if (_mode == Intent.Load)
        {
            // A corrupt or newer-format save is shown so the player knows it is there; it is never
            // handed to a load, which would tear the title (or the running game) down to fail.
            primary = RowAction(Loc.T("common.load"));
            primary.Disabled = info.Health != SaveHealth.Ok;
            primary.Pressed += () =>
            {
                if (string.IsNullOrEmpty(_loadWarning))
                {
                    Choose(slot);
                }
                else
                {
                    Ask(Loc.T("slots.ask.load_title"), _loadWarning, Loc.T("common.load"), () => Choose(slot));
                }
            };
            box.AddChild(primary);
        }
        else if (ShellSessionRules.WriteNeedsHold(info.Health))
        {
            // Writing over a save cannot be undone. A save that cannot be loaded counts: it may be
            // recoverable, and "Empty" would hide that it exists.
            primary = HoldAction(
                box, Loc.T("common.overwrite"), out _,
                Loc.T("slots.ask.overwrite_title"), Loc.TF("slots.ask.overwrite_text", label),
                () => Choose(slot));
        }
        else
        {
            primary = RowAction(Loc.T(_mode == Intent.New ? "common.new_game" : "slots.save_here"), UiCue.Confirm);
            primary.Pressed += () => Choose(slot);
            box.AddChild(primary);
        }

        Button? delete = null;
        if (filled)
        {
            delete = HoldAction(
                box, Loc.T("common.delete"), out HoldRing? ring,
                Loc.T("slots.ask.delete_title"), Loc.TF("slots.ask.delete_text", label),
                () => Delete(slot));
            if (ring != null)
            {
                _deleteRings[slot] = ring;
            }
        }

        _rows.Add(new RowButtons(primary, delete));
        return box;
    }

    /// <summary>
    /// A button for something that cannot be undone, with its ring: hold it to confirm. With the
    /// holds-to-presses setting on there is no ring and a press asks first, with Cancel focused,
    /// so one stray press still changes nothing.
    /// </summary>
    private Button HoldAction(
        Container box, string text, out HoldRing? ring, string askTitle, string askText, Action onConfirmed)
    {
        Button button = RowAction(text);
        ring = null;
        if (UiFx.HoldsToPresses)
        {
            button.Pressed += () => Ask(askTitle, askText, text, onConfirmed);
        }
        else
        {
            ring = UiFx.HoldRing(onConfirmed);
            ring.Attach(button);

            // A button that loses focus mid-press never reports the release (it goes to whatever
            // took focus), and the ring would fill on its own and destroy the save.
            button.FocusExited += ring.Release;
            button.TooltipText = Loc.T("slots.hold_hint");
            button.SetMeta(HoldMeta, true);
            box.AddChild(ring);
        }

        box.AddChild(button);
        return button;
    }

    /// <summary>Asks before something that loses data, with Cancel focused.</summary>
    private void Ask(string title, string text, string confirmLabel, Action onConfirmed)
    {
        _prompt?.Close();
        _prompt = SessionPrompt.Open(
            this, title, text,
            new SessionPrompt.Choice(Loc.T("common.cancel"), UiCue.Back, () => _prompt = null, Focused: true),
            new SessionPrompt.Choice(confirmLabel, UiCue.Confirm, () =>
            {
                _prompt = null;
                onConfirmed();
            }, Danger: true));
        MoveChild(_legend, -1); // it names this prompt's keys, so it stays above the scrim
    }

    private void Delete(string slot)
    {
        // A file that would not go leaves the slot on disk; the re-inspection below then shows
        // it still there instead of an emptied row that comes back.
        if (SaveManager.Instance is { } saves && !saves.DeleteSlot(slot, out IReadOnlyList<string> failures) &&
            failures.Count > 0)
        {
            Embervale.Core.Events.EventBus.Instance?.Publish(new SaveNoticeEvent(Loc.T("slots.delete_failed"), Warning: true));
        }

        _thumbnails.Remove(slot);
        Inspect();
        RefreshList();
    }

    private void Choose(string slot)
    {
        Action<string>? chosen = _onChosen;
        QueueFree();
        chosen?.Invoke(slot);
    }

    // --- Legend ---------------------------------------------------------------

    private void OnFocusChanged(Control focus) => UpdateLegend();

    private void UpdateLegend()
    {
        bool hold = GetViewport()?.GuiGetFocusOwner() is { } focus && focus.HasMeta(HoldMeta);
        _legend.Set(new[]
        {
            new LegendEntry("ui_accept", Loc.T(hold ? "session.legend.hold" : "session.legend.select")),
            new LegendEntry("ui_cancel", Loc.T("session.legend.back")),
        });
    }

    /// <summary>The caption under a filled row: for a loadable save how long they played and when
    /// they left, on the player's own clock; otherwise why it cannot be loaded.</summary>
    private static string Describe(SaveSlotInfo info)
    {
        if (info.Health == SaveHealth.Newer)
        {
            return Loc.TF("slots.newer_help", info.FormatVersion, SaveManager.CurrentFormatVersion);
        }

        if (info.Health != SaveHealth.Ok)
        {
            return Loc.T("slots.corrupt_help");
        }

        (int hours, int minutes) = ShellSessionRules.Playtime(info.PlaytimeSeconds);
        string played = Loc.TF("slots.row.played", Loc.TF("slots.playtime", hours, $"{minutes:00}"));
        string saved = Loc.TF("slots.row.saved", ShellSessionRules.LocalDate(info.TimestampUnix, TimeZoneInfo.Local));
        string meta = Loc.TF("slots.meta", played, saved);
        return info.RecoveredFromBackup ? Loc.TF("slots.backup_help", meta) : meta;
    }

    // --- Capture hooks --------------------------------------------------------

    /// <summary>Capture hook: shows <paramref name="rows"/> in place of what is on disk, so every
    /// kind of row can be photographed without authoring a damaged save.</summary>
    public void ShowRowsForCapture(IReadOnlyList<SaveSlotInfo> rows)
    {
        _entries.Clear();
        _entries.AddRange(rows);
        RefreshList();
    }

    /// <summary>How many rows the list holds. Read by the screenshot harness.</summary>
    public int RowCountForCapture => _entries.Count;

    /// <summary>Capture hook: starts holding <paramref name="slot"/>'s Delete and leaves it held.
    /// The hold is slowed and completes to nothing, so a capture never deletes a save. False when
    /// the row has no ring (no such row, or the holds-to-presses setting is on).</summary>
    public bool HoldDeleteForCapture(string slot)
    {
        if (!_deleteRings.TryGetValue(slot, out HoldRing? ring) || !IsInstanceValid(ring))
        {
            return false;
        }

        ring.Completed = static () => { };
        ring.Seconds = CaptureHoldSeconds;
        ring.Press();
        _captureRing = ring;
        return ring.Holding;
    }

    /// <summary>How full the ring <see cref="HoldDeleteForCapture"/> is holding has got, 0..1.</summary>
    public float HoldProgressForCapture => _captureRing != null && IsInstanceValid(_captureRing) ? _captureRing.Progress : 0f;

    /// <summary>Capture hook: lets go of the held Delete and rebuilds the rows with their real rings.</summary>
    public void ReleaseHoldForCapture()
    {
        if (_captureRing != null && IsInstanceValid(_captureRing))
        {
            _captureRing.Release();
        }

        RefreshList();
    }
}
