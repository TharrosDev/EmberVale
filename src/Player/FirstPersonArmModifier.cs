using Embervale.Animation;
using Godot;

namespace Embervale.Player;

/// <summary>
/// The half of <see cref="FirstPersonArmComponent"/> that runs inside the skeleton's own
/// modification pass, after the AnimationTree has posed it and before the pose is drawn. The tree
/// poses the arm afresh every frame, so what this writes lasts one frame and never accumulates.
/// </summary>
internal sealed partial class FirstPersonArmModifier : SkeletonModifier3D
{
    /// <summary>The component holding the references. Not called "Owner": Node already has one.</summary>
    public required FirstPersonArmComponent Arm { get; init; }

    /// <summary>How far the arm is raised, 0..1.</summary>
    public float Weight { get; private set; }

    private int _upper = -1;
    private int _hand = -1;
    private bool _resolved;

    // ...WithDelta, not _ProcessModification: the plain one takes no arguments (see FootIkModifier).
    public override void _ProcessModificationWithDelta(double delta)
    {
        if (GetSkeleton() is not { } skeleton || !GodotObject.IsInstanceValid(Arm))
        {
            return;
        }

        if (!_resolved)
        {
            _resolved = true;
            Resolve(skeleton);
            if (_upper < 0)
            {
                // No arm this can find (the stand-in capsule, a rig with no hand). Nothing to do,
                // and no reason to go on being called.
                SetDeferred(SkeletonModifier3D.PropertyName.Active, false);
            }
        }

        if (_upper < 0)
        {
            return;
        }

        Weight = FirstPersonArmRules.StepWeight(Weight, Arm.WantedRaise(), (float)delta);
        if (Weight <= 0.001f || Arm.Camera is not { } camera || !GodotObject.IsInstanceValid(camera))
        {
            return;
        }

        Transform3D toWorld = skeleton.GlobalTransform;
        Transform3D upper = skeleton.GetBoneGlobalPose(_upper);
        Vector3 shoulder = toWorld * upper.Origin;
        Vector3 handInSkeleton = skeleton.GetBoneGlobalPose(_hand).Origin;
        Vector3 hand = toWorld * handInSkeleton;

        Transform3D view = camera.GlobalTransform;
        Basis axes = view.Basis.Orthonormalized();
        float side = (shoulder - view.Origin).Dot(axes.X) < 0f ? -1f : 1f;
        Vector2 size = camera.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        float aspect = size.X > 1f && size.Y > 1f ? size.X / size.Y : 16f / 9f;

        Vector3 line = axes * FirstPersonArmRules.ViewDirection(camera.Fov, aspect, side);
        Vector3 target = FirstPersonArmRules.HandTarget(view.Origin, line, shoulder, shoulder.DistanceTo(hand));
        Quaternion swing = FirstPersonArmRules.Swing(shoulder, hand, target, Weight);
        if (swing.IsEqualApprox(Quaternion.Identity))
        {
            Arm.Animation?.ReportCastingHand(handInSkeleton);
            return;
        }

        // The swing is a world rotation; the bone wants it in the skeleton's space. Then the same
        // rewrite FootIkModifier uses: global becomes delta * global, so local becomes
        // local * global^-1 * delta * global.
        Quaternion frame = toWorld.Basis.Orthonormalized().GetRotationQuaternion();
        Quaternion inSkeleton = frame.Inverse() * swing * frame;
        Quaternion global = upper.Basis.GetRotationQuaternion();
        Quaternion local = skeleton.GetBonePoseRotation(_upper);
        skeleton.SetBonePoseRotation(_upper, (local * (global.Inverse() * inSkeleton * global)).Normalized());

        // Where the hand is drawn this frame, for the effects that sit in it.
        Arm.Animation?.ReportCastingHand(skeleton.GetBoneGlobalPose(_hand).Origin);
    }

    /// <summary>The casting hand (the one <c>CharacterAnimationComponent.TryGetCastingHand</c>
    /// reads) and the upper arm it hangs from: the hand's parent's parent, found by walking up
    /// rather than by name, as the foot IK finds a leg.</summary>
    private void Resolve(Skeleton3D skeleton)
    {
        string name = HumanoidBones.FindHand(skeleton, right: false);
        _hand = name.Length > 0 ? skeleton.FindBone(name) : -1;
        int lower = _hand >= 0 ? skeleton.GetBoneParent(_hand) : -1;
        _upper = lower >= 0 ? skeleton.GetBoneParent(lower) : -1;
        if (_upper < 0)
        {
            _hand = -1;
        }
    }
}
