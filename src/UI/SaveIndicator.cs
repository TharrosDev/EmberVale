using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The small "Saving..." chip in the bottom-right corner (ics save-ui). It never blocks input and
/// never pauses anything: it appears on <see cref="SaveStartedEvent"/> and leaves once the save has
/// ended and it has been up for <see cref="MinimumSeconds"/>, so a save that finishes inside one
/// frame is still seen rather than flickering past.
/// </summary>
public partial class SaveIndicator : Control
{
    private const float MinimumSeconds = 0.9f;

    private bool _saving;
    private float _remaining;

    public SaveIndicator()
    {
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);

        PanelContainer chip = UiTheme.Chip(Loc.T("save.indicator"), UiTheme.Dim);
        chip.MouseFilter = MouseFilterEnum.Ignore;
        chip.SetAnchorsPreset(LayoutPreset.BottomRight);
        chip.GrowHorizontal = GrowDirection.Begin;
        chip.GrowVertical = GrowDirection.Begin;
        chip.OffsetRight = -UiTheme.SpaceLg;
        chip.OffsetBottom = -UiTheme.SpaceLg;
        AddChild(chip);
    }

    public override void _EnterTree()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Subscribe<SaveStartedEvent>(OnStarted);
        bus?.Subscribe<GameSavedEvent>(OnSaved);
        bus?.Subscribe<SaveFailedEvent>(OnFailed);
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Unsubscribe<SaveStartedEvent>(OnStarted);
        bus?.Unsubscribe<GameSavedEvent>(OnSaved);
        bus?.Unsubscribe<SaveFailedEvent>(OnFailed);
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _remaining -= (float)delta;
        if (!_saving && _remaining <= 0f)
        {
            Visible = false;
        }
    }

    private void OnStarted(SaveStartedEvent e)
    {
        _saving = true;
        _remaining = MinimumSeconds;
        Visible = true;
    }

    private void OnSaved(GameSavedEvent e) => _saving = false;

    private void OnFailed(SaveFailedEvent e) => _saving = false;
}
