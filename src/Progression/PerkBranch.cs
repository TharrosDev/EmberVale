namespace Embervale.Progression;

/// <summary>
/// The skill tree a perk sits in. Branches are a way to <em>group</em> perks and count the points
/// invested (tier gates read <see cref="PerksComponent.BranchPoints"/>); they are never a class lock.
/// </summary>
// APPEND ONLY: ordinals are authored into perk .tres files — never reorder/insert/remove
// (EnumStabilityTests).
public enum PerkBranch
{
    /// <summary>No branch: the six original perks, which sit outside the trees.</summary>
    None,
    Warrior,
    Archer,
    Mage,
    Rogue,
    Crafter,
    Social,
    Ashbound,
}
