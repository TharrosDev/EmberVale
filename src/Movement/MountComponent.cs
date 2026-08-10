using Embervale.Animation;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Movement;

/// <summary>Published when the player mounts or dismounts. <c>MessageKey</c> is a
/// <c>data/locale/strings.csv</c> key, resolved at display time like every other notice.</summary>
public readonly record struct MountChangedEvent(bool Mounted, string MessageKey) : IGameEvent;

/// <summary>Published when a mount request is refused — no mount owned, or the horse is blown.</summary>
public readonly record struct MountRefusedEvent(string ReasonKey) : IGameEvent;

/// <summary>
/// The player's mount (Phase 39A): summon, ride, dismiss, and the gallop that costs something.
///
/// <b>The mount is a state of the rider, not a second body.</b> There is no horse
/// <c>CharacterBody3D</c> and no second locomotion motor — the player's own capsule keeps moving,
/// wearing a horse. That buys the whole feature for one component: no navigation, no second
/// persistence record, no "where does the horse stand while you are inside a shop", and no
/// dismount-placement search. What it costs is written down rather than hidden: the capsule stays
/// the player's, so a mounted horse is exactly as wide and as tall a climber as a man on foot.
/// ⚠️ <b>That is invariant 16 and not a mount bug</b> — <c>CharacterBody3D</c> has no step-up, and
/// Phase 39C is the pass that decides whether that stays true.
///
/// <b>Ownership is 38D's flag and nothing else.</b> <c>ServiceKind.Stable</c> charged 400 gold and
/// set <see cref="OwnedFlagId"/>; this reads it. The two halves are held together by a
/// <c>--validate</c> rule rather than by a comment, because a flag id is a string in two files and
/// nothing else in the repo would ever notice them drifting apart.
/// </summary>
[GlobalClass]
public partial class MountComponent : EntityComponent, ISaveable
{
    /// <summary>The story flag <c>data/services/EmberCrownStable.tres</c> grants on purchase.
    /// ⚠️ <c>ContentValidator</c> asserts the two are the same string — see the class remarks.</summary>
    public const string OwnedFlagId = "flag.stable.mount_owned";

    internal const string MountModelPath = "res://assets/models/creatures/mnt_horse.glb";

    /// <summary>Where the rider sits, measured against the imported model in the engine and then
    /// rendered from four angles with the market behind it (<c>tools/mount_shots.gd</c>).
    /// ⚠️ These are not derivable from the file: the pack's glTF accessors read ~4.8 m tall because
    /// its armature carries a 100x scale, and the seat is a bone height, not a fraction of a box.
    /// The imported horse stands 2.41 m to the ears and 2.84 m nose to tail at
    /// <c>nodes/root_scale = 0.5</c>.</summary>
    private const float SaddleHeight = 0.86f;

    /// <summary>Forward offset of the seat, so the rider sits over the withers rather than the rump.
    /// Negative is forward — Godot's forward is -Z.</summary>
    private const float SaddleForward = -0.52f;

    /// <summary>Below this horizontal speed the mount idles rather than walking.</summary>
    private const float WalkThreshold = 0.6f;

    private StatsComponent? _stats;
    private StoryFlagsComponent? _flags;
    private CharacterAnimationComponent? _animation;
    private Node3D? _bodyMesh;
    private Node3D? _cameraPivot;
    private Node3D? _visual;
    private AnimationPlayer? _visualAnimation;
    private string _idleClip = "", _walkClip = "", _gallopClip = "";
    private StatModifier? _speedModifier;
    private MountRules.GallopState _gallop = MountRules.Fresh;

    /// <summary>Whether the player is currently on the horse.</summary>
    public bool IsMounted { get; private set; }

    /// <summary>Remaining gallop pool, for the HUD and for the save.</summary>
    public float Stamina => _gallop.Stamina;

    public string SaveId => SaveKey("mount");

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        _stats = owner.GetComponent<StatsComponent>();
        _flags = owner.GetComponent<StoryFlagsComponent>();
        _animation = owner.GetComponent<CharacterAnimationComponent>();
        _bodyMesh = owner.Body.GetNodeOrNull<Node3D>("BodyMesh");
        _cameraPivot = owner.Body.GetNodeOrNull<Node3D>("CameraPivot");
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>Whistle: mount if the stablemaster has been paid, dismount if already up. The one
    /// verb the key and the dev command both call.</summary>
    public void Toggle()
    {
        if (IsMounted)
        {
            Dismount("mount.dismissed");
            return;
        }

        if (_flags?.Has(OwnedFlagId) != true)
        {
            EventBus.Instance?.Publish(new MountRefusedEvent("mount.not_owned"));
            return;
        }

        Mount("mount.summoned");
    }

    /// <summary>
    /// Advances the gallop pool and the mount's own animation, and answers the only question the
    /// controller needs: <b>does the body sprint this frame?</b> Held sprint is a request; a blown
    /// horse refuses it. Returns <paramref name="sprintHeld"/> untouched when not mounted, so the
    /// controller has no mounted/unmounted branch of its own.
    /// </summary>
    public bool Tick(double delta, bool sprintHeld)
    {
        if (!IsMounted)
        {
            return sprintHeld;
        }

        bool wasExhausted = _gallop.Exhausted;
        _gallop = MountRules.Step(_gallop, sprintHeld, (float)delta);

        // Toasts once per blow-out, not once per frame: the refusal repeats every physics tick.
        if (_gallop.Exhausted && !wasExhausted)
        {
            EventBus.Instance?.Publish(new MountRefusedEvent("mount.exhausted"));
        }

        PlayGait();
        return _gallop.Galloping;
    }

    private void Mount(string messageKey)
    {
        if (IsMounted)
        {
            return;
        }

        IsMounted = true;
        AttachVisual();
        Seat(true);

        // A fresh modifier per mount, kept so the exact instance can be pulled off again. Removing
        // by source (this component) would also be correct; keeping the instance means a stray
        // second Mount() cannot leave an unpaired one behind.
        if (_stats != null && _speedModifier == null)
        {
            _speedModifier = new StatModifier(MountRules.SpeedMultiplier, ModifierType.PercentMult, this);
            _stats.GetStat(StatType.MoveSpeed).AddModifier(_speedModifier);
        }

        EventBus.Instance?.Publish(new MountChangedEvent(true, messageKey));
        Log.Info("Mounted.");
    }

    private void Dismount(string messageKey)
    {
        if (!IsMounted)
        {
            return;
        }

        IsMounted = false;
        StripState();
        EventBus.Instance?.Publish(new MountChangedEvent(false, messageKey));
        Log.Info("Dismounted.");
    }

    /// <summary>Everything <see cref="Mount"/> applied, removed — the half that <see cref="Load"/>
    /// also needs, and the reason it is a method rather than the tail of <see cref="Dismount"/>.
    /// ⚠️ The speed modifier is <b>removed</b>, never overwritten: it multiplies
    /// <c>StatType.MoveSpeed</c>, which <see cref="LocomotionComponent"/> feeds straight into a
    /// <c>CharacterBody3D</c>'s velocity, and a leaked one stacks silently on every reload.</summary>
    private void StripState()
    {
        if (_speedModifier != null)
        {
            _stats?.GetStat(StatType.MoveSpeed).RemoveModifier(_speedModifier);
            _speedModifier = null;
        }

        Seat(false);

        if (_visual != null)
        {
            _visual.QueueFree();
            _visual = null;
            _visualAnimation = null;
        }

        _gallop = MountRules.Fresh;
    }

    /// <summary>Raises (or lowers) the rider and the camera. The camera pivot moves with the body:
    /// without it the first-person eye stays at 1.62 m, which while mounted is <em>inside the
    /// horse's neck</em>. That is not a subtle framing complaint — it is the shipping camera mode.</summary>
    private void Seat(bool mounted)
    {
        if (_bodyMesh != null)
        {
            _bodyMesh.Position = mounted ? new Vector3(0f, SaddleHeight, SaddleForward) : Vector3.Zero;
        }

        if (_cameraPivot != null)
        {
            Vector3 seat = _cameraPivot.Position;
            _cameraPivot.Position = new Vector3(
                seat.X, mounted ? PlayerEyeHeight + SaddleHeight : PlayerEyeHeight, seat.Z);
        }

        if (_animation != null)
        {
            _animation.Riding = mounted;
        }
    }

    /// <summary>Mirrors <c>PlayerFactory.EyeHeight</c>. Duplicated rather than made internal because
    /// this component is not player-only by type — the value is the one thing it assumes about its
    /// owner, and a wrong one is visible the instant anybody renders it.</summary>
    private const float PlayerEyeHeight = 1.62f;

    private void AttachVisual()
    {
        if (_visual != null || GD.Load<PackedScene>(MountModelPath)?.Instantiate() is not Node3D horse)
        {
            return;
        }

        horse.Name = "MountVisual";
        horse.RotateY(Mathf.Pi); // glTF forward is +Z, Godot's is -Z — the same turn the body makes
        _visual = horse;

        // ⚠️ Deferred, per CLAUDE.md §7: a body mid-setup REFUSES the add, logs it and carries on,
        // leaving a live node that is not in the tree. Mounting normally happens long after setup,
        // but Load() runs on the restore path where it does not.
        Entity!.Body.CallDeferred(Node.MethodName.AddChild, horse);
        horse.Ready += ResolveGaitClips;
    }

    private void ResolveGaitClips()
    {
        _visualAnimation = FindAnimationPlayer(_visual);
        if (_visualAnimation == null)
        {
            return;
        }

        string[] clips = _visualAnimation.GetAnimationList();
        _idleClip = AnimationClips.Resolve(clips, "idle");
        _walkClip = AnimationClips.Resolve(clips, "run");
        _gallopClip = AnimationClips.Resolve(clips, "gallop");
    }

    /// <summary>Idle, walk or gallop, chosen from what the body is actually doing rather than from
    /// what the player asked for — a horse held against a wall should not gallop on the spot.</summary>
    private void PlayGait()
    {
        if (_visualAnimation == null)
        {
            return;
        }

        float speed = 0f;
        if (Entity?.Body is CharacterBody3D body)
        {
            Vector3 v = body.Velocity;
            speed = new Vector2(v.X, v.Z).Length();
        }

        string next =
            speed <= WalkThreshold ? _idleClip
            : _gallop.Galloping && _gallopClip.Length > 0 ? _gallopClip
            : _walkClip;

        if (next.Length > 0 && _visualAnimation.CurrentAnimation != next)
        {
            _visualAnimation.Play(next, customBlend: 0.15);
        }
    }

    private static AnimationPlayer? FindAnimationPlayer(Node? node)
    {
        if (node == null)
        {
            return null;
        }

        if (node is AnimationPlayer player)
        {
            return player;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindAnimationPlayer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public Godot.Collections.Dictionary Save() => new()
    {
        ["mounted"] = IsMounted,
        ["stamina"] = _gallop.Stamina,
    };

    /// <summary>
    /// ⚠️ <b>Replaces, never merges.</b> A quickload keeps every live component, so a save taken on
    /// foot must actively put the player back on the ground — including stripping a speed modifier
    /// the saved timeline never had. Everything is torn down first and rebuilt from the file, which
    /// is the <c>EquipmentComponent.Load</c> / <c>PerksComponent.Load</c> pattern.
    /// </summary>
    public void Load(Godot.Collections.Dictionary data)
    {
        bool wasMounted = IsMounted;
        IsMounted = false;
        StripState();

        if (data.TryGetValue("mounted", out Variant mounted) && mounted.AsBool())
        {
            Mount(string.Empty); // a load restores state; it does not narrate one
        }

        if (data.TryGetValue("stamina", out Variant stamina))
        {
            float value = Mathf.Clamp(stamina.AsSingle(), 0f, MountRules.StaminaMax);
            _gallop = new MountRules.GallopState(value, value <= 0f, false);
        }

        if (wasMounted != IsMounted)
        {
            Log.Info($"Mount restored: {(IsMounted ? "mounted" : "on foot")}.");
        }
    }
}
