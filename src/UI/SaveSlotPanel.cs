using System;
using Embervale.Localization;
using Embervale.Save;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The save-slot browser (Phase 24C): a modal list of slots the player picks from to start a new
/// game or load an existing one, with each filled slot showing its header metadata (region, level,
/// corruption tier, playtime, date) and a screenshot thumbnail. Opened by the <see cref="MainMenu"/>
/// in one of two <see cref="Intent"/>s; deletes a slot (with an inline confirm) via
/// <see cref="SaveManager.DeleteSlot"/>. Built in code through <see cref="UiTheme"/>.
/// </summary>
public partial class SaveSlotPanel : CanvasLayer
{
    public enum Intent
    {
        /// <summary>Choosing a slot to start a fresh game (empty slots act; filled ask to overwrite).</summary>
        New,

        /// <summary>Choosing an existing save to load (empty slots are inert).</summary>
        Load,
    }

    // A small fixed roster of manual slots; the reserved "quick" slot (F5) lives outside it.
    private static readonly string[] Roster = { "slot1", "slot2", "slot3" };

    private Intent _mode;
    private Action<string>? _onChosen;
    private Action? _onBack;
    private string? _pendingActionSlot; // slot awaiting a second "confirm" click (overwrite/delete)
    private bool _pendingIsDelete;

    private VBoxContainer _list = null!;

    /// <summary>Smallest height the slot list scrolls in; the workspace frame gives it the rest.</summary>
    private const float ScrollMinHeight = 160f;

    private const float ActionWidth = 96f;

    public void Configure(Intent mode, Action<string> onChosen, Action onBack)
    {
        _mode = mode;
        _onChosen = onChosen;
        _onBack = onBack;
    }

    public override void _Ready()
    {
        Layer = 12; // above the main menu
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

        Label header = UiTheme.Header(Loc.T(_mode == Intent.New ? "slots.new_title" : "slots.load_title"));
        col.AddChild(header);
        col.AddChild(UiTheme.Divider());

        // The list scrolls inside the frame: with three manual slots and the autosave ring it is taller
        // than a 720 px window, and an unscrolled list pushed the whole panel off both edges.
        (ScrollContainer scroll, _list) = UiTheme.ScrollList();
        scroll.CustomMinimumSize = new Vector2(0f, ScrollMinHeight);
        col.AddChild(scroll);

        col.AddChild(UiTheme.Divider());
        Button back = UiTheme.Action(Loc.T("common.back"));
        back.Pressed += () => { _onBack?.Invoke(); QueueFree(); };
        col.AddChild(back);

        RefreshList();
    }

    public override void _Process(double delta)
    {
        // Esc / gamepad B backs out (30.5J), matching the settings panel.
        if (Godot.Input.IsActionJustPressed("ui_cancel"))
        {
            _onBack?.Invoke();
            QueueFree();
        }
    }

    private void RefreshList()
    {
        foreach (Node child in _list.GetChildren())
        {
            child.QueueFree();
        }

        for (int i = 0; i < Roster.Length; i++)
        {
            string slot = Roster[i];
            SaveSlotInfo? info = SaveManager.Instance?.ReadHeader(slot);
            _list.AddChild(BuildRow(slot, Loc.TF("slots.slot", i + 1), info));
        }

        // Phase 24D: in Load mode, surface existing autosaves as read-only rows (Load + Delete, no
        // Overwrite — a New game can never clobber them since they're absent from the New roster).
        if (_mode == Intent.Load && SaveManager.Instance is { } manager)
        {
            for (int i = 0; i < AutosaveService.RingSlots.Length; i++)
            {
                string slot = AutosaveService.RingSlots[i];
                if (manager.ReadHeader(slot) is { } autoInfo)
                {
                    _list.AddChild(BuildRow(slot, Loc.TF("slots.autosave", i + 1), autoInfo));
                }
            }
        }

        // Refresh frees the row the gamepad/keyboard focus sat on (confirm/delete flows) —
        // re-land on the first row once the new tree exists (30.5J).
        Callable.From(() => UiFocus.GrabFirst(_list)).CallDeferred();
    }

    private Control BuildRow(string slot, string label, SaveSlotInfo? info)
    {
        // A Card, not a Panel (37.5F). Every save row had been a full framed panel, so after 37.5A
        // a list of six slots was six brass frames and six grain shaders stacked vertically - the
        // frames competed with each other and with the panel actually containing them.
        //
        // The spine carries corruption: a save's tier is the one thing about it that is a *state*
        // rather than a statistic, and it is what a returning player is orienting on.
        bool corrupted = info != null && !string.Equals(info.CorruptionTier, "Untainted", System.StringComparison.OrdinalIgnoreCase);
        Color spine = info == null ? UiTheme.Disabled
            : corrupted ? UiTheme.CorruptionText
            : UiTheme.Accent;

        PanelContainer rowPanel = UiTheme.Compact(UiTheme.Card(spine));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        rowPanel.AddChild(row);

        row.AddChild(BuildThumbnail(slot, info != null));

        var text = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        text.AddThemeConstantOverride("separation", UiTheme.LineGap);

        Label title = UiTheme.Body(label, info == null ? UiTheme.Disabled : UiTheme.Text);
        UiTheme.ApplyType(title, UiTheme.FontRole.Display, UiTheme.HeaderFontSize);
        text.AddChild(title);

        if (info == null)
        {
            text.AddChild(UiTheme.Body(Loc.T("slots.empty"), UiTheme.Disabled));
        }
        else
        {
            // Structured rather than one crammed line: the region names the place, the chips carry
            // the two facts a player compares between slots, and the caption carries the two they
            // read once. The old single string put all five at the same weight.
            // Region and chips share a line (and wrap if the column is narrow), which keeps a row to
            // three lines: a four-line row made six slots a screen and a half tall.
            var facts = new HFlowContainer();
            facts.AddThemeConstantOverride("h_separation", UiTheme.ChipGap);
            facts.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
            Label region = UiTheme.Body(info.Region, UiTheme.Accent);
            region.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            facts.AddChild(region);
            facts.AddChild(UiTheme.Chip(Loc.TF("slots.level", info.Level), UiTheme.Text));
            facts.AddChild(UiTheme.Chip(info.CorruptionTier, corrupted ? UiTheme.CorruptionText : UiTheme.Dim));
            text.AddChild(facts);

            text.AddChild(UiTheme.Caption(DescribeSave(info)));
        }

        row.AddChild(text);
        row.AddChild(BuildActions(slot, info != null));
        return rowPanel;
    }

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

    private Control BuildActions(string slot, bool filled)
    {
        var box = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        box.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        bool awaitingThisSlot = _pendingActionSlot == slot;

        if (awaitingThisSlot)
        {
            Button confirm = RowAction(Loc.T(_pendingIsDelete ? "common.confirm_delete" : "common.confirm_new"));
            confirm.AddThemeColorOverride("font_color", UiTheme.Bad);
            confirm.Pressed += () => CommitPending(slot);
            box.AddChild(confirm);

            Button cancel = RowAction(Loc.T("common.cancel"));
            cancel.Pressed += () => { _pendingActionSlot = null; RefreshList(); };
            box.AddChild(cancel);
            return box;
        }

        if (_mode == Intent.New)
        {
            // Empty → start directly; filled → overwriting an existing save needs a confirm.
            Button start = RowAction(Loc.T(filled ? "common.overwrite" : "common.new_game"));
            start.Pressed += () =>
            {
                if (filled)
                {
                    _pendingActionSlot = slot;
                    _pendingIsDelete = false;
                    RefreshList();
                }
                else
                {
                    Choose(slot);
                }
            };
            box.AddChild(start);
        }
        else
        {
            Button load = RowAction(Loc.T("common.load"));
            load.Disabled = !filled;
            load.Pressed += () => Choose(slot);
            box.AddChild(load);
        }

        if (filled)
        {
            Button delete = RowAction(Loc.T("common.delete"));
            delete.Pressed += () =>
            {
                _pendingActionSlot = slot;
                _pendingIsDelete = true;
                RefreshList();
            };
            box.AddChild(delete);
        }

        return box;
    }

    private void CommitPending(string slot)
    {
        if (_pendingIsDelete)
        {
            SaveManager.Instance?.DeleteSlot(slot);
            _pendingActionSlot = null;
            RefreshList();
        }
        else
        {
            Choose(slot); // overwrite confirmed → start a new game into the slot
        }
    }

    private void Choose(string slot)
    {
        Action<string>? chosen = _onChosen;
        QueueFree();
        chosen?.Invoke(slot);
    }

    /// <summary>The two facts a player reads once rather than compares: how long they played and
    /// when they left. Region, level and corruption tier moved onto the card itself in 37.5F.</summary>
    private static string DescribeSave(SaveSlotInfo info)
    {
        int total = (int)info.PlaytimeSeconds;
        string played = Loc.TF("slots.playtime", total / 3600, $"{(total % 3600) / 60:00}");
        string date = Time.GetDatetimeStringFromUnixTime((long)info.TimestampUnix, true);
        return Loc.TF("slots.meta", played, date);
    }
}
