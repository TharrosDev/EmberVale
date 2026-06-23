using System;
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
        var backdrop = new ColorRect { Color = new Color(0.02f, 0.02f, 0.04f, 0.92f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(backdrop);

        PanelContainer panel = UiTheme.Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(540, 0);
        AddChild(panel);

        MarginContainer pad = UiTheme.Padding(18);
        panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        pad.AddChild(col);

        Label header = UiTheme.Header(_mode == Intent.New ? "NEW GAME — CHOOSE A SLOT" : "LOAD GAME");
        col.AddChild(header);
        col.AddChild(new HSeparator());

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 8);
        col.AddChild(_list);

        col.AddChild(new HSeparator());
        Button back = UiTheme.Action("Back");
        back.CustomMinimumSize = new Vector2(0, 32);
        back.Pressed += () => { _onBack?.Invoke(); QueueFree(); };
        col.AddChild(back);

        RefreshList();
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
            _list.AddChild(BuildRow(slot, $"Slot {i + 1}", info));
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
                    _list.AddChild(BuildRow(slot, $"Autosave {i + 1}", autoInfo));
                }
            }
        }
    }

    private Control BuildRow(string slot, string label, SaveSlotInfo? info)
    {
        PanelContainer rowPanel = UiTheme.Panel();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        MarginContainer rowPad = UiTheme.Padding(8);
        rowPanel.AddChild(rowPad);
        rowPad.AddChild(row);

        row.AddChild(BuildThumbnail(slot, info != null));

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddChild(UiTheme.Header(label));
        text.AddChild(UiTheme.Body(info != null ? DescribeSave(info) : "— Empty —",
            info != null ? UiTheme.Text : UiTheme.Dim));
        row.AddChild(text);

        row.AddChild(BuildActions(slot, info != null));
        return rowPanel;
    }

    private static Control BuildThumbnail(string slot, bool filled)
    {
        var rect = new TextureRect
        {
            CustomMinimumSize = new Vector2(96, 54),
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

    private Control BuildActions(string slot, bool filled)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 6);

        bool awaitingThisSlot = _pendingActionSlot == slot;

        if (awaitingThisSlot)
        {
            Button confirm = UiTheme.Action(_pendingIsDelete ? "Confirm delete" : "Confirm new");
            confirm.AddThemeColorOverride("font_color", UiTheme.Bad);
            confirm.Pressed += () => CommitPending(slot);
            box.AddChild(confirm);

            Button cancel = UiTheme.Action("Cancel");
            cancel.Pressed += () => { _pendingActionSlot = null; RefreshList(); };
            box.AddChild(cancel);
            return box;
        }

        if (_mode == Intent.New)
        {
            // Empty → start directly; filled → overwriting an existing save needs a confirm.
            Button start = UiTheme.Action(filled ? "Overwrite" : "New Game");
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
            Button load = UiTheme.Action("Load");
            load.Disabled = !filled;
            load.Pressed += () => Choose(slot);
            box.AddChild(load);
        }

        if (filled)
        {
            Button delete = UiTheme.Action("Delete");
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

    private static string DescribeSave(SaveSlotInfo info)
    {
        int total = (int)info.PlaytimeSeconds;
        string played = $"{total / 3600}h {(total % 3600) / 60:00}m";
        string date = Time.GetDatetimeStringFromUnixTime((long)info.TimestampUnix, true);
        return $"{info.Region} · Lv {info.Level} · {info.CorruptionTier} · {played} · {date}";
    }
}
