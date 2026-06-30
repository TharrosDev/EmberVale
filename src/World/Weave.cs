using Godot;

namespace Embervale.World;

/// <summary>
/// The active region's live magic <b>potency</b> — the fading Weave dial (Phase 29.5E). One global value
/// (like <see cref="SafeZones"/>), populated from the active <see cref="RegionResource.WeavePotency"/> at
/// world build and on each region transition, and read by <see cref="Embervale.Magic.SpellcastingComponent"/>
/// to bend cast cost/power via <see cref="WeaveMath"/>. Dev-tunable live through the <c>weave</c> console
/// command.
///
/// Not separately saved: potency is authored region data, so loading a save and configuring its region
/// restores it. ponytail: a single ambient value per region; ley-site restoration is a content layer on
/// top (a `Set` call from an altar interactable) — not its own system.
/// </summary>
public static class Weave
{
    public const float DefaultPotency = 1f;

    private static float _potency = DefaultPotency;

    /// <summary>The active region's magic potency in [0,1] (1 = the Weave flows full).</summary>
    public static float Potency => _potency;

    public static void Set(float potency) => _potency = Mathf.Clamp(potency, 0f, 1f);

    public static void Reset() => _potency = DefaultPotency;

    /// <summary>Damage/healing multiplier for a cast here (corrupted casts strengthen as potency falls).</summary>
    public static float PowerMultiplier(bool corrupted) => WeaveMath.PowerMultiplier(_potency, corrupted);

    /// <summary>Mana-cost multiplier for a cast here (corrupted casts cheapen as potency falls).</summary>
    public static float CostMultiplier(bool corrupted) => WeaveMath.CostMultiplier(_potency, corrupted);
}
