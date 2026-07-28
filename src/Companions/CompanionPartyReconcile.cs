using System.Collections.Generic;

namespace Embervale.Companions;

/// <summary>What the roster must do to make the live party match a loaded save.</summary>
/// <param name="Dismiss">Companions in the world that the save does not have.</param>
/// <param name="Recruit">Companions the save has that are not in the world.</param>
/// <param name="Keep">Companions present on both sides — repositioned, not rebuilt.</param>
public sealed record CompanionReconcilePlan(
    IReadOnlyList<string> Dismiss,
    IReadOnlyList<string> Recruit,
    IReadOnlyList<string> Keep);

/// <summary>
/// The pure set-difference behind loading a party (Phase 32D). Loading is not "spawn what the save
/// says" — the world may already hold companions (a mid-session load, a reload after a region
/// change), and rebuilding a companion that is already standing there would drop its live component
/// state and re-run its recruit announcement. So a load is a <em>reconcile</em>: dismiss the extras,
/// recruit the missing, and move the ones that survive.
///
/// It mirrors what <see cref="Save.PersistentSpawnDirector"/> does for placed actors, extracted to a
/// Godot-free function so the round-trip rule is unit-tested rather than inferred from a save file.
/// </summary>
public static class CompanionPartyReconcile
{
    public static CompanionReconcilePlan Plan(IEnumerable<string> live, IEnumerable<string> desired)
    {
        var liveSet = new HashSet<string>(live ?? System.Array.Empty<string>());
        var desiredSet = new HashSet<string>(desired ?? System.Array.Empty<string>());

        var dismiss = new List<string>();
        var keep = new List<string>();
        foreach (string id in liveSet)
        {
            if (desiredSet.Contains(id))
            {
                keep.Add(id);
            }
            else
            {
                dismiss.Add(id);
            }
        }

        var recruit = new List<string>();
        foreach (string id in desiredSet)
        {
            if (!liveSet.Contains(id))
            {
                recruit.Add(id);
            }
        }

        // Stable ordering keeps a load deterministic (and the formation slots reproducible) rather
        // than depending on hash iteration order.
        dismiss.Sort(System.StringComparer.Ordinal);
        recruit.Sort(System.StringComparer.Ordinal);
        keep.Sort(System.StringComparer.Ordinal);
        return new CompanionReconcilePlan(dismiss, recruit, keep);
    }
}
