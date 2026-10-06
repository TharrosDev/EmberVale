using Embervale.Core.Services;
using Embervale.Player;

namespace Embervale.Debugging;

/// <summary>
/// Rendered coverage of the session's meta screens: death, the creator's turntable and light rigs, a
/// save slot's hold-to-delete and the ending paintings. One baseline frame of the loaded world until
/// those land.
/// <c>godot --path . -- --metashots</c> continues the most recent save, like the other session harnesses.
/// Run WITHOUT <c>--headless</c>.
/// </summary>
public sealed partial class MetaShots : ShotHarness
{
    protected override string Flag => "--metashots";

    protected override string OutputDir => "user://metashots";

    protected override string? ValidateShotState(string name) =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter _)
            ? null
            : "player is not registered";

    protected override void BuildShotList()
    {
        Shot("00-world", () => { });
    }
}
