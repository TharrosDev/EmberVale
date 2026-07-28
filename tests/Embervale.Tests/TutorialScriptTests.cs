using System.Collections.Generic;
using Embervale.Onboarding;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the onboarding running order (Phase 33B). The sequence is the design — which verb is
/// taught when, and that it always terminates — and its failure modes are the kind that strand a
/// player: a step that loops back on itself, or saved state naming a step the script no longer has.
/// </summary>
public class TutorialScriptTests
{
    [Fact]
    public void TeachesLookingBeforeMoving()
    {
        // A player who can't find the camera can't be taught anything else.
        Assert.Equal(TutorialStep.Look, TutorialScript.First);
        Assert.Equal(TutorialStep.Move, TutorialScript.Next(TutorialStep.Look));
    }

    [Fact]
    public void TeachesMovementBeforeCombat()
    {
        Assert.True(
            TutorialScript.IndexOf(TutorialStep.Move) < TutorialScript.IndexOf(TutorialStep.Attack),
            "combat must not be taught before the player can walk");
    }

    [Fact]
    public void WalkingTheScriptTerminates()
    {
        // The load-bearing property: following Next from the first step always reaches None, so the
        // sequence can never trap the player on a permanent hint.
        var seen = new HashSet<TutorialStep>();
        TutorialStep step = TutorialScript.First;
        int guard = 0;

        while (step != TutorialStep.None)
        {
            Assert.True(seen.Add(step), $"step {step} repeats — the script loops");
            step = TutorialScript.Next(step);
            Assert.True(++guard <= 64, "the script did not terminate");
        }

        Assert.Equal(TutorialScript.Basics.Length, seen.Count);
    }

    [Fact]
    public void LastStepEndsTheSequence()
    {
        TutorialStep last = TutorialScript.Basics[^1];
        Assert.Equal(TutorialStep.None, TutorialScript.Next(last));
    }

    [Fact]
    public void UnknownStepEndsTheSequence()
    {
        // Saved state from an older build must degrade to "finished", never to a hint that no
        // longer exists.
        Assert.Equal(TutorialStep.None, TutorialScript.Next(TutorialStep.None));
        Assert.Equal(-1, TutorialScript.IndexOf(TutorialStep.None));
    }

    [Fact]
    public void EveryTaughtStepHasCopy()
    {
        foreach (TutorialStep step in TutorialScript.Basics)
        {
            Assert.False(string.IsNullOrEmpty(TutorialScript.HintKey(step)), $"{step} has no hint key");
        }
    }

    [Fact]
    public void EveryStepButLookNamesAnInputAction()
    {
        foreach (TutorialStep step in TutorialScript.Basics)
        {
            string action = TutorialScript.ActionFor(step);
            if (step == TutorialStep.Look)
            {
                Assert.Equal(string.Empty, action); // the mouse itself, no bound action
            }
            else
            {
                Assert.False(string.IsNullOrEmpty(action), $"{step} has no input action for its glyph");
            }
        }
    }

    [Fact]
    public void NoStepAppearsTwiceInTheRunningOrder()
    {
        Assert.Equal(TutorialScript.Basics.Length, new HashSet<TutorialStep>(TutorialScript.Basics).Count);
    }

    [Fact]
    public void NoneIsNeverTaught()
    {
        Assert.DoesNotContain(TutorialStep.None, TutorialScript.Basics);
    }
}
