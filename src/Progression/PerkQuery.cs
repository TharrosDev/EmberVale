using Embervale.Entities;

namespace Embervale.Progression;

/// <summary>The one way gameplay reads a perk effect: a capped value, and 0 for an entity without perks
/// (every NPC), so a call site needs no branch of its own.</summary>
public static class PerkQuery
{
    /// <summary>The entity's capped total for <paramref name="kind"/>. <paramref name="arg"/> narrows to a
    /// qualifier such as a spell school; effects authored without one always count.</summary>
    public static float Of(IEntity? entity, PerkEffectKind kind, string? arg = null)
    {
        return entity?.GetComponent<PerksComponent>() is { } perks
            ? PerkEffectMath.Clamp(kind, perks.Effects.Get(kind, arg))
            : 0f;
    }
}
