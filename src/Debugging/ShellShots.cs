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
    }
}
