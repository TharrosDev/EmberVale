namespace Embervale.Housing;

/// <summary>Why a property's storage can or cannot be opened right now.</summary>
public enum StorageOutcome
{
    /// <summary>The component names a property id nothing in the database answers to.</summary>
    UnknownProperty,

    /// <summary>A real holding, but not one the player has claimed.</summary>
    NotOwned,

    /// <summary>Openable now.</summary>
    Open,
}

/// <summary>
/// Whether a holding's storage will open, and if not, which reason to say out loud (Phase 37B). The
/// pure sibling of <see cref="PropertyClaim"/>, and pure for the same reason: the prompt and the
/// interaction both read this one function, so what the player is told and what happens cannot
/// drift apart.
///
/// The order is deliberate. An unresolvable id is an <b>authoring</b> fault, not a gate the player
/// can pass — reporting it as "not yours" would send someone off to buy a property that does not
/// exist, and would hide a typo behind a plausible-looking refusal.
/// </summary>
public static class PropertyStorage
{
    /// <summary>
    /// Resolves an attempt to open a property's storage. <paramref name="propertyKnown"/> is
    /// <c>false</c> when the authored id does not resolve against <see cref="PropertyDatabase"/>.
    /// </summary>
    public static StorageOutcome Resolve(bool propertyKnown, bool owned)
    {
        if (!propertyKnown)
        {
            return StorageOutcome.UnknownProperty;
        }

        return owned ? StorageOutcome.Open : StorageOutcome.NotOwned;
    }
}
