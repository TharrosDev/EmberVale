using Embervale.Combat;
using Embervale.Combat.Actions;
using Xunit;

namespace Embervale.Tests;

/// <summary>Covers telegraph classes: what an action reads as (parryable, unblockable, sweep), the
/// fan angle, and the timing of the parry cue, which must come off the real wind-up.</summary>
public class CombatTelegraphClassTests
{
    private static TelegraphClass Class(ActionKind kind, string id = "", string hitbox = "", bool interruptible = true) =>
        TelegraphClasses.Classify(new TelegraphSource(kind, id, hitbox, interruptible));

    [Fact]
    public void ALightSwing_IsParryable() => Assert.Equal(TelegraphClass.Parryable, Class(ActionKind.Attack, "sword.slash"));

    [Fact]
    public void AnInterruptibleHeavy_IsStandard() =>
        Assert.Equal(TelegraphClass.Standard, Class(ActionKind.HeavyAttack, "sword.heavy"));

    [Fact]
    public void ACommittedHeavy_IsUnblockable() =>
        Assert.Equal(TelegraphClass.Unblockable, Class(ActionKind.HeavyAttack, "ironking.slam", interruptible: false));

    [Fact]
    public void ADeclaredUnblockableId_WinsOverEverything()
    {
        Assert.Equal(TelegraphClass.Unblockable, Class(ActionKind.Attack, "boss.unblockable_grab"));
        Assert.Equal(TelegraphClass.Unblockable, Class(ActionKind.Attack, "ogre.grab"));
    }

    [Theory]
    [InlineData("ironking.sweep", "")]
    [InlineData("dragon.bite", "Wing")]
    [InlineData("dragon.bite", "Tail")]
    [InlineData("golem.cleave", "")]
    [InlineData("dragon.breath", "")]
    public void WideArcs_AreSweeps(string id, string hitbox) =>
        Assert.Equal(TelegraphClass.Sweep, Class(ActionKind.Attack, id, hitbox));

    [Fact]
    public void ASweepThatIsAlsoCommitted_StaysASweep() =>
        Assert.Equal(TelegraphClass.Sweep, Class(ActionKind.HeavyAttack, "ironking.sweep", interruptible: false));

    [Fact]
    public void ACast_IsStandard() => Assert.Equal(TelegraphClass.Standard, Class(ActionKind.Cast, "spell.fireball"));

    [Fact]
    public void SweepDegrees_TailComesRoundAndSpinIsACircle()
    {
        var tail = new TelegraphSource(ActionKind.Attack, "dragon.bite", "Tail", true);
        var spin = new TelegraphSource(ActionKind.Attack, "boss.spin", "", true);
        var wing = new TelegraphSource(ActionKind.Attack, "dragon.bite", "Wing", true);
        var breath = new TelegraphSource(ActionKind.Cast, "dragon.breath", "", true);
        Assert.Equal(360f, TelegraphClasses.SweepDegrees(spin));
        Assert.True(TelegraphClasses.SweepDegrees(tail) > TelegraphClasses.SweepDegrees(wing));
        Assert.True(TelegraphClasses.SweepDegrees(breath) < TelegraphClasses.SweepDegrees(wing));
        Assert.Equal(TelegraphClasses.DefaultSweepDegrees, TelegraphClasses.SweepDegrees(wing));
    }

    [Fact]
    public void EveryClass_HasALabelKey()
    {
        var keys = new System.Collections.Generic.HashSet<string>();
        foreach (TelegraphClass cls in System.Enum.GetValues<TelegraphClass>())
        {
            Assert.True(keys.Add(TelegraphClasses.LabelKey(cls)));
            Assert.StartsWith("combat.feedback.", TelegraphClasses.LabelKey(cls));
        }
    }

    [Fact]
    public void ClassValues_AreAppendOnly()
    {
        Assert.Equal(0, (int)TelegraphClass.Standard);
        Assert.Equal(1, (int)TelegraphClass.Parryable);
        Assert.Equal(2, (int)TelegraphClass.Unblockable);
        Assert.Equal(3, (int)TelegraphClass.Sweep);
    }

    [Fact]
    public void ParryCue_OpensTheParryWindowPlusAReactionBeatBeforeContact()
    {
        // A 1.0 s wind-up with a 0.2 s window: the cue opens 0.28 s before the blow, at 72%.
        Assert.Equal(0.72f, TelegraphMath.ParryCueStart(1.0f, 0.2f), 3);
    }

    [Fact]
    public void ParryCue_ScalesWithTheRealWindup_NotAConstant()
    {
        float slow = TelegraphMath.ParryCueStart(2.0f, 0.2f);
        float fast = TelegraphMath.ParryCueStart(0.6f, 0.2f);
        Assert.True(slow > fast);

        // A phase that doubles attack speed halves the wind-up, and the cue moves with it.
        Assert.True(TelegraphMath.ParryCueStart(0.5f, 0.2f) < TelegraphMath.ParryCueStart(1.0f, 0.2f));
    }

    [Fact]
    public void ParryCue_IsClampedToTheBackHalfAndNeverTooLate()
    {
        Assert.Equal(0.5f, TelegraphMath.ParryCueStart(0.1f, 0.2f));  // a flicker of a wind-up
        Assert.Equal(0.95f, TelegraphMath.ParryCueStart(60f, 0.2f));  // a glacial one
        Assert.Equal(1f, TelegraphMath.ParryCueStart(0f, 0.2f));      // nothing to warn of
    }

    [Fact]
    public void InParryCue_IsFalseEarlyTrueLate()
    {
        Assert.False(TelegraphMath.InParryCue(0.3f, 1.0f, 0.2f));
        Assert.True(TelegraphMath.InParryCue(0.9f, 1.0f, 0.2f));
        Assert.True(TelegraphMath.InParryCue(0.72f, 1.0f, 0.2f));
    }

    [Fact]
    public void TimingRing_ClosesToAQuarterAsTheBlowLands()
    {
        Assert.Equal(1f, TelegraphMath.TimingRingScale(0f));
        Assert.Equal(0.25f, TelegraphMath.TimingRingScale(1f), 3);
        Assert.True(TelegraphMath.TimingRingScale(0.5f) < TelegraphMath.TimingRingScale(0.2f));
    }

    [Fact]
    public void OnlyAnUnblockablePulses()
    {
        Assert.Equal(1f, TelegraphMath.ClassPulse(TelegraphClass.Parryable, 0.37f));
        Assert.Equal(1f, TelegraphMath.ClassPulse(TelegraphClass.Sweep, 0.37f));
        Assert.NotEqual(1f, TelegraphMath.ClassPulse(TelegraphClass.Unblockable, 0.07f));
    }
}
