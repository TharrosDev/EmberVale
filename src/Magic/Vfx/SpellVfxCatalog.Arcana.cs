using System.Collections.Generic;

namespace Embervale.Magic.Vfx;

/// <summary>The arcane, nature and necrotic recipes, and the enemy-only spells.</summary>
public static partial class SpellVfxCatalog
{
    /// <summary>Adds each of these spells' recipes to <paramref name="recipes"/>, keyed by spell id.
    /// Until a spell is listed here it draws its school's fallback.</summary>
    private static void RegisterArcana(Dictionary<string, SpellVfxRecipe> recipes)
    {
    }
}
