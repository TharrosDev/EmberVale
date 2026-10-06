namespace Embervale.Magic.Vfx;

public static partial class SpellVfx
{
    /// <summary>
    /// The special cases of the fire, frost and lightning spells (the spells whose recipes live in
    /// <c>SpellVfxCatalog.Elemental.cs</c>). Empty until a spell needs more than its recipe; see
    /// <see cref="SpellVfxSpecial"/> for what a hook may do, and the header of <c>SpellVfx.cs</c>
    /// for when to reach for one.
    ///
    /// <code>
    /// table.For("spell.emberlash").Impact = static (in VfxCast cast, in SpellImpactInfo hit) =>
    /// {
    ///     // draw through cast.Fx; return true to replace the generic impact, false to add to it
    ///     return false;
    /// };
    /// </code>
    /// </summary>
    private static void RegisterElementalSpecials(SpellVfxSpecialTable table)
    {
    }
}
