using System;
using System.Collections.Generic;
using Embervale.Localization;
using Embervale.Save;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The save-slot browser (Phase 24C): a modal list of slots the player picks from to start a new
/// game, load a save, or (from the pause menu) save into a slot. Each filled slot shows its header
/// metadata (character, region, level, corruption tier, playtime, date) and a screenshot thumbnail.
/// Opened by the <see cref="MainMenu"/> and the <see cref="PauseMenu"/> in one of three
/// <see cref="Intent"/>s; deletes a slot (with an inline confirm) via
/// <see cref="SaveManager.DeleteSlot"/>. Built in code through <see cref="UiTheme"/>.
///
/// <para>Slots are read with <see cref="SaveManager.InspectSlot"/>, so a save that is corrupt or was
/// written by a newer build is a row with a badge and an explanation rather than an "Empty" the
/// player would start a new game over without knowing. Such a row cannot be loaded; writing over it
/// asks first, exactly as writing over a good save does.</para>
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

    /// <summary>The second click a row is waiting for.</summary>
    private enum Pending
    {
        None,
        Overwrite,
        Delete,
        Load,
    }

    private Intent _mode;
    private Action<string>? _onChosen;
    private Action? _onBack;
    private string? _loadWarning;
    private string? _currentSlot;
    private string? _pendingSlot;
    private Pending _pending;

    // Inspected once per open and again after a delete: InspectSlot parses the whole save, and a
    // confirm click only changes which buttons a row shows.
    private readonly List<SaveSlotInfo> _entries = new();

    private VBoxContainer _list = null!;
    private Button _back = null!;

    /// <summary>Smallest height the slot list scrolls in; the workspace frame gives it the rest.</summary>
    private const float ScrollMinHeight = 160f;

    private const float ActionWidth = 96f;

    /// <param name="loadWarning">Already-localized text shown above the list in <see cref="Intent.Load"/>;
    /// when set, loading a row asks for a second click (the running game has unsaved progress).</param>
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
    }

    private void Build()
    {
        var backdrop = UiTheme.Scrim(0.92f);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(backdrop);

        PanelContainer panel = UiTheme.Panel();
        UiTheme.ApplyWorkspace(panel, 0.66f);
        AddChild(panel);

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceLg);
        panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        pad.AddChild(col);

        string titleKey = _mode switch
        {
            Intent.New => "slots.new_title",
            Intent.Save => "slots.save_title",
            _ => "slots.load_title",
        };
        col.AddChild(UiTheme.Header(Loc.T(titleKey)));

        if (_mode == Intent.Load && !string.IsNullOrEmpty(_loadWarning))
        {
            Label warning = UiTheme.Caption(_loadWarning, UiTheme.Bad);
            warning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(warning);
        }

        col.AddChild(UiTheme.Divider());

        // The list scrolls inside the frame: with three manual slots and the autosave ring it is taller
        // than a 720 px window, and an unscrolled list pushed the whole panel off both edges.
        (ScrollContainer scroll, _list) = UiTheme.ScrollList();
        scroll.CustomMinimumSize = new Vector2(0f, ScrollMinHeight);
        col.AddChild(scroll);

        col.AddChild(UiTheme.Divider());
        _back = UiTheme.Action(Loc.T("common.back"));
        _back.Pressed += Back;
        col.AddChild(_back);

        Inspect();
        RefreshList();
    }

    public override void _Process(double delta)
    {
        // Esc / gamepad B backs out (30.5J), matching the settings panel. A row waiting for its
        // second click takes the press first, so cancel always undoes the nearest thing.
        if (!Godot.Input.IsActionJustPressed(UiLive.UiCancel))
        {
            return;
        }

        if (_pending != Pending.None)
        {
            ClearPending();
            RefreshList();
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
        foreach (Node child in _list.GetChildren())
        {
            child.QueueFree();
        }

        if (_entries.Count == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("slots.none"), UiTheme.Disabled));
        }

        foreach (SaveSlotInfo info in _entries)
        {
            _list.AddChild(BuildRow(info));
        }

        // Refresh frees the row the gamepad/keyboard focus sat on (confirm/delete flows) —
        // re-land on the first row once the new tree exists (30.5J), or on Back when no row can act.
        Callable.From(() =>
        {
            if (IsInstanceValid(this) && !IsQueuedForDeletion() && !UiFocus.GrabFirst(_list))
            {
                _back.GrabFocus();
            }
        }).CallDeferred();
    }

    private Control BuildRow(SaveSlotInfo info)
    {
        // A Card, not a Panel (37.5F). Every save row had been a full framed panel, so after 37.5A
        // a list of six slots was six brass frames and six grain shaders stacked vertically - the
        // frames competed with each other and with the panel actually containing them.
        //
        // The spine carries corruption: a save's tier is the one thing about it that is a *state*
        // rather than a statistic, and it is what a returning player is orienting on. A save that
        // cannot be loaded outranks that: its spine is the warning colour.
        bool filled = info.Health != SaveHealth.Missing;
        bool loadable = info.Health == SaveHealth.Ok;
        bool corrupted = loadable && !string.Equals(info.CorruptionTier, "Untainted", StringComparison.OrdinalIgnoreCase);
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

        string label = info.DisplayName.Length > 0 ? info.DisplayName : SlotLabel(info.Slot);
        Label title = UiTheme.Body(label, filled ? UiTheme.Text : UiTheme.Disabled);
        UiTheme.ApplyType(title, UiTheme.FontRole.Display, UiTheme.HeaderFontSize);
        text.AddChild(title);

        if (!filled)
        {
            text.AddChild(UiTheme.Body(Loc.T("slots.empty"), UiTheme.Disabled));
        }
        else
        {
            // Structured rather than one crammed line: the name and region say whose save and where,
            // the chips carry the facts a player compares between slots, and the caption carries the
            // two they read once. Name, region and chips share a line (and wrap if the column is
            // narrow), which keeps a row to three lines: a four-line row made six slots a screen and
            // a half tall.
            var facts = new HFlowContainer();
            facts.AddThemeConstantOverride("h_separation", UiTheme.ChipGap);
            facts.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);

            if (!loadable)
            {
                facts.AddChild(UiTheme.Chip(
                    Loc.T(info.Health == SaveHealth.Newer ? "slots.badge.newer" : "slots.badge.corrupt"), UiTheme.Bad));
            }

            // The newest save in the slot is damaged and a load will read the one before it: said on
            // the row, in words, before the player commits to it.
            if (loadable && info.RecoveredFromBackup)
            {
                facts.AddChild(UiTheme.Chip(Loc.T("slots.badge.backup"), UiTheme.Bad));
            }

            if (loadable)
            {
                Label name = UiTheme.Body(info.CharacterName, UiTheme.Text);
                name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                facts.AddChild(name);

                Label region = UiTheme.Body(info.Region, UiTheme.Accent);
                region.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                facts.AddChild(region);
                facts.AddChild(UiTheme.Chip(Loc.TF("slots.level", info.Level), UiTheme.Text));
                facts.AddChild(UiTheme.Chip(info.CorruptionTier, corrupted ? UiTheme.CorruptionText : UiTheme.Dim));
            }

            if (_mode == Intent.Load)
            {
                facts.AddChild(UiTheme.Chip(Loc.T(KindKey(info.Kind)), UiTheme.Dim));
            }
            else if (_mode == Intent.Save && info.Slot == _currentSlot)
            {
                facts.AddChild(UiTheme.Chip(Loc.T("slots.current"), UiTheme.Accent));
            }

            text.AddChild(facts);

            Label caption = UiTheme.Caption(Describe(info), loadable ? null : UiTheme.Bad);
            caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.AddChild(caption);
        }

        row.AddChild(text);
        row.AddChild(BuildActions(info));
        return rowPanel;
    }

    private static string KindKey(SaveKind kind) => kind switch
    {
        SaveKind.Quick => "slots.kind.quick",
        SaveKind.Auto => "slots.kind.auto",
        _ => "slots.kind.manual",
    };

    private static Control BuildThumbnail(string slot, bool filled)
    {
        var rect = new TextureRect
        {
            CustomMinimumSize = new Vector2(112, 63),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        };

        if (filled && SaveManager.Instance is { } manager)
        {
            string path = manager.ScreenshotPath(slot);
            if (FileAccess.FileExists(path) && Image.LoadFromFile(path) is { } image)
            {
                rect.Texture = ImageTexture.CreateFromImage(image);
            }
        }

        return rect;
    }

    /// <summary>A button in a slot row: a full control tall and wide enough that Load and Delete are
    /// equal targets rather than two different-sized words.</summary>
    private static Button RowAction(string text)
    {
        Button button = UiTheme.Action(text);
        button.CustomMinimumSize = new Vector2(ActionWidth, UiTheme.ControlHeight);
        return button;
    }

    private Control BuildActions(SaveSlotInfo info)
    {
        string slot = info.Slot;
        bool filled = info.Health != SaveHealth.Missing;
        var box = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        box.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        if (_pending != Pending.None && _pendingSlot == slot)
        {
            Button confirm = RowAction(Loc.T(ConfirmKey()));
            confirm.AddThemeColorOverride("font_color", UiTheme.Bad);
            confirm.Pressed += () => CommitPending(slot);
            box.AddChild(confirm);

            Button cancel = RowAction(Loc.T("common.cancel"));
            cancel.Pressed += () => { ClearPending(); RefreshList(); };
            box.AddChild(cancel);
            return box;
        }

        if (_mode == Intent.Load)
        {
            // A corrupt or newer-format save is shown so the player knows it is there; it is never
            // handed to a load, which would tear the title (or the running game) down to fail.
            Button load = RowAction(Loc.T("common.load"));
            load.Disabled = info.Health != SaveHealth.Ok;
            load.Pressed += () =>
            {
                if (string.IsNullOrEmpty(_loadWarning))
                {
                    Choose(slot);
                }
                else
                {
                    Ask(slot, Pending.Load);
                }
            };
            box.AddChild(load);
        }
        else
        {
            // Empty → act directly; filled → writing over a save needs a confirm. A save that cannot
            // be loaded counts as filled: it may be recoverable, and "Empty" would hide that it exists.
            string emptyKey = _mode == Intent.New ? "common.new_game" : "slots.save_here";
            Button write = RowAction(Loc.T(filled ? "common.overwrite" : emptyKey));
            write.Pressed += () =>
            {
                if (filled)
                {
                    Ask(slot, Pending.Overwrite);
                }
                else
                {
                    Choose(slot);
                }
            };
            box.AddChild(write);
        }

        if (filled)
        {
            Button delete = RowAction(Loc.T("common.delete"));
            delete.Pressed += () => Ask(slot, Pending.Delete);
            box.AddChild(delete);
        }

        return box;
    }

    private string ConfirmKey() => _pending switch
    {
        Pending.Delete => "common.confirm_delete",
        Pending.Load => "slots.confirm_load",
        _ => _mode == Intent.New ? "common.confirm_new" : "slots.confirm_overwrite",
    };

    private void Ask(string slot, Pending pending)
    {
        _pendingSlot = slot;
        _pending = pending;
        RefreshList();
    }

    private void ClearPending()
    {
        _pendingSlot = null;
        _pending = Pending.None;
    }

    private void CommitPending(string slot)
    {
        if (_pending == Pending.Delete)
        {
            // A file that would not go leaves the slot on disk; the re-inspection below then shows
            // it still there instead of an emptied row that comes back.
            if (SaveManager.Instance is { } saves && !saves.DeleteSlot(slot, out IReadOnlyList<string> failures) &&
                failures.Count > 0)
            {
                Embervale.Core.Events.EventBus.Instance?.Publish(new SaveNoticeEvent(Loc.T("slots.delete_failed"), Warning: true));
            }

            ClearPending();
            Inspect();
            RefreshList();
        }
        else
        {
            Choose(slot); // overwrite or load confirmed
        }
    }

    private void Choose(string slot)
    {
        Action<string>? chosen = _onChosen;
        QueueFree();
        chosen?.Invoke(slot);
    }

    /// <summary>The caption under a filled row: for a loadable save the two facts a player reads
    /// once rather than compares (how long they played and when they left; region, level and
    /// corruption tier moved onto the card itself in 37.5F), otherwise why it cannot be loaded.</summary>
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

        int total = (int)info.PlaytimeSeconds;
        string played = Loc.TF("slots.playtime", total / 3600, $"{(total % 3600) / 60:00}");
        string date = Time.GetDatetimeStringFromUnixTime((long)info.TimestampUnix, true);
        string meta = Loc.TF("slots.meta", played, date);
        return info.RecoveredFromBackup ? Loc.TF("slots.backup_help", meta) : meta;
    }
}
