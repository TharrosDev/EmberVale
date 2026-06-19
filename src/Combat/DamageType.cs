namespace Embervale.Combat;

/// <summary>
/// Schools of damage. Physical is mitigated by armor; the elemental/arcane types
/// align with the magic schools (Phase 12) and will be mitigated by per-type
/// resistances. <see cref="True"/> bypasses all mitigation.
/// </summary>
public enum DamageType
{
    Physical,
    Fire,
    Frost,
    Lightning,
    Arcane,
    Nature,
    Necrotic,
    True,
}
