using System.Linq;
using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure halves of learning and the spellbook: what a learn attempt comes to, which refusals are worth
/// telling the player, the lock rule, and the plain-language rule lines a spell earns from its contract
/// fields. The route itself (events, the caster) is Godot-bound and probed.
/// </summary>
public class SpellLearningRulesTests
{
    [Fact]
    public void Evaluate_OrdersTheChecks()
    {
        Assert.Equal(LearnOutcome.NoCaster, SpellLearnRules.Evaluate(false, true, false, 4, 0));
        Assert.Equal(LearnOutcome.UnknownSpell, SpellLearnRules.Evaluate(true, false, false, 4, 0));
        Assert.Equal(LearnOutcome.AlreadyKnown, SpellLearnRules.Evaluate(true, true, true, 0, 2));
        Assert.Equal(LearnOutcome.CorruptionLocked, SpellLearnRules.Evaluate(true, true, false, 1, 2));
        Assert.Equal(LearnOutcome.Learned, SpellLearnRules.Evaluate(true, true, false, 2, 2));
        Assert.Equal(LearnOutcome.Learned, SpellLearnRules.Evaluate(true, true, false, 0, 0));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Corrupted_MeansGatedAboveUntainted(int tierRequired, bool expected)
    {
        Assert.Equal(expected, SpellLearnRules.IsCorrupted(tierRequired));
    }

    [Fact]
    public void Routes_AreTheDocumentedStrings_AndUnknownOnesFallBack()
    {
        Assert.Equal("tome", LearnRoutes.Tome);
        Assert.Equal("dialogue", LearnRoutes.Dialogue);
        Assert.Equal("trainer", LearnRoutes.Trainer);
        Assert.Equal("tome", SpellLearnRules.NormalizeRoute("tome"));
        Assert.Equal("trainer", SpellLearnRules.NormalizeRoute("trainer"));
        Assert.Equal("dialogue", SpellLearnRules.NormalizeRoute("anything else"));
        Assert.Equal("dialogue", SpellLearnRules.NormalizeRoute(null));
    }

    [Fact]
    public void Refusals_AreToldExceptAConversationReofferingAKnownSpell()
    {
        Assert.True(SpellLearnRules.ShouldAnnounceRefusal(LearnOutcome.CorruptionLocked, LearnRoutes.Dialogue));
        Assert.True(SpellLearnRules.ShouldAnnounceRefusal(LearnOutcome.CorruptionLocked, LearnRoutes.Tome));
        Assert.True(SpellLearnRules.ShouldAnnounceRefusal(LearnOutcome.AlreadyKnown, LearnRoutes.Tome));
        Assert.False(SpellLearnRules.ShouldAnnounceRefusal(LearnOutcome.AlreadyKnown, LearnRoutes.Dialogue));
        Assert.False(SpellLearnRules.ShouldAnnounceRefusal(LearnOutcome.Learned, LearnRoutes.Tome));
    }

    [Fact]
    public void RetiredIds_ResolveToTheirReplacement()
    {
        Assert.Equal("spell.emberlash", SpellAliases.Resolve("spell.firebolt"));
        Assert.Equal("spell.sunfall", SpellAliases.Resolve("spell.fireball"));
        Assert.Equal("spell.frost_nova", SpellAliases.Resolve("spell.frost_nova"));
    }

    [Theory]
    [InlineData(false, 1, 2, true)]
    [InlineData(false, 2, 2, false)]
    [InlineData(true, 0, 2, false)] // a known spell is never locked, whatever happened to the tier since
    [InlineData(false, 0, 0, false)]
    public void TierLock_AppliesOnlyToUnknownSpellsBelowTheirTier(bool known, int now, int required, bool locked)
    {
        Assert.Equal(locked, SpellBookRules.IsTierLocked(known, now, required));
    }

    private static string[] Keys(SpellFacts facts) => SpellBookRules.Rules(facts).Select(r => r.Key).ToArray();

    [Fact]
    public void PlainProjectile_HasNoSpecialRule()
    {
        Assert.Empty(SpellBookRules.Rules(new SpellFacts(SpellDelivery.Projectile, CastMode.Instant)));
    }

    [Fact]
    public void Ground_SaysWhenItLands_AndPullWhenItPulls()
    {
        var facts = new SpellFacts(SpellDelivery.Ground, CastMode.Instant, GroundDelay: 1.2f, PullStrength: 6f);
        Assert.Equal(new[] { "magic.book.rule.ground", "magic.book.rule.pull" }, Keys(facts));
        Assert.Equal(1.2f, SpellBookRules.Rules(facts)[0].Value);
    }

    [Fact]
    public void Barrier_SaysSolidOrHazard_ShotsAndBreakable()
    {
        var wall = new SpellFacts(SpellDelivery.Barrier, CastMode.Instant, BarrierDuration: 8f,
            BarrierBlocksBodies: true, BarrierBlocksProjectiles: true, BarrierHealth: 60f);
        Assert.Equal(
            new[]
            {
                "magic.book.rule.barrier", "magic.book.rule.barrier_solid",
                "magic.book.rule.barrier_shots", "magic.book.rule.barrier_breakable",
            },
            Keys(wall));

        var fire = new SpellFacts(SpellDelivery.Barrier, CastMode.Instant, BarrierDuration: 6f);
        Assert.Equal(new[] { "magic.book.rule.barrier", "magic.book.rule.barrier_hazard" }, Keys(fire));
    }

    [Fact]
    public void Dash_Blink_Mark_Silence_Consume_HealthCost_AndTheOptOuts()
    {
        Assert.Equal(
            new[] { "magic.book.rule.dash" },
            Keys(new SpellFacts(SpellDelivery.Dash, CastMode.Instant, DashDistance: 8f)));
        Assert.Equal(
            new[] { "magic.book.rule.blink" },
            Keys(new SpellFacts(SpellDelivery.Self, CastMode.Instant, BlinkDistance: 10f)));
        Assert.Equal(
            new[] { "magic.book.rule.mark", "magic.book.rule.silence" },
            Keys(new SpellFacts(SpellDelivery.Projectile, CastMode.Instant,
                AppliedControls: StatusControl.Mark | StatusControl.Silence)));
        Assert.Equal(
            new[] { "magic.book.rule.consumes", "magic.book.rule.health_cost", "magic.book.rule.unblockable",
                "magic.book.rule.hyperarmour" },
            Keys(new SpellFacts(SpellDelivery.Projectile, CastMode.Instant, HealthCost: 12f, Blockable: false,
                Interruptible: false, Consumes: true)));
    }
}
