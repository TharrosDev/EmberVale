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
    private static readonly string[] InHouse =
    {
        "attack", "block-loop", "cast", "channel-loop", "death", "hit", "idle-loop", "run-loop",
    };

    // Verbatim from a downloaded Quaternius model, typo and all.
    private static readonly string[] Quaternius =
    {
        "CharacterArmature|Bite_Front", "CharacterArmature|Dance", "CharacterArmature|Death",
        "CharacterArmature|HitRecieve", "CharacterArmature|Idle", "CharacterArmature|Jump",
        "CharacterArmature|No", "CharacterArmature|Walk", "CharacterArmature|Yes",
    };

    [Theory]
    [InlineData("idle", "idle-loop")]
    [InlineData("run", "run-loop")]
    [InlineData("block", "block-loop")]
    [InlineData("attack", "attack")]
    [InlineData("hit", "hit")]
    [InlineData("death", "death")]
    [InlineData("cast", "cast")]
    [InlineData("channel", "channel-loop")]
    public void InHouseRig_StillResolvesEverySlot(string slot, string expected) =>
        Assert.Equal(expected, AnimationClips.Resolve(InHouse, slot));

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
    public void ExactSlotNameBeatsAnAlias()
    {
        // Order in the list is deliberately hostile: the alias appears first.
        string[] both = { "Bite", "Attack" };
        Assert.Equal("Attack", AnimationClips.Resolve(both, "attack"));
    }

    [Fact]
    public void BareNameWithoutPrefixStillWorks()
    {
        string[] mixamoish = { "mixamorig|Idle", "Run" };
        Assert.Equal("mixamorig|Idle", AnimationClips.Resolve(mixamoish, "idle"));
        Assert.Equal("Run", AnimationClips.Resolve(mixamoish, "run"));
    }

    [Fact]
    public void TrailingBarIsNotTreatedAsAPrefix()
    {
        // A name ending in '|' would slice to empty and match every slot; it must not.
        Assert.Equal(string.Empty, AnimationClips.Resolve(new[] { "Idle|" }, "run"));
    }
}
