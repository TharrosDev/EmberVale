using Embervale.Magic.Vfx;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The live-effect budget's bookkeeping. An effect is a group of blocks; the budget counts groups;
/// and when it is full the group that gives way is the oldest one that is not the player's, never
/// an essential one (a wind-up aura is a warning), and a standing one (a zone, a bolt in flight)
/// only when no one-shot is left.
/// </summary>
public class VfxLedgerTests
{
    [Fact]
    public void AGroupOpensWithItsFirstBlockAndClosesWithItsLast()
    {
        var ledger = new VfxLedger();
        int group = ledger.NextId();

        Assert.Equal(0, ledger.LiveGroups); // an id alone holds no place

        ledger.BlockAdded(group, 0d, player: true, essential: false, sustained: false);
        ledger.BlockAdded(group, 0d, player: true, essential: false, sustained: false);
        ledger.BlockAdded(group, 0d, player: true, essential: false, sustained: false);

        Assert.Equal(1, ledger.LiveGroups);
        Assert.Equal(3, ledger.BlocksOf(group));

        Assert.False(ledger.BlockRemoved(group));
        Assert.False(ledger.BlockRemoved(group));
        Assert.True(ledger.IsLive(group));
        Assert.True(ledger.BlockRemoved(group));

        Assert.False(ledger.IsLive(group));
        Assert.Equal(0, ledger.LiveGroups);
    }

    [Fact]
    public void IdsAreNeverZeroAndNeverRepeatInARow()
    {
        var ledger = new VfxLedger();
        int first = ledger.NextId();
        int second = ledger.NextId();

        Assert.NotEqual(0, first);
        Assert.NotEqual(0, second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GroupZeroIsNotCounted()
    {
        // Ground marks live under group 0: they have a budget of their own.
        var ledger = new VfxLedger();

        ledger.BlockAdded(0, 0d, player: false, essential: false, sustained: false);

        Assert.Equal(0, ledger.LiveGroups);
        Assert.False(ledger.BlockRemoved(0));
    }

    [Fact]
    public void RemovingFromAnUnknownGroupDoesNothing()
    {
        var ledger = new VfxLedger();

        Assert.False(ledger.BlockRemoved(42));
        Assert.Equal(0, ledger.LiveGroups);
    }

    [Fact]
    public void TheOldestEffectThatIsNotThePlayersGivesWay()
    {
        var ledger = new VfxLedger();
        int playerOld = Open(ledger, 0d, player: true);
        int enemyOld = Open(ledger, 1d, player: false);
        int enemyNew = Open(ledger, 5d, player: false);

        Assert.Equal(enemyOld, ledger.PickVictim(10d));

        ledger.BlockRemoved(enemyOld);
        Assert.Equal(enemyNew, ledger.PickVictim(10d));

        // Only the player's are left: the oldest of those.
        ledger.BlockRemoved(enemyNew);
        Assert.Equal(playerOld, ledger.PickVictim(10d));
    }

    [Fact]
    public void AnEssentialEffectIsNeverOffered()
    {
        var ledger = new VfxLedger();
        Open(ledger, 0d, player: false, essential: true);

        Assert.Equal(0, ledger.PickVictim(10d));

        int ordinary = Open(ledger, 9d, player: true);
        Assert.Equal(ordinary, ledger.PickVictim(10d));
    }

    [Fact]
    public void AnEssentialEffectTakesNoPlaceInTheBudget()
    {
        // Eight casters winding up with eight bolts in the air must not spend the lowest tier's
        // whole budget before a single impact has been drawn.
        var ledger = new VfxLedger();
        var essentials = new int[16];
        for (int i = 0; i < essentials.Length; i++)
        {
            essentials[i] = Open(ledger, i, player: false, essential: true, sustained: true);
        }

        Assert.Equal(16, ledger.LiveGroups);
        Assert.Equal(0, ledger.BudgetedGroups);
        Assert.False(VfxBudgetRules.OverBudget(VfxTier.Performance, ledger.BudgetedGroups));

        int impact = Open(ledger, 20d, player: true);
        Assert.Equal(1, ledger.BudgetedGroups);

        foreach (int id in essentials)
        {
            ledger.BlockRemoved(id);
        }

        Assert.Equal(1, ledger.LiveGroups);
        Assert.Equal(1, ledger.BudgetedGroups);
        ledger.BlockRemoved(impact);
        Assert.Equal(0, ledger.BudgetedGroups);
    }

    [Fact]
    public void AStandingEffectGivesWayOnlyWhenNoOneShotIsLeft()
    {
        var ledger = new VfxLedger();
        int zone = Open(ledger, 0d, player: false, sustained: true);
        int flash = Open(ledger, 8d, player: true);

        // The player's own fresh flash goes before an enemy's old zone: the zone is still hurting.
        Assert.Equal(flash, ledger.PickVictim(10d));

        ledger.BlockRemoved(flash);
        Assert.Equal(zone, ledger.PickVictim(10d));
    }

    [Fact]
    public void AnEmptyLedgerOffersNothing()
    {
        Assert.Equal(0, new VfxLedger().PickVictim(1d));
    }

    [Fact]
    public void TheBudgetIsMetByRecyclingUntilThereIsRoom()
    {
        // The director's loop, without the nodes: at the Performance budget, opening one more
        // effect than fits recycles exactly one, and it is the oldest enemy one.
        var ledger = new VfxLedger();
        int budget = VfxBudgetRules.For(VfxTier.Performance).LiveEffects;
        int oldest = 0;
        for (int i = 0; i < budget; i++)
        {
            int id = Open(ledger, i, player: i % 2 == 0);
            if (i == 1)
            {
                oldest = id;
            }
        }

        Assert.True(VfxBudgetRules.OverBudget(VfxTier.Performance, ledger.BudgetedGroups));

        int victim = ledger.PickVictim(100d);
        Assert.Equal(oldest, victim);
        ledger.BlockRemoved(victim);

        Assert.False(VfxBudgetRules.OverBudget(VfxTier.Performance, ledger.BudgetedGroups));
    }

    [Fact]
    public void LightsAreTakenUpToTheTiersCountAndReturned()
    {
        var ledger = new VfxLedger();
        int max = VfxBudgetRules.For(VfxTier.Performance).MaxLights;

        for (int i = 0; i < max; i++)
        {
            Assert.True(ledger.TakeLight(max));
        }

        Assert.False(ledger.TakeLight(max));
        Assert.Equal(max, ledger.Lights);

        ledger.ReturnLight();
        Assert.True(ledger.TakeLight(max));

        for (int i = 0; i < max + 3; i++)
        {
            ledger.ReturnLight();
        }

        Assert.Equal(0, ledger.Lights); // returning more than was taken never goes negative
    }

    [Fact]
    public void OnlyTheTopTierHasAShadowedLight()
    {
        var ledger = new VfxLedger();

        Assert.False(ledger.TakeShadow(VfxBudgetRules.For(VfxTier.High).ShadowedLights));
        Assert.True(ledger.TakeShadow(VfxBudgetRules.For(VfxTier.Ultra).ShadowedLights));
        Assert.False(ledger.TakeShadow(VfxBudgetRules.For(VfxTier.Ultra).ShadowedLights));

        ledger.ReturnShadow();
        Assert.Equal(0, ledger.ShadowedLights);
    }

    [Fact]
    public void ForgettingClearsEverything()
    {
        var ledger = new VfxLedger();
        Open(ledger, 0d, player: true);
        ledger.TakeLight(4);
        ledger.TakeShadow(1);

        ledger.Forget();

        Assert.Equal(0, ledger.LiveGroups);
        Assert.Equal(0, ledger.BudgetedGroups);
        Assert.Equal(0, ledger.Lights);
        Assert.Equal(0, ledger.ShadowedLights);
        Assert.Equal(0, ledger.PickVictim(1d));
    }

    private static int Open(VfxLedger ledger, double now, bool player, bool essential = false, bool sustained = false)
    {
        int id = ledger.NextId();
        ledger.BlockAdded(id, now, player, essential, sustained);
        return id;
    }
}
