using Embervale.UI;

namespace Embervale.Debugging;

/// <summary>Rendered title-shell coverage: the authored main menu and its real settings flow.</summary>
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
    }
}
