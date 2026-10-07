using Embervale.Animation;
using Embervale.Entities;
using Embervale.Magic;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Brings the casting hand into the first-person view while a spell is being wound up, charged or
/// channelled (<see cref="FirstPersonArmRules"/>). Presentation only: it rewrites one bone's pose
/// after the animation has posed the body, and gameplay reads none of it. The aim, the muzzle and
/// the spell's true path are untouched; the hand's drawn position is handed to the animation
/// component so effects that start "at the casting hand" start at the hand the player can see.
///
/// <para>It does nothing in third person, where the clip's own pose is the picture.</para>
/// </summary>
[GlobalClass]
public partial class FirstPersonArmComponent : EntityComponent
{
    /// <summary>The player camera. Injected by the factory.</summary>
    public Camera3D? Camera { get; set; }

    private FirstPersonArmModifier? _modifier;

    internal PlayerCameraRig? Rig { get; private set; }

    internal SpellcastingComponent? Casting { get; private set; }

    internal CharacterAnimationComponent? Animation { get; private set; }

    /// <summary>How far the arm is currently raised, 0..1. Read by the camera harness.</summary>
    public float Raise => _modifier != null && GodotObject.IsInstanceValid(_modifier) ? _modifier.Weight : 0f;

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        Rig = owner.GetComponent<PlayerCameraRig>();
        Casting = owner.GetComponent<SpellcastingComponent>();
        Animation = owner.GetComponent<CharacterAnimationComponent>();

        if (FootIkComponent.FindRig(owner.Body) is not { } skeleton)
        {
            return;
        }

        // On the skeleton, not on this component: the engine only runs a modifier that is a direct
        // child of the skeleton it modifies (see FootIkComponent).
        _modifier = new FirstPersonArmModifier { Name = "FirstPersonArm", Arm = this };
        skeleton.CallDeferred(Node.MethodName.AddChild, _modifier);
    }

    protected override void OnTeardown()
    {
        if (_modifier != null && GodotObject.IsInstanceValid(_modifier))
        {
            _modifier.QueueFree();
        }

        _modifier = null;
    }

    /// <summary>How far the arm should be up right now: all the way while a cast holds the spell
    /// selection (a wind-up, a charge or a channel) and the view is first person, fading with the
    /// swap out to third.</summary>
    internal float WantedRaise() =>
        Casting is { SelectionLocked: true } && Rig != null && GodotObject.IsInstanceValid(Camera)
            ? 1f - CameraRigMath.Ease(Rig.ThirdPersonBlend)
            : 0f;
}
