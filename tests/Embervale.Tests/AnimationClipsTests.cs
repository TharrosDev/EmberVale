using Embervale.Animation;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Clip-name resolution across the vocabularies the project actually has to import. Both lists below
/// are real: the first is what <c>chr_player_base.glb</c> ships, the second is what a Quaternius
/// monster ships. The failure this guards against is silent — an unresolved slot leaves the actor in
/// its bind pose, so a T-posing enemy is the only symptom.
/// </summary>
public class AnimationClipsTests
{
    // Exactly what Godot's AnimationPlayer reports for chr_player_base.glb after import — verified
    // by loading the imported scene, not read off the source file. The importer strips the authored
    // "-loop" suffix, so these arrive bare even though the glTF stores "idle-loop".
    private static readonly string[] InHouse =
    {
        "attack", "block", "cast", "channel", "death", "hit", "idle", "run",
    };

    // Likewise verified post-import, for the CC0 orc now standing in for the goblin.
    private static readonly string[] Goblin =
    {
        "CharacterArmature|Death", "CharacterArmature|Duck", "CharacterArmature|HitReact",
        "CharacterArmature|Idle", "CharacterArmature|Jump", "CharacterArmature|Jump_Idle",
        "CharacterArmature|Jump_Land", "CharacterArmature|No", "CharacterArmature|Punch",
        "CharacterArmature|Run", "CharacterArmature|Walk", "CharacterArmature|Wave",
        "CharacterArmature|Weapon", "CharacterArmature|Yes",
    };

    // Verbatim from a downloaded Quaternius model, typo and all.
    private static readonly string[] Quaternius =
    {
        "CharacterArmature|Bite_Front", "CharacterArmature|Dance", "CharacterArmature|Death",
        "CharacterArmature|HitRecieve", "CharacterArmature|Idle", "CharacterArmature|Jump",
        "CharacterArmature|No", "CharacterArmature|Walk", "CharacterArmature|Yes",
    };

    [Theory]
    [InlineData("idle", "idle")]
    [InlineData("run", "run")]
    [InlineData("block", "block")]
    [InlineData("attack", "attack")]
    [InlineData("hit", "hit")]
    [InlineData("death", "death")]
    [InlineData("cast", "cast")]
    [InlineData("channel", "channel")]
    public void InHouseRig_StillResolvesEverySlot(string slot, string expected) =>
        Assert.Equal(expected, AnimationClips.Resolve(InHouse, slot));

    [Theory]
    [InlineData("idle", "CharacterArmature|Idle")]
    [InlineData("run", "CharacterArmature|Run")]
    [InlineData("attack", "CharacterArmature|Punch")]  // no blade clip, so punch carries the slot
    [InlineData("hit", "CharacterArmature|HitReact")]
    [InlineData("death", "CharacterArmature|Death")]
    public void GoblinReplacement_ResolvesEveryCombatSlot(string slot, string expected) =>
        Assert.Equal(expected, AnimationClips.Resolve(Goblin, slot));

    [Fact]
    public void GoblinReplacement_HasNoBlockOrCastAndSaysSo()
    {
        // The in-house goblin had none either, so this is parity rather than a regression.
        Assert.Equal(string.Empty, AnimationClips.Resolve(Goblin, "block"));
        Assert.Equal(string.Empty, AnimationClips.Resolve(Goblin, "cast"));
    }

    [Theory]
    [InlineData("idle", "CharacterArmature|Idle")]
    [InlineData("run", "CharacterArmature|Walk")]      // pack says Walk; the slot is our locomotion beat
    [InlineData("attack", "CharacterArmature|Bite_Front")]
    [InlineData("hit", "CharacterArmature|HitRecieve")] // Quaternius ships this misspelling
    [InlineData("death", "CharacterArmature|Death")]
    public void ArmaturePrefixedPack_ResolvesThroughPrefixAndAlias(string slot, string expected) =>
        Assert.Equal(expected, AnimationClips.Resolve(Quaternius, slot));

    [Fact]
    public void MissingSlot_ReturnsEmptyRatherThanGuessing()
    {
        // The monster has no block/cast/channel. Empty is correct — the component guards on length
        // and simply never plays one. Returning a wrong clip would be far worse than none.
        Assert.Equal(string.Empty, AnimationClips.Resolve(Quaternius, "block"));
        Assert.Equal(string.Empty, AnimationClips.Resolve(Quaternius, "cast"));
        Assert.Equal(string.Empty, AnimationClips.Resolve(Quaternius, "channel"));
    }

    [Fact]
    public void MoreSpecificClipWinsOverTheGenericSlotName()
    {
        // Deliberate policy, and a reversal of this file's first draft. "Attack" reads like the
        // obvious winner, but real rigs use it for stances — the rogue's "Attacking_Idle" is a
        // wind-up pose, and preferring the generic word resolved the attack slot to it, leaving the
        // character posturing and never striking. A clip named for a weapon or a strike is always
        // the strike, so those are tried first.
        Assert.Equal("Bite", AnimationClips.Resolve(new[] { "Bite", "Attack" }, "attack"));
        Assert.Equal("Bite", AnimationClips.Resolve(new[] { "Attack", "Bite" }, "attack"));
    }

    [Fact]
    public void GenericAttackStillWinsWhenNoWeaponClipExists()
    {
        // The in-house rigs name it plainly, and nothing more specific is present.
        Assert.Equal("attack", AnimationClips.Resolve(InHouse, "attack"));
        Assert.Equal("CharacterArmature|Punch", AnimationClips.Resolve(Goblin, "attack"));
    }

    [Fact]
    public void BareNameWithoutPrefixStillWorks()
    {
        string[] mixamoish = { "mixamorig|Idle", "Run" };
        Assert.Equal("mixamorig|Idle", AnimationClips.Resolve(mixamoish, "idle"));
        Assert.Equal("Run", AnimationClips.Resolve(mixamoish, "run"));
    }

    // The actual clip list of the CC0 knight now standing in for the Iron King. It ships weapon
    // variants that share a prefix with the plain clip, which is where first-match-wins gets
    // dangerous — a fantasy boss idling in a rifle stance is not something the engine would flag.
    private static readonly string[] IronKing =
    {
        "CharacterArmature|Idle_Gun", "CharacterArmature|Idle",       // deliberately reversed
        "CharacterArmature|Run_Gun", "CharacterArmature|Run",
        "CharacterArmature|Run_Slash", "CharacterArmature|Slash",
        "CharacterArmature|HitReact", "CharacterArmature|Death", "CharacterArmature|Punch",
    };

    [Theory]
    [InlineData("idle", "CharacterArmature|Idle")]
    [InlineData("run", "CharacterArmature|Run")]
    [InlineData("attack", "CharacterArmature|Slash")]  // slash outranks punch for a blade user
    [InlineData("hit", "CharacterArmature|HitReact")]
    [InlineData("death", "CharacterArmature|Death")]
    public void ExactMatchBeatsAPrefixVariantWhateverTheOrder(string slot, string expected) =>
        Assert.Equal(expected, AnimationClips.Resolve(IronKing, slot));

    [Fact]
    public void PrefixStillWinsWhenThereIsNoExactMatch()
    {
        // The in-house naming has no bare "idle" at all, so the prefix pass must still carry it.
        Assert.Equal("idle-loop", AnimationClips.Resolve(new[] { "idle-loop" }, "idle"));
    }

    // The rogue standing in for Kael. "Attacking_Idle" is a stance, not a swing; resolving the
    // attack slot to it would leave the companion winding up and never striking.
    private static readonly string[] Rogue =
    {
        "Attacking_Idle", "Dagger_Attack", "Dagger_Attack2", "Death", "Idle", "PickUp",
        "Punch", "RecieveHit", "RecieveHit_Attacking", "Roll", "Run", "Walk",
    };

    [Theory]
    [InlineData("attack", "Dagger_Attack")]
    [InlineData("idle", "Idle")]
    [InlineData("run", "Run")]
    [InlineData("hit", "RecieveHit")]
    [InlineData("death", "Death")]
    public void WeaponClipBeatsAStanceNamedAttack(string slot, string expected) =>
        Assert.Equal(expected, AnimationClips.Resolve(Rogue, slot));

    [Fact]
    public void SwordClipWinsForABladeCarrier()
    {
        // The adventurer's only swing is "Sword_Slash"; its punches are secondary.
        string[] adventurer = { "Idle", "Idle_Sword", "Punch_Left", "Punch_Right", "Run", "Sword_Slash" };
        Assert.Equal("Sword_Slash", AnimationClips.Resolve(adventurer, "attack"));
        Assert.Equal("Idle", AnimationClips.Resolve(adventurer, "idle"));
    }

    /// <summary>The Quaternius fliers (dragon, demon) ship no "idle" and no "walk" at all — only
    /// Flying_Idle and Fast_Flying. Before those aliases both slots resolved to empty, which leaves
    /// the creature in its bind pose: a T-posing dragon, and nothing logs an error.</summary>
    private static readonly string[] Dragon =
    {
        "CharacterArmature|Death", "CharacterArmature|Fast_Flying", "CharacterArmature|Flying_Idle",
        "CharacterArmature|Headbutt", "CharacterArmature|HitReact", "CharacterArmature|No",
        "CharacterArmature|Punch", "CharacterArmature|Yes",
    };

    [Theory]
    [InlineData("idle", "CharacterArmature|Flying_Idle")]
    [InlineData("run", "CharacterArmature|Fast_Flying")]
    [InlineData("attack", "CharacterArmature|Punch")]
    [InlineData("hit", "CharacterArmature|HitReact")]
    [InlineData("death", "CharacterArmature|Death")]
    public void AFlierResolvesEverySlotItOwns(string slot, string expected) =>
        Assert.Equal(expected, AnimationClips.Resolve(Dragon, slot));

    [Fact]
    public void FlyingIdleIsNeverMistakenForRunning()
    {
        // "flying" as a run alias would prefix-match Flying_Idle and fly a creature on the spot.
        // A rig with only the idle must report no run clip rather than the wrong one.
        string[] hoverOnly = { "CharacterArmature|Flying_Idle", "CharacterArmature|Death" };
        Assert.Equal(string.Empty, AnimationClips.Resolve(hoverOnly, "run"));
        Assert.Equal("CharacterArmature|Flying_Idle", AnimationClips.Resolve(hoverOnly, "idle"));
    }

    [Fact]
    public void ARealIdleStillBeatsTheFlyingFallback()
    {
        // Alias order matters: a grounded rig owning both must not resolve to the flier's clip.
        string[] both = { "Flying_Idle", "Idle", "Walk" };
        Assert.Equal("Idle", AnimationClips.Resolve(both, "idle"));
        Assert.Equal("Walk", AnimationClips.Resolve(both, "run"));
    }

    /// <summary>The animal rig uses a different armature prefix and gallops instead of running.</summary>
    [Fact]
    public void AnAnimalRigResolvesThroughItsOwnPrefix()
    {
        string[] wolf =
        {
            "AnimalArmature|Attack", "AnimalArmature|Death", "AnimalArmature|Gallop",
            "AnimalArmature|Idle", "AnimalArmature|Walk",
        };
        Assert.Equal("AnimalArmature|Idle", AnimationClips.Resolve(wolf, "idle"));
        Assert.Equal("AnimalArmature|Walk", AnimationClips.Resolve(wolf, "run"));
        Assert.Equal("AnimalArmature|Attack", AnimationClips.Resolve(wolf, "attack"));
    }

    [Fact]
    public void TrailingBarIsNotTreatedAsAPrefix()
    {
        // A name ending in '|' would slice to empty and match every slot; it must not.
        Assert.Equal(string.Empty, AnimationClips.Resolve(new[] { "Idle|" }, "run"));
    }
}
