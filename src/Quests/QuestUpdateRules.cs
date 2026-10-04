namespace Embervale.Quests;

/// <summary>
/// Pure rules behind the journal's "updated" dot. A quest's stage signature is a small deterministic
/// hash of its status and each objective's stage; the log remembers the signature the player last
/// looked at, and a quest is UPDATED while its current signature differs from the remembered one.
/// </summary>
public static class QuestUpdateRules
{
    public const int Inert = 0;
    public const int Active = 1;
    public const int Complete = 2;

    /// <summary>Stage of one objective: complete wins over active, an inert (gated off or locked)
    /// objective is neither.</summary>
    public static int Stage(bool active, bool complete) => complete ? Complete : active ? Active : Inert;

    /// <summary>Deterministic signature of a quest's visible stage. Stable across runs and saves
    /// (no string hashing, no platform-dependent hash codes).</summary>
    public static int Signature(QuestStatus status, int[] stages)
    {
        unchecked
        {
            int h = ((int)status + 1) * 7919;
            foreach (int s in stages)
            {
                h = (h * 31) + s + 1;
            }

            return h;
        }
    }

    /// <summary>True for an ACTIVE quest whose signature is not the one last seen. A quest with no
    /// remembered signature (<paramref name="seen"/> is null) counts as updated, so a brand-new quest
    /// shows the dot until it is opened. Finished quests never show one.</summary>
    public static bool IsUpdated(int? seen, int current, QuestStatus status) =>
        status == QuestStatus.Active && seen != current;
}
