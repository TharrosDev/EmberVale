using System.Collections.Generic;
using Embervale.Combat.Actions;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Player;
using Embervale.Settings;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Lock-on / soft target (Phase 29H), built out from the Phase 18 <c>FocusedEntity</c>. Holds the current
/// locked <see cref="Target"/>, acquires the nearest hostile (or a preferred aimed-at entity) on toggle,
/// cycles between nearby hostiles, and drops a target that dies or leaves range. The owning controller
/// faces the body at the target; the HUD reticles it. Target queries are a physics sphere sweep, run only
/// on input (toggle/cycle), never per frame.
///
/// <para>The camera-facing rules (Settings.LockOnFraming) are: cycling walks the candidates left to
/// right on screen, a target lost to range or cover gets a short grace before the lock breaks, and the
/// body swings round to a newly locked target over a beat instead of cutting. With the setting off all
/// three fall back to the original behaviour.</para>
/// </summary>
[GlobalClass]
public partial class LockOnComponent : EntityComponent
{
    [Export] public float AcquireRange { get; set; } = 18f;

    /// <summary>A locked target is kept until it leaves this (larger) range.</summary>
    [Export] public float DropRange { get; set; } = 24f;

    /// <summary>Seconds the body takes to swing round to a newly locked target, after which it faces
    /// exactly (no lag on a moving foe).</summary>
    private const float SettleSeconds = 0.3f;

    /// <summary>Ease time of that swing.</summary>
    private const float SettleEaseSeconds = 0.07f;

    /// <summary>Top speed of that swing, radians a second (about 515 degrees).</summary>
    private const float SettleMaxRate = 9f;

    /// <summary>A target is dropped outright, grace or not, past this multiple of the drop range.</summary>
    private const float HardDropFactor = 1.25f;

    /// <summary>Seconds between line-of-sight checks on the held target.</summary>
    private const float SightInterval = 0.1f;

    private CharacterBody3D? _body;
    private int _team;
    private SettingsService? _settings;
    private IEntity? _target;
    private float _lostSeconds;
    private float _settleLeft;
    private float _sightTimer;
    private bool _inSight = true;
    private ulong _tickStamp;
    private ulong _faceStamp;
    private readonly FlickGate _flick = new();
    private Vector3 _lastPoint;
    private LockBreakReason _lostBy = LockBreakReason.LostSight;

    public IEntity? Target
    {
        get => _target;
        private set => SetTarget(value, LockBreakReason.Invalid);
    }

    /// <summary>Whether Lock-On Assist is on (the live comfort setting): a wider cone, threats first,
    /// the lock passing on to the next enemy on a kill, and flick switching.</summary>
    private static bool Assist => LiveComfort.Get().LockOnAssist;

    /// <summary>
    /// Changes the lock and says so: <see cref="LockChangedEvent"/> for any change, plus
    /// <see cref="LockBrokenEvent"/> (with <paramref name="reason"/>) when a held lock ends. A cycle
    /// from one target to another is a change, not a break.
    /// </summary>
    private void SetTarget(IEntity? value, LockBreakReason reason)
    {
        if (ReferenceEquals(_target, value))
        {
            return;
        }

        IEntity? previous = _target;
        Vector3 lastPoint = previous is { Body: { } body } && GodotObject.IsInstanceValid(body)
            ? body.GlobalPosition + (Vector3.Up * 1.0f)
            : _lastPoint;

        _target = value;
        _lostSeconds = 0f;
        _sightTimer = 0f;
        _inSight = true;
        _tickStamp = 0;
        _faceStamp = 0;
        _settleLeft = value != null ? SettleSeconds : 0f;
        _flick.Reset();
        if (value is { Body: { } newBody } && GodotObject.IsInstanceValid(newBody))
        {
            _lastPoint = newBody.GlobalPosition + (Vector3.Up * 1.0f);
        }

        if (Entity == null)
        {
            return;
        }

        EventBus.Instance?.Publish(new LockChangedEvent(Entity, value));
        if (value == null && previous != null)
        {
            EventBus.Instance?.Publish(new LockBrokenEvent(Entity, previous, reason, lastPoint));
        }
    }

    /// <summary>Ends the lock for <paramref name="reason"/>; with the assist on a kill passes it to the
    /// next enemy close by instead of dropping.</summary>
    private void Drop(LockBreakReason reason)
    {
        SetTarget(null, reason);
        if (!LockOn.ShouldAutoAdvance(Assist, reason))
        {
            return;
        }

        foreach (IEntity candidate in Acquire())
        {
            if (DistanceSq(candidate) <= LockOn.AutoAdvanceRange * LockOn.AutoAdvanceRange)
            {
                SetTarget(candidate, LockBreakReason.Invalid);
                return;
            }
        }
    }

    public bool IsLocked => Target != null;

    /// <summary>The locked target as a plain node — for callers that cannot see <see cref="IEntity"/>
    /// (GDScript, the headless probe).</summary>
    public Node? TargetNode => _target as Node;

    protected override void OnInitialize()
    {
        _body = Entity!.Body as CharacterBody3D;
        _team = Entity!.GetComponent<CombatComponent>()?.Team ?? 0;
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;
    }

    /// <summary>Whether the eased lock behaviour is on; true with no settings service.</summary>
    private bool Framing => _settings?.Current.LockOnFraming ?? true;

    /// <summary>Toggles lock: releases if already locked, otherwise locks the <paramref name="preferred"/>
    /// entity (the aimed-at focus) if it's a valid hostile, else the nearest hostile.</summary>
    public void Toggle(IEntity? preferred)
    {
        if (Target != null)
        {
            SetTarget(null, LockBreakReason.Toggled);
            return;
        }

        Target = IsValid(preferred) ? preferred : Nearest();
    }

    /// <summary>Toggles the lock with no aimed-at preference. A plain-argument door for callers that
    /// cannot pass an <see cref="IEntity"/> — the headless probe is one (GDScript cannot reach a method
    /// taking an interface).</summary>
    public void ToggleNearest() => Toggle(null);

    /// <summary>Switches to the next/previous nearby hostile. With the framing setting on this is
    /// left to right across the screen; nothing to switch to leaves a good lock alone.</summary>
    public void Cycle(int dir)
    {
        List<IEntity> targets = Acquire();
        if (targets.Count == 0)
        {
            // Nothing to cycle to must not break a lock that is still good.
            if (Target == null || !Framing)
            {
                SetTarget(null, LockBreakReason.Invalid);
            }

            return;
        }

        int current = Target != null ? targets.IndexOf(Target) : -1;
        if (!Framing)
        {
            Target = targets[LockOn.CycleIndex(current, targets.Count, dir)];
            return;
        }

        // Left to right on screen rather than down a score list that reshuffles as things move.
        var bearings = new List<float>(targets.Count);
        foreach (IEntity candidate in targets)
        {
            bearings.Add(BearingOf(candidate));
        }

        Target = targets[LockOn.CycleByBearing(bearings, current, dir)];
    }

    /// <summary>
    /// Drops the target if it has died or is gone for good. Cheap — call each frame. A target that
    /// has only stepped out of range or behind cover keeps the lock for
    /// <see cref="LockOn.LossGraceSeconds"/> so a dodge round a pillar does not throw away the lock
    /// (and the camera framing with it); death and a far overshoot drop it at once.
    /// </summary>
    public void Tick()
    {
        if (Target is not { } target)
        {
            return;
        }

        if (!Framing)
        {
            if (!IsValid(target))
            {
                Drop(DropReason(target));
            }

            return;
        }

        float dt = Elapsed(ref _tickStamp);
        float distanceSq = DistanceSq(target);
        float hard = DropRange * HardDropFactor;
        if (!IsAliveHostile(target) || distanceSq > hard * hard)
        {
            Drop(DropReason(target));
            return;
        }

        if (target.Body is { } held && GodotObject.IsInstanceValid(held))
        {
            _lastPoint = held.GlobalPosition + (Vector3.Up * 1.0f);
        }

        // Flick the right stick to step to the next target in that direction (the mouse does the
        // same from _Input). Only with the assist on; the cycle keys always work.
        if (Assist && InputMap.HasAction(InputActions.LookLeft) && InputMap.HasAction(InputActions.LookRight))
        {
            // Fed even while the spell wheel has the stick, so a push made for the wheel is already
            // "held" when the wheel closes and is not then read as a flick. Its result is dropped.
            int flick = _flick.FeedStick(Input.GetAxis(InputActions.LookLeft, InputActions.LookRight), dt);
            if (flick != 0 && !PlayGate.WheelOpen)
            {
                Cycle(flick);
                return;
            }
        }

        _sightTimer -= dt;
        if (_sightTimer <= 0f)
        {
            _sightTimer = SightInterval;
            _inSight = HasLineOfSight(target);
        }

        bool inRange = LockOn.InRange(distanceSq, DropRange * DropRange);
        bool inView = _inSight && inRange;
        if (!inView)
        {
            _lostBy = inRange ? LockBreakReason.LostSight : LockBreakReason.OutOfRange;
        }

        _lostSeconds = LockOn.StepLoss(_lostSeconds, inView, dt);
        if (LockOn.ShouldDrop(_lostSeconds, LockOn.LossGraceSeconds))
        {
            Drop(_lostBy);
        }
    }

    /// <summary>Why a target that fails the validity test is gone: dead (or freed), out past the drop
    /// range, or simply no longer a hostile.</summary>
    private LockBreakReason DropReason(IEntity target)
    {
        if (target is not Node node || !GodotObject.IsInstanceValid(node) ||
            target.GetComponent<StatsComponent>() is not { IsAlive: true })
        {
            return LockBreakReason.TargetDied;
        }

        return DistanceSq(target) > DropRange * DropRange ? LockBreakReason.OutOfRange : LockBreakReason.Invalid;
    }

    /// <summary>Flicking the mouse while locked on steps to the next target that way (with the assist
    /// on). The body faces the target on its own while locked, so the mouse's yaw is free for this.</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion motion || Target == null || !Framing || !Assist ||
            Input.MouseMode != Input.MouseModeEnum.Captured ||
            GameManager.Instance is { IsPlaying: false } || UiState.MenuOpen || PlayGate.WheelOpen)
        {
            return;
        }

        int dir = _flick.FeedMouse(motion.Relative.X, (float)GetProcessDeltaTime());
        if (dir != 0)
        {
            Cycle(dir);
        }
    }

    /// <summary>
    /// While locked on, yaws the owner's body to face the target (look input only pitches). The
    /// level look — the target sampled at the body's own height — keeps it a pure yaw, so attacks
    /// and strafing orient at the foe rather than tilting the character at a taller or shorter one.
    ///
    /// <para>It lives here rather than in the player's input router because the rule is about the
    /// lock, not about the player: whoever holds a target faces it.</para>
    /// </summary>
    public void FaceTarget()
    {
        if (Target is not { } target || target.Body is not Node3D targetBody ||
            Entity?.Body is not Node3D body)
        {
            return;
        }

        Vector3 to = targetBody.GlobalPosition - body.GlobalPosition;
        to.Y = 0f;
        if (to.LengthSquared() < 0.01f)
        {
            return;
        }

        // A fresh lock or a cycle swings round over a beat, so the camera (which rides the body's
        // yaw) settles onto the target instead of cutting to it. Afterwards it faces exactly.
        float dt = Elapsed(ref _faceStamp);
        if (Framing && _settleLeft > 0f)
        {
            _settleLeft -= dt;
            Vector3 rotation = body.GlobalRotation;
            rotation.Y = FramingMath.SlewYaw(
                rotation.Y, FramingMath.YawTo(to), dt, SettleEaseSeconds, SettleMaxRate);
            body.GlobalRotation = rotation;
            return;
        }

        body.LookAt(
            new Vector3(targetBody.GlobalPosition.X, body.GlobalPosition.Y, targetBody.GlobalPosition.Z),
            Vector3.Up);
    }

    /// <summary>Seconds since the stamp was last taken, clamped so a hitch or a first call is not a
    /// jump; stamps <paramref name="stamp"/> for next time. Read from the clock because the router
    /// calls these from its own tick and hands neither a delta.</summary>
    private static float Elapsed(ref ulong stamp)
    {
        ulong now = Time.GetTicksUsec();
        float dt = stamp == 0 ? 1f / 60f : Mathf.Clamp((now - stamp) / 1_000_000f, 0f, 0.1f);
        stamp = now;
        return dt;
    }

    private IEntity? Nearest()
    {
        List<IEntity> targets = Acquire();
        return targets.Count > 0 ? targets[0] : null;
    }

    private List<IEntity> Acquire()
    {
        var result = new List<IEntity>();
        if (_body == null)
        {
            return result;
        }

        PhysicsDirectSpaceState3D space = _body.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = AcquireRange },
            Transform = new Transform3D(Basis.Identity, _body.GlobalPosition),
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() },
        };

        var seen = new HashSet<IEntity>();
        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, maxResults: 32))
        {
            if (hit["collider"].AsGodotObject() is Node node
                && EntityNode.FindOwner(node) is { } entity
                && seen.Add(entity)
                && IsValid(entity))
            {
                result.Add(entity);
            }
        }

        // ⚠️ SCORED, NOT SORTED BY DISTANCE. The nearest valid enemy used to win outright, so
        // standing between two of them locked whichever was a hand's width closer and something
        // behind the player beat something they were looking straight at. LockOn.Score weights the
        // angle from where the player is actually looking three times as heavily as the distance.
        //
        // Scored ONCE per candidate, then sorted on the cached number: the old comparator re-ran the
        // scorer (and its line-of-sight ray) on every comparison, so the ranking could disagree with
        // itself mid-sort. Ties fall to the runtime id so equal scores never trade places.
        var scored = new List<(float Score, ulong Id, IEntity Entity)>(result.Count);
        foreach (IEntity candidate in result)
        {
            float score = ScoreOf(candidate);
            if (score >= 0f)
            {
                scored.Add((score, candidate.RuntimeId, candidate));
            }
        }

        scored.Sort((a, b) => a.Score != b.Score ? a.Score.CompareTo(b.Score) : a.Id.CompareTo(b.Id));
        result.Clear();
        foreach (var item in scored)
        {
            result.Add(item.Entity);
        }

        return result;
    }

    /// <summary>The lock-on score for a candidate — lower is better, negative is not a candidate.
    /// Off-screen and behind-cover candidates are rejected here rather than being sorted to the
    /// back, because a target the player cannot see is never what they meant.</summary>
    private float ScoreOf(IEntity entity)
    {
        if (_body == null)
        {
            return -1f;
        }

        Vector3 to = entity.Body.GlobalPosition - _body.GlobalPosition;
        float distance = to.Length();
        Vector3 forward = Camera != null
            ? -Camera.GlobalTransform.Basis.Z
            : -_body.GlobalTransform.Basis.Z;

        float angle = distance <= 0.001f ? 0f : forward.AngleTo(to / distance);
        bool assist = Assist;
        float score = LockOn.Score(
            distance, angle, AcquireRange, LockOn.AcquireAngle(MaxAcquireAngle, assist), HasLineOfSight(entity));

        // With the assist on, what is swinging at the player and what is nearly dead rank ahead of
        // what is merely nearest.
        bool threat = entity.GetComponent<CharacterActionComponent>() is
            { Phase: ActionPhase.Startup or ActionPhase.Active };
        float health = entity.GetComponent<StatsComponent>()?.GetNormalized(StatType.Health) ?? 1f;
        return LockOn.Prioritised(score, assist, threat, health);
    }

    /// <summary>Which way a candidate lies across the screen, radians, positive right of the view.</summary>
    private float BearingOf(IEntity entity)
    {
        if (_body == null)
        {
            return 0f;
        }

        Vector3 forward = Camera != null
            ? -Camera.GlobalTransform.Basis.Z
            : -_body.GlobalTransform.Basis.Z;
        return FramingMath.SignedBearing(forward, entity.Body.GlobalPosition - _body.GlobalPosition);
    }

    /// <summary>A candidate behind world geometry is not lockable. One ray, only for candidates that
    /// already passed the cheaper angle and range tests.
    ///
    /// ⚠️ <b>CameraBlocker, not World.</b> Actor bodies share the World layer, and this ray ends inside
    /// the candidate's own capsule, so a World mask always hit the target's own body first and the
    /// "blocked" verdict made every actor unlockable in a real session (found by rendering it). Static
    /// geometry and terrain carry CameraBlocker and people do not — the same split the camera spring
    /// uses (<see cref="CombatLayers.CameraBlocker"/>).</summary>
    private bool HasLineOfSight(IEntity entity)
    {
        if (_body == null)
        {
            return false;
        }

        Vector3 from = _body.GlobalPosition + (Vector3.Up * 1.4f);
        Vector3 to = entity.Body.GlobalPosition + (Vector3.Up * 1.0f);
        var query = PhysicsRayQueryParameters3D.Create(from, to, CombatLayers.CameraBlocker);
        query.Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() };
        return _body.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    /// <summary>Half-angle from the camera's forward a candidate may sit at, in radians. Roughly a
    /// 150-degree cone: generous enough to lock something at the edge of the screen, tight enough
    /// that nothing behind the player is ever a candidate.</summary>
    private const float MaxAcquireAngle = 1.3f;

    /// <summary>The player camera, so scoring can use where the player is LOOKING rather than where
    /// the body happens to face. Injected by the factory; null falls back to body facing.</summary>
    public Camera3D? Camera { get; set; }

    private float DistanceSq(IEntity entity) =>
        _body == null ? float.MaxValue : (entity.Body.GlobalPosition - _body.GlobalPosition).LengthSquared();

    private bool IsValid(IEntity? entity) =>
        IsAliveHostile(entity) && LockOn.InRange(DistanceSq(entity!), DropRange * DropRange);

    /// <summary>Exists, is not us, is on another team and is alive — everything but range.</summary>
    private bool IsAliveHostile(IEntity? entity)
    {
        return entity is Node node
            && GodotObject.IsInstanceValid(node)
            && !ReferenceEquals(entity, Entity)
            && entity.GetComponent<CombatComponent>() is { } combat && combat.Team != _team
            && entity.GetComponent<StatsComponent>() is { IsAlive: true };
    }
}
