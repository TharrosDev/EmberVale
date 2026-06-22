using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Godot;

namespace Embervale.Corruption;

/// <summary>
/// The LORE's defining mechanic: a 0–100 corruption meter on the player, raised by absorbing
/// fallen-Flamebearer power and dark choices. Its value maps to a <see cref="CorruptionTier"/>,
/// which later systems (appearance, dialogue gates, NPC dread, corrupted abilities, the endings
/// dial) read to decide consequences. Persists its value via <see cref="ISaveable"/>; crossing a
/// tier boundary fires a <see cref="CorruptionTierChangedEvent"/> on top of the per-point
/// <see cref="CorruptionChangedEvent"/>.
/// </summary>
[GlobalClass]
public partial class CorruptionComponent : EntityComponent, ISaveable
{
    private int _value;

    public string SaveId => SaveKey("corruption");

    /// <summary>Current corruption, in [<see cref="CorruptionTiers.Min"/>, <see cref="CorruptionTiers.Max"/>].</summary>
    public int Value => _value;

    /// <summary>The tier the current value falls into.</summary>
    public CorruptionTier Tier => CorruptionTiers.Of(_value);

    protected override void OnInitialize()
    {
        SaveManager.Instance?.Register(this);
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>Raises (or, with a negative delta, lowers) corruption, clamped and announced.</summary>
    public void Add(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        Apply(_value + delta);
    }

    /// <summary>Sets corruption to an absolute value, clamped and announced.</summary>
    public void Set(int value)
    {
        Apply(value);
    }

    /// <summary>Clamps to range, no-ops if unchanged, then publishes the change (and a tier
    /// event when the band crosses).</summary>
    private void Apply(int newValue)
    {
        int updated = Mathf.Clamp(newValue, CorruptionTiers.Min, CorruptionTiers.Max);
        if (updated == _value)
        {
            return;
        }

        CorruptionTier oldTier = CorruptionTiers.Of(_value);
        _value = updated;
        CorruptionTier newTier = CorruptionTiers.Of(_value);

        EventBus.Instance?.Publish(new CorruptionChangedEvent(_value, newTier));
        if (newTier != oldTier)
        {
            EventBus.Instance?.Publish(new CorruptionTierChangedEvent(oldTier, newTier));
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        return new Godot.Collections.Dictionary { ["value"] = _value };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (!data.TryGetValue("value", out Variant valueVar))
        {
            return;
        }

        _value = Mathf.Clamp(valueVar.AsInt32(), CorruptionTiers.Min, CorruptionTiers.Max);

        // Re-sync any already-subscribed consequence systems from a clean Untainted baseline.
        CorruptionTier tier = CorruptionTiers.Of(_value);
        EventBus.Instance?.Publish(new CorruptionChangedEvent(_value, tier));
        if (tier != CorruptionTier.Untainted)
        {
            EventBus.Instance?.Publish(new CorruptionTierChangedEvent(CorruptionTier.Untainted, tier));
        }
    }
}
