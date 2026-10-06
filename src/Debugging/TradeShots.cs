using Embervale.Core.Services;
using Embervale.Player;

namespace Embervale.Debugging;

/// <summary>
/// Rendered coverage of the trade screens: vendor, crafting, storage, appraisal and the contract
/// board, with their compare tooltips. One baseline frame of the loaded world until those land.
/// <c>godot --path . -- --tradeshots</c> continues the most recent save, like the other session harnesses.
/// Run WITHOUT <c>--headless</c>.
/// </summary>
public sealed partial class TradeShots : ShotHarness
{
    protected override string Flag => "--tradeshots";

    protected override string OutputDir => "user://tradeshots";

    protected override string? ValidateShotState(string name) =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter _)
            ? null
            : "player is not registered";

    protected override void BuildShotList()
    {
        Shot("00-world", () => { });
    }
}
