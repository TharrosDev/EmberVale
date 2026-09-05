using System;
using System.Collections.Generic;
using Godot;

namespace Embervale.Animation;

/// <summary>
/// The canonical places equipment hangs on a body. <b>One vocabulary for the whole cast</b> — player,
/// NPC, enemy, companion and boss — so a shield is attached to <see cref="Shield"/> once rather than
/// to <c>Wrist.L</c> here, <c>LeftHand</c> there and <c>Hand_L</c> somewhere else.
/// </summary>
// APPEND ONLY: ordinals may reach a .tres — never reorder/insert/remove (EnumStabilityTests).
public enum EquipmentSocket
{
    /// <summary>The dominant hand. A drawn one-handed weapon lives here.</summary>
    HandR,

    /// <summary>The off hand. A torch, a second blade, a spell focus.</summary>
    HandL,

    /// <summary>Sheathed primary weapon, across the back.</summary>
    BackPrimary,

    /// <summary>Sheathed secondary, or a slung two-hander.</summary>
    BackSecondary,

    /// <summary>Scabbard or sheathed blade at the right hip.</summary>
    HipR,

    /// <summary>Scabbard, pouch or tool at the left hip.</summary>
    HipL,

    /// <summary>A shield, on the off arm rather than in the off hand — a strapped shield does not
    /// rotate with the wrist.</summary>
    Shield,

    /// <summary>A bow, held or slung.</summary>
    Bow,

    /// <summary>A quiver, on the back or hip depending on the piece.</summary>
    Quiver,

    /// <summary>Helm, crown, crest.</summary>
    Head,

    /// <summary>Torso pieces — plate, tabard, mantle, cape.</summary>
    Chest,

    /// <summary>Belt-line pieces — pouches, keys, rings of tools.</summary>
    Hips,

    ShoulderR,
    ShoulderL,
}

/// <summary>
/// How a piece is oriented once it is following its bone. The two answers are genuinely different
/// and both are needed, which is why this is a flag rather than one behaviour.
/// </summary>
// APPEND ONLY: ordinals may reach a .tres — never reorder/insert/remove (EnumStabilityTests).
public enum SocketSpace
{
    /// <summary>
    /// The piece takes the bone's own orientation, so it rolls with the wrist and swings with the
    /// arm. What a held weapon wants. Implemented with a native <see cref="BoneAttachment3D"/>,
    /// which costs no per-frame script at all.
    /// </summary>
    BoneLocal,

    /// <summary>
    /// The piece keeps the CHARACTER's axes and only follows the bone's animated *delta* from its
    /// rest pose.
    ///
    /// ⚠️ <b>This is not a nicety, and it is why the kit followers were hand-rolled.</b> The
    /// retargeted bodies do not share bone-local axes — a pauldron authored upright against one
    /// body's <c>LeftUpperArm</c> lies on its side on the next. Factoring the rest orientation out
    /// makes an authored offset mean the same thing on every rig in the cast.
    /// </summary>
    BodyAligned,
}

/// <summary>
/// Resolves an <see cref="EquipmentSocket"/> to a real bone on a real skeleton, and says how a piece
/// on it should be oriented.
///
/// <para><b>Why this exists.</b> Before it there were five attachment implementations —
/// <c>PlayerFactory.AttachWeaponVisual</c>, <c>PlayerFactory.AttachGear</c>, <c>NpcKitFollower</c>,
/// <c>EnemyKitFollower</c> and the mount's direct reparent — three of which
/// <c>docs/3D_ASSETS.md</c> named as known duplication. Each carried its own bone-name guessing:
/// one knew <c>Wrist.R</c>, another knew <c>RightHand</c>, a third normalised punctuation, and the
/// player's knew only the profile name and silently <c>QueueFree</c>d the sword when it missed.
/// A bone-name heuristic that misses is invisible — a sword that is not there looks exactly like a
/// build that never had one.</para>
/// </summary>
public static class EquipmentSockets
{
    /// <summary>What a socket needs from a rig: the bone names to try, in order, and the space a
    /// piece on it is oriented in by default.</summary>
    public readonly record struct Binding(string[] BoneNames, SocketSpace Space);

    /// <summary>
    /// The contract. Profile names first — a rig retargeted onto <c>SkeletonProfileHumanoid</c>
    /// names these exactly, so on the 31 retargeted bodies the first candidate always wins. The rest
    /// are the words the un-retargeted packs use, and they are the fallback rather than the rule.
    /// </summary>
    private static readonly Dictionary<EquipmentSocket, Binding> Bindings = new()
    {
        [EquipmentSocket.HandR] = new(
            new[] { "RightHand", "Wrist.R", "Hand.R", "Hand_R", "mixamorig_RightHand" },
            SocketSpace.BoneLocal),
        [EquipmentSocket.HandL] = new(
            new[] { "LeftHand", "Wrist.L", "Hand.L", "Hand_L", "mixamorig_LeftHand" },
            SocketSpace.BoneLocal),

        // Sheathed and worn pieces are body-aligned: a scabbard hangs off the body, not off the
        // spine bone's local roll.
        [EquipmentSocket.BackPrimary] = new(
            new[] { "UpperChest", "Chest", "Spine", "Torso", "Back" }, SocketSpace.BodyAligned),
        [EquipmentSocket.BackSecondary] = new(
            new[] { "UpperChest", "Chest", "Spine", "Torso", "Back" }, SocketSpace.BodyAligned),
        [EquipmentSocket.HipR] = new(new[] { "Hips", "Pelvis", "Torso" }, SocketSpace.BodyAligned),
        [EquipmentSocket.HipL] = new(new[] { "Hips", "Pelvis", "Torso" }, SocketSpace.BodyAligned),

        // ⚠️ A shield straps to the FOREARM, not the hand. Hung off the wrist bone it counter-rotates
        // with every grip roll in the animation and reads as spinning on the arm.
        [EquipmentSocket.Shield] = new(
            new[] { "LeftLowerArm", "LeftForeArm", "Forearm.L", "LeftHand", "Wrist.L" },
            SocketSpace.BodyAligned),

        [EquipmentSocket.Bow] = new(
            new[] { "LeftHand", "Wrist.L", "Hand.L", "Hand_L" }, SocketSpace.BoneLocal),
        [EquipmentSocket.Quiver] = new(
            new[] { "UpperChest", "Chest", "Spine", "Torso" }, SocketSpace.BodyAligned),

        [EquipmentSocket.Head] = new(new[] { "Head", "Skull" }, SocketSpace.BoneLocal),
        [EquipmentSocket.Chest] = new(
            new[] { "Chest", "UpperChest", "Spine", "Torso" }, SocketSpace.BodyAligned),
        [EquipmentSocket.Hips] = new(new[] { "Hips", "Pelvis", "Torso" }, SocketSpace.BodyAligned),
        [EquipmentSocket.ShoulderR] = new(
            new[] { "RightUpperArm", "RightArm", "UpperArm.R", "Arm.R" }, SocketSpace.BodyAligned),
        [EquipmentSocket.ShoulderL] = new(
            new[] { "LeftUpperArm", "LeftArm", "UpperArm.L", "Arm.L" }, SocketSpace.BodyAligned),
    };

    /// <summary>Every socket in the contract, for validation and probes.</summary>
    public static IEnumerable<EquipmentSocket> All => Bindings.Keys;

    /// <summary>The space a piece on this socket is oriented in unless the caller overrides it.</summary>
    public static SocketSpace SpaceOf(EquipmentSocket socket) =>
        Bindings.TryGetValue(socket, out Binding b) ? b.Space : SocketSpace.BoneLocal;

    /// <summary>The candidate bone names for a socket, in preference order.</summary>
    public static IReadOnlyList<string> BoneNames(EquipmentSocket socket) =>
        Bindings.TryGetValue(socket, out Binding b) ? b.BoneNames : Array.Empty<string>();

    /// <summary>
    /// The bone index this socket resolves to on this skeleton, or <c>-1</c>.
    ///
    /// <para><b>-1 is a valid answer and callers must treat it as one</b> — a wolf has no hand and a
    /// dragon has no hip. What is not acceptable is resolving to the wrong bone, which is why the
    /// punctuation-insensitive sweep runs only after every declared candidate has been tried
    /// exactly.</para>
    /// </summary>
    /// <param name="preferredBone">An exact bone name to try before the socket's own candidates.
    /// ⚠️ This is what lets the authored kit profiles keep naming the bone they were placed
    /// against. Sixty of them do, and several sit on quadruped rigs that carry BOTH a
    /// <c>Spine</c> and a <c>Torso</c> — resolving those through the humanoid preference order
    /// would silently move a carapace up the animal's back. An authored bone wins over a
    /// contract default; the contract is the fallback, not an override.</param>
    public static int Resolve(Skeleton3D skeleton, EquipmentSocket socket, string preferredBone = "")
    {
        if (preferredBone.Length > 0)
        {
            int preferred = skeleton.FindBone(preferredBone);
            if (preferred >= 0)
            {
                return preferred;
            }
        }

        foreach (string candidate in BoneNames(socket))
        {
            int bone = skeleton.FindBone(candidate);
            if (bone >= 0)
            {
                return bone;
            }
        }

        // Last resort: the same name with punctuation and case thrown away, which is what catches
        // "Hand_R" against "hand.r" and the handful of packs that agree on the word but not the
        // spelling. Absorbed from EnemyVisualKit.FindBone, which was the only place that had it.
        var loose = new List<string>(BoneNames(socket));
        if (preferredBone.Length > 0)
        {
            loose.Insert(0, preferredBone);
        }

        foreach (string candidate in loose)
        {
            string wanted = Normalize(candidate);
            for (int index = 0; index < skeleton.GetBoneCount(); index++)
            {
                if (Normalize(skeleton.GetBoneName(index)) == wanted)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Case- and punctuation-insensitive bone-name comparison. Pure, so the alias table above can be
    /// tested without an engine — which matters, because every defect this file replaces was a
    /// name-matching one.
    /// </summary>
    public static string Normalize(string value) => value
        .Replace(".", string.Empty, StringComparison.Ordinal)
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .Replace(":", string.Empty, StringComparison.Ordinal)
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant();

    /// <summary>Whether a bone name is one this socket accepts, ignoring spelling. The pure half of
    /// <see cref="Resolve"/>.</summary>
    public static bool Accepts(EquipmentSocket socket, string boneName)
    {
        string normalized = Normalize(boneName);
        foreach (string candidate in BoneNames(socket))
        {
            if (Normalize(candidate) == normalized)
            {
                return true;
            }
        }

        return false;
    }
}
