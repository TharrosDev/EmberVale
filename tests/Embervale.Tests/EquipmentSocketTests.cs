using System.Collections.Generic;
using System.Linq;
using Embervale.Animation;
using Godot;
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

    // ----- where the authored transform goes -----
    //
    // ⚠️ A held piece's offset, grip rotation and scale used to be set on the BoneAttachment3D it
    // hangs from. The engine writes the bone's pose over that node's transform on every skeleton
    // update, so all three were discarded: every weapon in the game sat at the hand bone's raw
    // orientation and at scale 1. They are now the piece's own local transform
    // (WeaponGrip.Local), and bone pose x that transform is the intended result. The engine half
    // (that the piece really follows the hand) is tools/equipment_socket_probe.gd and a render.

    private static readonly Vector3 Blade = new Vector3(-0.30f, 0.25f, -0.90f).Normalized();

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True((expected - actual).Length() < tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void AnUnauthoredPieceSitsExactlyOnItsBone()
    {
        // A shield, a helm: no offset, rotation or scale passed. Nothing about them may move.
        Assert.Equal(Transform3D.Identity, WeaponGrip.Local(Vector3.Zero, Vector3.Zero, Vector3.One));
    }

    [Fact]
    public void TheGripRotationSurvivesTheTripThroughDegrees()
    {
        Transform3D local = WeaponGrip.Local(Vector3.Zero, WeaponGrip.HandRotationDegrees, Vector3.One);

        // The weapon's long axis (+Y) comes out along the blade direction the grip was measured to.
        AssertClose(Blade, local.Basis * Vector3.Up);
        AssertClose(WeaponGrip.Hand.Column0, local.Basis.Column0);
        AssertClose(WeaponGrip.Hand.Column2, local.Basis.Column2);
        AssertClose(Vector3.Zero, local.Origin);
    }

    [Fact]
    public void TheGripIsARotationAndNothingElse()
    {
        Basis hand = WeaponGrip.Hand;

        Assert.Equal(1f, hand.Determinant(), 4);
        Assert.Equal(1f, hand.Column0.Length(), 4);
        Assert.Equal(1f, hand.Column1.Length(), 4);
        Assert.Equal(1f, hand.Column2.Length(), 4);
        Assert.Equal(0f, hand.Column0.Dot(hand.Column1), 4);
        Assert.Equal(0f, hand.Column1.Dot(hand.Column2), 4);
    }

    [Theory]
    [InlineData(1.5f)]   // the Iron King's mace
    [InlineData(0.6f)]
    public void AScaledWeaponKeepsItsGripAndChangesOnlyItsSize(float scale)
    {
        Transform3D plain = WeaponGrip.Local(Vector3.Zero, WeaponGrip.HandRotationDegrees, Vector3.One);
        Transform3D scaled = WeaponGrip.Local(Vector3.Zero, WeaponGrip.HandRotationDegrees, Vector3.One * scale);

        AssertClose(plain.Basis.Column0 * scale, scaled.Basis.Column0);
        AssertClose(plain.Basis.Column1 * scale, scaled.Basis.Column1);
        AssertClose(plain.Basis.Column2 * scale, scaled.Basis.Column2);

        // A point one metre up the blade is `scale` metres out along it.
        AssertClose(Blade * scale, scaled * Vector3.Up);
    }

    [Fact]
    public void ScaleIsAlongThePiecesOwnAxesNotTheBones()
    {
        // The Node3D convention (rotation x scale). A piece lengthened along its own +Y gets longer
        // along the blade, whichever way the grip turned it.
        Transform3D local = WeaponGrip.Local(Vector3.Zero, WeaponGrip.HandRotationDegrees, new Vector3(1f, 2f, 1f));

        AssertClose(Blade * 2f, local.Basis.Column1);
        AssertClose(WeaponGrip.Hand.Column0, local.Basis.Column0);
    }

    [Fact]
    public void BonePoseTimesTheLocalTransformIsWhatTheAttachmentWasMeantToBe()
    {
        // What "a bone attachment with this Position, RotationDegrees and Scale" describes: the
        // bone's pose, then the offset in the bone's axes, then the turn, then the size.
        var pose = new Transform3D(Basis.FromEuler(new Vector3(0.3f, 1.1f, -0.4f)), new Vector3(1f, 2f, 3f));
        var offset = new Vector3(0.1f, -0.2f, 0.05f);
        const float Scale = 1.5f;

        Transform3D world = pose * WeaponGrip.Local(offset, WeaponGrip.HandRotationDegrees, Vector3.One * Scale);

        // The grip point lands at the bone plus the offset turned into the bone's axes.
        AssertClose(pose.Origin + (pose.Basis * offset), world.Origin);

        // A point p on the weapon lands at bone * (offset + grip * (scale * p)).
        var p = new Vector3(0.02f, 0.9f, -0.01f);
        AssertClose(pose * (offset + (WeaponGrip.Hand * (p * Scale))), world * p);
    }
}
