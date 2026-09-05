using Embervale.Combat.Actions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Which blow an AI reaches for — the division of labour §19 is about.
///
/// The AI knows it wants to hit something and how far away it is. It does not know what a wind-up
/// is, when a hitbox opens, or how long any of it takes. This is the whole of the part it owns, and
/// it is pure so the range bands can be pinned without an engine — which matters because an enemy
/// that picks an out-of-range attack does not throw an error, it swings at air.
/// </summary>
public class ActionSelectionTests
{
    private static ActionSelection.Candidate Action(
        string id, float min, float max, float weight = 1f) => new(min, max, weight);

    private static readonly ActionSelection.Candidate[] Dragon =
    {
        Action("bite", 0f, 4f),
        Action("wing", 0f, 5f),
        Action("tail", 0f, 7f),
    };

    [Fact]
    public void NothingIsChosenWhenNothingReaches() =>
        Assert.Equal(-1, ActionSelection.Choose(Dragon, distance: 20f, roll: 0.5f));

    [Fact]
    public void AnEmptyChainChoosesNothing() =>
        Assert.Equal(-1, ActionSelection.Choose(System.Array.Empty<ActionSelection.Candidate>(), 1f, 0.5f));

    [Fact]
    public void OnlyTheActionsThatReachAreCandidates()
    {
        // At 6 m only the tail reaches, so every roll must produce it. A selection that ignored
        // range would have the dragon biting from across the room.
        foreach (float roll in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            Assert.Equal(2, ActionSelection.Choose(Dragon, distance: 6f, roll));
        }
    }

    [Fact]
    public void AMinimumRangeExcludesTargetsThatAreTooClose()
    {
        // A bow authored with a minimum reach must not be chosen point-blank.
        var bow = new[] { Action("loose", min: 4f, max: 26f) };
        Assert.Equal(-1, ActionSelection.Choose(bow, distance: 1.5f, roll: 0.5f));
        Assert.Equal(0, ActionSelection.Choose(bow, distance: 10f, roll: 0.5f));
    }

    [Fact]
    public void TheRangeBoundsAreInclusive()
    {
        var one = new[] { Action("a", 2f, 4f) };
        Assert.Equal(0, ActionSelection.Choose(one, distance: 2f, roll: 0.5f));
        Assert.Equal(0, ActionSelection.Choose(one, distance: 4f, roll: 0.5f));
        Assert.Equal(-1, ActionSelection.Choose(one, distance: 4.01f, roll: 0.5f));
    }

    [Fact]
    public void AZeroWeightActionIsNeverChosen()
    {
        // ⚠️ Zero weight means player-only. A finisher or riposte a designer keeps off the AI's menu
        // says so with a zero, and no fallback may reach past that.
        var chain = new[] { Action("player_only", 0f, 5f, weight: 0f) };
        Assert.Equal(-1, ActionSelection.Choose(chain, distance: 2f, roll: 0.5f));
        Assert.False(ActionSelection.InRange(chain[0], 2f));
    }

    [Fact]
    public void WeightDecidesTheShareOfTheRoll()
    {
        // Three-to-one: the first action owns the bottom 75% of the roll.
        var chain = new[] { Action("common", 0f, 5f, weight: 3f), Action("rare", 0f, 5f, weight: 1f) };
        Assert.Equal(0, ActionSelection.Choose(chain, 2f, roll: 0f));
        Assert.Equal(0, ActionSelection.Choose(chain, 2f, roll: 0.74f));
        Assert.Equal(1, ActionSelection.Choose(chain, 2f, roll: 0.76f));
        Assert.Equal(1, ActionSelection.Choose(chain, 2f, roll: 1f));
    }

    [Fact]
    public void AnOutOfBoundsRollStillPicksSomething()
    {
        // The caller supplies the randomness; a bad value must degrade to a valid choice rather than
        // to "this enemy does not attack".
        Assert.InRange(ActionSelection.Choose(Dragon, 2f, roll: -5f), 0, 2);
        Assert.InRange(ActionSelection.Choose(Dragon, 2f, roll: 5f), 0, 2);
    }

    [Fact]
    public void MaxReachIsTheFurthestAnyAvailableActionCanGo()
    {
        Assert.Equal(7f, ActionSelection.MaxReach(Dragon), 3);
        Assert.Equal(0f, ActionSelection.MaxReach(System.Array.Empty<ActionSelection.Candidate>()), 3);
    }

    [Fact]
    public void MaxReachIgnoresPlayerOnlyActions()
    {
        var chain = new[] { Action("ai", 0f, 3f), Action("player_only", 0f, 40f, weight: 0f) };
        Assert.Equal(3f, ActionSelection.MaxReach(chain), 3);
    }

}
