namespace Embervale.Companions;

/// <summary>
/// The standing order a companion is under — what it does when nothing is attacking. The player
/// changes this through the roster (<see cref="CompanionRoster.SetStance"/>); the quick command UI
/// that drives it from gameplay is Phase 32B.
/// </summary>
public enum CompanionStance
{
    /// <summary>Stay in formation on the player, moving as the player moves.</summary>
    Follow,

    /// <summary>Hold the spot the order was given at, guarding it instead of trailing the player.</summary>
    Hold,
}
