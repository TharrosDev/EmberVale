using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// What being hit actually does to a body.
///
/// ⚠️ <b>Every actor used to react identically.</b> One <c>StaggerDuration</c> and one poise pool
/// meant a goblin and the Iron King behaved the same once their numbers were spent — every broken
/// guard was the same 0.6 s interruption. The numbers differed; the response did not, which is the
/// "weightless ragdoll" §9 names and the reason hitting things felt the same whatever you hit.
/// </summary>
public class PoiseReactionTests
{
    [Fact]
    public void ABossStaggersButIsNeverPutDownOrPushed()
    {
        // ⚠️ Not a number to tune. A boss that can be knocked over can be chain-knocked, and a fight
        // that can be chain-knocked has exactly one answer. Its poise still breaks — that is how the
        // punish window opens — it simply staggers where it stands.
        foreach (float overkill in new[] { 0f, 0.5f, 1f, 5f })
        {
            StaggerResponse r = PoiseReaction.Resolve(ReactionClass.Boss, overkill);
            Assert.Equal(StaggerResponse.Stagger, r);
            Assert.Equal(0f, PoiseReaction.Knockback(ReactionClass.Boss, r, authored: 10f), 4);
        }
    }

    [Fact]
    public void ALightBlowOnlyFlinchesAnArmouredBodyAndDoesNotInterruptIt()
    {
        // The whole distinction: an armoured enemy takes a light hit, reacts visibly, and keeps
        // swinging. That is what makes hitting one feel different from hitting a goblin rather than
        // merely slower.
        StaggerResponse light = PoiseReaction.Resolve(ReactionClass.Armored, overkill: 0.1f);
        Assert.Equal(StaggerResponse.Flinch, light);
        Assert.False(PoiseReaction.Interrupts(light));

        StaggerResponse solid = PoiseReaction.Resolve(ReactionClass.Armored, overkill: 0.4f);
        Assert.Equal(StaggerResponse.Stagger, solid);
        Assert.True(PoiseReaction.Interrupts(solid));
    }

    [Fact]
    public void OnlySmallBodiesAreKnockedDown()
    {
        Assert.Equal(StaggerResponse.Knockdown, PoiseReaction.Resolve(ReactionClass.Small, 0.9f));
        foreach (ReactionClass body in new[]
                 { ReactionClass.Humanoid, ReactionClass.Armored, ReactionClass.Large, ReactionClass.Boss })
        {
            Assert.NotEqual(StaggerResponse.Knockdown, PoiseReaction.Resolve(body, 5f));
        }
    }

    [Fact]
    public void ALargeBodyShrugsOffAnythingShortOfARealBlow()
    {
        Assert.Equal(StaggerResponse.Flinch, PoiseReaction.Resolve(ReactionClass.Large, 0.3f));
        Assert.Equal(StaggerResponse.Heavy, PoiseReaction.Resolve(ReactionClass.Large, 0.9f));
    }

    [Fact]
    public void AFlinchNeverInterruptsAndEverythingElseDoes()
    {
        Assert.False(PoiseReaction.Interrupts(StaggerResponse.None));
        Assert.False(PoiseReaction.Interrupts(StaggerResponse.Flinch));
        Assert.True(PoiseReaction.Interrupts(StaggerResponse.Stagger));
        Assert.True(PoiseReaction.Interrupts(StaggerResponse.Heavy));
        Assert.True(PoiseReaction.Interrupts(StaggerResponse.Knockdown));
    }

    [Fact]
    public void TheHeavierTheResponseTheLongerItLasts()
    {
        const float based = 0.6f;
        Assert.Equal(0f, PoiseReaction.Duration(StaggerResponse.None, based), 4);
        Assert.True(PoiseReaction.Duration(StaggerResponse.Flinch, based) < based);
        Assert.Equal(based, PoiseReaction.Duration(StaggerResponse.Stagger, based), 4);
        Assert.True(PoiseReaction.Duration(StaggerResponse.Heavy, based) > based);
        Assert.True(PoiseReaction.Duration(StaggerResponse.Knockdown, based)
                    > PoiseReaction.Duration(StaggerResponse.Heavy, based));
    }

    [Fact]
    public void DurationScalesOffTheBodysOwnStagger() =>
        // A creature authored slow to recover stays slow to recover; the class scales it rather than
        // replacing it.
        Assert.Equal(2f, PoiseReaction.Duration(StaggerResponse.Stagger, 2f), 4);

    [Fact]
    public void KnockbackIsScaledByBodyAndNeverAppliedToAFlinch()
    {
        Assert.Equal(0f, PoiseReaction.Knockback(ReactionClass.Humanoid, StaggerResponse.Flinch, 5f), 4);

        float small = PoiseReaction.Knockback(ReactionClass.Small, StaggerResponse.Stagger, 5f);
        float human = PoiseReaction.Knockback(ReactionClass.Humanoid, StaggerResponse.Stagger, 5f);
        float large = PoiseReaction.Knockback(ReactionClass.Large, StaggerResponse.Stagger, 5f);

        Assert.True(small > human, "a small body should be pushed further than a person");
        Assert.True(large < human, "a large body should barely move");
        Assert.True(large > 0f, "a large body should still move a little");
    }

    [Fact]
    public void AnAttackThatAuthorsNoKnockbackPushesNothing() =>
        Assert.Equal(0f, PoiseReaction.Knockback(ReactionClass.Small, StaggerResponse.Heavy, 0f), 4);

    [Fact]
    public void OverkillIsWhatTheBlowHadLeftOverRelativeToTheBody()
    {
        // Relative rather than absolute, so the same blow means "a lot" to a goblin and "a little"
        // to a dragon without anyone authoring per-creature thresholds.
        Assert.Equal(0f, PoiseReaction.Overkill(poiseDamage: 10f, poiseLeft: 20f, maxPoise: 40f), 4);
        Assert.Equal(0.5f, PoiseReaction.Overkill(poiseDamage: 30f, poiseLeft: 10f, maxPoise: 40f), 4);

        // The same 30 poise damage against a much tougher body is barely a scratch past its guard.
        Assert.Equal(0.05f, PoiseReaction.Overkill(30f, 10f, 400f), 4);
    }

    [Fact]
    public void ABodyWithNoPoiseAtAllIsAlwaysFullyOverwhelmed() =>
        Assert.Equal(1f, PoiseReaction.Overkill(1f, 0f, maxPoise: 0f), 4);
}
