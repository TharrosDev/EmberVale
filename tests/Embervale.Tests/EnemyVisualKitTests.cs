using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

public sealed class EnemyVisualKitTests
{
    [Theory]
    // BoarHead, not AshMawCarapace: the carapace is shared bulk and its presence never proved the
    // boar had a silhouette of its own. It did not — the Head slot wore the AshMaw's jaws and the
    // archetype read as the bull underneath it. Assert the piece that makes it a boar.
    [InlineData("enemy.thornback_boar", "BoarHead")]
    [InlineData("enemy.barrow_wight", "WightBurialArmor")]
    [InlineData("enemy.grave_shade", "ShadeVeil")]
    [InlineData("enemy.clan_shaman", "ShamanMask")]
    [InlineData("enemy.hollow_necromancer", "NecroRibs")]
    [InlineData("enemy.soldier", "SoldierHarness")]
    [InlineData("enemy.bandit", "BanditMantle")]
    [InlineData("enemy.syndicate_enforcer", "EnforcerArmor")]
    [InlineData("enemy.dire_wolf", "DireWolfMane")]
    [InlineData("enemy.frost_stalker", "FrostStalkerRidge")]
    [InlineData("enemy.wild_dragon", "WildDragonCrown")]
    [InlineData("enemy.ash_dragon", "AshDragonCrown")]
    [InlineData("enemy.frost_drake", "FrostDragonCrest")]
    [InlineData("enemy.ancient_dragon", "AncientDragonCrown")]
    [InlineData("enemy.iron_king", "IronKingPlate")]
    public void PriorityIdentityHasAuthoredSilhouettePiece(string templateId, string piece)
    {
        EnemyVisualKit.Profile profile = Assert.IsType<EnemyVisualKit.Profile>(EnemyVisualKit.Resolve(templateId));
        Assert.Contains(profile.Pieces, candidate => candidate.Name == piece);
    }

    [Fact]
    public void WolfRemainsUnmodifiedWhileDireWolfIsStructuralVariant()
    {
        Assert.Null(EnemyVisualKit.Resolve("enemy.wolf"));
        EnemyVisualKit.Profile dire = Assert.IsType<EnemyVisualKit.Profile>(EnemyVisualKit.Resolve("enemy.dire_wolf"));
        Assert.True(dire.Pieces.Count >= 2);
    }

    [Fact]
    public void CustomAshMawDoesNotReceiveRigOverlay()
    {
        Assert.Null(EnemyVisualKit.Resolve("enemy.ash_maw"));
    }

    [Fact]
    public void DuplicatePairsResolveToDifferentPieceSets()
    {
        EnemyVisualKit.Profile wight = Assert.IsType<EnemyVisualKit.Profile>(EnemyVisualKit.Resolve("enemy.barrow_wight"));
        EnemyVisualKit.Profile shade = Assert.IsType<EnemyVisualKit.Profile>(EnemyVisualKit.Resolve("enemy.grave_shade"));
        EnemyVisualKit.Profile shaman = Assert.IsType<EnemyVisualKit.Profile>(EnemyVisualKit.Resolve("enemy.clan_shaman"));
        EnemyVisualKit.Profile necro = Assert.IsType<EnemyVisualKit.Profile>(EnemyVisualKit.Resolve("enemy.hollow_necromancer"));
        Assert.NotEqual(wight.Pieces[0].Name, shade.Pieces[0].Name);
        Assert.NotEqual(shaman.Pieces[0].Name, necro.Pieces[0].Name);
    }
}
