using Embervale.Core.Diagnostics;
using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Enemies;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic;
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

    /// <summary>The full-body Meshy library (the 2026-09-04 overhaul). See
    /// <see cref="ModelAssets.MeshyAnimationLibrary"/> for why it is a different thing from the
    /// upper-body one above rather than a bigger one.</summary>
    private const string MeshyLibraryPath = ModelAssets.MeshyAnimationLibrary;

    /// <summary>The library name the clips are added under; it becomes their <c>lib/Name</c> prefix,
    /// which <see cref="AnimationClips"/> strips.</summary>
    private static readonly StringName LibraryName = "lib";

    /// <summary>Prefix the Meshy clips are addressed by. Its clips are named for Embervale's own
    /// gameplay slots ("idle", "run", "attack1"), so once <c>AnimationClips.Bare</c> strips this
    /// prefix they match a slot exactly rather than through an alias guess.</summary>
    private static readonly StringName MeshyLibraryName = "meshy";

    /// <summary>What the importer's bone renamer names a retargeted skeleton. It doubles as the
    /// marker that a rig speaks the shared library's bone vocabulary — see
    /// <see cref="AddSharedLibrary"/>.</summary>
    private const string RetargetedSkeletonName = "GeneralSkeleton";

    /// <summary>Loaded once for the whole cast — every character shares the one resource, and its
    /// clips are only ever read.</summary>
    private static AnimationLibrary? _sharedLibrary;
    private static AnimationLibrary? _meshyLibrary;

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

    /// <summary>The tree, when this body could support one. Null means the simple fallback below is
    /// driving instead — see <see cref="LocomotionTree.Build"/> for when that happens.</summary>
    private AnimationTree? _tree;
    private AnimationNodeStateMachinePlayback? _playback;
    private readonly System.Collections.Generic.Dictionary<string, string> _slots = new();

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
            _player ??= AdoptPlayerlessBody(bodyRoot);
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

        BuildTree();

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

        // The full-body library rides the same gate. Both are keyed on the skeleton literally being
        // called GeneralSkeleton, which is the retarget's own marker — an unretargeted rig gets
        // neither rather than a broken one.
        _meshyLibrary ??= GD.Load<AnimationLibrary>(MeshyLibraryPath);
        if (_meshyLibrary != null && !_player.HasAnimationLibrary(MeshyLibraryName))
        {
            _player.AddAnimationLibrary(MeshyLibraryName, _meshyLibrary);
        }
    }

    /// <summary>
    /// Gives a rigged body that ships no clips of its own an <see cref="AnimationPlayer"/>, so it can
    /// still receive the shared library.
    ///
    /// ⚠️ <b>This is the fix for a body that could never animate at all.</b> Godot creates no
    /// AnimationPlayer for a glTF with zero animations, and every path in this component — including
    /// <see cref="AddSharedLibrary"/> — is gated on having one. <c>npc_innkeeper.glb</c> has exactly
    /// zero, so Gilda Ironmonger has stood in the Embermarket in her bind pose since she was placed:
    /// no clips of her own, and no library because there was nothing to attach one to. She imports
    /// cleanly, validates, and looks like a statue — the same silent failure
    /// <c>docs/3D_ASSETS.md</c> records for <c>npc_woman_dress</c>.
    ///
    /// It is only worth doing now because the shared library became self-sufficient: a full-body
    /// 24-clip set is a complete animation set on its own, where the old upper-body one would have
    /// given her three slots and no legs.
    ///
    /// The player is parented to this component rather than to the body — the body is still setting
    /// up its children during a component's _Ready and Godot refuses an AddChild there (CLAUDE.md
    /// §7) — with its root pointed back at the model, which is where the library's
    /// <c>%GeneralSkeleton</c> track paths resolve.
    /// </summary>
    private AnimationPlayer? AdoptPlayerlessBody(Node3D bodyRoot)
    {
        if (_skeleton == null)
        {
            return null;
        }

        var player = new AnimationPlayer
        {
            Name = "SharedAnimationPlayer",
            RootNode = bodyRoot.GetPath(),
        };
        AddChild(player);
        Log.Info($"{Entity?.DisplayName}: '{bodyRoot.Name}' ships no clips of its own; " +
                 "attaching the shared library to a created AnimationPlayer.");
        return player;
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
    /// <summary>
    /// The clip the tree and the action system should use for a slot — <b>the shared full-body
    /// library first, by exact name.</b>
    ///
    /// ⚠️ <b>Do not route this through <see cref="ResolveClip"/>.</b> That resolver is fuzzy by
    /// design and picks the first match in the player's clip list, which puts the OLD upper-body
    /// library ahead of the full-body one purely because it was registered first. The blend space
    /// came up holding <c>lib/Idle</c>, <c>lib/Walk</c> and <c>lib/Sprint</c> — clips with no leg
    /// tracks at all — so locomotion ran with the legs perfectly still and nothing logged a word.
    /// A body without the shared library (every quadruped) still falls through to its own clips.
    /// </summary>
    private string SharedClip(string slot)
    {
        string direct = $"{MeshyLibraryName}/{slot}";
        return _player != null && _player.HasAnimation(direct) ? direct : ResolveClip(slot);
    }

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
        if (!ReferenceEquals(e.Entity, Entity) || e.RemainingHealth <= 0f ||
            _combat is { IsBlocking: true })
        {
            return;
        }

        if (_tree != null && _playback != null)
        {
            // ⚠️ A flinch must not steal a committed swing. The action owns the body until it says
            // otherwise, and interrupting it here would reopen exactly the desync Stage 1 closed:
            // the hitbox would still be following the action's timeline while the visible body had
            // moved on to a flinch. Poise decides whether a hit interrupts, not the presentation.
            if (_actionClip.Length == 0)
            {
                _playback.Travel(LocomotionTree.HitState);
            }

            return;
        }

        PlayOneShot(_hit);
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
        // standing clip lifts the hips half a metre out of the saddle. ⚠️ This refusal is now
        // FALLBACK-ONLY — a tree-driven body plays the swing on its upper-body layer instead, with
        // the legs holding the ride pose, which is what 39B's comment said the real fix would be.
        if (_player == null || _deathPlayed || (Riding && _tree == null))
        {
            return -1f;
        }

        string clip = SharedClip(slot);
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
        float speed = ActionTimeline.ClipSpeedFor(clipSeconds, actual);
        _actionClip = clip;

        if (_tree != null && _playback != null)
        {
            if (Riding)
            {
                // Upper body only: the arms swing, the seat holds. Nothing travels, so locomotion
                // keeps the legs — which is the animation 39B had to give up entirely.
                SetUpperBodyClip(clip);
                return actual;
            }

            SetActionClip(clip, speed);
            _playback.Travel(LocomotionTree.ActionState);
            return actual;
        }

        _player.Play(clip, customBlend: 0.08, customSpeed: speed);
        return actual;
    }

    private void SetActionClip(string clip, float speed)
    {
        if (_tree?.TreeRoot is not AnimationNodeBlendTree root ||
            root.GetNode(LocomotionTree.StateMachineNode) is not AnimationNodeStateMachine machine ||
            machine.GetNode(LocomotionTree.ActionState) is not AnimationNodeBlendTree action ||
            action.GetNode("Anim") is not AnimationNodeAnimation anim)
        {
            return;
        }

        anim.Animation = clip;
        _tree.Set(LocomotionTree.ActionScaleParam, speed);
    }

    private void SetUpperBodyClip(string clip)
    {
        if (_tree?.TreeRoot is AnimationNodeBlendTree root &&
            root.GetNode("UpperBody") is AnimationNodeAnimation anim)
        {
            anim.Animation = clip;
            _tree.Set(LocomotionTree.UpperBodyBlendParam, 1f);
        }
    }

    public float ActionProgress
    {
        get
        {
            if (_actionClip.Length == 0)
            {
                return -1f;
            }

            if (_tree != null && _playback != null)
            {
                // ⚠️ The TREE's playback position, not the AnimationPlayer's. An active
                // AnimationTree drives the player, so CurrentAnimation and
                // CurrentAnimationPosition stop tracking what is on screen — reading them here
                // would hand the action a clock that has quietly stopped, which is the exact class
                // of defect this whole rebuild exists to end.
                if (_playback.GetCurrentNode() != LocomotionTree.ActionState)
                {
                    return -1f;
                }

                double length = _playback.GetCurrentLength();
                return length <= 0d ? 1f : (float)(_playback.GetCurrentPlayPosition() / length);
            }

            if (_player == null || _player.CurrentAnimation != _actionClip)
            {
                return -1f;
            }

            double playerLength = _player.CurrentAnimationLength;
            return playerLength <= 0d ? 1f : (float)(_player.CurrentAnimationPosition / playerLength);
        }
    }

    /// <summary>Releases the clip back to locomotion. Called when the action ends or is cancelled.</summary>
    public void StopAction()
    {
        _actionClip = "";
        if (_tree != null && _playback?.GetCurrentNode() == LocomotionTree.ActionState)
        {
            _playback.Travel(LocomotionTree.LocomotionState);
        }
    }

    /// <summary>
    /// Stands the <see cref="AnimationTree"/> up, or leaves it null and lets the fallback ladder
    /// drive.
    ///
    /// ⚠️ The tree is added as a child of THIS component rather than of the body. The body is still
    /// setting up its own children while a component's _Ready runs, and Godot refuses an AddChild
    /// there — it logs and carries on, leaving a live node that is not in the tree, whose _Ready
    /// never fires and which leaks as an orphan (CLAUDE.md §7). The component itself is not busy.
    /// </summary>
    private void BuildTree()
    {
        if (_player == null)
        {
            return;
        }

        foreach (string slot in new[]
                 { "idle", "walk", "run", "sprint", "walk_back", "block", "hit", "death" })
        {
            _slots[slot] = SharedClip(slot);
        }

        if (LocomotionTree.Build(_slots, _skeleton) is not { } root)
        {
            return;
        }

        var tree = new AnimationTree
        {
            Name = "AnimationTree",
            TreeRoot = root,
            AnimPlayer = _player.GetPath(),
            // The clips are authored at 30 fps and blended per frame, so the tree ticks with the
            // frame rather than with physics; a physics-stepped tree visibly stutters at high
            // refresh rates.
            CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Idle,
        };
        AddChild(tree);
        tree.Active = true;

        _tree = tree;
        _playback = tree.Get(LocomotionTree.PlaybackParam).As<AnimationNodeStateMachinePlayback>();
    }

    /// <summary>Signed forward speed in m/s — negative when backing up. The blend space's only
    /// input, and the reason walking no longer pops into a run at a threshold.</summary>
    private float ForwardSpeed()
    {
        float speed = HorizontalSpeed();
        if (Entity?.Body is not CharacterBody3D body || speed < 0.05f)
        {
            return speed;
        }

        Vector3 v = body.Velocity;
        Vector3 facing = -body.GlobalBasis.Z;
        float along = (v.X * facing.X) + (v.Z * facing.Z);
        return along < 0f ? -speed : speed;
    }

    public override void _Process(double delta)
    {
        _lastDelta = (float)delta;

        if (_player == null)
        {
            return;
        }

        if (_tree != null)
        {
            TickTree();
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

    /// <summary>The whole per-frame job when a tree is driving: feed it the speed, and put it in the
    /// right state. Clip selection is the tree's problem now, not a ladder's.</summary>
    private void TickTree()
    {
        if (_tree == null || _playback == null)
        {
            return;
        }

        if (_stats is { IsAlive: false })
        {
            if (!_deathPlayed)
            {
                _playback.Travel(LocomotionTree.DeathState);
                _deathPlayed = true;
            }

            return;
        }

        if (_deathPlayed)
        {
            // Respawned. Travelling back is not enough — the death state is deliberately terminal —
            // so the machine is restarted from its entry.
            _playback.Start(LocomotionTree.LocomotionState);
            _deathPlayed = false;
        }

        _tree.Set(LocomotionTree.SpeedParam, ForwardSpeed());

        // The upper-body layer carries the guard and channel poses. Blending rather than switching
        // is what lets a blocking character keep walking, and a mounted one keep its seat (39B).
        bool upperBody = _combat is { IsBlocking: true } ||
                         _spellcasting is { IsCharging: true } or { IsChanneling: true };
        float target = upperBody ? 1f : 0f;
        float current = (float)_tree.Get(LocomotionTree.UpperBodyBlendParam);
        _tree.Set(LocomotionTree.UpperBodyBlendParam,
            Mathf.MoveToward(current, target, _lastDelta / UpperBodyBlendSeconds));

        if (_actionClip.Length == 0 && _playback.GetCurrentNode() == LocomotionTree.ActionState)
        {
            _playback.Travel(LocomotionTree.LocomotionState);
        }

        // The flinch is a one-shot state with no exit condition of its own; locomotion reclaims the
        // body once the clip has run. Without this the actor stays bent over its wound forever.
        if (_playback.GetCurrentNode() == LocomotionTree.HitState &&
            _playback.GetCurrentPlayPosition() >= _playback.GetCurrentLength() - 0.05d)
        {
            _playback.Travel(LocomotionTree.LocomotionState);
        }
    }

    /// <summary>Seconds the guard pose takes to blend in or out. Long enough to read as raising a
    /// weapon rather than snapping to it.</summary>
    private const float UpperBodyBlendSeconds = 0.18f;

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
