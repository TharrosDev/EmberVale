using System;
using System.Text;

namespace Embervale.World;

/// <summary>Why one placement candidate was refused. Runtime-only (never saved or authored); the
/// ordinals index <see cref="SafePlacementReport"/>'s tally.</summary>
public enum PlacementRejection
{
    /// <summary>Accepted.</summary>
    None,

    /// <summary>The context node is not in the tree, so there is no world to ask.</summary>
    NotInTree,

    /// <summary>Navigation was required and the map has not synchronised yet.</summary>
    NavigationUnavailable,

    /// <summary>Navigation was required and the nearest navmesh point is beyond the correction.</summary>
    OffNavigation,

    /// <summary>No collision under the candidate within the vertical window — usually a cell whose
    /// collider is not resident yet.</summary>
    NoGround,

    /// <summary>Ground was found, but further from the candidate than the correction allows.</summary>
    TooFar,

    /// <summary>Ground was found, but steeper than a capsule can stand on.</summary>
    TooSteep,

    /// <summary>Ground was fine; the capsule standing on it overlaps something.</summary>
    Blocked,
}

/// <summary>
/// What a placement search tried and why each candidate failed — the diagnostic a caller logs when
/// <see cref="SafePlacementService"/> gives up.
///
/// ⚠️ <b>"PLACEMENT FAILED" IS NOT A DIAGNOSIS.</b> Forty candidates all refused for
/// <see cref="PlacementRejection.NoGround"/> is a streaming problem (collision is not resident yet —
/// wait); forty refused for <see cref="PlacementRejection.Blocked"/> is an authoring problem (the
/// landing is inside a building — move it). The two need opposite fixes and used to produce the same
/// silence. Pure, so the tally and its summary are unit-tested.
/// </summary>
public sealed class SafePlacementReport
{
    private readonly int[] _counts = new int[Enum.GetValues<PlacementRejection>().Length];

    /// <summary>Candidates tested, including the accepted one.</summary>
    public int Attempts { get; private set; }

    /// <summary>True once a candidate was accepted.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>0 for the desired point itself, 1..n for a search candidate; -1 while unresolved.</summary>
    public int AcceptedIndex { get; private set; } = -1;

    public void Record(PlacementRejection rejection)
    {
        if (!Succeeded && rejection == PlacementRejection.None)
        {
            Succeeded = true;
            AcceptedIndex = Attempts;
        }
        Attempts++;
        _counts[(int)rejection]++;
    }

    public int Count(PlacementRejection rejection) => _counts[(int)rejection];

    /// <summary>The refusal that happened most often, or <see cref="PlacementRejection.None"/>
    /// when nothing was refused.</summary>
    public PlacementRejection Dominant
    {
        get
        {
            PlacementRejection best = PlacementRejection.None;
            int bestCount = 0;
            for (int i = 1; i < _counts.Length; i++)
            {
                if (_counts[i] > bestCount)
                {
                    bestCount = _counts[i];
                    best = (PlacementRejection)i;
                }
            }
            return best;
        }
    }

    /// <summary>One line for a log: attempts, outcome, and the refusal counts that are non-zero.</summary>
    public string Summary()
    {
        var text = new StringBuilder();
        text.Append(Succeeded ? $"placed on candidate {AcceptedIndex}" : "no candidate accepted");
        text.Append($" after {Attempts} attempt(s)");
        bool first = true;
        for (int i = 1; i < _counts.Length; i++)
        {
            if (_counts[i] == 0)
            {
                continue;
            }
            text.Append(first ? "; refused: " : ", ");
            text.Append($"{(PlacementRejection)i}×{_counts[i]}");
            first = false;
        }
        return text.ToString();
    }
}
