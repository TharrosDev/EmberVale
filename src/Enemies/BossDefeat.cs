namespace Embervale.Enemies;

/// <summary>
/// What a boss's death should actually do (Phase 36E), kept pure so the one decision that has
/// already gone wrong once is pinned by tests rather than by reading.
///
/// <b>The bug this replaces.</b> The director granted rewards behind an "already defeated?" guard but
/// queued the defeat dialogue <em>unconditionally</em>, for any <see cref="BossEntity"/> death. Once
/// the Iron King was down, killing any dragon re-opened his "absorb the flame?" choice — which
/// carries +25 corruption and no condition of its own — so the game's defining meter could be topped
/// up once per boss kill, for as long as there were bosses.
/// </summary>
public static class BossDefeat
{
    /// <summary>What one boss death is worth. All three parts move together on purpose: a reward
    /// that can be taken twice and a flag that records it taking once are the same defect.</summary>
    public readonly record struct Outcome(bool GrantReward, bool SetFlag, bool OpenDialogue)
    {
        /// <summary>A death that has already paid out — the beat still plays, nothing is granted.</summary>
        public static Outcome None => new(false, false, false);
    }

    /// <summary>
    /// Resolves a defeat. <paramref name="alreadyDefeated"/> is whether this boss's
    /// <c>DefeatFlagId</c> is already set on the player; the two ids are the boss's authored reward
    /// and defeat conversation, either of which may be empty for a boss that grants neither.
    ///
    /// Everything is gated on the same first-time check, which is the fix: the dialogue is part of
    /// the reward, not a cosmetic that plays whenever something large dies.
    /// </summary>
    public static Outcome Resolve(bool alreadyDefeated, string? rewardItemId, string? dialogueId)
    {
        if (alreadyDefeated)
        {
            return Outcome.None;
        }

        return new Outcome(
            GrantReward: !string.IsNullOrEmpty(rewardItemId),
            SetFlag: true,
            OpenDialogue: !string.IsNullOrEmpty(dialogueId));
    }
}
