using Embervale.Combat.Actions;
using Embervale.Entities;
using Embervale.Movement;
using Godot;

namespace Embervale.Animation;

/// <summary>
/// Plants a character's feet on the ground it is actually standing on.
///
/// <para><b>What it fixes.</b> Animation is authored on flat ground, so on the realm's real terrain
/// — which after the world-generation replacement has 43 m of relief in Ember Crown and 98 m in
/// Frostfang — a character's feet hang above a downhill slope and sink into an uphill one, and on a
/// stair one boot floats over the lower step. Every NPC in a settlement on a hillside floats or
/// wades.</para>
///
/// <para><b>How (the 2026-09 upgrade).</b> A ray per foot finds the ground beside the body, and the
/// correction each foot wants is the height of THAT ground relative to the body's own floor — not
/// relative to the animated foot, which would drag a swinging foot back down to the ground and kill
/// the stride. <see cref="FootPlacement.PelvisDrop"/> lowers the hips so the low leg can reach,
/// then an analytic two-bone solve (<see cref="FootPlacement.SolveTwoBone"/>) rotates thigh and shin
/// to put each ankle where it belongs, keeping the knee in its animated plane. A planted foot is then
/// rolled to the ground normal (<see cref="FootPlacement.SlopeRotation"/>), a swinging one is not.
/// Every correction is eased with <see cref="FootPlacement.Smooth"/>, so a foot crossing a stair
/// nose arrives over a few frames instead of popping. It all runs as a
/// <see cref="SkeletonModifier3D"/> — the engine's own hook, after the AnimationTree has posed the
/// skeleton — so it never races the animation.</para>
///
/// <para><b>First person.</b> The camera rides the head bone with the body visible, so the pelvis
/// drop reaches the eye on a slope. That is the intended read (you are standing lower on the downhill
/// foot) and the rig's own eye smoothing absorbs it.</para>
///
/// <para>⚠️ <b>It is off far more often than it is on, deliberately.</b> Airborne, mounted, rolling,
/// mid-action, out of view or beyond <see cref="MaxDistance"/>, the correction fades out and the rays
/// stop being cast. A jumping character has no ground worth meeting; a rider's legs belong to the
/// saddle; a warping one has something else owning its position; and paying two raycasts a frame for
/// every actor in a region is exactly the "expensive IK at unlimited range" §22 warns about.</para>
/// </summary>
[GlobalClass]
public partial class FootIkComponent : EntityComponent
{
    /// <summary>How far above the body's floor the ground ray starts. At least <see cref="MaxLift"/>,
    /// or a foot on a raised step finds the step's underside.</summary>
    [Export] public float ProbeAbove { get; set; } = 0.5f;

    /// <summary>How far below the body's floor the ray looks. Longer than any step this game allows,
    /// and short enough that a foot over a ledge finds nothing rather than the valley floor.</summary>
    [Export] public float ProbeBelow { get; set; } = 0.6f;

    /// <summary>Height of the ankle above the sole in the authored animation. Used to tell a planted
    /// foot from a swinging one.</summary>
    [Export] public float AnkleHeight { get; set; } = 0.1f;

    /// <summary>Most a foot is raised to meet higher ground (a stair, an uphill slope).</summary>
    [Export] public float MaxLift { get; set; } = 0.35f;

    /// <summary>Most a foot, and the pelvis with it, is lowered to meet lower ground.</summary>
    [Export] public float MaxDrop { get; set; } = 0.35f;

    /// <summary>How far the ankle may roll to match a slope.</summary>
    [Export] public float MaxSlopeDegrees { get; set; } = 35f;

    /// <summary>Seconds the whole correction takes to fade in or out (leaving the ground, mounting).</summary>
    [Export] public float BlendSeconds { get; set; } = 0.15f;

    /// <summary>How quickly each foot, the pelvis and the ground normal follow a new target, per
    /// second. Higher is snappier; around 12 settles a stair step in about a tenth of a second
    /// without the pop an instant correction makes.</summary>
    [Export] public float Sharpness { get; set; } = 12f;

    /// <summary>Animated height above the sole at or below which a foot counts as fully planted…</summary>
    [Export] public float PlantedHeight { get; set; } = 0.03f;

    /// <summary>…and at or above which it is in the swing and keeps its animated angle.</summary>
    [Export] public float LiftedHeight { get; set; } = 0.15f;

    /// <summary>Beyond this, in metres from the camera, the feet are nobody's business. Two rays per
    /// actor per frame across a loaded region is not free, and at 25 m a boot is a few pixels.</summary>
    [Export] public float MaxDistance { get; set; } = 25f;

    private Skeleton3D? _skeleton;
    private FootIkModifier? _modifier;

    internal LocomotionComponent? Locomotion { get; private set; }
    internal MountComponent? Mount { get; private set; }
    internal CharacterActionComponent? Action { get; private set; }

    protected override void OnInitialize()
    {
        Locomotion = Entity!.GetComponent<LocomotionComponent>();
        Mount = Entity.GetComponent<MountComponent>();
        Action = Entity.GetComponent<CharacterActionComponent>();

        _skeleton = FindRig(Entity.Body);
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

    /// <summary>The body's animated skeleton, or null. Factories name the visual "Mesh"; the player
    /// and scene NPCs name it "BodyMesh". Shared with <see cref="Player.FootstepComponent"/>, which
    /// reads the same feet to time its footfalls.</summary>
    internal static Skeleton3D? FindRig(Node3D body)
    {
        Node3D? root = body.GetNodeOrNull<Node3D>("BodyMesh") ?? body.GetNodeOrNull<Node3D>("Mesh");
        return root == null ? null : FindSkeleton(root);
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

    /// <summary>One leg's bones and its eased state between frames.</summary>
    private struct Leg
    {
        public int Upper;
        public int Lower;
        public int Foot;
        public float Offset;
        public Vector3 Normal;
    }

    private Leg _left;
    private Leg _right;
    private int _hips = -1;
    private bool _resolved;
    private bool _valid;
    private float _weight;
    private float _pelvis;

    // ⚠️ ...WithDelta, not _ProcessModification. Godot 4.7 declares both; the plain one takes no
    // arguments, so overriding it with a delta silently overrides nothing and the feet never move.
    public override void _ProcessModificationWithDelta(double delta)
    {
        if (GetSkeleton() is not { } skeleton || !GodotObject.IsInstanceValid(Ik) ||
            Ik.Entity?.Body is not { } body)
        {
            return;
        }

        if (!_resolved)
        {
            _resolved = true;
            _valid = Resolve(skeleton);
            if (!_valid)
            {
                // A quadruped, or a rig with no profile feet. Correct to do nothing, and to stop
                // being called for it.
                SetDeferred(SkeletonModifier3D.PropertyName.Active, false);
            }
        }

        if (!_valid)
        {
            return;
        }

        float dt = (float)delta;
        _weight = FootPlacement.StepWeight(_weight, ShouldPlace(skeleton, body), dt, Ik.BlendSeconds);
        if (_weight <= 0.001f)
        {
            // Faded out: forget the eased state so the next landing eases in from the animation
            // rather than from wherever the feet were when the character left the ground.
            _left.Offset = _right.Offset = _pelvis = 0f;
            _left.Normal = _right.Normal = Vector3.Up;
            return;
        }

        Transform3D toWorld = skeleton.GlobalTransform;
        Transform3D toSkeleton = toWorld.AffineInverse();
        float floorY = body.GlobalPosition.Y;

        // The animated feet, captured before anything here moves them.
        Transform3D leftPose = skeleton.GetBoneGlobalPose(_left.Foot);
        Transform3D rightPose = skeleton.GetBoneGlobalPose(_right.Foot);
        Vector3 leftWorld = toWorld * leftPose.Origin;
        Vector3 rightWorld = toWorld * rightPose.Origin;

        Probe(skeleton, body, leftWorld, floorY, dt, ref _left);
        Probe(skeleton, body, rightWorld, floorY, dt, ref _right);

        // The pelvis drops first: the legs are then solved against a body that has already made
        // room for them, which is what keeps the low knee bent instead of the leg straight.
        float drop = FootPlacement.PelvisDrop(_left.Offset, _right.Offset, Ik.MaxDrop);
        _pelvis = FootPlacement.Smooth(_pelvis, drop, Ik.Sharpness, dt);
        float pelvis = _pelvis * _weight;
        if (_hips >= 0 && Mathf.Abs(pelvis) > 0.0005f)
        {
            // World metres into the hips' own parent space: the skeleton node may be scaled (an
            // import at 0.01) or the hips parented under a root bone, and a raw metre offset in
            // either would drop the body by the wrong amount.
            Vector3 shift = toSkeleton.Basis * new Vector3(0f, pelvis, 0f);
            int parent = skeleton.GetBoneParent(_hips);
            if (parent >= 0)
            {
                shift = skeleton.GetBoneGlobalPose(parent).Basis.Inverse() * shift;
            }

            skeleton.SetBonePosePosition(_hips, skeleton.GetBonePosePosition(_hips) + shift);
        }

        Vector3 pole = toSkeleton.Basis * -body.GlobalBasis.Z;
        Vector3 up = (toSkeleton.Basis * Vector3.Up).Normalized();
        SolveLeg(skeleton, toSkeleton, ref _left, leftWorld, leftPose, floorY, pole, up);
        SolveLeg(skeleton, toSkeleton, ref _right, rightWorld, rightPose, floorY, pole, up);
    }

    private bool Resolve(Skeleton3D skeleton)
    {
        if (!ResolveLeg(skeleton, "LeftFoot", out _left) || !ResolveLeg(skeleton, "RightFoot", out _right))
        {
            return false;
        }

        _hips = skeleton.FindBone("Hips");
        return true;
    }

    /// <summary>A leg is the foot and its two parents — found by walking up from the foot rather
    /// than by name, so a rig that calls its shin "Calf" or "LowerLeg" still resolves.</summary>
    private static bool ResolveLeg(Skeleton3D skeleton, string footName, out Leg leg)
    {
        leg = new Leg { Foot = skeleton.FindBone(footName), Normal = Vector3.Up };
        leg.Lower = leg.Foot >= 0 ? skeleton.GetBoneParent(leg.Foot) : -1;
        leg.Upper = leg.Lower >= 0 ? skeleton.GetBoneParent(leg.Lower) : -1;
        return leg.Upper >= 0;
    }

    private bool ShouldPlace(Skeleton3D skeleton, Node3D body)
    {
        bool grounded = body is not CharacterBody3D character || character.IsOnFloor();
        bool acting = Ik.Action is { Phase: not ActionPhase.Idle };
        bool mounted = Ik.Mount is { IsMounted: true };
        // A flier's legs hang in the air like a jumper's, so flight counts with the roll.
        bool dashing = Ik.Locomotion is { IsDashing: true } || Ik.Locomotion is { Flying: true };

        float distance = Ik.MaxDistance;
        if (skeleton.GetViewport()?.GetCamera3D() is { } camera)
        {
            distance = camera.GlobalPosition.DistanceTo(skeleton.GlobalPosition);
        }

        return FootPlacement.ShouldPlace(
            grounded, acting, skeleton.IsVisibleInTree(), distance, Ik.MaxDistance, mounted, dashing);
    }

    /// <summary>Finds the ground under one foot and eases that leg's offset and normal toward it.
    /// The offset is the ground's height relative to the BODY's floor, so the animated swing is kept
    /// and only the terrain difference is added.</summary>
    private void Probe(Skeleton3D skeleton, Node3D body, Vector3 foot, float floorY, float dt, ref Leg leg)
    {
        float target = 0f;
        Vector3 normal = Vector3.Up;

        var query = PhysicsRayQueryParameters3D.Create(
            new Vector3(foot.X, floorY + Ik.ProbeAbove, foot.Z),
            new Vector3(foot.X, floorY - Ik.ProbeBelow, foot.Z),
            Combat.CombatLayers.World);
        query.HitBackFaces = false;
        if (body is CollisionObject3D self)
        {
            query.Exclude = new Godot.Collections.Array<Rid> { self.GetRid() };
        }

        Godot.Collections.Dictionary hit = skeleton.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            target = FootPlacement.FootLift(floorY, ((Vector3)hit["position"]).Y, Ik.MaxLift, Ik.MaxDrop);
            normal = (Vector3)hit["normal"];
        }

        leg.Offset = FootPlacement.Smooth(leg.Offset, target, Ik.Sharpness, dt);
        Vector3 eased = leg.Normal.Lerp(normal, 1f - Mathf.Exp(-Ik.Sharpness * dt));
        leg.Normal = eased.LengthSquared() > 0.0001f ? eased.Normalized() : Vector3.Up;
    }

    private void SolveLeg(Skeleton3D skeleton, Transform3D toSkeleton, ref Leg leg,
        Vector3 animatedWorld, Transform3D animatedPose, float floorY, Vector3 pole, Vector3 up)
    {
        Vector3 target = toSkeleton * (animatedWorld + (Vector3.Up * (leg.Offset * _weight)));

        Transform3D upper = skeleton.GetBoneGlobalPose(leg.Upper);
        Transform3D lower = skeleton.GetBoneGlobalPose(leg.Lower);
        Transform3D foot = skeleton.GetBoneGlobalPose(leg.Foot);
        if (FootPlacement.SolveTwoBone(upper.Origin, lower.Origin, foot.Origin, target, pole,
                out Quaternion upperDelta, out Quaternion lowerDelta))
        {
            Rotate(skeleton, leg.Upper, upper, upperDelta);
            Rotate(skeleton, leg.Lower, lower, lowerDelta);
        }

        // The foot keeps its animated orientation in the world — rotating the shin would otherwise
        // carry it along — and a planted one is rolled onto the ground.
        float sole = animatedWorld.Y - floorY - Ik.AnkleHeight;
        float planted = FootPlacement.Planted(sole, Ik.PlantedHeight, Ik.LiftedHeight) * _weight;
        Vector3 normal = (toSkeleton.Basis * leg.Normal).Normalized();
        Quaternion slope = FootPlacement.SlopeRotation(up, normal, Ik.MaxSlopeDegrees, planted);
        Quaternion wanted = slope * animatedPose.Basis.GetRotationQuaternion();
        Quaternion shin = skeleton.GetBoneGlobalPose(leg.Lower).Basis.GetRotationQuaternion();
        skeleton.SetBonePoseRotation(leg.Foot, (shin.Inverse() * wanted).Normalized());
    }

    /// <summary>Applies a skeleton-space delta rotation to a bone by rewriting its LOCAL pose: the
    /// global rotation becomes <c>delta * global</c>, so local becomes
    /// <c>local * global⁻¹ * delta * global</c>.</summary>
    private static void Rotate(Skeleton3D skeleton, int bone, Transform3D global, Quaternion delta)
    {
        Quaternion g = global.Basis.GetRotationQuaternion();
        Quaternion local = skeleton.GetBonePoseRotation(bone);
        skeleton.SetBonePoseRotation(bone, (local * (g.Inverse() * delta * g)).Normalized());
    }
}
