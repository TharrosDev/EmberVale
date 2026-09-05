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

    /// <summary>How long a press stays buffered while the actor is committed, in seconds.</summary>
    [Export] public float BufferWindow { get; set; } = 0.18f;

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
    public float MoveScale => Current?.MoveScale ?? 1f;

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
    private bool _clipDriven;

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

        // Idle is where every actor spends nearly all of its life and there is nothing to advance
        // there. A request re-arms the callback; _PhysicsProcess parks it again once the action and
        // its buffer are done.
        SetPhysicsProcess(false);
    }

    /// <summary>Requests the actor's next attack — a fresh swing, or the next link if pressed inside
    /// the combo window. A press during commitment is buffered and auto-released the instant the
    /// action becomes cancellable. Returns true if something started now.</summary>
    public bool TryAttack()
    {
        SetPhysicsProcess(true);

        if (IsCommitted)
        {
            _buffer = BufferWindow;
            return false;
        }

        return StartNext();
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

        int pick = ActionSelection.Choose(candidates, distance, GD.Randf());
        if (pick < 0)
        {
            return false;
        }

        SetPhysicsProcess(true);
        return Begin(chain[pick], pick);
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
        return Begin(definition, comboIndex: 0);
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
        SetWindup(false);
        _animation?.StopAction();
        EventBus.Instance?.Publish(new AttackInterruptedEvent(Entity!));
    }

    private bool StartNext()
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

        return Begin(chain[next], next);
    }

    private bool Begin(ActionDefinitionResource definition, int comboIndex)
    {
        if (_combat is { IsStaggered: true })
        {
            return false;
        }

        if (_stats != null && _stats.GetCurrent(StatType.Stamina) < definition.StaminaCost)
        {
            return false;
        }

        CloseHitbox();
        _stats?.ModifyCurrent(StatType.Stamina, -definition.StaminaCost);

        Current = definition;
        ComboIndex = comboIndex;
        _progress = 0f;
        _elapsed = 0d;

        // Ask the animation for the clock. A positive authored Duration warps the clip to fit it; 0
        // lets the clip's own length decide; -1 back means this body has no clip for the slot and
        // the fallback timer runs the identical fractions.
        float speed = ActionSpeed();
        float desired = definition.Duration > 0f ? definition.Duration / speed : 0f;
        float actual = _animation?.StartAction(definition.AnimationSlot, desired) ?? -1f;

        _clipDriven = actual > 0f;
        _duration = _clipDriven ? actual : definition.FallbackDuration / speed;

        Phase = ActionPhase.Startup;
        SetWindup(true);
        _warpDegreesLeft = definition.MaxWarpDegrees;
        _released = false;
        _warpDistanceLeft = definition.MaxWarpDistance;

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
        if (Current == null && _buffer <= 0d && AiRecoveryRemaining <= 0f)
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

        if (_buffer > 0d)
        {
            _buffer -= delta;
        }

        if (AttackBuffer.ShouldRelease(_buffer, IsCommitted) && StartNext())
        {
            _buffer = 0d;
            return;
        }

        if (Current == null)
        {
            return;
        }

        ActionWindows windows = Current.Windows;

        // Only the startup is interruptible, and only for an action that says so. Once the blow is
        // live it is committed — which is what keeps the punish window something to aim for rather
        // than a race. Hyperarmor is simply Interruptible = false.
        if (_combat is { IsStaggered: true } &&
            ActionTimeline.StaggerCancels(_progress, windows, Current.Interruptible))
        {
            Cancel();
            return;
        }

        _elapsed += delta;

        // The animation is the clock whenever it is holding one. ActionProgress goes negative the
        // moment the player moves off the clip — a death, a mount, a blend stealing it — and the
        // elapsed timer takes over mid-action rather than the action hanging forever.
        float animated = _clipDriven ? _animation?.ActionProgress ?? -1f : -1f;
        _progress = animated >= 0f ? animated : ActionTimeline.ProgressOf(_elapsed, _duration);

        Phase = ActionTimeline.PhaseAt(_progress, windows);
        SetWindup(Phase == ActionPhase.Startup);

        ApplyWarp(Current, delta);

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
        SetWindup(false);
        _animation?.StopAction();
    }

    /// <summary>
    /// Closes the last of the gap to the target during an action's startup.
    ///
    /// ⚠️ <b>The translation is SWEPT, not assigned.</b> <c>MoveAndCollide</c> stops the actor at the
    /// first thing in the way, which is what makes "an attack cannot warp through a wall" true by
    /// construction rather than by a check somebody has to remember. Assigning the position directly
    /// would put a lunging enemy inside the geometry the player was hiding behind.
    /// </summary>
    private void ApplyWarp(ActionDefinitionResource definition, double delta)
    {
        if (definition.RootMotion != RootMotionMode.WarpToTarget ||
            WarpTarget is not { } target || !GodotObject.IsInstanceValid(target) ||
            Entity?.Body is not CharacterBody3D body)
        {
            return;
        }

        float fraction = MotionWarp.Fraction(_progress, definition.ActiveFrom, delta, _duration);
        if (fraction <= 0f)
        {
            return;
        }

        Vector3 here = body.GlobalPosition;
        Vector3 there = target.GlobalPosition;

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

    /// <summary>Pooled arrows, built on first use so a melee actor never pays for one.</summary>
    private Core.Pooling.NodePool<Arrow>? _arrows;

    /// <summary>
    /// Sends an arrow instead of opening a volume.
    ///
    /// ⚠️ It spawns on the action's release frame like everything else — the string is drawn, and the
    /// arrow leaves when the animation shows it leaving. A bow that fired on key-down would put the
    /// arrow across the room before the draw finished, which is the melee desync all over again in a
    /// system that never had it.
    /// </summary>
    private void Shoot(ActionDefinitionResource definition)
    {
        if (Weapon is not { IsRanged: true } bow || Entity?.Body is not Node3D body)
        {
            return;
        }

        _arrows ??= new Core.Pooling.NodePool<Arrow>(() => new Arrow { Released = a => _arrows?.Return(a) }, prewarm: 2);

        float mounted = MountedCombat.DamageScale(
            _mount is { IsMounted: true }, _mount is { IsGalloping: true });
        (float amount, bool isCrit) = CombatMath.RollAttack(
            bow.BaseDamage * definition.DamageScale * mounted, _stats);

        Vector3 from = body.GlobalPosition + (Vector3.Up * 1.4f);
        Vector3 direction = AimPoint is { } aim && aim.DistanceSquaredTo(from) > 0.04f
            ? (aim - from).Normalized()
            : -body.GlobalBasis.Z;

        Arrow arrow = _arrows.Get();
        if (arrow.GetParent() == null)
        {
            // ⚠️ CurrentScene is null outside a normal game boot — every `--script` harness runs with
            // no current scene — and `CurrentScene?.AddChild` then silently does nothing. The arrow
            // is a live object that is not in the tree: no physics, no overlaps, no hit, no error.
            // Falling back to the tree root keeps it in the WORLD (never parented to the shooter,
            // which would carry it along) and keeps the probes honest.
            SceneTree tree = body.GetTree();
            (tree.CurrentScene ?? tree.Root).AddChild(arrow);
        }

        arrow.GlobalPosition = from;
        arrow.Launch(
            new DamagePacket(amount, bow.DamageType, Entity, isCrit,
                bow.PoiseDamage * definition.PoiseScale),
            Entity, _combat?.Team ?? 0, direction,
            bow.ProjectileSpeed, bow.ProjectileRange, bow.ProjectileModelPath);
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
        float baseDamage = Weapon.BaseDamage * definition.DamageScale * mounted;

        (float amount, bool isCrit) = CombatMath.RollAttack(baseDamage, _stats);
        box.Activate(new DamagePacket(
            amount, Weapon.DamageType, Entity, isCrit, Weapon.PoiseDamage * definition.PoiseScale));
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
