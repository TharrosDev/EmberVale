using Embervale.Combat;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// Presentation helpers for magic schools. A spell's school is simply its
/// <see cref="DamageType"/> (Fire/Frost/Lightning/Arcane/Nature/Necrotic), so the same value drives
/// mitigation in <see cref="CombatMath"/> — via <see cref="CombatMath.ResistanceStat"/>, which maps
/// it to the matching resistance since Phase 34E — and the colour used to tint projectiles and UI
/// here. Centralised so every school looks consistent.
/// </summary>
public static class SpellSchools
{
    /// <summary>A representative colour for a school, used for projectile/effect tinting.</summary>
    public static Color Color(DamageType school) => school switch
    {
        DamageType.Fire => new Color(1.0f, 0.45f, 0.12f),
        DamageType.Frost => new Color(0.45f, 0.78f, 1.0f),
        DamageType.Lightning => new Color(0.85f, 0.80f, 0.30f),
        DamageType.Arcane => new Color(0.70f, 0.40f, 0.95f),
        DamageType.Nature => new Color(0.40f, 0.85f, 0.45f),
        DamageType.Necrotic => new Color(0.55f, 0.30f, 0.55f),
        _ => new Color(0.85f, 0.85f, 0.85f),
    };
}
