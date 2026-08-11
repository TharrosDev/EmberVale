using System.Collections.Generic;
using Godot;

namespace Embervale.UI;

/// <summary>One label competing for space on the plot. Lower <see cref="Rank"/> wins a collision.</summary>
public readonly record struct LabelCandidate(Rect2 Rect, int Rank, int Index);

/// <summary>
/// Decides which map labels actually get drawn (39.5C).
///
/// ⚠️ <b>This is NOT marker clustering, and the difference is the whole reason it exists.</b> The
/// deferred table said clustering lands when "two `Detail` markers overlap at `DetailZoom`", and
/// measured against the real world they do not: the closest pair in the game is 2.13 m apart, which
/// at 9 px/m is 19 px, and a Detail pin is 4 px across. **The markers are fine. Their labels are
/// not.** A name is 50–70 px wide and centred over its pin, so at Detail zoom the Ember Crown's town
/// hub drew the Vault, the Miner's Yard, the Anvil, the Booth, the Waystone, the Long Counter and
/// the Crafting Yard as one illegible pile of struck-through text. Clustering would have merged the
/// markers — the one thing that was working — and left the labels exactly as they were.
///
/// The rule is the standard cartographic one and it is deliberately greedy rather than optimal:
/// walk the candidates in priority order and keep a label only if its box hits nothing already kept.
/// A perfect solver is NP-hard, invisible at this scale, and would still have to pick something to
/// drop.
///
/// Kept engine-free (Godot structs only, no <c>GodotObject</c>) so it is unit testable headlessly,
/// like <see cref="MapProjection"/>, <see cref="MinimapFilter"/> and <see cref="CompassMath"/>.
/// </summary>
public static class LabelPlacer
{
    /// <summary>Padding added around every label box before testing overlap, so two labels that merely
    /// touch still read as two labels rather than one run-on word.</summary>
    private const float Breathing = 2f;

    /// <summary>
    /// The indices of the candidates that should be drawn.
    ///
    /// <paramref name="candidates"/> are considered in ascending <see cref="LabelCandidate.Rank"/>,
    /// ties broken by <see cref="LabelCandidate.Index"/> so the result is stable frame to frame — a
    /// label that flickers in and out as the sort order churns is worse than one that is never drawn.
    ///
    /// A candidate is dropped when its box overlaps a kept one, or when it would spill outside
    /// <paramref name="bounds"/>. ⚠️ **The bounds test matters as much as the overlap test**: the plot
    /// clips its contents, so a name running past the right edge is not omitted, it is *sliced in
    /// half* — and "The Fact" where "The Factor's Rest" should be reads as corrupted data rather than
    /// as a label that did not fit.
    /// </summary>
    public static List<int> Place(IReadOnlyList<LabelCandidate> candidates, Rect2 bounds)
    {
        var order = new List<LabelCandidate>(candidates);
        order.Sort((a, b) =>
        {
            int byRank = a.Rank.CompareTo(b.Rank);
            return byRank != 0 ? byRank : a.Index.CompareTo(b.Index);
        });

        var kept = new List<Rect2>();
        var result = new List<int>();

        foreach (LabelCandidate candidate in order)
        {
            Rect2 box = candidate.Rect.Grow(Breathing);
            if (!bounds.Encloses(candidate.Rect))
            {
                continue;
            }

            bool collides = false;
            foreach (Rect2 taken in kept)
            {
                if (taken.Intersects(box))
                {
                    collides = true;
                    break;
                }
            }

            if (collides)
            {
                continue;
            }

            kept.Add(box);
            result.Add(candidate.Index);
        }

        result.Sort();
        return result;
    }
}
