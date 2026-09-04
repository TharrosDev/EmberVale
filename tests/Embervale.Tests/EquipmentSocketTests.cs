using System.Collections.Generic;
using System.Linq;
using Embervale.Animation;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The socket contract (the 2026-09-04 combat/animation overhaul).
///
/// ⚠️ <b>Every defect this contract replaces was a bone-name defect, and every one of them was
/// silent.</b> The player's visual sword was <c>QueueFree</c>d on every spawn for an entire phase
/// because one call site knew only <c>RightHand</c> while the adopted bodies all say <c>Wrist.R</c>;
/// nothing logged, and a sword that is not there looks exactly like a build that never had one.
/// These tests are cheap precisely because that class of bug is not.
/// </summary>
public class EquipmentSocketTests
{
    [Fact]
    public void EverySocketDeclaresAtLeastOneBone()
    {
        foreach (EquipmentSocket socket in EquipmentSockets.All)
        {
            Assert.NotEmpty(EquipmentSockets.BoneNames(socket));
        }
    }

    [Fact]
    public void EverySocketInTheEnumIsInTheContract()
    {
        // A socket added to the enum and forgotten in the table resolves to nothing on every rig in
        // the game, and the only symptom is a piece that never appears.
        var declared = new HashSet<EquipmentSocket>(EquipmentSockets.All);
        foreach (EquipmentSocket socket in System.Enum.GetValues<EquipmentSocket>())
        {
            Assert.Contains(socket, declared);
        }
    }

    [Theory]
    [InlineData(EquipmentSocket.HandR, "RightHand")]
    [InlineData(EquipmentSocket.HandL, "LeftHand")]
    [InlineData(EquipmentSocket.Head, "Head")]
    [InlineData(EquipmentSocket.Chest, "Chest")]
    [InlineData(EquipmentSocket.Hips, "Hips")]
    [InlineData(EquipmentSocket.ShoulderL, "LeftUpperArm")]
    [InlineData(EquipmentSocket.ShoulderR, "RightUpperArm")]
    public void TheProfileNameIsTriedFirst(EquipmentSocket socket, string profileBone)
    {
        // 31 of the 33 humanoids are retargeted onto SkeletonProfileHumanoid, so the profile name is
        // the answer on almost every body. Putting it anywhere but first would make the common case
        // depend on a fallback.
        Assert.Equal(profileBone, EquipmentSockets.BoneNames(socket)[0]);
    }

    [Theory]
    [InlineData(EquipmentSocket.HandR, "Wrist.R")]
    [InlineData(EquipmentSocket.HandL, "Wrist.L")]
    public void TheQuaterniusNameIsStillAccepted(EquipmentSocket socket, string packBone)
    {
        // "wrist" is what the vendored packs call the hand, and its absence from one call site is
        // the whole of the missing-sword bug. It stays reachable.
        Assert.True(EquipmentSockets.Accepts(socket, packBone));
    }

    [Theory]
    [InlineData("Hand_R", "handr")]
    [InlineData("Hand.R", "handr")]
    [InlineData("hand-r", "handr")]
    [InlineData("mixamorig:RightHand", "mixamorigrighthand")]
    public void SpellingIsNormalisedAwayButLettersAreNot(string bone, string expected) =>
        Assert.Equal(expected, EquipmentSockets.Normalize(bone));

    [Fact]
    public void NormalisationDoesNotMakeDifferentBonesEqual()
    {
        // The loose match runs only after every exact candidate has failed, and it must still not
        // confuse a left bone for a right one.
        Assert.NotEqual(EquipmentSockets.Normalize("Hand.R"), EquipmentSockets.Normalize("Hand.L"));
        Assert.False(EquipmentSockets.Accepts(EquipmentSocket.HandR, "LeftHand"));
        Assert.False(EquipmentSockets.Accepts(EquipmentSocket.HandL, "RightHand"));
    }

    [Fact]
    public void HeldThingsTakeTheBonesOrientationAndWornThingsDoNot()
    {
        // The distinction the two hand-rolled followers existed for. A sword must roll with the
        // wrist; a pauldron authored upright must stay upright on every rig in the cast, because the
        // retargeted bodies do not share bone-local axes.
        Assert.Equal(SocketSpace.BoneLocal, EquipmentSockets.SpaceOf(EquipmentSocket.HandR));
        Assert.Equal(SocketSpace.BoneLocal, EquipmentSockets.SpaceOf(EquipmentSocket.HandL));
        Assert.Equal(SocketSpace.BoneLocal, EquipmentSockets.SpaceOf(EquipmentSocket.Bow));

        Assert.Equal(SocketSpace.BodyAligned, EquipmentSockets.SpaceOf(EquipmentSocket.ShoulderL));
        Assert.Equal(SocketSpace.BodyAligned, EquipmentSockets.SpaceOf(EquipmentSocket.Chest));
        Assert.Equal(SocketSpace.BodyAligned, EquipmentSockets.SpaceOf(EquipmentSocket.Hips));
        Assert.Equal(SocketSpace.BodyAligned, EquipmentSockets.SpaceOf(EquipmentSocket.BackPrimary));
        Assert.Equal(SocketSpace.BodyAligned, EquipmentSockets.SpaceOf(EquipmentSocket.Quiver));
    }

    [Fact]
    public void AShieldStrapsToTheForearmRatherThanTheHand()
    {
        // Hung off the wrist a strapped shield counter-rotates with every grip roll in the
        // animation and reads as spinning on the arm.
        Assert.Equal("LeftLowerArm", EquipmentSockets.BoneNames(EquipmentSocket.Shield)[0]);
        Assert.Equal(SocketSpace.BodyAligned, EquipmentSockets.SpaceOf(EquipmentSocket.Shield));
    }

    [Fact]
    public void NoSocketListsTheSameBoneTwice()
    {
        // ⚠️ EXACT names, not normalised ones. "Hand.R" and "Hand_R" normalise to the same thing but
        // are NOT redundant: the first pass is Skeleton3D.FindBone, which matches the literal
        // string, so a rig that spells it either way is caught there rather than falling through to
        // the punctuation-insensitive sweep. The redundancy worth failing on is the same string
        // listed twice.
        foreach (EquipmentSocket socket in EquipmentSockets.All)
        {
            IReadOnlyList<string> bones = EquipmentSockets.BoneNames(socket);
            Assert.Equal(bones.Count, bones.Distinct().Count());
        }
    }

    [Fact]
    public void TheLeftAndRightSocketsShareNoBone()
    {
        // A left/right pair that overlaps would attach both pauldrons to one arm on any rig missing
        // one of them — and the piece that "did not appear" would in fact be inside the other one.
        (EquipmentSocket, EquipmentSocket)[] pairs =
        {
            (EquipmentSocket.HandR, EquipmentSocket.HandL),
            (EquipmentSocket.ShoulderR, EquipmentSocket.ShoulderL),
        };

        foreach ((EquipmentSocket right, EquipmentSocket left) in pairs)
        {
            var rightBones = EquipmentSockets.BoneNames(right).Select(EquipmentSockets.Normalize).ToHashSet();
            foreach (string bone in EquipmentSockets.BoneNames(left))
            {
                Assert.DoesNotContain(EquipmentSockets.Normalize(bone), rightBones);
            }
        }
    }
}
