using Godot;

namespace Embervale.Items;

/// <summary>
/// An <see cref="ItemResource"/> that can be used from the backpack to apply an instant effect,
/// then is consumed (one removed from the stack). Kept to flat effect fields — like
/// <see cref="EquippableItemResource"/>'s flat bonuses — so the <c>.tres</c> stays authorable without
/// sub-resources. <see cref="InventoryComponent.Consume"/> applies it.
/// </summary>
[GlobalClass]
public partial class ConsumableItemResource : ItemResource
{
    /// <summary>Health restored on use. Zero for consumables with no healing component.</summary>
    [Export] public float HealAmount { get; set; }

    // --- Effect model (ics-base). Absent-defaults describe exactly the legacy potion: a Heal with
    // no magnitude of its own, no cooldown, which is why HealAmount above still drives it. ---

    /// <summary>What using it does. <see cref="ConsumableEffectKind.Heal"/> with a zero
    /// <see cref="Magnitude"/> is the legacy shape and heals for <see cref="HealAmount"/>.</summary>
    [ExportGroup("Effect")]
    [Export] public ConsumableEffectKind Effect { get; set; } = ConsumableEffectKind.Heal;

    /// <summary>The amount restored, or the modifier value of a <see cref="ConsumableEffectKind.Buff"/>.</summary>
    [Export] public float Magnitude { get; set; }

    /// <summary>How long a buff lasts; 0 for an instant effect. A restore with a duration is spread
    /// evenly over it.</summary>
    [Export] public float DurationSeconds { get; set; }

    /// <summary>The stat a <see cref="ConsumableEffectKind.Buff"/> modifies.</summary>
    [Export] public Stats.StatType BuffStat { get; set; } = Stats.StatType.Armor;

    /// <summary>How a <see cref="ConsumableEffectKind.Buff"/> applies its <see cref="Magnitude"/>.</summary>
    [Export] public Stats.ModifierType BuffKind { get; set; } = Stats.ModifierType.Flat;

    /// <summary>The <c>status.*</c> ids a <see cref="ConsumableEffectKind.Cure"/> removes; empty
    /// removes every harmful one.</summary>
    [Export] public Godot.Collections.Array<string> CureStatusIds { get; set; } = new();

    /// <summary>Seconds before another consumable of the same <see cref="CooldownGroup"/> can be
    /// used; 0 for none.</summary>
    [ExportGroup("Cooldown")]
    [Export] public float CooldownSeconds { get; set; }

    /// <summary>Consumables sharing a group share one cooldown (e.g. "potion", "food"); empty means
    /// the item cools down alone, keyed by its own id.</summary>
    [Export] public string CooldownGroup { get; set; } = "";

    /// <summary>The health a use restores: the magnitude of a Heal, or the legacy
    /// <see cref="HealAmount"/> when no magnitude is authored. 0 for every other effect.</summary>
    public float EffectiveHeal => Effect != ConsumableEffectKind.Heal ? 0f : (Magnitude > 0f ? Magnitude : HealAmount);

    /// <summary>The cooldown key: <see cref="CooldownGroup"/>, or the item id when ungrouped.</summary>
    public string CooldownKey => string.IsNullOrEmpty(CooldownGroup) ? Id : CooldownGroup;
}
