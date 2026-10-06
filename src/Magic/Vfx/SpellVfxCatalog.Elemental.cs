using System.Collections.Generic;

namespace Embervale.Magic.Vfx;

/// <summary>The fire, frost and lightning recipes.</summary>
public static partial class SpellVfxCatalog
{
    /// <summary>Adds each elemental spell's recipe to <paramref name="recipes"/>, keyed by spell id.
    /// Until a spell is listed here it draws its school's fallback.</summary>
    private static void RegisterElemental(Dictionary<string, SpellVfxRecipe> recipes)
    {
    }
}
