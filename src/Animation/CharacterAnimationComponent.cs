using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Enemies;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Npc;
using Embervale.Stats;
using Godot;
using Embervale.Core;

namespace Embervale.Animation;

/// <summary>
/// Drives a rigged character's <see cref="AnimationPlayer"/> from the existing combat/locomotion
/// state (Phase 30C) — the visuals-only bridge between gameplay components and the 30B/30C glTF
/// clips. Convention over configuration: the body model (under <see cref="BodyMeshPath"/>) ships
/// clips whose names start with <c>idle</c>, <c>run</c>, <c>block</c>, <c>attack</c>, <c>hit</c>
/// and <c>death</c> (loop clips are authored with Godot's <c>-loop</c> suffix); any humanoid using
/// those names gets animation for free (the 30F enemy sets reuse this component).
///
/// Gameplay timing is untouched: hit/attack windows stay owned by <see cref="CharacterActionComponent"/>
/// and friends — this component only watches events and per-frame state and plays clips.
/// </summary>
[GlobalClass]
public partial class CharacterAnimationComponent : EntityComponent
{
    /// <summary>Node name of the body visual root the <see cref="AnimationPlayer"/> lives under.</summary>
    [Export] public string BodyMeshPath { get; set; } = "BodyMesh";

    /// <summary>Horizontal speed (m/s) above which locomotion reads as running.</summary>
    [Export] public float RunSpeedThreshold { get; set; } = 0.6f;

    /// <summary>The shared 46-clip Quaternius library (Phase 38A), retargeted onto
    /// <c>SkeletonProfileHumanoid</c> so its tracks address <c>%GeneralSkeleton</c> by profile bone
    /// name. Extracted from the .glb by <c>tools/extract_anim_library.gd</c> so the library's
    /// Mannequin mesh never reaches a build.</summary>
    private const string LibraryPath = ModelAssets.AnimationLibrary;

    /// <summary>The library name the clips are added under; it becomes their <c>lib/Name</c> prefix,
    /// which <see cref="AnimationClips"/> strips.</summary>
    private static readonly StringName LibraryName = "lib";

    /// <summary>What the importer's bone renamer names a retargeted skeleton. It doubles as the
    /// marker that a rig speaks the shared library's bone vocabulary — see
    /// <see cref="AddSharedLibrary"/>.</summary>
    private const string RetargetedSkeletonName = "GeneralSkeleton";

    /// <summary>Loaded once for the whole cast — every character shares the one resource, and its
    /// clips are only ever read.</summary>
    private static AnimationLibrary? _sharedLibrary;

    private AnimationPlayer? _player;
    private CombatComponent? _combat;
    private StatsComponent? _stats;
    private SpellcastingComponent? _spellcasting;
    private Skeleton3D? _skeleton;
    private string _idle = "", _run = "", _block = "", _hit = "", _death = "";
    private string _cast = "", _channel = "", _ride = "";

    /// <summary>Set by <see cref="Movement.MountComponent"/> while the owner is on a mount. It sits
    /// above locomotion in the selection below because a rider's legs are not running — without it
    /// the body plays the run loop while the horse carries it, which reads as sprinting on the spot
    /// four feet off the ground.</summary>
    public bool Riding { get; set; }

    /// <summary>The clip the running action is being clocked by, or empty when the fallback timer
    /// is doing it instead.</summary>
    private string _actionClip = "";

    private bool _deathPlayed;
    private Vector3? _lastPosition;
    private float _lastDelta;

    protected override void OnInitialize()
    {
        _combat = Entity!.GetComponent<CombatComponent>();
        _stats = Entity.GetComponent<StatsComponent>();

        if (Entity.Body.GetNodeOrNull<Node3D>(BodyMeshPath) is { } bodyRoot)
        {
            _player = FindAnimationPlayer(bodyRoot);
            _skeleton = FindSkeleton(bodyRoot);
        }

        if (_skeleton != null)
        {
            NpcVisualKit.Attach(Entity, _skeleton);
            EnemyVisualKit.Attach(Entity, _skeleton);
        }

        if (_player != null)
        {
            AddSharedLibrary();
            _idle = ResolveClip("idle");
            _run = ResolveClip("run");
            _block = ResolveClip("block");
            _hit = ResolveClip("hit");
            _death = ResolveClip("death");
            _cast = ResolveClip("cast");
            _channel = ResolveClip("channel");
            _ride = ResolveClip("ride");
        }

        _spellcasting = Entity.GetComponent<SpellcastingComponent>();

        EventBus.Instance?.Subscribe<EntityDamagedEvent>(OnDamaged);
        EventBus.Instance?.Subscribe<SpellCastEvent>(OnSpellCast);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<EntityDamagedEvent>(OnDamaged);
        EventBus.Instance?.Unsubscribe<SpellCastEvent>(OnSpellCast);
    }

    /// <summary>Hands this character the shared library — but <b>only if its rig was retargeted</b>.
    ///
    /// The library's tracks address <c>%GeneralSkeleton</c> by <c>SkeletonProfileHumanoid</c> bone
    /// name, which is exactly what the importer's bone renamer produces, so the skeleton being
    /// *called* <c>GeneralSkeleton</c> is the retarget's own marker and a reliable gate. Without it a
    /// non-retargeted rig would still <i>resolve</i> a block/cast clip and then play a clip whose
    /// every track points at bones it does not have — the actor freezes mid-guard rather than simply
    /// having no block, and nothing is logged either way.</summary>
    private void AddSharedLibrary()
    {
        if (_skeleton == null || (string)_skeleton.Name != RetargetedSkeletonName ||
            _player!.HasAnimationLibrary(LibraryName))
        {
            return;
        }

        _sharedLibrary ??= GD.Load<AnimationLibrary>(LibraryPath);
        if (_sharedLibrary != null)
        {
            _player.AddAnimationLibrary(LibraryName, _sharedLibrary);
        }
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

    /// <summary>The 30E cast beat: play the cast thrust and pop a school-tinted flash at the
    /// casting hand. A channeled spell publishes a cast event per tick — the sustained
    /// channel-loop pose covers those, so per-tick one-shots/flashes are skipped.</summary>
    private void OnSpellCast(SpellCastEvent e)
    {
        if (!ReferenceEquals(e.Caster, Entity) || _spellcasting is { IsChanneling: true })
        {
            return;
        }

        PlayOneShot(_cast);

        if (SpellDatabase.Get(e.SpellId) is { } spell)
        {
            var flash = new SpellFlash
            {
                Radius = 0.5f,
                FlashColor = SpellSchools.Color(spell.School),
            };
            Entity!.Body.GetTree().CurrentScene.AddChild(flash);
            flash.GlobalPosition = CastingHandPosition();
        }
    }

    /// <summary>World position of the left (casting) hand bone, falling back to chest height.</summary>
    private Vector3 CastingHandPosition()
    {
        if (_skeleton != null && HumanoidBones.FindHand(_skeleton, right: false) is { Length: > 0 } hand)
        {
            return (_skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_skeleton.FindBone(hand))).Origin;
        }

        return Entity!.Body.GlobalPosition + (Vector3.Up * 1.3f);
    }

    private static AnimationPlayer? FindAnimationPlayer(Node node)
    {
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

    /// <summary>The imported clip for a gameplay slot ("" if the model has none) — tolerant of the
    /// importer keeping or stripping the authored <c>-loop</c> suffix, of an exporter's
    /// <c>Armature|</c> prefix, and of a pack that calls the beat something else. See
    /// <see cref="AnimationClips"/> for why both of those matter.</summary>
    private string ResolveClip(string slot)
    {
        if (_resolved.TryGetValue(slot, out string? cached))
        {
            return cached;
        }

        // Resolve walks the model's whole clip list; an action asks per swing, so the answer is
        // cached. The clip list cannot change after import, so the cache never goes stale.
        string clip = AnimationClips.Resolve(_player!.GetAnimationList(), slot);
        _resolved[slot] = clip;
        return clip;
    }

    private readonly System.Collections.Generic.Dictionary<string, string> _resolved = new();

    private void OnDamaged(EntityDamagedEvent e)
    {
        // A blocked/absorbed poke shouldn't flinch through a block pose; death owns the rest.
        if (ReferenceEquals(e.Entity, Entity) && e.RemainingHealth > 0f && _combat is not { IsBlocking: true })
        {
            PlayOneShot(_hit);
        }
    }

    /// <summary>
    /// ⚠️ <b>A rider plays no full-body one-shot (39B), and the render is why.</b> The library has no
    /// mounted attack, so a swing from the saddle plays the standing <c>Sword_Slash</c> — and a
    /// standing clip puts the hips ~0.5 m higher than the seated pose the saddle offset was measured
    /// against, so the rider does not "straighten mid-swing": he <b>stands up inside the horse</b>,
    /// sunk to the knee in its barrel, for the length of every attack and every flinch.
    ///
    /// Holding the ride pose instead costs the mounted swing its animation — the blow still lands,
    /// still rolls damage, still gets 39B's charge bonus, and the impact still reads. A missing
    /// animation is a smaller defect than a wrong one, and this is the only lever that does not cost
    /// art: the real fix is an <c>AnimationTree</c> with a bone-filtered upper-body layer, which is a
    /// sub-phase and not a patch.
    /// </summary>
    private void PlayOneShot(string clip)
    {
        if (_player != null && clip.Length > 0 && !_deathPlayed && !Riding)
        {
            _player.Play(clip);
        }
    }

    /// <summary>
    /// Starts the clip that shows an action and <b>hands the action its clock</b>.
    ///
    /// <para>Returns how many seconds the action will actually take, or <c>-1</c> when this body has
    /// no clip for the slot. Those are the two halves of the contract:</para>
    /// <list type="bullet">
    /// <item><paramref name="desiredSeconds"/> of <c>0</c> means <b>the clip decides</b> — its own
    /// length is returned and becomes the action's duration. This is the animation-authoritative
    /// case and the default for anything with a real clip.</item>
    /// <item>A positive <paramref name="desiredSeconds"/> means the designer decides, and the clip
    /// is played at the speed that makes it span exactly that long. A dagger's flick and the Iron
    /// King's heave are then visibly different swings rather than the same clip twice.</item>
    /// <item><c>-1</c> means the caller must run its own timer. The blow still lands, still rolls
    /// damage, still reads — a missing animation is a smaller defect than a wrong one, which is the
    /// same trade <see cref="PlayOneShot"/> already makes for a rider (39B).</item>
    /// </list>
    /// </summary>
    public float StartAction(string slot, float desiredSeconds)
    {
        // A rider gets no full-body one-shot for the reason PlayOneShot documents at length: the
        // standing clip lifts the hips half a metre out of the saddle. Refusing here rather than
        // silently playing it hands the action its fallback timer instead.
        if (_player == null || _deathPlayed || Riding)
        {
            return -1f;
        }

        string clip = ResolveClip(slot);
        if (clip.Length == 0)
        {
            return -1f;
        }

        float clipSeconds = (float)_player.GetAnimation(clip).Length;
        if (clipSeconds <= 0f)
        {
            return -1f;
        }

        float actual = desiredSeconds > 0f ? desiredSeconds : clipSeconds;
        _actionClip = clip;
        _player.Play(clip, customBlend: 0.08, customSpeed: ActionTimeline.ClipSpeedFor(clipSeconds, actual));
        return actual;
    }

    /// <summary>How far through its clip the running action is (<c>0..1</c>), or <c>-1</c> when no
    /// action clip is playing. This is what makes the animation the clock rather than a follower:
    /// the hit window is read off this number, not off a stopwatch beside it.</summary>
    public float ActionProgress
    {
        get
        {
            if (_player == null || _actionClip.Length == 0 || _player.CurrentAnimation != _actionClip)
            {
                return -1f;
            }

            double length = _player.CurrentAnimationLength;
            return length <= 0d ? 1f : (float)(_player.CurrentAnimationPosition / length);
        }
    }

    /// <summary>Releases the clip back to locomotion. Called when the action ends or is cancelled.</summary>
    public void StopAction() => _actionClip = "";

    public override void _Process(double delta)
    {
        _lastDelta = (float)delta;

        if (_player == null)
        {
            return;
        }

        // Death latches until the entity is alive again (respawn), then control resumes.
        if (_stats is { IsAlive: false })
        {
            if (!_deathPlayed && _death.Length > 0)
            {
                _player.Play(_death);
                _deathPlayed = true;
            }

            return;
        }

        _deathPlayed = false;

        // A running action owns the body outright — the ladder below may not reclaim it, or
        // locomotion would blend the swing away halfway through its own hit window.
        if (_actionClip.Length > 0 && _player.CurrentAnimation == _actionClip && _player.IsPlaying())
        {
            return;
        }

        // Let the remaining one-shots (hit/cast) finish before locomotion reclaims the player.
        if (_player.IsPlaying() &&
            (_player.CurrentAnimation == _hit || _player.CurrentAnimation == _cast))
        {
            return;
        }

        string next =
            Riding && _ride.Length > 0 ? _ride
            : _spellcasting is { IsCharging: true } or { IsChanneling: true } && _channel.Length > 0 ? _channel
            : _combat is { IsBlocking: true } && _block.Length > 0 ? _block
            : HorizontalSpeed() > RunSpeedThreshold && _run.Length > 0 ? _run
            : _idle;

        if (next.Length > 0 && _player.CurrentAnimation != next)
        {
            _player.Play(next, customBlend: 0.15);
        }
    }

    private float HorizontalSpeed()
    {
        if (Entity?.Body is CharacterBody3D body)
        {
            Vector3 v = body.Velocity;
            return new Vector2(v.X, v.Z).Length();
        }

        // A scene-placed NPC is a plain Node3D and ScheduleComponent walks it by writing
        // GlobalPosition, so there is no Velocity to read — without this it always reports 0
        // and a townsperson slides to the market in an idle pose. Differentiate the position
        // instead; the same component then drives the whole cast, not just the actors that
        // happen to be CharacterBody3D.
        if (Entity?.Body is { } node)
        {
            Vector3 here = node.GlobalPosition;
            float speed = _lastPosition.HasValue && _lastDelta > 0f
                ? new Vector2(here.X - _lastPosition.Value.X, here.Z - _lastPosition.Value.Z).Length() / _lastDelta
                : 0f;
            _lastPosition = here;
            return speed;
        }

        return 0f;
    }
}
