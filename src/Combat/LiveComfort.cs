using Embervale.Core.Services;
using Embervale.Settings;

namespace Embervale.Combat;

/// <summary>
/// The live <see cref="CombatComfort"/>: the current settings run through the contract, or
/// <see cref="CombatComfort.Full"/> with no settings service. Every hit-stop, flash, number and
/// lock-on helper reads it here, so the sliders and Reduced Motion reach all of them the same way.
/// </summary>
public static class LiveComfort
{
    public static CombatComfort Get() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? CombatComfort.From(settings.Current)
            : CombatComfort.Full;
}
