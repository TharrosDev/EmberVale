using Embervale.UI;

namespace Embervale.Debugging;

/// <summary>Rendered title-shell coverage: the authored main menu and its real settings flow.</summary>
public sealed partial class ShellShots : ShotHarness
{
    protected override string Flag => "--shellshots";

    protected override string OutputDir => "user://shellshots";

    public MainMenu? Menu { get; set; }

    protected override void BuildShotList()
    {
        Shot("00-main-menu", () => { });
        Shot("01-settings", () => Menu?.OpenSettingsForCapture());
    }
}
