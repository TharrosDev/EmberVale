namespace Embervale.Magic.Vfx;

public static partial class SpellVfx
{
    /// <summary>
    /// The special cases of the arcane, nature and necrotic spells and of the enemy-only spells (the
    /// spells whose recipes live in <c>SpellVfxCatalog.Arcana.cs</c>), and of the spell combos.
    /// Empty until a spell needs more than its recipe; see <see cref="SpellVfxSpecial"/> for what a
    /// hook may do, and the header of <c>SpellVfx.cs</c> for when to reach for one.
    /// </summary>
    private static void RegisterArcanaSpecials(SpellVfxSpecialTable table)
    {
    }
}
