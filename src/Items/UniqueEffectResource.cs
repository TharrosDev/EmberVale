using Godot;

namespace Embervale.Items;

/// <summary>
/// One named special property: the thing a legendary does that no affix can. Authored as a
/// <c>.tres</c> under <c>data/unique_effects/</c>, indexed by <see cref="UniqueEffectDatabase"/>, and
/// pointed at by <see cref="ItemResource.UniqueEffectId"/> or an
/// <see cref="ItemSetBonusResource.EffectId"/>. The behaviour is the <see cref="Kind"/>; the fields
/// below are its numbers, read as <see cref="UniqueEffectKind"/> documents per member.
/// </summary>
[GlobalClass]
public partial class UniqueEffectResource : Resource
{
    /// <summary>Stable id, e.g. "unique.kingsbane". The database key.</summary>
    [Export] public string Id { get; set; } = "unique.unknown";

    /// <summary>Locale key of the effect's name, conventionally <c>&lt;id&gt;.name</c>.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    /// <summary>Locale key of the tooltip line, conventionally <c>&lt;id&gt;.desc</c>. Placeholders:
    /// {0} magnitude, {1} chance, {2} threshold, {3} duration, {4} cooldown.</summary>
    [Export] public string DescriptionKey { get; set; } = string.Empty;

    [Export] public UniqueEffectKind Kind { get; set; } = UniqueEffectKind.OnHitStatus;

    /// <summary>The effect's main number; a fraction (0..1) or a flat amount, per the kind.</summary>
    [Export] public float Magnitude { get; set; }

    /// <summary>Trigger probability, 0..1. Kinds that always trigger ignore it.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Chance { get; set; } = 1f;

    /// <summary>A health fraction, 0..1, for the kinds gated on one.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Threshold { get; set; }

    [Export] public float DurationSeconds { get; set; }

    /// <summary>Minimum seconds between triggers; 0 for none.</summary>
    [Export] public float CooldownSeconds { get; set; }

    /// <summary>The <c>status.*</c> id applied by <see cref="UniqueEffectKind.OnHitStatus"/>.</summary>
    [Export] public string StatusId { get; set; } = string.Empty;
}
