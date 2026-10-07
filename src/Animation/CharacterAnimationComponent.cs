using Embervale.Core.Diagnostics;
using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Enemies;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Embervale.Movement;
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
    private LocomotionComponent? _locomotion;
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
    private bool _actionHeld;
    private float _actionSpeed = 1f;
    private float _heldPlayerSpeed = 1f;

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
        if (_tree == null && _player != null)
        {
            // No tree: the player is the mixer, and this component steps it (see AdvanceMixer).
            _player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        }

        _spellcasting = Entity.GetComponent<SpellcastingComponent>();
        // The run speed itself is read on the first tick (TickLod), not here: it asks the stats
        // component, and a sibling's OnInitialize is not guaranteed to have run yet.
        _locomotion = Entity.GetComponent<LocomotionComponent>();

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
        if (!ReferenceEquals(e.Caster, Entity) || _spellcasting is { IsChanneling: true } ||
            SpellDatabase.Get(e.SpellId) is { CastMode: CastMode.Channeled })
        {
            return;
        }

        if (_actionClip.Length == 0)
        {
            if (_tree != null)
            {
                // Arms only: the thrust plays on the upper-body layer and the legs keep whatever
                // locomotion had them doing. Played on the AnimationPlayer it would do nothing at
                // all, because an active tree is what poses the body.
                PlayUpperOneShot(_cast);
            }
            else
            {
                PlayOneShot(_cast);
            }
        }

        if (SpellDatabase.Get(e.SpellId) is { } spell)
        {
            SpellVfx.HandFlash(Entity!, spell, CastingHandPosition());
        }
    }

    /// <summary>World position of the left (casting) hand bone, falling back to chest height.</summary>
    private Vector3 CastingHandPosition() =>
        TryGetCastingHand(out Vector3 hand) ? hand : Entity!.Body.GlobalPosition + (Vector3.Up * 1.3f);

    /// <summary>World position of the left (casting) hand bone, for effects that start at the hand.
    /// False when the body has no skeleton or nothing on it reads as a hand (a quadruped, a turret).</summary>
    public bool TryGetCastingHand(out Vector3 position)
    {
        // Something that poses the arm after the animation (the first-person arm) said where the
        // hand is actually drawn. Only as fresh as the last frame: the moment it stops saying so,
        // the bone is the answer again.
        if (_hasReportedHand && Engine.GetProcessFrames() - _reportedHandFrame <= 1 &&
            _skeleton != null && IsInstanceValid(_skeleton) && _skeleton.IsInsideTree())
        {
            position = _skeleton.GlobalTransform * _reportedHand;
            return true;
        }

        if (_skeleton != null && IsInstanceValid(_skeleton) && _skeleton.IsInsideTree() &&
            HumanoidBones.FindHand(_skeleton, right: false) is { Length: > 0 } hand)
        {
            position = (_skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_skeleton.FindBone(hand))).Origin;
            return true;
        }

        position = default;
        return false;
    }

    private Vector3 _reportedHand;
    private ulong _reportedHandFrame;
    private bool _hasReportedHand;

    /// <summary>
    /// Tells the component where the casting hand is drawn this frame, in the skeleton's own space
    /// (a bone's global pose origin), when a skeleton modifier has moved it from where the clip put
    /// it. A modifier's pose lasts one frame and the bone reads back as the clip's afterwards, so
    /// without this an effect anchored to the hand would sit where the hand is not. Kept in the
    /// skeleton's space, like the bone it stands in for: it is read up to a frame later, and a
    /// world position that old trails a running body by the distance it covered. Called every frame
    /// it applies; it lapses by itself.
    /// </summary>
    public void ReportCastingHand(Vector3 inSkeleton)
    {
        _reportedHand = inSkeleton;
        _reportedHandFrame = Engine.GetProcessFrames();
        _hasReportedHand = true;
    }

    /// <summary>The body's skeleton, for effects that anchor to a bone; null when it has none.</summary>
    public Skeleton3D? Skeleton => _skeleton != null && IsInstanceValid(_skeleton) ? _skeleton : null;

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
                TravelTo(HitStateName);
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
        ResumeAction();
        // A combo link starts the next action without stopping the last, so the layer is let go
        // here: left held, a swing off the upper-body library would sit over the arms of the
        // full-body one that follows it. The route below takes it again if this clip wants it.
        _upperAction = false;
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
        _actionSpeed = speed;
        _actionClip = clip;

        // The clip is about to become this action's clock, so it runs at full rate from its very
        // first frame rather than from the next tick's level-of-detail pass.
        if (_lodCoarse)
        {
            SetCoarse(false);
        }

        if (_tree != null && _playback != null)
        {
            if (Riding || IsUpperBodyClip(clip))
            {
                // Upper body only: the arms swing, the seat holds. Nothing travels, so locomotion
                // keeps the legs — which is the animation 39B had to give up entirely. The same
                // door takes any clip out of the upper-body library: it has no leg tracks, so in
                // the action state it would stand the legs in their rest pose for its whole length.
                SetUpperBodyClip(clip, speed);
                // Chained straight out of a full-body action, the machine is still parked on that
                // clip's last frame; hand the legs back to locomotion.
                if (_playback.GetCurrentNode() == ActionStateName)
                {
                    TravelTo(LocomotionStateName);
                }

                return actual;
            }

            SetActionClip(clip, speed);
            TravelTo(ActionStateName);
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
            action.GetNode(LocomotionTree.ActionAnimNode) is not AnimationNodeAnimation anim)
        {
            return;
        }

        anim.Animation = clip;
        _tree.Set(ActionScaleParamName, speed);
    }

    /// <summary>Hands the upper-body layer to an action's clip for as long as the action runs
    /// (<see cref="StopAction"/> lets it go), at the rate that spans the action's duration.</summary>
    private void SetUpperBodyClip(string clip, float speed)
    {
        if (!ShowOnUpperBody(clip, speed))
        {
            return;
        }

        _upperAction = true;
        _upperHold = 0f;
        _upperBlend = 1f;
        _tree!.Set(UpperBodyBlendParamName, 1f);
    }

    /// <summary>Plays a clip once on the upper-body layer and lets the layer go when it has run.
    /// The layer eases in over <see cref="UpperBodyOneShotSeconds"/> rather than snapping, and is
    /// not taken from an action that already has it.</summary>
    private void PlayUpperOneShot(string clip)
    {
        if (_player == null || clip.Length == 0 || _deathPlayed || _upperAction || !_player.HasAnimation(clip))
        {
            return;
        }

        float seconds = (float)_player.GetAnimation(clip).Length;
        if (seconds > 0f && ShowOnUpperBody(clip, 1f))
        {
            // Let go a blend's length early, so the layer is easing out as the clip ends rather
            // than holding its last frame while it does.
            _upperHold = Mathf.Max(seconds - UpperBodyBlendSeconds, UpperBodyOneShotSeconds);
        }
    }

    /// <summary>Puts a clip on the upper-body node from its first frame. False when there is no
    /// tree to put it on.</summary>
    private bool ShowOnUpperBody(string clip, float speed)
    {
        if (_tree == null || clip.Length == 0)
        {
            return false;
        }

        _upperAnim ??= _tree.TreeRoot is AnimationNodeBlendTree root
            ? root.GetNode(LocomotionTree.UpperBodyNode) as AnimationNodeAnimation
            : null;
        if (_upperAnim == null)
        {
            return false;
        }

        if (_upperClip != clip)
        {
            _upperAnim.Animation = clip;
            _upperClip = clip;
        }

        _tree.Set(UpperBodyScaleParamName, speed);
        _tree.Set(UpperBodySeekParamName, 0f);
        return true;
    }

    /// <summary>Whether a clip comes out of the upper-body library, which carries no leg tracks and
    /// so can only ever be shown through the upper-body layer.</summary>
    private static bool IsUpperBodyClip(string clip) =>
        clip.StartsWith(UpperBodyLibraryPrefix, System.StringComparison.Ordinal);

    private const string UpperBodyLibraryPrefix = "lib/";

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
                if (_playback.GetCurrentNode() != ActionStateName)
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
        ResumeAction();
        _actionClip = "";
        // An action shown on the upper-body layer just lets the layer go; TickTree eases it out.
        _upperAction = false;
        if (_tree != null && _playback?.GetCurrentNode() == ActionStateName)
        {
            TravelTo(LocomotionStateName);
        }
    }

    /// <summary>Holds a channel's visible release pose while its action remains committed.</summary>
    public void HoldAction()
    {
        if (_actionHeld || _actionClip.Length == 0)
        {
            return;
        }

        _actionHeld = true;
        _heldPlayerSpeed = _player?.SpeedScale ?? 1f;
        if (_tree != null && _upperAction)
        {
            _tree.Set(UpperBodyScaleParamName, 0f);
        }
        else if (_tree != null && _playback?.GetCurrentNode() == ActionStateName)
        {
            _tree.Set(ActionScaleParamName, 0f);
        }
        else if (_player != null)
        {
            _player.SpeedScale = 0f;
        }
    }

    /// <summary>Resumes the held clip for recovery or cancellation, restoring its previous speed.</summary>
    public void ResumeAction()
    {
        if (!_actionHeld)
        {
            return;
        }

        _actionHeld = false;
        if (_tree != null)
        {
            _tree.Set(ActionScaleParamName, _actionSpeed);
            if (_upperAction)
            {
                _tree.Set(UpperBodyScaleParamName, _actionSpeed);
            }
        }
        if (_player != null)
        {
            _player.SpeedScale = _heldPlayerSpeed;
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

        // The strafes and the fall come from the full-body library BY NAME or not at all. The fuzzy
        // resolver would answer "fall" with a body's own "Fall_Dead" and "strafe" with nothing, and
        // a missing one is handled where the tree is built (a borrowed walk, no airborne state).
        foreach (string slot in new[] { "strafe_left", "strafe_right", LocomotionTree.FallState })
        {
            string direct = $"{MeshyLibraryName}/{slot}";
            _slots[slot] = _player.HasAnimation(direct) ? direct : string.Empty;
        }

        if (LocomotionTree.Build(_slots, _skeleton) is not { } root)
        {
            return;
        }

        _hasFall = LocomotionTree.HasFall(_slots);
        _upperClip = _slots["block"].Length > 0 ? _slots["block"] : _slots["idle"];

        var tree = new AnimationTree
        {
            Name = "AnimationTree",
            TreeRoot = root,
            AnimPlayer = _player.GetPath(),
            // The clips are authored at 30 fps and blended per frame, so the tree ticks with the
            // frame rather than with physics; a physics-stepped tree visibly stutters at high
            // refresh rates. This component steps it from _Process (AdvanceMixer).
            // ⚠️ Set once, before the tree is active, and never written again: changing the mode
            // on an active tree deactivates and reactivates it, which restarts the state machine
            // from its entry. A corpse stands back up.
            CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
        };
        AddChild(tree);
        tree.Active = true;

        _tree = tree;
        _playback = tree.Get(LocomotionTree.PlaybackParam).As<AnimationNodeStateMachinePlayback>();
        _upperBlend = (float)tree.Get(UpperBodyBlendParamName);
    }

    /// <summary>
    /// Where the body is in the locomotion blend space: sideways and forward speed, each over the
    /// actor's own run speed (<see cref="LocomotionBlend.Gait"/>). The space's only input, and the
    /// reason walking no longer pops into a run at a threshold.
    ///
    /// <para>A rider's legs are not what is moving it, so in the saddle this is the idle point: the
    /// body's speed there is the horse's.</para>
    /// </summary>
    private Vector2 Gait()
    {
        if (Riding)
        {
            return Vector2.Zero;
        }

        float speed = HorizontalSpeed();
        if (Entity?.Body is not CharacterBody3D body)
        {
            // Walked along by position (a scheduled NPC): there is a speed and no heading to split
            // it by, and such a body always walks the way it faces.
            (float x, float y) = LocomotionBlend.Gait(speed, 0f, _runSpeed);
            return new Vector2(x, y);
        }

        if (speed < LocomotionBlend.StillSpeed)
        {
            return Vector2.Zero;
        }

        Vector3 v = body.Velocity;
        Basis basis = body.GlobalBasis;
        Vector3 facing = -basis.Z;
        Vector3 right = basis.X;
        (float strafe, float forward) = LocomotionBlend.Gait(
            (v.X * facing.X) + (v.Z * facing.Z), (v.X * right.X) + (v.Z * right.Z), _runSpeed);
        return new Vector2(strafe, forward);
    }

    /// <summary>The speed this actor's gaits are measured against. A body with no motor is walked by
    /// its schedule, which is slower than any stat it carries would say.</summary>
    private float ResolveRunSpeed() => _locomotion == null
        ? LocomotionBlend.FallbackRunSpeed
        : LocomotionBlend.RunSpeed(_stats?.GetValue(StatType.MoveSpeed) ?? 0f, _locomotion.BaseSpeed);

    public override void _Process(double delta)
    {
        _lastDelta = (float)delta;

        if (_player == null)
        {
            // No rig to drive, and nothing can give this body one later: the player is resolved
            // once in OnInitialize. The event handlers need no tick, so stop being called.
            SetProcess(false);
            return;
        }

        TickLod();
        TickState();
        // After the state tick, so this frame's parameters and travel requests are the ones posed,
        // which is the order the engine gave when the mixer was a child stepping itself.
        AdvanceMixer(delta);
    }

    private void TickState()
    {
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
                TravelTo(DeathStateName);
                _deathPlayed = true;
            }

            // The layer comes off a corpse. A body killed mid-swing or mid-guard otherwise lies
            // there with its arms and chest still held in that pose over the death clip.
            ReleaseUpperBody();
            return;
        }

        if (_deathPlayed)
        {
            // Respawned. Travelling back is not enough — the death state is deliberately terminal —
            // so the machine is restarted from its entry.
            _playback.Start(LocomotionStateName);
            _settledTicks = 0;
            _deathPlayed = false;
            _falling = false;
            _airborne = 0f;
        }

        // Parameters are written only when they change. A tree parameter keeps its value, and a
        // standing character's gait and guard blend are the same numbers frame after frame, so
        // re-sending them was two or three engine calls per character per frame for nothing.
        Vector2 gait = Gait();
        if (gait != _sentGait)
        {
            _sentGait = gait;
            _tree.Set(SpeedParamName, gait);
        }

        TickFall();
        TickUpperBody();

        // The two checks below need the machine's current state, which costs an engine call and a
        // freshly allocated StringName every time it is asked. They only have something to do for a
        // short while after this component asks the machine to go somewhere, so the state is read
        // until it has been seen resting in locomotion for SettledTicks ticks running and then left
        // alone until the next request (every Travel/Start here resets the count).
        if (_settledTicks >= SettledTicks)
        {
            return;
        }

        using StringName current = _playback.GetCurrentNode();
        _settledTicks = current == LocomotionStateName ? _settledTicks + 1 : 0;

        if (_actionClip.Length == 0 && current == ActionStateName)
        {
            TravelTo(LocomotionStateName);
        }

        // The flinch is a one-shot state with no exit condition of its own; locomotion reclaims the
        // body once the clip has run. Without this the actor stays bent over its wound forever.
        if (current == HitStateName &&
            _playback.GetCurrentPlayPosition() >= _playback.GetCurrentLength() - 0.05d)
        {
            TravelTo(LocomotionStateName);
        }
    }

    /// <summary>
    /// The airborne pose. Off the ground and moving vertically for a moment, a body in locomotion
    /// eases into the fall clip, and back out when it lands. An action, a flinch or a death that
    /// takes the body in the air simply keeps it; the fall is asked for again once that is over.
    /// </summary>
    private void TickFall()
    {
        if (!_hasFall || _locomotion == null || _playback == null)
        {
            return;
        }

        bool grounded = _locomotion.IsGrounded;
        float vertical = Entity?.Body is CharacterBody3D body ? body.Velocity.Y : 0f;
        _airborne = LocomotionBlend.AirborneStep(_airborne, grounded, vertical, _lastDelta);

        bool wanted = LocomotionBlend.Falling(_airborne) && _actionClip.Length == 0 && !Riding;
        if (wanted == _falling)
        {
            return;
        }

        using StringName current = _playback.GetCurrentNode();
        if (wanted)
        {
            // Only out of locomotion. While something else has the body this is asked again every
            // frame, which is one state read a frame for the length of a flinch in mid-air.
            if (current == LocomotionStateName)
            {
                TravelTo(FallStateName);
            }

            return;
        }

        if (current == FallStateName)
        {
            TravelTo(LocomotionStateName);
        }

        _falling = false;
    }

    /// <summary>
    /// The upper-body layer: which clip it shows and how far it is blended in. It carries the guard,
    /// the charge and channel pose, the cast thrust and a rider's swing. Blending rather than
    /// switching is what lets a blocking or channelling character keep walking, and a mounted one
    /// keep its seat (39B).
    ///
    /// <para>⚠️ The node holds ONE clip, and it used to hold the guard and nothing else: a caster
    /// charging a spell raised the layer and stood there blocking. The clip now follows what the
    /// layer is up for. An action or a one-shot keeps the node until it is done; otherwise a charge
    /// or channel shows the channel pose, and only a guard shows the guard.</para>
    /// </summary>
    private void TickUpperBody()
    {
        if (_tree == null)
        {
            return;
        }

        if (_upperHold > 0f)
        {
            _upperHold = Mathf.Max(_upperHold - _lastDelta, 0f);
        }

        bool casting = _spellcasting is { IsCharging: true } or { IsChanneling: true };
        bool guarding = _combat is { IsBlocking: true };
        bool taken = _upperAction || _upperHold > 0f;

        if (!taken)
        {
            string pose = casting && _channel.Length > 0 ? _channel
                : guarding || casting ? _slots["block"]
                : _upperClip;
            if (pose.Length > 0 && pose != _upperClip)
            {
                ShowOnUpperBody(pose, 1f);
            }
        }

        float target = taken || casting || guarding ? 1f : 0f;
        float seconds = _upperHold > 0f ? UpperBodyOneShotSeconds : UpperBodyBlendSeconds;
        float blend = Mathf.MoveToward(_upperBlend, target, _lastDelta / seconds);
        if (blend != _upperBlend)
        {
            _upperBlend = blend;
            _tree.Set(UpperBodyBlendParamName, blend);
        }
    }

    /// <summary>Lets the upper-body layer go whatever had it and eases it out.</summary>
    private void ReleaseUpperBody()
    {
        _upperAction = false;
        _upperHold = 0f;
        if (_tree == null || _upperBlend <= 0f)
        {
            return;
        }

        _upperBlend = Mathf.MoveToward(_upperBlend, 0f, _lastDelta / UpperBodyBlendSeconds);
        _tree.Set(UpperBodyBlendParamName, _upperBlend);
    }

    // Built once. The tree's parameter paths and state names were string constants, and every
    // call that took one converted it to a new StringName: an engine round trip and a finalizable
    // object apiece, seven or so per animated character per frame.
    private static readonly StringName SpeedParamName = LocomotionTree.SpeedParam;
    private static readonly StringName UpperBodyBlendParamName = LocomotionTree.UpperBodyBlendParam;
    private static readonly StringName ActionScaleParamName = LocomotionTree.ActionScaleParam;
    private static readonly StringName UpperBodyScaleParamName = LocomotionTree.UpperBodyScaleParam;
    private static readonly StringName UpperBodySeekParamName = LocomotionTree.UpperBodySeekParam;
    private static readonly StringName FallStateName = LocomotionTree.FallState;
    private static readonly StringName LocomotionStateName = LocomotionTree.LocomotionState;
    private static readonly StringName ActionStateName = LocomotionTree.ActionState;
    private static readonly StringName HitStateName = LocomotionTree.HitState;
    private static readonly StringName DeathStateName = LocomotionTree.DeathState;

    /// <summary>Consecutive locomotion ticks after which the state machine is taken to be at rest.
    /// A travel request is picked up on the machine's next step, so a handful of frames is ample
    /// and the cost of being generous is a few extra reads.</summary>
    private const int SettledTicks = 12;

    private int _settledTicks;
    private Vector2 _sentGait = new(float.NaN, float.NaN);
    private float _upperBlend;

    /// <summary>The speed this actor's gaits are measured against; re-read with the level-of-detail
    /// check, since a slow or a haste moves the stat.</summary>
    private float _runSpeed = LocomotionBlend.FallbackRunSpeed;

    /// <summary>Whether the tree has an airborne state, how long the body has been falling, and
    /// whether the machine was last sent there.</summary>
    private bool _hasFall;
    private float _airborne;
    private bool _falling;

    /// <summary>The upper-body node, the clip on it, whether an action has it (until
    /// <see cref="StopAction"/>) and the seconds a one-shot still has it for.</summary>
    private AnimationNodeAnimation? _upperAnim;
    private string _upperClip = "";
    private bool _upperAction;
    private float _upperHold;

    private void TravelTo(StringName state)
    {
        _playback!.Travel(state);
        _settledTicks = 0;
        _falling = state == FallStateName;
    }

    // --- distance level of detail -----------------------------------------------------------------

    /// <summary>Beyond this many metres from the player a body's animation is stepped on every
    /// <see cref="LodStride"/>th frame instead of every frame. At that range a person is a couple
    /// of dozen pixels tall, and posing and skinning a skeleton nobody can read at full rate was
    /// the largest per-actor cost a crowd had.</summary>
    private const float LodDistance = 40f;
    private const int LodStride = 3;
    private const double LodCheckSeconds = 0.5d;

    private double _lodTimer;
    private bool _lodFar;
    private bool _lodCoarse;
    private double _lodBanked;
    private int _lodFrame;

    /// <summary>Whichever mixer is actually advancing the pose: the tree when there is one (it
    /// drives the player), otherwise the player itself. Both are in manual callback mode for their
    /// whole life, so <see cref="AdvanceMixer"/> is the only thing that moves a pose.</summary>
    private AnimationMixer? Mixer => _tree != null ? _tree : _player;

    private void TickLod()
    {
        _lodTimer -= _lastDelta;
        if (_lodTimer <= 0d)
        {
            _lodTimer = LodCheckSeconds;
            _lodFar = IsFarOrHidden();
            _runSpeed = ResolveRunSpeed();
        }

        // ⚠️ Never while an action clip is running: the clip IS that action's clock
        // (ActionProgress), and a clock that moves every third frame would move its hit window.
        bool coarse = _lodFar && _actionClip.Length == 0;
        if (coarse != _lodCoarse)
        {
            SetCoarse(coarse);
        }
    }

    /// <summary>Steps the pose: every frame at full rate, every <see cref="LodStride"/>th frame
    /// when coarse, and then by the whole of the skipped time so clips keep their real speed.</summary>
    private void AdvanceMixer(double delta)
    {
        _lodBanked += delta;
        if (_lodCoarse && ++_lodFrame < LodStride)
        {
            return;
        }

        Flush();
    }

    private void Flush()
    {
        double step = _lodBanked;
        _lodBanked = 0d;
        _lodFrame = 0;
        if (step > 0d && Mixer is { } mixer && GodotObject.IsInstanceValid(mixer))
        {
            mixer.Advance(step);
        }
    }

    /// <summary>Only a flag: the mixer's mode never changes, so nothing restarts. Leaving coarse
    /// hands the pose whatever time was banked, so what follows starts from the present.</summary>
    private void SetCoarse(bool coarse)
    {
        if (!coarse)
        {
            Flush();
        }

        _lodFrame = 0;
        _lodCoarse = coarse;
    }

    /// <summary>Far from the player, or not drawn at all (an unrecruited companion, a flag-gated
    /// actor). With no player there is nothing to be far from — a menu scene or a render harness —
    /// and the body animates at full rate.</summary>
    private bool IsFarOrHidden()
    {
        if (Entity?.Body is not { } body || !GodotObject.IsInstanceValid(body))
        {
            return false;
        }

        if (!body.IsVisibleInTree())
        {
            return true;
        }

        if (Core.Services.ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out Player.PlayerCharacter player) || !GodotObject.IsInstanceValid(player) ||
            ReferenceEquals(player, Entity))
        {
            return false;
        }

        return body.GlobalPosition.DistanceSquaredTo(player.GlobalPosition) > LodDistance * LodDistance;
    }

    /// <summary>Seconds the guard pose takes to blend in or out. Long enough to read as raising a
    /// weapon rather than snapping to it.</summary>
    private const float UpperBodyBlendSeconds = 0.18f;

    /// <summary>Seconds a one-shot (the cast thrust) takes to come up on the layer. Shorter than the
    /// guard's, or the first frames of the thrust are spent blending in.</summary>
    private const float UpperBodyOneShotSeconds = 0.08f;

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
