using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The small "Saving..." chip in the bottom-right corner (ics save-ui). It never blocks input and
/// never pauses anything: it appears on <see cref="SaveStartedEvent"/> and leaves once the save has
/// ended and it has been up for <see cref="MinimumSeconds"/>, so a save that finishes inside one
/// frame is still seen rather than flickering past. A small HUD plate that fades in and out, and
/// ticks only while it is up: a save starting is the one thing that wakes it.
/// </summary>
public partial class SaveIndicator : Control
{
    private const float MinimumSeconds = 0.9f;

    private readonly PanelContainer _chip;
    private bool _saving;
    private float _remaining;

    public SaveIndicator()
    {
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _chip = UiTheme.HudPlate();
        _chip.Visible = false;
        _chip.AddChild(UiTheme.HudInk(UiTheme.Caption(Loc.T("save.indicator"), UiTheme.Text)));
        _chip.SetAnchorsPreset(LayoutPreset.BottomRight);
        _chip.GrowHorizontal = GrowDirection.Begin;
        _chip.GrowVertical = GrowDirection.Begin;
        _chip.OffsetRight = -UiTheme.SpaceLg;
        _chip.OffsetBottom = -UiTheme.SpaceLg;
        AddChild(_chip);
        SetProcess(false);
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
        _remaining -= (float)delta;
        if (!_saving && _remaining <= 0f)
        {
            UiFx.FadeOut(_chip, seconds: UiTheme.DurationBase);
            SetProcess(false);
        }
    }

    private void OnStarted(SaveStartedEvent e)
    {
        _saving = true;
        _remaining = MinimumSeconds;
        if (!IsProcessing())
        {
            UiFx.FadeIn(_chip, UiTheme.DurationFast);
            SetProcess(true);
        }
    }

    private void OnSaved(GameSavedEvent e) => _saving = false;

    private void OnFailed(SaveFailedEvent e) => _saving = false;
}
