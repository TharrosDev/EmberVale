namespace Embervale.Quests;

/// <summary>
/// Pure objective-completion predicates behind <see cref="QuestProgress"/>. Kept Godot-free (plain ints)
/// so the "no stuck objectives" boundary — when a count satisfies its required total — is unit-testable
/// without authoring <see cref="QuestResource"/>/<see cref="ObjectiveResource"/> instances.
/// </summary>
public static class ObjectiveProgress
{
    /// <summary>An objective is met once its progress reaches its required total. A non-positive
    /// requirement is satisfied immediately, so such an objective can never get stuck.</summary>
    public static bool IsComplete(int count, int required) => count >= required;

    /// <summary>True when every objective's count meets its requirement. Mismatched lengths compare
    /// only the overlap (extra requirements count as unmet); an empty list is trivially met.</summary>
    public static bool AllMet(int[] counts, int[] required)
    {
        if (required.Length > counts.Length)
        {
            return false;
        }

        for (int i = 0; i < required.Length; i++)
        {
            if (!IsComplete(counts[i], required[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether objective <paramref name="index"/> is LIVE — countable, advanceable, and drawn (41D).
    ///
    /// Two independent reasons an objective can be inert, and they compose because they answer here
    /// together:
    /// <list type="number">
    /// <item><b>Its branch gate is shut</b> — <paramref name="gateOpen"/>[index] is false, i.e. the
    /// player is on the other path.</item>
    /// <item><b>It is locked behind an earlier step</b> — <paramref name="sequential"/> is set and
    /// some earlier objective is still unmet.</item>
    /// </list>
    ///
    /// ⚠️ <b>The sequential scan skips earlier objectives whose gate is shut</b>, and that single line
    /// is what lets ordering and branching be authored on one quest. Without it, an ordered quest
    /// whose second objective belongs to the path you did NOT take would block every objective after
    /// it forever — a quest that cannot be finished and cannot be seen to be stuck.
    ///
    /// Pure and Godot-free for this file's reason: the interesting behaviour is which combinations of
    /// gate and order produce a live objective, and that is arithmetic over three arrays.
    /// </summary>
    public static bool IsActive(int index, bool[] gateOpen, int[] counts, int[] required, bool sequential)
    {
        if (index < 0 || index >= required.Length || index >= counts.Length || index >= gateOpen.Length)
        {
            return false;
        }

        if (!gateOpen[index])
        {
            return false;
        }

        if (!sequential)
        {
            return true;
        }

        for (int i = 0; i < index; i++)
        {
            if (gateOpen[i] && !IsComplete(counts[i], required[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when every LIVE objective has met its requirement (41D) — the gate-aware counterpart to
    /// <see cref="AllMet"/>.
    ///
    /// ⚠️ <b>Zero live objectives is NOT completion, and that guard is the whole reason this is not a
    /// one-line filter.</b> A quest whose every objective belongs to a branch has nothing live until a
    /// flag picks a path, and "all zero of them are met" is trivially true — so without this the quest
    /// would complete, with rewards, on the frame it was accepted. It is the mirror of 41C's
    /// only-stealth-objectives trap: a set that begins in the finished position because it is empty.
    /// </summary>
    public static bool AllLiveMet(bool[] gateOpen, int[] counts, int[] required, bool sequential)
    {
        bool anyLive = false;
        for (int i = 0; i < required.Length; i++)
        {
            if (!gateOpen[i])
            {
                continue;
            }

            anyLive = true;
            if (!IsComplete(counts[i], required[i]))
            {
                return false;
            }
        }

        // `sequential` is deliberately unused here: locking only ever DELAYS an objective, never
        // excuses it, so an ordered quest is finished under exactly the same condition an unordered
        // one is. It is a parameter so callers do not have to know that.
        _ = sequential;
        return anyLive;
    }

    /// <summary>
    /// Advances a <see cref="ObjectiveType.Defend"/> hold by one poll tick (41B): adds
    /// <paramref name="delta"/> seconds to <paramref name="held"/> and returns the number of WHOLE
    /// seconds earned, leaving the sub-second remainder behind.
    ///
    /// Pure and Godot-free for this file's reason — the poll runs at 4 Hz while the objective is
    /// authored in seconds, so the interesting behaviour is that four quarter-ticks make exactly one
    /// second and never five or three. That is arithmetic, and arithmetic is testable.
    /// </summary>
    public static int TickHold(ref float held, float delta)
    {
        if (delta <= 0f)
        {
            return 0;
        }

        held += delta;
        int whole = (int)held;
        held -= whole;
        return whole;
    }
}
