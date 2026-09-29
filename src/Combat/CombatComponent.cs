using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// The defender-side combat brain for an entity. It owns poise/stagger state and
/// blocking, resolves incoming <see cref="DamagePacket"/>s through
/// <see cref="CombatMath"/>, applies the result to the <see cref="StatsComponent"/>,
/// and raises combat events. A <see cref="Hurtbox"/> routes hits here.
/// </summary>
[GlobalClass]
public partial class CombatComponent : EntityComponent
{
    /// <summary>
    /// Faction id used to prevent friendly fire. A <see cref="Hitbox"/> ignores
    /// hurtboxes whose owner shares its team. 0 = player, 1 = hostile, others are
    /// independent (e.g. neutral training targets).
    /// </summary>
    [Export]
    public int Team { get; set; }

    [Export]
    public float MaxPoise { get; set; } = 50f;

    /// <summary>Poise recovered per second while not staggered.</summary>
    [Export]
    public float PoiseRegen { get; set; } = 20f;

    [Export]
    public float StaggerDuration { get; set; } = 0.6f;

    /// <summary>What kind of body this is, for the purposes of being hit. Authored per archetype;
    /// the default is what the game is balanced around.</summary>
    [Export] public ReactionClass Reaction { get; set; } = ReactionClass.Humanoid;

    /// <summary>How this body last reacted to a broken guard. Read by presentation — the reaction
    /// clip, the knockback — so nothing has to re-derive it from the numbers.</summary>
    public StaggerResponse LastResponse { get; private set; } = StaggerResponse.None;

    /// <summary>Fraction of damage negated while blocking (0..1).</summary>
    [Export]
    public float BlockMitigation { get; set; } = 0.7f;

    [Export]
    public float BlockStaminaCost { get; set; } = 10f;

    /// <summary>A hit landing within this many seconds of raising the guard is parried (Phase 29F).</summary>
    [Export]
    public float ParryWindow { get; set; } = 0.2f;

    /// <summary>How long a parried attacker is staggered — the riposte opening.</summary>
    [Export]
    public float ParryStaggerDuration { get; set; } = 1.1f;

    /// <summary>Stamina a parry costs. With the one-parry-per-guard-raise latch this stops free
    /// tap-block parry-spam from dominating the read (DESIGN §1.4).</summary>
    [Export]
    public float ParryStaminaCost { get; set; } = 12f;

    /// <summary>Fraction of poise damage a (mistimed) block still takes, so a held guard can be broken.
    /// Rises toward double as stamina runs out (<see cref="DefenceRules.BlockPoiseFactor"/>).</summary>
    [Export]
    public float BlockPoiseFactor { get; set; } = 0.5f;

    /// <summary>Seconds a broken guard staggers this body, before the reaction class scales it
    /// (<see cref="DefenceRules.GuardBreakSeconds"/>). A guard breaks when a blow cannot be paid for in
    /// stamina, or when it is one no guard holds (a fully charged or plunging blow).</summary>
    [Export]
    public float GuardBreakStagger { get; set; } = 1.2f;

    /// <summary>
    /// Half-width of the arc a guard covers, in degrees from the defender's facing.
    ///
    /// ⚠️ <b>A GUARD USED TO COVER EVERY DIRECTION AT ONCE.</b> <see cref="IsBlocking"/> is a plain
    /// bool and <see cref="ReceiveDamage"/> asked nothing else, so a held block absorbed — and could
    /// parry — a blow landing squarely in the defender's back. That removes the whole point of pack
    /// flanking (34A) and of the boss's own repositioning, and it applies to the player and every
    /// enemy alike. 100° each way is a shield's honest cover: generous enough that a hit from the
    /// side quarter still counts, narrow enough that being surrounded is a real problem.
    /// </summary>
    [Export]
    public float GuardArcDegrees { get; set; } = 100f;

    private StatsComponent? _stats;
    private float _poise;
    private double _staggerTimer;
    private double _flinchTimer;
    private double _openTimer;
    private OpenCause _openCause;
    private float _blockElapsed;
    private bool _wasBlocking;
    private bool _parryConsumed;

    /// <summary>Set by a controller (player input / AI) to raise the guard.</summary>
    public bool IsBlocking { get; set; }

    /// <summary>While true the entity ignores all incoming damage — the dodge i-frame window (Phase 29E).</summary>
    public bool IsInvulnerable { get; set; }

    /// <summary>True while an interrupting reaction (stagger, heavy, knockdown, a parried or guard-broken
    /// body) holds this actor. ⚠️ A flinch is deliberately NOT this: it is presentation only, so everything
    /// that asks "was I interrupted?" (the action timeline, dodge, casting) keeps working through one.</summary>
    public bool IsStaggered => _staggerTimer > 0d;

    /// <summary>True during a flinch, the short reaction that does not interrupt.</summary>
    public bool IsFlinching => _flinchTimer > 0d;

    /// <summary>Seconds left on the current stagger, 0 when not staggered.</summary>
    public float StaggerRemaining => _staggerTimer > 0d ? (float)_staggerTimer : 0f;

    /// <summary>True while this body is a critical opening: parried, guard-broken or poise-broken, and
    /// for a short grace after (<see cref="DefenceRules.OpeningGraceSeconds"/>). The first riposte
    /// inside the window closes it.</summary>
    public bool IsOpen => _openTimer > 0d || RecoveryLive;

    /// <summary>Set by <c>CharacterActionComponent</c> for the committed tail of a
    /// <c>RecoveryVulnerable</c> action (a heavy's or plunge's recovery): the swing is paid for after it
    /// lands. The first riposte inside the tail closes it; it re-arms with the next such tail.</summary>
    public bool RecoveryOpen
    {
        get => _recoveryOpen;
        set
        {
            _recoveryOpen = value;
            if (!value)
            {
                _recoveryConsumed = false;
            }
        }
    }

    private bool _recoveryOpen;
    private bool _recoveryConsumed;
    private bool RecoveryLive => _recoveryOpen && !_recoveryConsumed;

    /// <summary>Why this body is open, <see cref="OpenCause.None"/> when it is not. A timed opening
    /// (stagger, parry, guard break) outranks the recovery tail when both hold.</summary>
    public OpenCause CurrentOpenCause =>
        _openTimer > 0d ? _openCause : (RecoveryLive ? OpenCause.Recovery : OpenCause.None);

    /// <summary>Seconds left in the punish window, 0 when closed.</summary>
    public float OpenRemaining => _openTimer > 0d ? (float)_openTimer : 0f;

    /// <summary>Is the guard actually up: held, and not knocked out of by a stagger. Presentation that
    /// draws a raised shield should ask this rather than <see cref="IsBlocking"/>.</summary>
    public bool GuardUp => IsBlocking && !IsStaggered;

    /// <summary>True while this actor is in its own attack wind-up (written by
    /// <see cref="CharacterActionComponent"/>, which owns that window). Read here because incoming poise
    /// damage is resolved here — see <see cref="WindupPoiseMultiplier"/>.</summary>
    public bool InWindup { get; set; }

    /// <summary>How much extra poise damage this actor takes while <see cref="InWindup"/>. Authored
    /// per boss phase (<c>BossPhaseResource.WindupPoiseMultiplier</c>) and pushed here by
    /// <c>BossController</c>; <c>1</c> — the default, and every non-boss — is no change.</summary>
    public float WindupPoiseMultiplier { get; set; } = 1f;

    public float PoiseNormalized => MaxPoise <= 0f ? 0f : Mathf.Clamp(_poise / MaxPoise, 0f, 1f);

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        ValidateAuthoring();
        _poise = MaxPoise;
    }

    /// <summary>
    /// Shouts about knobs that are outside the range the pipeline can honour.
    ///
    /// ⚠️ <b>EVERY ONE OF THESE USED TO PRODUCE BROKEN GAMEPLAY IN SILENCE.</b> A
    /// <see cref="BlockMitigation"/> over 1 makes <c>amount *= 1 - BlockMitigation</c> negative, and
    /// negative damage is healing — an enemy authored at 1.2 heals itself by guarding. A
    /// <see cref="MaxPoise"/> of 0 or less means <c>_poise &lt;= 0</c> on the first hit, so the actor
    /// staggers on every blow forever. Neither shows up as an error anywhere; they show up as a
    /// fight that feels wrong. The values are also clamped where they are used, so a bad
    /// <c>.tres</c> is loud AND survivable rather than loud and broken.
    /// </summary>
    private void ValidateAuthoring()
    {
        string who = Entity?.DisplayName ?? "an actor";
        if (BlockMitigation is < 0f or > 1f)
        {
            Log.Error($"{who}: BlockMitigation is {BlockMitigation}, outside 0..1. " +
                      "Over 1 would heal on block; clamped.");
        }

        if (BlockPoiseFactor < 0f)
        {
            Log.Error($"{who}: BlockPoiseFactor is {BlockPoiseFactor}; a negative factor restores " +
                      "poise on a blocked hit. Clamped to 0.");
        }

        if (MaxPoise <= 0f)
        {
            Log.Error($"{who}: MaxPoise is {MaxPoise}. Poise starts empty, so the actor would " +
                      "stagger on every hit for the rest of its life. Poise breaks are disabled " +
                      "for it instead.");
        }

        if (GuardBreakStagger < 0f)
        {
            Log.Error($"{who}: GuardBreakStagger is {GuardBreakStagger}; a negative stagger is none. " +
                      "Clamped to 0.");
        }

        if (GuardArcDegrees is <= 0f or > 360f)
        {
            Log.Error($"{who}: GuardArcDegrees is {GuardArcDegrees}; a guard covers nothing at or " +
                      "below 0. Clamped to 1..360.");
        }
    }

    /// <summary>
    /// The bearing of <paramref name="source"/> from this defender's facing, in degrees: 0 dead ahead,
    /// 180 directly behind. Null when there is no direction to judge (a trap, a status tick, a scripted
    /// hit, or an attacker standing inside the defender) — callers treat that as in front, because
    /// refusing a block for that reason would be a worse guess than allowing it.
    /// </summary>
    private float? BearingDegrees(IEntity? source)
    {
        if (Entity?.Body is not Node3D self || source?.Body is not Node3D attacker)
        {
            return null;
        }

        Vector3 toAttacker = attacker.GlobalPosition - self.GlobalPosition;
        toAttacker.Y = 0f;
        if (toAttacker.LengthSquared() < 1e-4f)
        {
            return null;
        }

        Vector3 forward = -self.GlobalTransform.Basis.Z;
        forward.Y = 0f;
        if (forward.LengthSquared() < 1e-4f)
        {
            return null;
        }

        float dot = Mathf.Clamp(forward.Normalized().Dot(toAttacker.Normalized()), -1f, 1f);
        return Mathf.RadToDeg(Mathf.Acos(dot));
    }

    public override void _Process(double delta)
    {
        if (_flinchTimer > 0d)
        {
            _flinchTimer -= delta;
        }

        if (_openTimer > 0d)
        {
            _openTimer -= delta;
            if (_openTimer <= 0d)
            {
                _openCause = OpenCause.None;
            }
        }

        if (_staggerTimer > 0d)
        {
            _staggerTimer -= delta;
        }
        else if (_poise < MaxPoise)
        {
            _poise = Mathf.Min(MaxPoise, _poise + (PoiseRegen * (float)delta));
        }

        // Track time since the guard was raised — the parry window measures from that moment, and each
        // raise re-arms the single parry (so a held guard can't chain free parries).
        if (IsBlocking && !_wasBlocking)
        {
            _blockElapsed = 0f;
            _parryConsumed = false;
        }
        else if (IsBlocking)
        {
            _blockElapsed += (float)delta;
        }
        else
        {
            _blockElapsed = 0f;
        }

        _wasBlocking = IsBlocking;
    }

    /// <summary>Forces a stagger of at least <paramref name="duration"/> seconds (an attacker that was
    /// parried, a guard that broke), resetting poise and raising the stagger event. A stagger opens the
    /// body to a riposte for <paramref name="cause"/> unless the cause is <see cref="OpenCause.None"/>.</summary>
    public void Stagger(
        float duration,
        OpenCause cause = OpenCause.Parry,
        StaggerResponse response = StaggerResponse.Stagger)
    {
        _staggerTimer = Mathf.Max(_staggerTimer, duration);
        _flinchTimer = 0d;
        _poise = MaxPoise;
        LastResponse = response;
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new EntityStaggeredEvent(Entity));
        }

        if (cause != OpenCause.None)
        {
            OpenUp(cause, duration);
        }
    }

    /// <summary>Opens the body to a riposte for the stagger plus a short grace. A stronger cause
    /// replaces a weaker one already open; the window never shrinks.</summary>
    private void OpenUp(OpenCause cause, float staggerSeconds)
    {
        float seconds = Mathf.Max(0f, staggerSeconds) + DefenceRules.OpeningGraceSeconds;
        if (!IsOpen || DefenceRules.RiposteBonus(cause) >= DefenceRules.RiposteBonus(_openCause))
        {
            _openCause = cause;
        }

        _openTimer = Mathf.Max(_openTimer, seconds);
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new PunishWindowOpenedEvent(Entity, _openCause, (float)_openTimer));
        }
    }

    /// <summary>The window closes on the first riposte that lands in it.</summary>
    private void CloseOpening()
    {
        _openTimer = 0d;
        _openCause = OpenCause.None;
        _recoveryConsumed = _recoveryOpen;
    }

    /// <summary>Resolves an incoming hit and applies it. Returns the resolved result.</summary>
    public DamageResult ReceiveDamage(DamagePacket packet)
    {
        if (_stats == null || !_stats.IsAlive || Entity == null)
        {
            return default;
        }

        // Dodge i-frames (Phase 29E): the hit whiffs entirely — no damage, no poise, no events.
        if (IsInvulnerable)
        {
            return default;
        }

        // What this kind of blow is worth against a defender (DefenceRules.Profile). The attacker
        // stamps Kind and Charge and never pre-scales by them, so nothing here counts twice.
        BlowProfile blow = DefenceRules.Profile(packet.Kind, packet.Charge);
        float amount = Mathf.Max(0f, packet.Amount) * blow.DamageMultiplier;
        float incomingPoise = Mathf.Max(0f, packet.PoiseDamage) * blow.PoiseMultiplier;
        bool blocked = false;
        bool guardBroken = false;
        ParryGrade parryGrade = ParryGrade.None;

        // Whether this blow is a critical opening is judged now, from the state the defender was in
        // when it arrived — not the state this very blow is about to put it in.
        float? bearing = BearingDegrees(packet.Source);
        HitKind opening = DefenceRules.ResolveOpening(
            packet.Kind, IsOpen, bearing is float b && DefenceRules.IsBehind(b), Team != 0);

        // A guard covers a front arc, and is knocked down with the body holding it. See
        // GuardArcDegrees and GuardUp.
        GuardZone zone = GuardUp && !packet.Unblockable
            ? DefenceRules.ZoneOf(bearing ?? 0f, GuardArcDegrees)
            : GuardZone.Outside;
        if (zone != GuardZone.Outside)
        {
            // Only a melee blow, squarely in front, on the guard's first raise can be parried; a late
            // timing still deflects. The latch and the stamina cost are what stop tap-block parry-spam
            // from dominating the read (DESIGN §1.4).
            parryGrade = blow.Parryable && zone == GuardZone.Front && !_parryConsumed
                ? Parry.Grade(_blockElapsed, ParryWindow)
                : ParryGrade.None;
            float parryCost = ParryStaminaCost * Parry.StaminaFactor(parryGrade);

            if (parryGrade != ParryGrade.None && _stats.GetCurrent(StatType.Stamina) >= parryCost)
            {
                _parryConsumed = true;
                if (parryCost > 0f)
                {
                    _stats.ModifyCurrent(StatType.Stamina, -parryCost);
                }

                StaggerAttacker(packet.Source, parryGrade);
                EventBus.Instance?.Publish(new ParryGradedEvent(Entity, packet.Source, parryGrade));

                if (Parry.OpensRiposte(parryGrade))
                {
                    EventBus.Instance?.Publish(new EntityParriedEvent(Entity, packet.Source));
                    return new DamageResult(0f, false, true, packet.Type, parryGrade);
                }

                // A late parry: not a clean negate, but a far better block than a plain one.
                amount *= 1f - Parry.LateMitigation;
                blocked = true;
            }
            else
            {
                parryGrade = ParryGrade.None;

                // Mistimed/held block: chip through, and the guard pays for the blow by its weight.
                // A guard that cannot pay, or a blow no guard holds, is broken and the full hit lands.
                float current = _stats.GetCurrent(StatType.Stamina);
                float cost = DefenceRules.GuardStaminaCost(BlockStaminaCost, blow.GuardPressure, incomingPoise);
                bool canBreak = blow.CrushesGuard || _stats.GetMax(StatType.Stamina) > 0f;
                if (!blow.CrushesGuard && current >= cost)
                {
                    _stats.ModifyCurrent(StatType.Stamina, -cost);
                    amount *= 1f - DefenceRules.ZoneMitigation(BlockMitigation, zone);
                    blocked = true;
                }
                else if (canBreak)
                {
                    _stats.ModifyCurrent(StatType.Stamina, -Mathf.Min(cost, current));
                    guardBroken = true;
                }
            }
        }

        // A riposte or a backstab: a blow that no guard or parry took lands as a critical. It stacks
        // only half over a rolled crit, and a riposte closes the window it used.
        bool openingLanded = !blocked && opening != HitKind.Normal;
        if (openingLanded)
        {
            amount *= DefenceRules.OpeningMultiplier(opening, CurrentOpenCause, packet.IsCrit, Reaction);
            if (opening == HitKind.Riposte)
            {
                CloseOpening();
            }
        }

        bool isCrit = packet.IsCrit || openingLanded;

        // Never negative: a mitigation that over-reduced would otherwise heal the defender, which
        // is what an out-of-range BlockMitigation used to do (see ValidateAuthoring). And never
        // nothing either: resistance is not immunity, so an unblocked hit does at least a point.
        float final = Mathf.Max(0f, CombatMath.FloorHit(
            CombatMath.Mitigate(amount, packet.Type, _stats), amount, blocked));
        _stats.ApplyDamage(final, packet.Source);

        if (openingLanded && packet.Source != null)
        {
            EventBus.Instance?.Publish(new CriticalHitEvent(packet.Source, Entity, opening));
        }

        if (guardBroken)
        {
            EventBus.Instance?.Publish(new GuardBrokenEvent(Entity, packet.Source));
        }

        // A kill blow doesn't also stagger the corpse — only poise-check a survivor (avoids a
        // Staggered event firing alongside the Died event on the same hit).
        if (_stats.IsAlive)
        {
            if (guardBroken)
            {
                // The long punish: heavier than a poise break, and it opens the body to a riposte.
                Stagger(
                    DefenceRules.GuardBreakSeconds(Reaction, Mathf.Max(0f, GuardBreakStagger)),
                    OpenCause.GuardBreak,
                    DefenceRules.GuardBreakResponse(Reaction));
            }
            else
            {
                ApplyPoise(incomingPoise, blocked);
            }
        }

        EventBus.Instance?.Publish(
            new DamageDealtEvent(packet.Source, Entity, final, packet.Type, isCrit, blocked));

        return new DamageResult(
            final, isCrit, blocked, packet.Type, parryGrade, guardBroken,
            openingLanded ? opening : HitKind.Normal);
    }

    /// <summary>The attacker's half of a parry: staggered for the graded duration, scaled for its body,
    /// and open to a riposte if the parry was good enough to earn one.</summary>
    private void StaggerAttacker(IEntity? attacker, ParryGrade grade)
    {
        CombatComponent? theirs = attacker?.GetComponent<CombatComponent>();
        if (theirs == null)
        {
            return;
        }

        float seconds = ParryStaggerDuration * Parry.AttackerStaggerFactor(grade)
                        * DefenceRules.ParryStaggerScale(theirs.Reaction);
        OpenCause cause = grade switch
        {
            ParryGrade.Perfect => OpenCause.PerfectParry,
            ParryGrade.Good => OpenCause.Parry,
            _ => OpenCause.None,
        };
        theirs.Stagger(seconds, cause, grade == ParryGrade.Perfect ? StaggerResponse.Heavy : StaggerResponse.Stagger);
    }

    /// <summary>
    /// The poise half of a hit that neither broke a guard nor died. A block still chips poise
    /// (<see cref="BlockPoiseFactor"/>, higher when tired) so a held guard can be broken into a stagger;
    /// a defender caught in its own wind-up takes more (36C). ⚠️ A body that is already staggered is
    /// not chipped further: its punish window is safe to spend and cannot be chain-locked (a poise
    /// re-break used to overwrite the running stagger, so a lighter reaction could even shorten it).
    /// </summary>
    private void ApplyPoise(float incomingPoise, bool blocked)
    {
        if (IsStaggered || Entity == null || _stats == null)
        {
            return;
        }

        float factor = DefenceRules.BlockPoiseFactor(
            Mathf.Max(0f, BlockPoiseFactor), _stats.GetNormalized(StatType.Stamina));
        float poiseDamage = CombatMath.PoiseDamage(
            incomingPoise, blocked, factor, InWindup ? WindupPoiseMultiplier : 1f);
        float overkill = PoiseReaction.Overkill(poiseDamage, _poise, MaxPoise);
        _poise -= poiseDamage;

        if (MaxPoise <= 0f || _poise > 0f)
        {
            return;
        }

        _poise = MaxPoise;

        // ⚠️ WHAT HAPPENS NOW DEPENDS ON WHAT THIS BODY IS. Every actor used to take the same 0.6 s
        // stagger from every broken guard, so a goblin and the Iron King reacted identically once their
        // numbers were spent — the "weightless ragdoll" the brief names. A flinch does not interrupt,
        // which is what makes an armoured enemy feel armoured rather than merely slower, and a boss is
        // never knocked off its feet because a boss that can be knocked over can be chain-knocked.
        LastResponse = PoiseReaction.Resolve(Reaction, overkill);
        float seconds = PoiseReaction.Duration(LastResponse, StaggerDuration);

        if (PoiseReaction.Interrupts(LastResponse))
        {
            _staggerTimer = seconds;
            _flinchTimer = 0d;
            EventBus.Instance?.Publish(new EntityStaggeredEvent(Entity));
            OpenUp(OpenCause.PoiseBreak, seconds);
        }
        else
        {
            _flinchTimer = seconds;
        }
    }
}
