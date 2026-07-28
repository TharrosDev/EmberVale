namespace Embervale.Onboarding;

/// <summary>
/// The verbs the onboarding teaches, in the order a new player meets them. Each is taught by doing:
/// a hint appears, the player performs the action, the hint clears itself.
/// </summary>
// APPEND ONLY: ordinals persist in saves — never reorder/insert/remove (EnumStabilityTests).
public enum TutorialStep
{
    /// <summary>Nothing to teach — either finished or switched off.</summary>
    None,

    /// <summary>Look around with the mouse.</summary>
    Look,

    /// <summary>Walk with the movement keys.</summary>
    Move,

    /// <summary>Sprint while moving.</summary>
    Sprint,

    /// <summary>Swing the weapon.</summary>
    Attack,

    /// <summary>Raise the guard.</summary>
    Block,

    /// <summary>Dodge roll.</summary>
    Dodge,

    /// <summary>Talk to someone / use something in the world.</summary>
    Interact,

    /// <summary>Open the character screen.</summary>
    Inventory,

    /// <summary>Open the quest journal.</summary>
    Journal,

    /// <summary>Cast the prepared spell.</summary>
    Cast,
}
