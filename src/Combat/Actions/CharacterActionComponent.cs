using System.Collections.Generic;
using Embervale.Animation;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Movement;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat.Actions;

/// <summary>
/// The one executor of character actions — attacks and combos today, and blocks, parries, dodges,
/// casts and bow releases as the later stages migrate onto it. Every actor in the game carries
/// exactly one.
///
/// <para><b>What it replaced and why.</b> <c>MeleeWeaponComponent</c> ran a <c>double</c> stopwatch
/// through Windup→Active→Recovery while <c>CharacterAnimationComponent</c> separately fired a clip
/// on the <see cref="AttackPerformedEvent"/> it published. Nothing tied the two together: the clip
/// played at its authored speed whatever the weapon's timings said, so the hitbox opened on a clock
/// the visible swing had never heard of. The Iron King's 0.55 s heave and a dagger's 0.15 s flick
/// played the same <c>Sword_Slash</c> identically.</para>
///
/// <para><b>How this closes it.</b> One action, one duration, one progress number. The duration
/// comes from the clip (<see cref="ActionDefinitionResource.Duration"/> of 0) or the clip is warped
/// to fit the duration, and the progress is read back off
/// <see cref="CharacterAnimationComponent.ActionProgress"/> — the animation player's own playback
/// position. The hit window is a fraction of that progress. There is no second clock to disagree
/// with, and when a body has no clip at all the identical fraction runs on a fallback timer, so an
/// unanimated actor fights correctly rather than not at all.</para>
/// </summary>
[GlobalClass]
public partial class CharacterActionComponent : EntityComponent
{
    /// <summary>The weapon supplying damage identity and, when it authors no chain, the fallback
    /// action shape. Swapped live by <c>EquipmentComponent</c>.</summary>
    [Export] public WeaponResource? Weapon { get; set; }

    /// <summary>How long a press stays buffered while the actor is committed, in seconds. The buffer
    /// stretches to reach the cancel point when that is a little further off (<see cref="BufferLead"/>).</summary>
    [Export] public float BufferWindow { get; set; } = 0.18f;

    /// <summary>A press made more than this many seconds before the action can cancel is dropped
    /// rather than buffered (<see cref="AttackBuffer.Accepts"/>): mashing through a long wind-up does
    /// not queue a phantom swing.</summary>
    [Export] public float BufferLead { get; set; } = AttackBuffer.DefaultLead;

    /// <summary>How much of normal movement the actor keeps while holding a charge.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float ChargeMoveScale { get; set; } = 0.4f;

    /// <summary>Whether <see cref="ActionDefinitionResource.TurnDegreesPerSecond"/> is enforced on this
    /// actor's facing. On for every AI actor. The player's router switches it off while free-looking
    /// (body yaw is the camera there) and on while locked on, so a committed swing cannot be steered
    /// round a circling target.</summary>
    public bool EnforceTurnLimit { get; set; } = true;

    /// <summary>True while a hold-to-charge heavy is being wound up.</summary>
    public bool IsCharging { get; private set; }

    /// <summary>The current charge, 0..1 (0 when not charging).</summary>
    public float Charge => IsCharging
        ? ChargeRules.Fraction(_chargeSeconds, Weapon?.MaxChargeSeconds ?? 1f)
        : 0f;

    /// <summary>What the last melee swing's packet was stamped as. Read by probes and presenters.</summary>
    public HitKind LastSwingKind { get; private set; }

    /// <summary>The charge (0..1) the last swing carried.</summary>
    public float LastSwingCharge { get; private set; }

    /// <summary>The damage multiplier the last swing was released with (charge, direction, plunge
    /// height), before the weapon's own scale.</summary>
    public float LastSwingDamageMultiplier { get; private set; } = 1f;

    /// <summary>True while a plunge is diving: airborne with the blow waiting for the ground.</summary>
    public bool IsDiving => _diving;

    /// <summary>True in the committed tail of the running action (after its blow, before it can
    /// cancel): the window a heavy swing can be punished in. B's poise pipeline may read this.</summary>
    public bool InCommittedRecovery =>
        Current != null && ActionTimeline.InCommittedRecovery(_progress, Current.Windows);

    /// <summary>The default swing volume, injected by the actor's factory.</summary>
    public Hitbox? Hitbox { get; set; }

    /// <summary>Extra named volumes an <see cref="ActionDefinitionResource.HitboxName"/> can select —
    /// a dragon's jaws, wing and tail. Empty for everything humanoid-sized.</summary>
    public Dictionary<string, Hitbox> NamedHitboxes { get; } = new();

    /// <summary>Which link of the current chain is running. Presenters read it to alternate a swing
    /// direction; the finisher is simply the link whose definition carries the bigger damage scale.</summary>
    public int ComboIndex { get; private set; }

    /// <summary>The running action, or null at rest.</summary>
    public ActionDefinitionResource? Current { get; private set; }

    public ActionPhase Phase { get; private set; } = ActionPhase.Idle;

    /// <summary>True while no new action or dodge may start. A press here is buffered, not dropped.</summary>
    public bool IsCommitted =>
        Current != null && !ActionTimeline.CanCancel(_progress, Current.Windows);

    /// <summary>How much of normal movement the actor keeps this frame — 1 at rest. Read by the
    /// player's input router and by AI locomotion, so a committed swing stops being a float.</summary>
    public float MoveScale => IsCharging ? ChargeMoveScale : Current?.MoveScale ?? 1f;

    /// <summary>Degrees per second the actor may still turn while acting, or a negative number for
    /// "unrestricted". 0 locks facing at the commit, which is what stops a swing tracking a
    /// circling target through its whole animation.</summary>
    public float TurnDegreesPerSecond => Current?.TurnDegreesPerSecond ?? -1f;

    /// <summary>Seconds an AI should wait before choosing its next action. Counts down after the
    /// action ends — the pause the old system had none of, which is why enemies attacked at maximum
    /// weapon cadence with no break between combos.</summary>
    public float AiRecoveryRemaining { get; private set; }

    private StatsComponent? _stats;
    private CombatComponent? _combat;
    private CharacterAnimationComponent? _animation;
    private MountComponent? _mount;
    /// <summary>What a warping action should close on — the locked target for the player, the AI's
    /// quarry for everything else. Null disables warping entirely, which is the common case.</summary>
    public Node3D? WarpTarget { get; set; }

    /// <summary>How far the actor stops short of its target when warping. Roughly a body plus a
    /// weapon: close enough to land the blow, far enough not to stand inside them.</summary>
    [Export] public float WarpReach { get; set; } = 1.4f;

    private Hitbox? _openHitbox;
    private float _warpDegreesLeft;
    private bool _released;
    private float _warpDistanceLeft;
    private float _progress;
    private double _elapsed;
    private double _duration;
    private double _buffer;
    private AttackDirection _bufferDirection;
    private bool _clipDriven;

    private float _chargeSeconds;
    private WeaponResource? _chargeWeapon;
    private SwingContext _swing = SwingContext.Neutral;
    private Vector3 _advanceLeft;
    private float _lastYaw;
    private bool _yawTracked;
    private int _lastAiPick = -1;

    private bool _diving;
    private float _diveSeconds;
    private float _plungeHeight;

    /// <summary>Longest a dive may wait for ground before giving up (a flier, a bottomless drop).</summary>
    private const float MaxDiveSeconds = 4f;

    /// <summary>The per-swing modifiers a directional press, a charge release or a plunge stamp on top
    /// of the definition. Never persisted, never authored: it exists for one swing.</summary>
    private readonly record struct SwingContext(
        HitKind? Kind,
        float Charge,
        float DamageMul,
        float PoiseMul,
        bool Hyperarmor,
        Vector3 Advance,
        float DurationScale,
        float SpeedScale,
        float Cost,
        bool ForceCost)
    {
        public static readonly SwingContext Neutral =
            new(null, 0f, 1f, 1f, false, Vector3.Zero, 1f, 1f, -1f, false);

        public static SwingContext Directed(AttackDirection direction)
        {
            DirectionModifier m = AttackDirections.Modifier(direction);
            return Neutral with
            {
                DamageMul = m.DamageScale,
                PoiseMul = m.PoiseScale,
                DurationScale = m.DurationScale,
                Advance = m.Advance,
            };
        }
    }

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
        _animation = Entity.GetComponent<CharacterAnimationComponent>();
        _mount = Entity.GetComponent<MountComponent>();

        // ⚠️ A WEAPON WITH NO HITBOX SWINGS FOREVER AND HITS NOTHING, IN SILENCE. The whole action
        // plays, the stamina is spent, a window that does not exist opens and closes, and no damage
        // is dealt for the life of the session. Said once, at build time, where the authoring that
        // caused it is still on screen.
        // ⚠️ A RANGED WEAPON HAS NO SWING VOLUME AND MUST NOT BE SCOLDED FOR IT. Without the
        // IsRanged arm, every archer in the game logs a hard error on spawn saying its attacks will
        // deal no damage — which is both false and exactly the sort of noise that trains people to
        // ignore the message when a real melee weapon is misconfigured.
        if (Weapon is { IsRanged: false } && Hitbox == null)
        {
            Log.Error($"{Entity.DisplayName}: {nameof(CharacterActionComponent)} has a weapon " +
                      $"('{Weapon.ResourceName}') but no Hitbox assigned. Every swing will deal no " +
                      "damage. Assign the actor's Hitbox node to this component.");
        }

        // The player's ground-pound volume, when its factory built one. Registered by name like the
        // dragon's arcs, so the plunge definition selects it and any other actor simply lacks it.
        if (Entity.Body.GetNodeOrNull<Hitbox>(PlungeHitboxNode) is { } plunge)
        {
            NamedHitboxes["PlungeArc"] = plunge;
        }

        // Idle is where every actor spends nearly all of its life and there is nothing to advance
        // there. A request re-arms the callback; _PhysicsProcess parks it again once the action and
        // its buffer are done.
        SetPhysicsProcess(false);
    }

    /// <summary>The node name a factory gives the player's plunge volume.</summary>
    public const string PlungeHitboxNode = "PlungeHitbox";

    /// <summary>Requests the actor's next attack — a fresh swing, or the next link if pressed inside
    /// the combo window. A press during commitment is buffered (if it is close enough to the cancel
    /// point to be worth keeping) and auto-released the instant the action becomes cancellable.
    /// Returns true if something started now.</summary>
    public bool TryAttack() => TryAttackDirected(AttackDirection.Neutral);

    /// <summary>As <see cref="TryAttack"/>, with the direction the attacker was moving in (chosen once
    /// at the commit; see <see cref="AttackDirections"/>). The direction of a buffered press is kept
    /// with it.</summary>
    public bool TryAttackDirected(AttackDirection direction)
    {
        SetPhysicsProcess(true);

        // A charge is its own commitment: a second press does not stack a swing on top of it.
        if (IsCharging || _diving)
        {
            return false;
        }

        if (IsCommitted)
        {
            BufferPress(direction);
            return false;
        }

        return StartNext(SwingContext.Directed(direction));
    }

    private void BufferPress(AttackDirection direction)
    {
        if (Current == null)
        {
            return;
        }

        float until = ActionTimeline.SecondsUntilCancel(_progress, Current.Windows, _duration);
        if (!AttackBuffer.Accepts(until, BufferLead))
        {
            return;
        }

        _buffer = AttackBuffer.Lifetime(BufferWindow, until);
        _bufferDirection = direction;
    }

    /// <summary>
    /// Starts winding up a heavy attack (the attack button held past the tap threshold). Returns false
    /// when the weapon has no melee heavy, the actor is acting or staggered, or cannot afford the
    /// swing. While charging the actor moves at <see cref="ChargeMoveScale"/>, drains stamina, and
    /// drops the charge (free, but the stamina is gone) on a stagger, a block, a dodge or a weapon swap.
    /// </summary>
    public bool BeginCharge()
    {
        if (IsCharging)
        {
            return true;
        }

        if (Weapon?.HeavyAttack() is not { } heavy || Current != null || _diving ||
            _combat is { IsStaggered: true } || _stats is { IsAlive: false })
        {
            return false;
        }

        if (_stats != null && _stats.GetCurrent(StatType.Stamina) < heavy.StaminaCost)
        {
            return false;
        }

        IsCharging = true;
        _chargeSeconds = 0f;
        _chargeWeapon = Weapon;
        SetPhysicsProcess(true);
        return true;
    }

    /// <summary>Swings the charged heavy. What it is depends on how long it was held: a plain
    /// <see cref="HitKind.Heavy"/> early, a <see cref="HitKind.Charged"/> blow from
    /// <see cref="ChargeRules.ChargedFrom"/>, hyperarmoured near full. Publishes
    /// <see cref="ChargeReleasedEvent"/>. Returns true if the swing started.</summary>
    public bool ReleaseCharge()
    {
        if (!IsCharging)
        {
            return false;
        }

        float charge = Charge;
        IsCharging = false;
        _chargeSeconds = 0f;

        if (Weapon?.HeavyAttack() is not { } heavy)
        {
            return false;
        }

        EventBus.Instance?.Publish(new ChargeReleasedEvent(Entity!, charge));

        float bonus = Weapon.ChargeDamageBonus;
        var swing = SwingContext.Neutral with
        {
            Kind = ChargeRules.KindOf(charge),
            Charge = charge,
            DamageMul = ChargeRules.DamageMultiplier(charge, bonus),
            PoiseMul = ChargeRules.PoiseMultiplier(charge, bonus),
            Hyperarmor = ChargeRules.GrantsHyperarmor(charge),
            SpeedScale = ChargeRules.ReleaseSpeed(charge),
            Cost = ChargeRules.ReleaseCost(heavy.StaminaCost, charge),

            // The stamina drained while holding was the price of the wind-up: a charge that ran the
            // bar down still swings, for whatever is left.
            ForceCost = true,
        };

        return Begin(heavy, 0, swing);
    }

    /// <summary>Drops a charge in progress without swinging.</summary>
    public void CancelCharge()
    {
        IsCharging = false;
        _chargeSeconds = 0f;
    }

    /// <summary>
    /// Strikes downward out of a jump or fall. The actor dives at <see cref="PlungeRules.DiveSpeed"/>,
    /// committed and unsteerable, and the blow lands when it reaches the ground, carrying the drop
    /// (<see cref="PlungeRules.HeightScale"/>). Refused on the ground, when mounted, from a drop under
    /// <see cref="PlungeRules.MinHeight"/>, or without the stamina. Returns true if the dive began.
    /// </summary>
    public bool TryPlunge()
    {
        if (Weapon?.PlungeAttack() is not { } plunge || Entity?.Body is not CharacterBody3D body ||
            IsCharging || _diving || IsCommitted || _combat is { IsStaggered: true })
        {
            return false;
        }

        bool grounded = body.IsOnFloor();
        float height = grounded ? 0f : HeightAboveGround(body);
        if (!PlungeRules.CanStart(height, grounded, _mount is { IsMounted: true }) ||
            (_stats != null && _stats.GetCurrent(StatType.Stamina) < plunge.StaminaCost))
        {
            return false;
        }

        CloseHitbox();
        _stats?.ModifyCurrent(StatType.Stamina, -plunge.StaminaCost);

        SetPhysicsProcess(true);
        Current = plunge;
        ComboIndex = 0;
        _progress = 0f;
        _elapsed = 0d;
        _released = false;
        _clipDriven = false;
        _duration = plunge.FallbackDuration;
        _buffer = 0d;
        _diving = true;
        _diveSeconds = 0f;
        _plungeHeight = height;
        _advanceLeft = Vector3.Zero;
        _swing = SwingContext.Neutral with
        {
            Kind = HitKind.Plunge,
            DamageMul = PlungeRules.HeightScale(height),
        };
        Phase = ActionPhase.Startup;
        SetWindup(true);
        EventBus.Instance?.Publish(
            new AttackPerformedEvent(Entity, 0, height / PlungeRules.DiveSpeed));
        return true;
    }

    /// <summary>The attack an attack press made straight out of a dodge becomes: a quick lunging cut
    /// (<see cref="WeaponResource.RollAttack"/>). Falls back to an ordinary swing for a weapon without
    /// one.</summary>
    public bool TryRollAttack()
    {
        if (Weapon?.RollAttack() is not { } cut)
        {
            return TryAttack();
        }

        SetPhysicsProcess(true);
        if (IsCharging || _diving || IsCommitted)
        {
            return false;
        }

        return Begin(cut, 0, SwingContext.Neutral);
    }

    /// <summary>
    /// The AI's entry point: attack a target at <paramref name="distance"/> metres, choosing which
    /// of the weapon's actions actually reaches.
    ///
    /// ⚠️ <b>This is what an AI is allowed to know.</b> It knows it wants to hit something and how
    /// far away that is. It does not know what a wind-up is, when a hitbox opens, or how long any of
    /// it takes — those belong to the action and its animation. Before this, every AI simply called
    /// TryAttack every physics frame and the weapon's own commitment was the only rate limit.
    /// </summary>
    public bool TryAttackAt(float distance)
    {
        if (AiRecoveryRemaining > 0f || IsCommitted)
        {
            return false;
        }

        ActionDefinitionResource[] chain = Chain();
        var candidates = new ActionSelection.Candidate[chain.Length];
        for (int i = 0; i < chain.Length; i++)
        {
            candidates[i] = ActionSelection.Candidate.Of(chain[i]);
        }

        // The last pick carries less weight, so an enemy varies its blows instead of repeating the
        // one the player has just learned to answer.
        int pick = ActionSelection.Choose(candidates, distance, GD.Randf(), _lastAiPick, 0.45f);
        if (pick < 0)
        {
            return false;
        }

        SetPhysicsProcess(true);
        bool started = Begin(chain[pick], pick, SwingContext.Neutral);
        if (started)
        {
            _lastAiPick = pick;
        }

        return started;
    }

    /// <summary>Starts the named action from this weapon's chain, if it has one. How a directional
    /// attacker (a dragon choosing bite, wing or tail) asks for a specific blow.</summary>
    public bool TryStartById(string actionId)
    {
        foreach (ActionDefinitionResource action in Chain())
        {
            if (action.Id == actionId)
            {
                return TryStart(action);
            }
        }

        return false;
    }

    /// <summary>Starts a specific action, subject to the same commitment, stagger and stamina rules.
    /// This is the entry point AI, spells, dodges and bows use.</summary>
    public bool TryStart(ActionDefinitionResource? definition)
    {
        if (definition == null || IsCommitted)
        {
            return false;
        }

        SetPhysicsProcess(true);
        return Begin(definition, 0, SwingContext.Neutral);
    }

    /// <summary>Drops the running action and tells anything presenting it to stop.</summary>
    public void Cancel()
    {
        if (Current == null)
        {
            return;
        }

        CloseHitbox();
        Current = null;
        Phase = ActionPhase.Idle;
        ComboIndex = 0;
        _progress = 0f;
        _buffer = 0d;   // a queued press must not fire the instant a stagger lifts
        _diving = false;
        _advanceLeft = Vector3.Zero;
        _yawTracked = false;
        _swing = SwingContext.Neutral;
        SetWindup(false);
        _animation?.StopAction();
        EventBus.Instance?.Publish(new AttackInterruptedEvent(Entity!));
    }

    private bool StartNext(SwingContext swing)
    {
        ActionDefinitionResource[] chain = Chain();
        if (chain.Length == 0)
        {
            return false;
        }

        // Continuing from inside the combo window advances the chain; anything else restarts it.
        int next = Current != null && ActionTimeline.InComboWindow(_progress, Current.Windows)
            ? (ComboIndex + 1) % chain.Length
            : 0;

        return Begin(chain[next], next, swing);
    }

    private bool Begin(ActionDefinitionResource definition, int comboIndex, SwingContext swing)
    {
        if (_combat is { IsStaggered: true })
        {
            return false;
        }

        float cost = swing.Cost >= 0f ? swing.Cost : definition.StaminaCost;
        if (_stats != null && !swing.ForceCost && _stats.GetCurrent(StatType.Stamina) < cost)
        {
            return false;
        }

        CloseHitbox();
        _stats?.ModifyCurrent(StatType.Stamina, -cost);

        Current = definition;
        ComboIndex = comboIndex;
        _progress = 0f;
        _elapsed = 0d;
        _swing = swing;
        _diving = false;

        // Ask the animation for the clock. A positive authored Duration warps the clip to fit it; 0
        // lets the clip's own length decide; -1 back means this body has no clip for the slot and
        // the fallback timer runs the identical fractions. A direction slows or quickens the whole
        // swing, a charge release quickens it: both scale the clock, so the windows stay honest.
        float speed = ActionSpeed() * swing.SpeedScale;
        float desired = definition.Duration > 0f
            ? definition.Duration / speed * swing.DurationScale
            : 0f;
        float actual = _animation?.StartAction(SlotFor(definition, swing), desired) ?? -1f;

        _clipDriven = actual > 0f;
        _duration = _clipDriven ? actual : definition.FallbackDuration / speed * swing.DurationScale;

        Phase = ActionPhase.Startup;
        SetWindup(true);
        _warpDegreesLeft = definition.MaxWarpDegrees;
        _released = false;
        _warpDistanceLeft = definition.MaxWarpDistance;
        BeginAdvance(definition, swing);
        if (Entity?.Body is Node3D body)
        {
            _lastYaw = body.Rotation.Y;
            _yawTracked = true;
        }

        // The telegraph is told the *effective* startup, not an authored constant: a phase buff or a
        // slow debuff moves the danger window, and a cue that ignores that is worse than none.
        EventBus.Instance?.Publish(
            new AttackPerformedEvent(Entity!, ComboIndex, (float)(_duration * definition.ActiveFrom)));
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        Tick(delta);

        // Back to rest: nothing to advance until the next request. Decided in one place rather than
        // at each of Tick's exits.
        if (Current == null && _buffer <= 0d && AiRecoveryRemaining <= 0f && !IsCharging)
        {
            SetPhysicsProcess(false);
        }
    }

    private void Tick(double delta)
    {
        if (AiRecoveryRemaining > 0f)
        {
            AiRecoveryRemaining -= (float)delta;
        }

        if (IsCharging)
        {
            TickCharge(delta);
        }

        if (_buffer > 0d)
        {
            _buffer -= delta;

            // Intent does not survive a stagger: a press queued before the hit must not fire the
            // moment the stagger lifts, long after the player stopped meaning it.
            if (_combat is { IsStaggered: true })
            {
                _buffer = 0d;
            }
        }

        if (AttackBuffer.ShouldRelease(_buffer, IsCommitted) &&
            StartNext(SwingContext.Directed(_bufferDirection)))
        {
            _buffer = 0d;
            return;
        }

        if (Current == null)
        {
            return;
        }

        ActionWindows windows = Current.Windows;

        if (_diving)
        {
            TickDive(delta);
            if (_diving)
            {
                return;
            }
        }

        // Only the startup is interruptible, and only for an action that says so. Once the blow is
        // live it is committed — which is what keeps the punish window something to aim for rather
        // than a race. Hyperarmor is simply Interruptible = false; a fully charged swing earns it.
        if (_combat is { IsStaggered: true } &&
            ActionTimeline.StaggerCancels(
                _progress, windows, Current.Interruptible && !_swing.Hyperarmor))
        {
            Cancel();
            return;
        }

        LimitTurn(Current, delta);

        _elapsed += delta;

        // The animation is the clock whenever it is holding one. ActionProgress goes negative the
        // moment the player moves off the clip — a death, a mount, a blend stealing it — and the
        // elapsed timer takes over mid-action rather than the action hanging forever.
        float animated = _clipDriven ? _animation?.ActionProgress ?? -1f : -1f;
        _progress = animated >= 0f ? animated : ActionTimeline.ProgressOf(_elapsed, _duration);

        Phase = ActionTimeline.PhaseAt(_progress, windows);

        // Startup is a punish window for poise, and so is the committed tail of an action that says
        // so (a heavy's recovery): the big swing is paid for after it lands.
        SetWindup(Phase == ActionPhase.Startup ||
                  (Current.RecoveryVulnerable && ActionTimeline.InCommittedRecovery(_progress, windows)));

        ApplyMotion(Current, delta);
        if (Entity?.Body is Node3D moved)
        {
            _lastYaw = moved.Rotation.Y;
        }

        bool shouldBeOpen = ActionTimeline.IsActive(_progress, windows);
        if (shouldBeOpen && !_released)
        {
            _released = true;

            // The rising edge of the active window is "now", for everything. A melee action opens
            // its volume; a cast and a bow have no volume and listen for this instead. One instant,
            // one event, so nothing needs a second clock to decide when a thing happens.
            OpenHitbox(Current);
            EventBus.Instance?.Publish(
                new ActionReleasedEvent(Entity!, Current.Id, Current.Kind));
        }
        else if (!shouldBeOpen && _openHitbox != null)
        {
            CloseHitbox();
        }

        if (_progress < 1f)
        {
            return;
        }

        AiRecoveryRemaining = Current.AiRecoverySeconds;
        Current = null;
        Phase = ActionPhase.Idle;
        ComboIndex = 0;
        _progress = 0f;
        _yawTracked = false;
        _advanceLeft = Vector3.Zero;
        _swing = SwingContext.Neutral;
        SetWindup(false);
        _animation?.StopAction();
    }

    /// <summary>Winds a charge: drains stamina, and drops it on anything that breaks a brace.</summary>
    private void TickCharge(double delta)
    {
        if (Weapon != _chargeWeapon || _combat is { IsStaggered: true, } || _stats is { IsAlive: false } ||
            _combat is { IsBlocking: true } || Current != null)
        {
            CancelCharge();
            return;
        }

        _chargeSeconds += (float)delta;
        if (_stats != null && Weapon != null)
        {
            _stats.ModifyCurrent(StatType.Stamina, -Weapon.ChargeStaminaPerSecond * (float)delta);

            // Wound to the last drop: it swings now, at the charge it reached, rather than the
            // player holding a bar that cannot pay for anything.
            if (_stats.GetCurrent(StatType.Stamina) <= 0f)
            {
                ReleaseCharge();
            }
        }
    }

    /// <summary>The dive of a plunge: falling fast and unsteered until the ground, then the blow's
    /// own clock starts. Cancelled by a stagger, or if the ground never comes.</summary>
    private void TickDive(double delta)
    {
        if (Current == null || Entity?.Body is not CharacterBody3D body ||
            _combat is { IsStaggered: true } || _diveSeconds > MaxDiveSeconds)
        {
            Cancel();
            return;
        }

        _diveSeconds += (float)delta;
        if (!body.IsOnFloor())
        {
            Vector3 v = body.Velocity;
            body.Velocity = new Vector3(v.X * 0.5f, Mathf.Min(v.Y, -PlungeRules.DiveSpeed), v.Z * 0.5f);
            return;
        }

        // Landed: the blow's clock starts here, so the shockwave is timed off the impact.
        ActionDefinitionResource plunge = Current;
        _diving = false;
        float speed = ActionSpeed();
        float actual = _animation?.StartAction(plunge.AnimationSlot, plunge.Duration / speed) ?? -1f;
        _clipDriven = actual > 0f;
        _duration = _clipDriven ? actual : plunge.FallbackDuration / speed;
        _elapsed = 0d;
        _progress = 0f;
        _yawTracked = true;
        _lastYaw = body.Rotation.Y;
    }

    /// <summary>The plunge's drop, and the slot a swing plays: a charged heavy uses the overhead clip.</summary>
    private static string SlotFor(ActionDefinitionResource definition, SwingContext swing) =>
        swing.Charge >= ChargeRules.OverheadFrom && definition.AnimationSlot == "heavy"
            ? "heavy_overhead"
            : definition.AnimationSlot;

    private float HeightAboveGround(CharacterBody3D body)
    {
        const float probe = 30f;
        Vector3 from = body.GlobalPosition + (Vector3.Up * 0.1f);
        var query = PhysicsRayQueryParameters3D.Create(
            from, from + (Vector3.Down * probe), CombatLayers.WorldStatic);
        query.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };

        Godot.Collections.Dictionary hit = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? probe : from.Y - hit["position"].AsVector3().Y;
    }

    /// <summary>Spends the swing's untargeted step (a directional lunge or back-cut, a roll cut's
    /// push) across the startup. With a warp target the forward part is dropped: the warp already
    /// closes that gap and both would overshoot.</summary>
    private void BeginAdvance(ActionDefinitionResource definition, SwingContext swing)
    {
        _advanceLeft = Vector3.Zero;
        if (Entity?.Body is not Node3D body)
        {
            return;
        }

        Vector3 local = swing.Advance + (Vector3.Forward * definition.AdvanceMetres);
        bool warping = definition.RootMotion == RootMotionMode.WarpToTarget &&
                       WarpTarget is { } t && GodotObject.IsInstanceValid(t);
        if (warping && local.Z < 0f)
        {
            local.Z = 0f;
        }

        _advanceLeft = body.GlobalBasis * local;
    }

    /// <summary>A committed swing may only be steered as far as its action allows (see
    /// <see cref="EnforceTurnLimit"/>): whatever turned the body since the last tick is capped.</summary>
    private void LimitTurn(ActionDefinitionResource definition, double delta)
    {
        if (!EnforceTurnLimit || !_yawTracked || definition.TurnDegreesPerSecond < 0f ||
            Entity?.Body is not Node3D body)
        {
            return;
        }

        float yaw = body.Rotation.Y;
        float allowed = MotionWarp.LimitedYaw(_lastYaw, yaw, definition.TurnDegreesPerSecond, delta);
        if (!Mathf.IsEqualApprox(allowed, yaw))
        {
            body.Rotation = new Vector3(body.Rotation.X, allowed, body.Rotation.Z);
        }
    }

    /// <summary>
    /// Closes the last of the gap to the target during an action's startup.
    ///
    /// ⚠️ <b>The translation is SWEPT, not assigned.</b> <c>MoveAndCollide</c> stops the actor at the
    /// first thing in the way, which is what makes "an attack cannot warp through a wall" true by
    /// construction rather than by a check somebody has to remember. Assigning the position directly
    /// would put a lunging enemy inside the geometry the player was hiding behind.
    /// </summary>
    private void ApplyMotion(ActionDefinitionResource definition, double delta)
    {
        if (Entity?.Body is not CharacterBody3D body)
        {
            return;
        }

        float fraction = MotionWarp.Fraction(_progress, definition.ActiveFrom, delta, _duration);
        if (fraction <= 0f)
        {
            return;
        }

        // The untargeted step (direction, roll cut) is swept the same way as the warp.
        Vector3 advance = MotionWarp.AdvanceStep(_advanceLeft, fraction);
        if (advance.LengthSquared() > 0f)
        {
            _advanceLeft -= advance;
            body.MoveAndCollide(advance);
        }

        if (definition.RootMotion != RootMotionMode.WarpToTarget ||
            WarpTarget is not { } target || !GodotObject.IsInstanceValid(target))
        {
            return;
        }

        Vector3 here = body.GlobalPosition;
        Vector3 there = target.GlobalPosition;

        // ⚠️ A SWING DOES NOT CHASE SOMEONE BEHIND IT. The warp may turn the actor only so far
        // (MaxWarpDegrees), so a target outside twice that is not one it can be aimed at; lunging
        // anyway would read as homing. It simply swings where it is facing.
        if (!MotionWarp.Reachable(body.Rotation.Y, here, there, definition.MaxWarpDegrees))
        {
            return;
        }

        // ⚠️ THE BUDGET IS PER ACTION, NOT PER FRAME, and it is spent as it is used. Passing the
        // authored maximum every frame caps each STEP rather than the journey: a long wind-up then
        // closes 3.6 m on a 1.6 m allowance, one frame at a time, and the "lunge" is a chase. Found
        // by the no-wall control in grounding_probe.gd, which is the only thing that measures the
        // total rather than the outcome.
        Vector3 step = MotionWarp.Step(here, there, WarpReach, _warpDistanceLeft, fraction);
        if (step.LengthSquared() > 0f)
        {
            _warpDistanceLeft -= step.Length();
            body.MoveAndCollide(step);
        }

        float yaw = MotionWarp.YawStep(body.Rotation.Y, here, there, _warpDegreesLeft, fraction);
        if (yaw != 0f)
        {
            body.Rotation = new Vector3(body.Rotation.X, body.Rotation.Y + yaw, body.Rotation.Z);
            _warpDegreesLeft -= Mathf.RadToDeg(Mathf.Abs(yaw));
        }
    }

    /// <summary>Mirrors the startup window onto the combat component, which is where incoming poise
    /// damage is resolved and therefore where a phase's wind-up vulnerability has to be applied.</summary>
    private void SetWindup(bool inWindup)
    {
        if (_combat != null)
        {
            _combat.InWindup = inWindup;
        }
    }

    /// <summary>Where a ranged shot is aimed. Set by the player's aim controller or by AI; falls
    /// back to the actor's facing.</summary>
    public Vector3? AimPoint { get; set; }

    /// <summary>Sets <see cref="AimPoint"/> from GDScript. ⚠️ The property itself is
    /// <c>Vector3?</c>, which does not marshal — assigning it from a <c>.gd</c> probe fails with
    /// "invalid assignment" and the script aborts mid-function, which reads as the test passing.
    /// That happened; this exists so it cannot happen again.</summary>
    public void AimAt(Vector3 point) => AimPoint = point;

    /// <summary>Owns the pooled arrows and the bow's release rules (see <see cref="RangedAttack"/>).</summary>
    private readonly RangedAttack _ranged = new();

    protected override void OnTeardown() => _ranged.Clear();

    private void Shoot(ActionDefinitionResource definition)
    {
        if (Weapon is { IsRanged: true } bow && Entity?.Body is Node3D body)
        {
            _ranged.Fire(bow, definition, Entity, body, _stats, _combat, _mount, AimPoint);
        }
    }

    private void OpenHitbox(ActionDefinitionResource definition)
    {
        // A bow has no swing volume; its release sends an arrow instead.
        if (Weapon is { IsRanged: true })
        {
            Shoot(definition);
            return;
        }

        Hitbox? box = definition.HitboxName.Length > 0 &&
                      NamedHitboxes.TryGetValue(definition.HitboxName, out Hitbox? named)
            ? named
            : Hitbox;

        if (box == null || Weapon == null)
        {
            return;
        }

        // 39B: a blow struck from a galloping horse carries the horse. Applied to the BASE damage,
        // before CombatMath rolls, so it scales with the action and the crit the same way every
        // other weapon factor does rather than becoming a fourth thing stacked on the outcome.
        float mounted = MountedCombat.DamageScale(
            _mount is { IsMounted: true }, _mount is { IsGalloping: true });
        float baseDamage = Weapon.BaseDamage * definition.DamageScale * _swing.DamageMul * mounted;

        // The attacker stamps what kind of blow this is; the defender side and presentation read it.
        // An authored heavy (a boss's slam) is a Heavy hit without anyone opting in.
        HitKind kind = _swing.Kind ??
                       (definition.Kind == ActionKind.HeavyAttack ? HitKind.Heavy : HitKind.Normal);
        LastSwingKind = kind;
        LastSwingCharge = _swing.Charge;
        LastSwingDamageMultiplier = _swing.DamageMul;

        (float amount, bool isCrit) = CombatMath.RollAttack(baseDamage, _stats);
        box.Activate(new DamagePacket(
            amount, Weapon.DamageType, Entity, isCrit,
            Weapon.PoiseDamage * definition.PoiseScale * _swing.PoiseMul, kind, _swing.Charge));
        _openHitbox = box;
    }

    private void CloseHitbox()
    {
        _openHitbox?.Deactivate();
        _openHitbox = null;
    }

    private ActionDefinitionResource[] Chain() =>
        Weapon == null ? System.Array.Empty<ActionDefinitionResource>() : Weapon.AttackChain();

    private float ActionSpeed()
    {
        float weaponSpeed = Weapon?.AttackSpeed ?? 1f;
        float statSpeed = _stats?.GetValue(StatType.AttackSpeed) ?? 1f;
        return Mathf.Max(0.1f, weaponSpeed * statSpeed);
    }
}
