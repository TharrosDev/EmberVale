using System.Collections.Generic;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Npc;
using Godot;

namespace Embervale.Animation;

/// <summary>
/// The one place anything is hung on a body. Weapons, shields, bows, quivers, helms, pauldrons,
/// pouches, NPC outfit pieces and enemy identity pieces — player, NPC, enemy, companion and boss all
/// go through this.
///
/// <para><b>What it replaced.</b> Five implementations of nearly the same thing:
/// <c>PlayerFactory.AttachWeaponVisual</c> (a <see cref="BoneAttachment3D"/> with a hand-derived
/// basis), <c>PlayerFactory.AttachGear</c> (a plain <see cref="BoneAttachment3D"/>),
/// <c>NpcKitFollower</c> and <c>EnemyKitFollower</c> (byte-identical hand-rolled followers), and the
/// mount's direct reparent. Three of them were named as known duplication in
/// <c>docs/3D_ASSETS.md</c>, left alone because collapsing them needed a full motion review. That
/// review is this stage.</para>
///
/// <para><b>The motion review's answer, and why it is one component and two mechanisms.</b> The two
/// behaviours were both correct and genuinely different. A held weapon must take the hand bone's own
/// orientation; a strapped pauldron must keep the character's axes and follow only the bone's
/// animated delta, because the retargeted bodies do not share bone-local axes. So
/// <see cref="SocketSpace"/> chooses, and <see cref="SocketSpace.BoneLocal"/> uses the engine's own
/// <see cref="BoneAttachment3D"/> rather than a script — which costs no per-frame C# at all, and
/// matters across a cast of 33.</para>
/// </summary>
[GlobalClass]
public partial class EquipmentPresentationComponent : EntityComponent
{
    /// <summary>Node name of the visual root the <see cref="Skeleton3D"/> lives under — the same
    /// convention <see cref="CharacterAnimationComponent.BodyMeshPath"/> uses, because it is the
    /// same node.</summary>
    [Export] public string BodyMeshPath { get; set; } = "BodyMesh";

    /// <summary>The rig everything hangs on, or null for an actor with no skeleton (a greybox
    /// stand-in, a prop-bodied enemy). Null is a valid state and every method below tolerates it.</summary>
    public Skeleton3D? Skeleton { get; private set; }

    /// <summary>
    /// Pieces to hang the moment the rig is found.
    ///
    /// A factory builds an actor detached and adds it to the tree afterwards, so there is no
    /// skeleton to attach to at build time — the repo's own convention is to set component
    /// properties before <c>AddChild</c> and let <see cref="OnInitialize"/> act on them (CLAUDE.md
    /// §6). This is that: the player's factory queues its sword and its pauldrons, and they land
    /// when the body does.
    /// </summary>
    public List<PendingPiece> Pending { get; } = new();

    /// <summary>One queued attachment. The arguments of <see cref="Attach(EquipmentSocket, string,
    /// string, Vector3, Vector3, Vector3?, SocketSpace?, string)"/>, held until there is a rig.</summary>
    public readonly record struct PendingPiece(
        EquipmentSocket Socket,
        string ScenePath,
        string Name,
        Vector3 Offset = default,
        Vector3 RotationDegrees = default,
        Vector3? Scale = null,
        SocketSpace? Space = null);

    private readonly Dictionary<string, Node3D> _attached = new();

    protected override void OnInitialize()
    {
        // Found independently rather than asked of CharacterAnimationComponent: a sibling's
        // OnInitialize is not guaranteed to have run (CLAUDE.md §7), and the walk is cheap and once.
        if (Entity!.Body.GetNodeOrNull<Node3D>(BodyMeshPath) is { } bodyRoot)
        {
            Skeleton = FindSkeleton(bodyRoot);
        }

        // The identity kits attach here rather than from CharacterAnimationComponent, which is
        // where they used to live. An outfit is not an animation concern, and the coupling is what
        // made "the component that plays clips" also the component that owned five bone-name
        // heuristics. Both kits no-op for an actor whose TemplateId has no profile.
        NpcVisualKit.Attach(Entity, this);
        EnemyVisualKit.Attach(Entity, this);

        foreach (PendingPiece piece in Pending)
        {
            Attach(piece.Socket, piece.ScenePath, piece.Name, piece.Offset, piece.RotationDegrees,
                piece.Scale, piece.Space);
        }

        Pending.Clear();
    }

    /// <summary>True when this body can carry equipment at all.</summary>
    public bool HasRig => Skeleton != null;

    /// <summary>Whether this rig has the bone a socket needs. A quadruped answers false for hands,
    /// which is correct rather than a failure.</summary>
    public bool Supports(EquipmentSocket socket) =>
        Skeleton != null && EquipmentSockets.Resolve(Skeleton, socket) >= 0;

    /// <summary>The bone a socket lands on, for probes and diagnostics. Empty when unsupported.</summary>
    public string BoneFor(EquipmentSocket socket)
    {
        if (Skeleton is not { } skeleton)
        {
            return string.Empty;
        }

        int bone = EquipmentSockets.Resolve(skeleton, socket);
        return bone < 0 ? string.Empty : skeleton.GetBoneName(bone);
    }

    /// <summary>
    /// Hangs a scene on a socket under <paramref name="name"/>, replacing whatever was there.
    /// Returns the attached visual, or null when the model or the bone is missing.
    /// </summary>
    /// <param name="space">Overrides the socket's default orientation. Pass null for the default,
    /// which is almost always right.</param>
    public Node3D? Attach(
        EquipmentSocket socket,
        string scenePath,
        string name,
        Vector3 offset = default,
        Vector3 rotationDegrees = default,
        Vector3? scale = null,
        SocketSpace? space = null,
        string preferredBone = "")
    {
        if (GD.Load<PackedScene>(scenePath)?.Instantiate() is not Node3D visual)
        {
            GD.PushWarning($"{Entity?.DisplayName}: equipment '{name}' could not load '{scenePath}'.");
            return null;
        }

        Node3D? mounted = Attach(socket, visual, name, offset, rotationDegrees, scale, space, preferredBone);
        if (mounted == null)
        {
            visual.QueueFree();
        }

        return mounted;
    }

    /// <summary>The already-instantiated overload — the kits pull their pieces out of one shared
    /// scene, so they have a node rather than a path.</summary>
    public Node3D? Attach(
        EquipmentSocket socket,
        Node3D visual,
        string name,
        Vector3 offset = default,
        Vector3 rotationDegrees = default,
        Vector3? scale = null,
        SocketSpace? space = null,
        string preferredBone = "")
    {
        if (Skeleton is not { } skeleton)
        {
            return null;
        }

        int bone = EquipmentSockets.Resolve(skeleton, socket, preferredBone);
        if (bone < 0)
        {
            // ⚠️ SAID OUT LOUD, ALWAYS. A bone-name miss is the single most common defect this file
            // exists to end, and it is completely invisible otherwise: the piece is simply not
            // there, which looks exactly like a build that never had one. The player's visual sword
            // was being QueueFree'd on every spawn for an entire phase because nothing said this.
            GD.PushWarning(
                $"{Entity?.DisplayName}: socket {socket} has no bone on this rig " +
                $"(tried {string.Join(", ", EquipmentSockets.BoneNames(socket))}); '{name}' not attached.");
            return null;
        }

        Detach(name);

        Vector3 finalScale = scale ?? Vector3.One;
        SocketSpace resolved = space ?? EquipmentSockets.SpaceOf(socket);
        Node3D mount = resolved == SocketSpace.BoneLocal
            ? new BoneAttachment3D
            {
                Name = $"Socket_{name}",
                BoneName = skeleton.GetBoneName(bone),
                Position = offset,
                RotationDegrees = rotationDegrees,
                Scale = finalScale,
            }
            : new SocketFollower
            {
                Name = $"Socket_{name}",
                Skeleton = skeleton,
                BoneIndex = bone,
                Offset = offset,
                AuthoredRotation = rotationDegrees * (Mathf.Pi / 180f),
                VisualScale = finalScale,
            };

        skeleton.AddChild(mount);

        // Reparent rather than AddChild: the kits hand us a node that is already in a scene they
        // are about to free, and keepGlobalTransform would drag that scene's placement along.
        if (visual.GetParent() is { } existing)
        {
            visual.Owner = null;
            visual.Reparent(mount, keepGlobalTransform: false);
        }
        else
        {
            mount.AddChild(visual);
        }

        visual.Transform = Transform3D.Identity;
        visual.Name = name;
        _attached[name] = mount;
        return visual;
    }

    /// <summary>
    /// The plain-signature door for GDScript.
    ///
    /// ⚠️ <c>Attach</c> itself is unreachable from a <c>.gd</c> probe: it is overloaded, and its
    /// optional <c>Vector3?</c> / <c>SocketSpace?</c> parameters do not marshal. Godot's binding
    /// layer simply reports the method as nonexistent, which reads as a missing method rather than
    /// an unmarshalable signature. This exists so `equipment_socket_probe.gd` can prove against the
    /// real rigs that a hung piece actually follows its bone — the half no unit test can reach.
    /// </summary>
    public Node3D? AttachSimple(int socket, string scenePath, string name) =>
        Attach((EquipmentSocket)socket, scenePath, name);

    /// <summary>Removes a previously attached piece. Silent when there was none.</summary>
    public void Detach(string name)
    {
        if (_attached.Remove(name, out Node3D? mount) && GodotObject.IsInstanceValid(mount))
        {
            mount.QueueFree();
        }
    }

    /// <summary>Shows or hides a piece without rebuilding it — how a scabbard keeps its blade while
    /// the blade is drawn.</summary>
    public void SetVisible(string name, bool visible)
    {
        if (_attached.TryGetValue(name, out Node3D? mount) && GodotObject.IsInstanceValid(mount))
        {
            mount.Visible = visible;
        }
    }

    /// <summary>True when something is currently hanging under this name.</summary>
    public bool IsAttached(string name) =>
        _attached.TryGetValue(name, out Node3D? mount) && GodotObject.IsInstanceValid(mount);

    protected override void OnTeardown()
    {
        _attached.Clear();
        Skeleton = null;
    }

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton)
        {
            return skeleton;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindSkeleton(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}

/// <summary>
/// Follows a bone's animated delta while keeping the piece in the CHARACTER's axes rather than the
/// bone's.
///
/// <para>⚠️ <b>Do not replace this with a <see cref="BoneAttachment3D"/>.</b> That is the same
/// warning <c>docs/3D_ASSETS.md</c> carried about the two followers this replaces, and it is still
/// true: the retargeted bodies do not share bone-local axes, so a pauldron authored upright against
/// one body's upper arm lies on its side on the next. Factoring the bone's rest orientation out —
/// <c>pose · rest⁻¹</c> — is what makes one authored offset mean the same thing on every rig in
/// the cast.</para>
///
/// <para>The arithmetic is unchanged from <c>NpcKitFollower</c> and <c>EnemyKitFollower</c>, which
/// were byte-identical to each other. This is the surviving copy.</para>
/// </summary>
internal sealed partial class SocketFollower : Node3D
{
    public required Skeleton3D Skeleton { get; init; }
    public int BoneIndex { get; init; }
    public Vector3 Offset { get; init; }
    public Vector3 AuthoredRotation { get; init; }
    public Vector3 VisualScale { get; init; } = Vector3.One;

    public override void _Ready()
    {
        TopLevel = true;
        Follow();
    }

    public override void _Process(double delta) => Follow();

    private void Follow()
    {
        if (!GodotObject.IsInstanceValid(Skeleton))
        {
            return;
        }

        Transform3D rest = Skeleton.GetBoneGlobalRest(BoneIndex);
        Transform3D pose = Skeleton.GetBoneGlobalPose(BoneIndex);
        Basis skeletonBasis = Skeleton.GlobalTransform.Basis.Orthonormalized();
        Basis delta = (pose.Basis * rest.Basis.Inverse()).Orthonormalized();
        Basis authored = Basis.FromEuler(AuthoredRotation);
        Basis finalBasis = (skeletonBasis * delta * authored).Scaled(VisualScale);
        Vector3 origin = (Skeleton.GlobalTransform * pose).Origin + skeletonBasis * (delta * Offset);
        GlobalTransform = new Transform3D(finalBasis, origin);
    }
}
