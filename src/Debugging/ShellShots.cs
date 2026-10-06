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

    private CharacterCreator? _creator;

    protected override void BuildShotList()
    {
        Shot("00-main-menu", () => { });

        // The creator's two halves: the race picker on top, then the background picker with a
        // non-default background chosen so its kit, perk and lean badge are all on screen.
        Shot("01-creator-races", () => _creator = Menu?.OpenCreatorForCapture());

        // Appearance (P8): three races with different picks, so the swatches filter by race and the live
        // preview (the same PlayerAppearance.Apply as the world) visibly changes.
        Shot("01a-creator-look-human", () => _creator?.SelectLookForCapture("race.human",
            "appearance.skin.tan", "appearance.hair.blonde", "appearance.eyes.blue", "appearance.build.broad"));
        Shot("01b-creator-look-umbral", () => _creator?.SelectLookForCapture("race.umbral",
            "appearance.skin.ashen", "appearance.hair.midnight", "appearance.eyes.violet", "appearance.ember.violet", "appearance.build.slim"));
        Shot("01c-creator-look-draekyn", () => _creator?.SelectLookForCapture("race.draekyn",
            "appearance.skin.scarlet", "appearance.hair.cinder", "appearance.eyes.gold", "appearance.ember.gold"));
        Shot("01d-creator-look-sylthari", () => _creator?.SelectLookForCapture("race.sylthari",
            "appearance.skin.moss", "appearance.hair.violet", "appearance.eyes.ice", "appearance.build.slim"));

        Shot("02-creator-backgrounds-default", () =>
        {
            _creator?.SelectBackgroundForCapture("background.wayfarer");
            _creator?.ScrollForCapture(toEnd: false);
        });
        Shot("03-creator-backgrounds-hunter", () =>
        {
            _creator?.SelectBackgroundForCapture("background.hunter");
            _creator?.ScrollForCapture(toEnd: true);
        });

        Shot("04-settings", () =>
        {
            _creator?.QueueFree();
            Menu?.OpenSettingsForCapture();
        });
        Shot("05-settings-middle", () => ScrollSettings(0.5f));
        Shot("06-settings-end", () => ScrollSettings(1f));
        Shot("07-settings-closed", CloseSettings);
        Shot("08-save-slots-load", () => OpenSlots(SaveSlotPanel.Intent.Load));
        Shot("09-save-slots-closed", CloseSlots);
        Shot("10-save-slots-new", () => OpenSlots(SaveSlotPanel.Intent.New));
        Shot("11-save-slots-new-closed", CloseSlots);
        Shot("12-loading", ShowLoading);
        Shot("13-loading-closed", HideLoading);
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
