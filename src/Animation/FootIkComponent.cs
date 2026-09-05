using Embervale.Entities;
using Godot;

namespace Embervale.Animation;

/// <summary>
/// Plants a character's feet on the ground it is actually standing on.
///
/// <para><b>What it fixes.</b> There was no IK of any kind in this repo. Animation is authored on
/// flat ground, so on the realm's real terrain — which after the world-generation replacement has
/// 43 m of relief in Ember Crown and 98 m in Frostfang — a character's feet hang above a downhill
/// slope and sink into an uphill one. Every NPC in a settlement on a hillside floats or wades.</para>
///
/// <para><b>How.</b> A ray per foot finds the ground, <see cref="FootPlacement"/> decides how far to
/// lift each and how far to drop the pelvis so the low leg does not hyperextend, and the result is
/// written as a bone override after the animation has posed the skeleton. A
/// <see cref="SkeletonModifier3D"/> is the engine's own hook for exactly this, and it runs at the
/// right point in the frame without this component racing the AnimationTree.</para>
///
/// <para>⚠️ <b>It is off far more often than it is on, deliberately.</b> Airborne, mid-action, out of
/// view or beyond <see cref="MaxDistance"/>, the correction fades out and the rays stop being cast.
/// A jumping character has no ground worth meeting; a warping one has something else owning its
/// position; and paying two raycasts a frame for every actor in a region is exactly the "expensive
/// IK at unlimited range" §22 warns about.</para>
/// </summary>
[GlobalClass]
public partial class FootIkComponent : EntityComponent
{
    /// <summary>How far above the foot the ground ray starts.</summary>
    [Export] public float ProbeAbove { get; set; } = 0.5f;

    /// <summary>How far below the foot the ray looks. Longer than any step this game allows, and
    /// short enough that a foot over a ledge finds nothing rather than the valley floor.</summary>
    [Export] public float ProbeBelow { get; set; } = 0.6f;

    /// <summary>Height of the ankle above the sole, so the foot BONE lands a foot's thickness above
    /// the ground rather than in it.</summary>
    [Export] public float AnkleHeight { get; set; } = 0.1f;

    [Export] public float MaxLift { get; set; } = 0.35f;
    [Export] public float MaxDrop { get; set; } = 0.35f;

    /// <summary>How far the ankle may roll to match a slope.</summary>
    [Export] public float MaxSlopeDegrees { get; set; } = 35f;

    /// <summary>Seconds the correction takes to fade in or out.</summary>
    [Export] public float BlendSeconds { get; set; } = 0.15f;

    /// <summary>Beyond this, in metres from the camera, the feet are nobody's business. Two rays per
    /// actor per frame across a loaded region is not free, and at 25 m a boot is a few pixels.</summary>
    [Export] public float MaxDistance { get; set; } = 25f;

    private Skeleton3D? _skeleton;
    private FootIkModifier? _modifier;

    protected override void OnInitialize()
    {
        // Factories name the visual "Mesh"; the player and scene NPCs name it "BodyMesh".
        Node3D? root = Entity!.Body.GetNodeOrNull<Node3D>("BodyMesh")
                       ?? Entity.Body.GetNodeOrNull<Node3D>("Mesh");
        if (root == null)
        {
            return;
        }

        _skeleton = FindSkeleton(root);
        if (_skeleton == null)
        {
            return;
        }

        // ⚠️ The modifier is parented to the SKELETON, not to this component. Godot only runs a
        // SkeletonModifier3D that is a direct child of the skeleton it modifies, and one parented
        // anywhere else is simply never called — with no error, and feet that never move.
        _modifier = new FootIkModifier { Name = "FootIk", Ik = this };
        _skeleton.CallDeferred(Node.MethodName.AddChild, _modifier);
    }

    protected override void OnTeardown()
    {
        if (_modifier != null && GodotObject.IsInstanceValid(_modifier))
        {
            _modifier.QueueFree();
        }

        _modifier = null;
        _skeleton = null;
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
/// The half that runs inside the skeleton's own modification pass, after the AnimationTree has posed
/// it and before the pose is committed.
/// </summary>
internal sealed partial class FootIkModifier : SkeletonModifier3D
{
    /// <summary>The component holding the tuning. ⚠️ NOT called "Owner" — Node already has an Owner
    /// and shadowing it is the kind of thing that compiles for a while and then hands somebody the
    /// wrong object.</summary>
    public required FootIkComponent Ik { get; init; }

    private int _leftFoot = -1;
    private int _rightFoot = -1;
    private int _hips = -1;
    private float _weight;
    private float _pelvis;

    // ⚠️ ...WithDelta, not _ProcessModification. Godot 4.7 declares both; the plain one takes no
    // arguments, so overriding it with a delta silently overrides nothing and the feet never move.
    public override void _ProcessModificationWithDelta(double delta)
    {
        if (GetSkeleton() is not { } skeleton || !GodotObject.IsInstanceValid(Ik))
        {
            return;
        }

        if (_leftFoot < 0)
        {
            _leftFoot = skeleton.FindBone("LeftFoot");
            _rightFoot = skeleton.FindBone("RightFoot");
            _hips = skeleton.FindBone("Hips");
            if (_leftFoot < 0 || _rightFoot < 0)
            {
                // A quadruped, or a rig with no profile feet. Correct to do nothing.
                SetProcess(false);
                return;
            }
        }

        bool wanted = ShouldPlace(skeleton);
        _weight = FootPlacement.StepWeight(_weight, wanted, (float)delta, Ik.BlendSeconds);
        if (_weight <= 0.001f)
        {
            return;
        }

        Transform3D toWorld = skeleton.GlobalTransform;
        float left = Solve(skeleton, toWorld, _leftFoot);
        float right = Solve(skeleton, toWorld, _rightFoot);

        // The pelvis drops first: the feet are then lifted relative to a body that has already made
        // room for them, which is what keeps the low knee bent instead of the leg straight.
        float drop = FootPlacement.PelvisDrop(left, right, Ik.MaxDrop) * _weight;
        _pelvis = Mathf.Lerp(_pelvis, drop, 0.4f);
        if (_hips >= 0 && Mathf.Abs(_pelvis) > 0.001f)
        {
            Vector3 hips = skeleton.GetBonePosePosition(_hips);
            skeleton.SetBonePosePosition(_hips, hips + new Vector3(0f, _pelvis, 0f));
        }

        Apply(skeleton, _leftFoot, left - _pelvis);
        Apply(skeleton, _rightFoot, right - _pelvis);
    }

    private bool ShouldPlace(Skeleton3D skeleton)
    {
        bool grounded = Ik.Entity?.Body is not CharacterBody3D body || body.IsOnFloor();
        bool acting = Ik.Entity?.GetComponent<Combat.Actions.CharacterActionComponent>()
            is { Phase: not Combat.Actions.ActionPhase.Idle };

        float distance = Ik.MaxDistance;
        if (skeleton.GetViewport()?.GetCamera3D() is { } camera)
        {
            distance = camera.GlobalPosition.DistanceTo(skeleton.GlobalPosition);
        }

        return FootPlacement.ShouldPlace(
            grounded, acting, skeleton.IsVisibleInTree(), distance, Ik.MaxDistance);
    }

    /// <summary>The vertical correction this foot wants, in metres, or 0 when it found no ground.</summary>
    private float Solve(Skeleton3D skeleton, Transform3D toWorld, int bone)
    {
        Vector3 foot = (toWorld * skeleton.GetBoneGlobalPose(bone)).Origin;
        Vector3 from = foot + (Vector3.Up * Ik.ProbeAbove);
        Vector3 to = foot - (Vector3.Down * -Ik.ProbeBelow);

        var query = PhysicsRayQueryParameters3D.Create(from, to, Combat.CombatLayers.World);
        query.HitBackFaces = false;
        if (Ik.Entity?.Body is CollisionObject3D self)
        {
            query.Exclude = new Godot.Collections.Array<Rid> { self.GetRid() };
        }

        Godot.Collections.Dictionary hit = skeleton.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return 0f;
        }

        float groundY = ((Vector3)hit["position"]).Y + Ik.AnkleHeight;
        return FootPlacement.FootLift(foot.Y, groundY, Ik.MaxLift, Ik.MaxDrop) * _weight;
    }

    private static void Apply(Skeleton3D skeleton, int bone, float lift)
    {
        if (Mathf.Abs(lift) <= 0.001f)
        {
            return;
        }

        Vector3 pose = skeleton.GetBonePosePosition(bone);
        skeleton.SetBonePosePosition(bone, pose + new Vector3(0f, lift, 0f));
    }
}
