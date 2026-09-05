using Embervale.Combat;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

public sealed class WorldPhysicsContractTests
{
    [Fact]
    public void TerrainMustOccupyWorldStatic()
    {
        Assert.Single(WorldPhysicsContract.Validate("Cell/TerrainCollider", "StaticBody3D", 0u, 0u));
        Assert.Empty(WorldPhysicsContract.Validate(
            "Cell/TerrainCollider", "StaticBody3D", CombatLayers.WorldStatic, 0u));
    }

    [Fact]
    public void CameraBlockerUsesDeliberateLayer()
    {
        Assert.Single(WorldPhysicsContract.Validate(
            "House/CameraBlocker", "StaticBody3D", CombatLayers.WorldStatic, 0u));
        Assert.Empty(WorldPhysicsContract.Validate(
            "House/CameraBlocker", "StaticBody3D",
            CombatLayers.WorldStatic | CombatLayers.CameraBlocker, 0u));
    }

    [Fact]
    public void HurtboxCannotBePhysical()
    {
        Assert.NotEmpty(WorldPhysicsContract.Validate(
            "Enemy/Hurtbox", "Area3D", CombatLayers.WorldStatic | CombatLayers.Hurtbox,
            CombatLayers.Hitbox));
        Assert.Empty(WorldPhysicsContract.Validate(
            "Enemy/Hurtbox", "Area3D", CombatLayers.Hurtbox, 0u));
    }

    [Fact]
    public void ActiveHitboxMustQueryHurtboxes()
    {
        Assert.Single(WorldPhysicsContract.Validate(
            "Enemy/Hitbox", "Area3D", CombatLayers.Hitbox, 0u));
        Assert.Empty(WorldPhysicsContract.Validate(
            "Enemy/Hitbox", "Area3D", CombatLayers.Hitbox, CombatLayers.Hurtbox));
    }
}
