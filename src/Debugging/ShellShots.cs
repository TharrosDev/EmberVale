using Embervale.Core;
using Embervale.Core.Events;
using Embervale.UI;
using Godot;

namespace Embervale.Debugging;

/// <summary>Rendered title-shell coverage: the authored main menu, its real settings flow, the save-slot
/// browser in both intents and the loading screen.</summary>
public sealed partial class ShellShots : ShotHarness
{
    protected override string Flag => "--shellshots";

    protected override string OutputDir => "user://shellshots";

    public MainMenu? Menu { get; set; }

    protected override void BuildShotList()
    {
        Shot("00-main-menu", () => { });
        Shot("01-settings", () => Menu?.OpenSettingsForCapture());
        Shot("02-settings-middle", () => ScrollSettings(0.5f));
        Shot("03-settings-end", () => ScrollSettings(1f));
        Shot("04-settings-closed", CloseSettings);
        Shot("05-save-slots-load", () => OpenSlots(SaveSlotPanel.Intent.Load));
        Shot("06-save-slots-closed", CloseSlots);
        Shot("07-save-slots-new", () => OpenSlots(SaveSlotPanel.Intent.New));
        Shot("08-save-slots-new-closed", CloseSlots);
        Shot("09-loading", ShowLoading);
        Shot("10-loading-closed", HideLoading);
    }

    private void ScrollSettings(float fraction)
    {
        if (QuestShotFixtures.FindFirst<SettingsPanel>(GetTree().Root) is { } settings &&
            QuestShotFixtures.FindFirst<ScrollContainer>(settings) is { } scroll)
        {
            scroll.ScrollVertical = (int)(scroll.GetVScrollBar().MaxValue * fraction);
        }
    }

    private void CloseSettings()
    {
        QuestShotFixtures.FindFirst<SettingsPanel>(GetTree().Root)?.QueueFree();
        if (Menu is not null)
        {
            Menu.Visible = true;
        }
    }

    private void OpenSlots(SaveSlotPanel.Intent intent)
    {
        var slots = new SaveSlotPanel { Name = "AuditSaveSlots" };
        slots.Configure(intent, _ => { }, () => { });
        GetTree().Root.AddChild(slots);
    }

    private void CloseSlots() => GetTree().Root.GetNodeOrNull("AuditSaveSlots")?.QueueFree();

    private void ShowLoading()
    {
        GetTree().Root.AddChild(new LoadingScreen { Name = "AuditLoading" });
        EventBus.Instance?.Publish(new GameStateChangedEvent(GameState.MainMenu, GameState.Loading));
    }

    private void HideLoading() => GetTree().Root.GetNodeOrNull("AuditLoading")?.QueueFree();
}
