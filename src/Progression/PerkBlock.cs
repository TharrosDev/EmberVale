namespace Embervale.Progression;

/// <summary>Why a perk cannot be learned right now. Ordered by how the UI should explain it: the
/// structural reasons come before affordability, so a locked perk says "locked", not "poor".</summary>
public enum PerkBlock
{
    None,
    Maxed,
    Corruption,
    Prerequisite,
    BranchPoints,
    SkillPoints,
}
