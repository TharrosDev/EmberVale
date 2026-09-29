using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Embervale.Corruption;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// The spellcasting brain for an entity: the spells it knows, which one is prepared,
/// per-spell cooldowns, and the cast itself (mana spend → wind-up → deliver). It is the magic
/// analogue of <see cref="CharacterActionComponent"/> and is deliberately input-agnostic —
/// the player controller (and enemy AI) decides <em>when</em> to call
/// <see cref="TryCast"/> / <see cref="Cycle"/>.
///
/// <para>A cast is a <b>committed action</b> (magic upgrade 2026-09): it has a visible wind-up
/// (<see cref="SpellResource.WindupSeconds"/>) that a stagger, a silence or a stun cancels — half the
/// mana comes back — a release frame the action and the spell agree on, and a recovery.
/// <see cref="SpellResource.Interruptible"/> false, or a Barkskin caster, lets a cast finish through a
/// stagger. The rules live in <see cref="SpellRules"/>.</para>
///
/// <para>Delivery is resource-driven (<see cref="SpellResource.Delivery"/>): a projectile fired along
/// the caster's aim, an instant burst around the caster, a self heal/buff, a cone, a ground spell that
/// lands after a telegraph, a standing barrier, or a dash. Known spells + the prepared index + ranks +
/// cooldowns persist via <see cref="ISaveable"/>; a retired spell id in a save resolves to its
/// replacement through <see cref="SpellDatabase.Get"/>.</para>
/// </summary>
[GlobalClass]
public partial class SpellcastingComponent : EntityComponent, ISaveable
{
    private const float SpellPoiseDamage = 12f;
    private const float DefaultNovaRadius = 4f;
    private const float MuzzleOffset = 1.2f;
    private const float WallMargin = 0.5f;
    private const float DashWallMargin = 0.6f;
    private const string BarkskinId = "status.barkskin";
    private const string StunnedId = "status.stunned";
    private const ulong AttackerMemoryMs = 1500;

    /// <summary>Spell ids this entity starts knowing (authored by the factory/scene).</summary>
    [Export]
    public Godot.Collections.Array<string> KnownSpellIds { get; set; } = new();

    /// <summary>Aim source for projectiles/area targeting; the player's camera pivot.
    /// Falls back to the entity body when not injected.</summary>
    public Node3D? AimNode { get; set; }

    private readonly List<SpellResource> _spells = new();
    private readonly Dictionary<string, double> _cooldowns = new();

    /// <summary>Scratch list of cooldowns that expired this tick, reused every frame. Removals are
    /// deferred into it because a dictionary cannot be removed from mid-enumeration (updating an
    /// existing value in place is fine on .NET 8, and that is what the tick does).</summary>
    private readonly List<string> _expiring = new();
    private readonly Dictionary<string, int> _ranks = new();

    private StatsComponent? _stats;
    private CombatComponent? _combat;
    private CorruptionComponent? _corruption;
    private Progression.ProgressionComponent? _progression;
    private SchoolMasteryComponent? _mastery;
    private int _selected;

    // Active charged/channeled cast (Phase 29.5A); null for instant casts and when idle.
    private SpellResource? _activeCast;
    private float _chargeElapsed;

    /// <summary>The spell waiting for its cast action to reach the release frame, and how strong the
    /// charge made it. Null whenever nothing is in flight.</summary>
    private SpellResource? _pending;
    private float _pendingPower = 1f;
    private CharacterActionComponent? _actions;
    private double _channelTickTimer;

    // --- committed cast (magic upgrade): what the pending cast paid and how strong it is ---
    private float _pendingCharge;
    private float _pendingMana;
    private bool _captureWindup;
    private float _capturedWindup;
    private IEntity? _lastAttacker;
    private ulong _lastAttackerAt;
    private StatusEffectsComponent? _statuses;
    private WorldRay? _worldRay;
    private StatModifier? _channelSlow;

    /// <summary>The spell whose wind-up is running (mana spent, waiting for its release frame), or null.</summary>
    public SpellResource? PendingSpell => _pending;

    /// <summary>The wind-up the last committed cast reported, in seconds (what the telegraph showed).</summary>
    public float LastWindupSeconds { get; private set; }

    /// <summary>True while a charged cast is being held (drives charge-meter UI later).</summary>
    public bool IsCharging => _activeCast is { CastMode: CastMode.Charged };

    /// <summary>True while a channeled cast is sustaining.</summary>
    public bool IsChanneling => _activeCast is { CastMode: CastMode.Channeled };

    /// <summary>How full the active charged cast is (0..1) — drives the HUD charge meter (29.5G).</summary>
    public float ChargeProgress =>
        _activeCast is { CastMode: CastMode.Charged } spell && spell.ChargeTime > 0f
            ? Mathf.Clamp(_chargeElapsed / spell.ChargeTime, 0f, 1f)
            : 0f;

    // Pooled projectiles: rapid casting reuses bolts instead of churning the scene tree.
    private NodePool<SpellProjectile>? _projectilePool;

    public string SaveId => SaveKey("spells");

    public IReadOnlyList<SpellResource> Spells => _spells;

    public int SelectedIndex => _selected;

    public SpellResource? Selected =>
        _spells.Count == 0 ? null : _spells[Mathf.Clamp(_selected, 0, _spells.Count - 1)];

    private StatusEffectsComponent? Statuses =>
        _statuses != null && IsInstanceValid(_statuses)
            ? _statuses
            : (_statuses = Entity?.GetComponent<StatusEffectsComponent>());

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
        _progression = Entity.GetComponent<Progression.ProgressionComponent>();
        _mastery = Entity.GetComponent<SchoolMasteryComponent>();
        _actions = Entity.GetComponent<CharacterActionComponent>();
        EventBus.Instance?.Subscribe<ActionReleasedEvent>(OnActionReleased);
        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttackPerformed);
        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamageDealt);
        _worldRay = new WorldRay();
        if (Entity.Body is CollisionObject3D collider)
        {
            _worldRay.Ignore(collider.GetRid());
        }

        _projectilePool = new NodePool<SpellProjectile>(
            () => new SpellProjectile { Released = ReturnProjectile }, prewarm: 4);
        RebuildSpells();
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<ActionReleasedEvent>(OnActionReleased);
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttackPerformed);
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamageDealt);
        DropChannelSlow();
        _worldRay?.Dispose();
        _worldRay = null;
        _projectilePool?.Clear();
        SaveManager.Instance?.Unregister(this);
    }

    private void ReturnProjectile(SpellProjectile projectile) => _projectilePool?.Return(projectile);

    public override void _Process(double delta)
    {
        // Interrupt (36C, and the magic upgrade): a stagger drops whatever is being charged, channelled
        // or wound up, and so does a silence or a stun; an uninterruptible spell or a Barkskin caster
        // rides a stagger out. BreathComponent already stops the moment IsChanneling goes false, so a
        // staggered dragon's breath ends through this too. Checked before the cooldown early-out: a cast
        // with nothing on cooldown is still a cast.
        TickInterrupts();

        if (_cooldowns.Count == 0)
        {
            return;
        }

        // Snapshot the keys so removing/updating entries during the tick is safe — into a reusable
        // buffer, not a fresh List every frame. This ticks on the player and on every caster enemy, so
        // the old allocation was a steady drip of garbage straight into the frame loop, and a GC hitch
        // is precisely what a game with i-frames and parry windows cannot afford (DESIGN §1.3).
        _expiring.Clear();
        foreach (KeyValuePair<string, double> entry in _cooldowns)
        {
            double remaining = entry.Value - delta;
            if (remaining <= 0d)
            {
                _expiring.Add(entry.Key);
            }
            else
            {
                _cooldowns[entry.Key] = remaining;
            }
        }

        foreach (string id in _expiring)
        {
            _cooldowns.Remove(id);
        }
    }

    // --- interrupts --------------------------------------------------------------------------------

    private bool Dead => _stats is { IsAlive: false };

    private bool Hyperarmoured => Statuses?.Has(BarkskinId) == true;

    /// <summary>Whether a stagger, silence or stun drops <paramref name="spell"/> right now.</summary>
    private bool InterruptsNow(SpellResource spell) =>
        Dead || SpellRules.Interrupts(
            _combat is { IsStaggered: true },
            Statuses is { IsSilenced: true },
            Statuses is { IsStunned: true },
            spell.Interruptible,
            Hyperarmoured);

    private void TickInterrupts()
    {
        if (_activeCast is { } held && InterruptsNow(held))
        {
            CancelCast();
            if (Entity != null)
            {
                EventBus.Instance?.Publish(new AttackInterruptedEvent(Entity));
                EventBus.Instance?.Publish(new SpellInterruptedEvent(Entity, held.Id, RecentAttacker()));
            }
        }

        // A committed cast in its wind-up: the mana is spent, the release frame has not come. An action
        // that ended without releasing (something cancelled it) is the same thing.
        if (_pending is { } pending && (InterruptsNow(pending) || _actions is { Current: null }))
        {
            InterruptPending();
        }
    }

    /// <summary>Drops the wind-up in flight: half the mana comes back, the action is cancelled, and
    /// <see cref="SpellInterruptedEvent"/> says who did it when that is known.</summary>
    private void InterruptPending()
    {
        SpellResource? spell = _pending;
        if (spell == null)
        {
            return;
        }

        float spent = _pendingMana;
        _pending = null;
        _pendingMana = 0f;
        _actions?.Cancel();
        if (spent > 0f && _stats is { IsAlive: true })
        {
            _stats.ModifyCurrent(StatType.Mana, SpellRules.InterruptRefund(spent));
        }

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new SpellInterruptedEvent(Entity, spell.Id, RecentAttacker()));
        }
    }

    private IEntity? RecentAttacker()
    {
        if (_lastAttacker != null && Godot.Time.GetTicksMsec() - _lastAttackerAt <= AttackerMemoryMs)
        {
            return _lastAttacker;
        }

        // A silence or a stun names its own source.
        if (Statuses != null)
        {
            foreach (StatusEffect effect in Statuses.ActiveEffects)
            {
                if ((effect.Definition.Controls & (StatusControl.Silence | StatusControl.Stun)) != 0)
                {
                    return effect.Source;
                }
            }
        }

        return null;
    }

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (ReferenceEquals(e.Target, Entity) && e.Source != null)
        {
            _lastAttacker = e.Source;
            _lastAttackerAt = Godot.Time.GetTicksMsec();
        }
    }

    /// <summary>While a cast is being started, remembers the effective wind-up the action reports.</summary>
    private void OnAttackPerformed(AttackPerformedEvent e)
    {
        if (_captureWindup && ReferenceEquals(e.Attacker, Entity))
        {
            _capturedWindup = e.WindupSeconds;
        }
    }

    // --- spellbook ---------------------------------------------------------------------------------

    /// <summary>Seconds of cooldown remaining for a spell (0 = ready).</summary>
    public float CooldownOf(SpellResource spell) =>
        _cooldowns.TryGetValue(spell.Id, out double cd) ? (float)Mathf.Max(0d, cd) : 0f;

    /// <summary>Moves the prepared-spell selection by <paramref name="direction"/> (wrapping).</summary>
    public void Cycle(int direction)
    {
        if (_spells.Count == 0)
        {
            return;
        }

        int count = _spells.Count;
        _selected = (((_selected + direction) % count) + count) % count;
        if (Entity != null && Selected != null)
        {
            EventBus.Instance?.Publish(new SpellSelectedEvent(Entity, Selected.Id));
        }
    }

    /// <summary>The caster's current corruption tier (Untainted when it has no
    /// <see cref="CorruptionComponent"/>). Resolved from the sibling on demand so it is
    /// always current — mirrors how <c>ReputationComponent</c> reads corruption.</summary>
    private CorruptionTier CorruptionTierNow =>
        (_corruption ??= Entity?.GetComponent<CorruptionComponent>())?.Tier ?? CorruptionTier.Untainted;

    /// <summary>Whether the caster is corrupted enough to learn the spell (Phase 23H gate).</summary>
    public bool MeetsCorruption(SpellResource spell) => CorruptionTierNow >= spell.MinCorruptionTier;

    /// <summary>Whether the spell is unknown, exists, and its corruption gate is met.</summary>
    public bool CanLearn(SpellResource spell) =>
        !_spells.Exists(s => s.Id == spell.Id) && MeetsCorruption(spell);

    /// <summary>Teaches a new spell at runtime (e.g. from a tome pickup or trainer). A spell
    /// gated above the caster's corruption tier (Phase 23H) is refused.</summary>
    public void Learn(string spellId)
    {
        // A retired id resolves to its replacement, so a tome or a dialogue that still names it teaches
        // the new spell (and never a second copy of one already known).
        if (SpellDatabase.Get(spellId) is not { } spell || _spells.Exists(s => s.Id == spell.Id))
        {
            return;
        }

        if (!MeetsCorruption(spell))
        {
            return;
        }

        _spells.Add(spell);
        if (!KnownSpellIds.Contains(spell.Id))
        {
            KnownSpellIds.Add(spell.Id);
        }
    }

    /// <summary>Whether the spell is already in this caster's spellbook.</summary>
    public bool IsKnown(SpellResource spell) => _spells.Exists(s => s.Id == spell.Id);

    /// <summary>The spell's current rank (1 once known, 0 if unknown).</summary>
    public int RankOf(SpellResource spell) =>
        _ranks.TryGetValue(spell.Id, out int rank) ? rank : (IsKnown(spell) ? 1 : 0);

    /// <summary>Can the caster buy (learn) this unknown spell now — corruption met and spell points spare?</summary>
    public bool CanBuy(SpellResource spell) =>
        !IsKnown(spell) && MeetsCorruption(spell) && (_progression?.SpellPoints ?? 0) >= spell.LearnCost;

    /// <summary>Buys an unknown spell with spell-book points (Phase 29.5G). Returns false if not affordable.</summary>
    public bool Buy(SpellResource spell)
    {
        if (!CanBuy(spell) || _progression?.SpendSpellPoints(spell.LearnCost) != true)
        {
            return false;
        }

        _spells.Add(spell);
        if (!KnownSpellIds.Contains(spell.Id))
        {
            KnownSpellIds.Add(spell.Id);
        }

        _ranks[spell.Id] = 1;
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new SpellsChangedEvent(Entity));
        }

        return true;
    }

    /// <summary>Can the caster rank up this known spell — below max and enough spell points?</summary>
    public bool CanUpgrade(SpellResource spell) =>
        IsKnown(spell) && RankOf(spell) < spell.MaxRank && (_progression?.SpellPoints ?? 0) >= spell.UpgradeCost;

    /// <summary>Spends spell-book points to raise a known spell's rank, empowering its damage/healing.</summary>
    public bool Upgrade(SpellResource spell)
    {
        if (!CanUpgrade(spell) || _progression?.SpendSpellPoints(spell.UpgradeCost) != true)
        {
            return false;
        }

        _ranks[spell.Id] = RankOf(spell) + 1;
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new SpellsChangedEvent(Entity));
        }

        return true;
    }

    // --- casting -----------------------------------------------------------------------------------

    /// <summary>Whether <paramref name="spell"/> is castable right now: it exists, the caster is
    /// alive and neither silenced nor stunned, it is off cooldown, the mana is there and a health
    /// cost would not kill the caster. <see cref="NotNullWhenAttribute"/> carries the non-null half of
    /// that verdict out to callers, so a guarded cast path needs no <c>!</c>.</summary>
    public bool CanCast([NotNullWhen(true)] SpellResource? spell)
    {
        if (spell == null || _stats == null || !_stats.IsAlive)
        {
            return false;
        }

        // A silenced or stunned caster cannot begin a cast, and a health cost is refused rather than
        // killing the caster. A blink is affordable at its cheapest jump; the real price is the jump's.
        if (!SpellRules.CanBegin(Statuses is { IsSilenced: true }, Statuses is { IsStunned: true }) ||
            !SpellRules.CanPayHealth(_stats.GetCurrent(StatType.Health), spell.HealthCost))
        {
            return false;
        }

        float floor = spell.BlinkDistance > 0f
            ? EffectiveManaCost(spell) * SpellRules.BlinkMinCostFraction
            : EffectiveManaCost(spell);
        return CooldownOf(spell) <= 0f && _stats.GetCurrent(StatType.Mana) >= floor;
    }

    /// <summary>
    /// Casts the prepared spell. Returns false if none is ready/affordable or the caster is already
    /// committed to another action. Charged and channeled spells route through <see cref="BeginCast"/>
    /// instead.
    ///
    /// <para>⚠️ The spell does not leave on the frame the key went down. The cast runs as an action on
    /// the shared timeline and the spell is delivered on that action's release, which is the frame the
    /// animation shows it leaving the hand — and the wind-up before it is the window a stagger can
    /// cancel. Mana and cooldown are spent up front, so a cast cannot be started twice while the first
    /// is in the air.</para>
    ///
    /// <para>An actor with no action component (a turret, a bare test harness) delivers immediately.</para>
    /// </summary>
    public bool TryCast() => StartCast(Selected, 1f, 0f);

    /// <summary>
    /// Begins a committed cast: the action starts (a wind-up the caster is open to being interrupted in),
    /// the mana, health and cooldown are paid, and <see cref="CastWindupStartedEvent"/> says how long the
    /// wind-up is. A caster already committed to another action, or staggered, cannot begin one: nothing
    /// is spent.
    /// </summary>
    private bool StartCast(SpellResource? spell, float power, float charge)
    {
        if (!CanCast(spell))
        {
            return false;
        }

        float mana = ManaToSpend(spell);
        if (_stats!.GetCurrent(StatType.Mana) < mana)
        {
            return false;
        }

        bool started = false;
        if (_actions != null)
        {
            _captureWindup = true;
            _capturedWindup = 0f;
            started = _actions.TryStart(SpellActions.For(spell, !spell.Interruptible || Hyperarmoured));
            _captureWindup = false;
            if (!started)
            {
                return false;
            }
        }

        Pay(spell, mana);
        if (started)
        {
            _pending = spell;
            _pendingPower = power;
            _pendingCharge = charge;
            _pendingMana = mana;
            LastWindupSeconds = _capturedWindup > 0f
                ? _capturedWindup
                : SpellRules.WindupOf(SpellRules.Shape(
                    spell.WindupSeconds, spell.RecoverySeconds, spell.CastMode == CastMode.Channeled));
            if (Entity != null)
            {
                EventBus.Instance?.Publish(new CastWindupStartedEvent(Entity, spell.Id, LastWindupSeconds));
            }
        }
        else
        {
            Deliver(spell, power, charge);
        }

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new SpellCastEvent(Entity, spell.Id));
        }

        return true;
    }

    /// <summary>Mana, health and cooldown a cast costs once it has begun.</summary>
    private void Pay(SpellResource spell, float mana)
    {
        _stats!.ModifyCurrent(StatType.Mana, -mana);
        if (spell.HealthCost > 0f)
        {
            _stats.ModifyCurrent(StatType.Health, -spell.HealthCost);
        }

        _cooldowns[spell.Id] = spell.Cooldown;
    }

    /// <summary>What a cast costs in mana now: the spell's cost, or for a blink the price of the jump
    /// the wall will actually allow.</summary>
    private float ManaToSpend(SpellResource spell)
    {
        float cost = EffectiveManaCost(spell);
        return spell.BlinkDistance > 0f
            ? SpellRules.BlinkCost(cost, TravelAlongAim(spell.BlinkDistance, WallMargin), spell.BlinkDistance)
            : cost;
    }

    /// <summary>Delivers a spell whose cast action has reached its release. The action timeline
    /// decides when that is; this only has to happen once per cast, which the null-out enforces.</summary>
    private void OnActionReleased(ActionReleasedEvent e)
    {
        if (!ReferenceEquals(e.Actor, Entity) || e.Kind != ActionKind.Cast || _pending is not { } spell)
        {
            return;
        }

        _pending = null;
        _pendingMana = 0f;
        Deliver(spell, _pendingPower, _pendingCharge);
    }

    /// <summary>Begins a cast on key-down (Phase 29.5A): Instant fires now; Charged starts charging;
    /// Channeled starts sustaining. No-op if a cast is already active or the spell isn't ready.</summary>
    public void BeginCast()
    {
        if (_activeCast != null)
        {
            return;
        }

        SpellResource? spell = Selected;
        switch (spell?.CastMode)
        {
            case CastMode.Charged when CanCast(spell):
                _activeCast = spell;
                _chargeElapsed = 0f;
                break;
            case CastMode.Channeled when CanCast(spell):
                _activeCast = spell;
                _channelTickTimer = 0d; // fire the first tick immediately
                ApplyChannelSlow(spell);
                break;
            default:
                TryCast();
                break;
        }
    }

    /// <summary>Advances an active charged/channeled cast each frame while the key is held.</summary>
    public void UpdateCast(double delta)
    {
        switch (_activeCast?.CastMode)
        {
            case CastMode.Charged:
                _chargeElapsed += (float)delta;
                break;
            case CastMode.Channeled:
                TickChannel(delta);
                break;
        }
    }

    /// <summary>Ends a cast on key-up (Phase 29.5A): a charged cast fires scaled by how long it was held;
    /// a channeled cast simply stops (and goes on cooldown). No-op for instant casts.</summary>
    public void EndCast()
    {
        SpellResource? spell = _activeCast;
        if (spell == null)
        {
            return;
        }

        _activeCast = null;
        DropChannelSlow();

        if (spell.CastMode == CastMode.Charged)
        {
            float held = spell.ChargeTime > 0f ? Mathf.Clamp(_chargeElapsed / spell.ChargeTime, 0f, 1f) : 1f;
            float power = SpellCharge.PowerMultiplier(_chargeElapsed, spell.ChargeTime, spell.MaxChargeMultiplier);

            // The release of a charged spell is the same beat as an instant one: the charge decided how
            // strong it is, the action decides when it leaves.
            StartCast(spell, power, held);
        }
        else if (spell.CastMode == CastMode.Channeled)
        {
            _cooldowns[spell.Id] = spell.Cooldown;
        }
    }

    /// <summary>Abandons an in-progress charged/channeled cast without firing (e.g. a menu opens or the
    /// game pauses) — no damage, no mana spent on release, no cooldown.</summary>
    public void CancelCast()
    {
        _activeCast = null;
        DropChannelSlow();

        // ⚠️ A cast interrupted between its start and its release must not still go off. The cooldown is
        // already set — that is deliberate — but the bolt does not leave, which is what makes
        // interrupting a caster worth doing. (A stagger, silence or stun goes through InterruptPending,
        // which also hands half the mana back and says so.)
        _pending = null;
        _pendingMana = 0f;
        _actions?.Cancel();
    }

    /// <summary>A channel is slowed while it runs: a mage holding a beam does not sprint.</summary>
    private void ApplyChannelSlow(SpellResource spell)
    {
        DropChannelSlow();
        if (_stats == null || spell.ChannelMoveScale >= 1f)
        {
            return;
        }

        _channelSlow = new StatModifier(Mathf.Clamp(spell.ChannelMoveScale, 0f, 1f) - 1f, ModifierType.PercentMult, this);
        _stats.GetStat(StatType.MoveSpeed).AddModifier(_channelSlow);
    }

    private void DropChannelSlow()
    {
        if (_channelSlow != null)
        {
            _stats?.GetStat(StatType.MoveSpeed).RemoveModifiersFromSource(this);
            _channelSlow = null;
        }
    }

    private void TickChannel(double delta)
    {
        SpellResource spell = _activeCast!;
        _channelTickTimer -= delta;
        if (_channelTickTimer > 0d)
        {
            return;
        }

        float tickCost = spell.ChannelManaPerSecond * spell.ChannelTickInterval
            * Weave.CostMultiplier(IsCorrupted(spell));
        if (_stats == null || !_stats.IsAlive || _stats.GetCurrent(StatType.Mana) < tickCost)
        {
            EndCast(); // out of mana / dead — the channel is interrupted
            return;
        }

        _stats.ModifyCurrent(StatType.Mana, -tickCost);
        _channelTickTimer = spell.ChannelTickInterval;
        Deliver(spell, 1f, 0f);
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new SpellCastEvent(Entity, spell.Id));
        }
    }

    // --- delivery ----------------------------------------------------------------------------------

    private void Deliver(SpellResource spell, float power, float charge)
    {
        int team = _combat?.Team ?? 0;

        switch (spell.Delivery)
        {
            case SpellDelivery.Ground:
                CastGround(spell, team, power, charge);
                break;
            case SpellDelivery.Barrier:
                CastBarrier(spell, team, power, charge);
                break;
            case SpellDelivery.Dash:
                CastDash(spell, team, power, charge);
                break;
            default:
                DeliverShaped(spell, team, power, charge);
                break;
        }

        // What the caster gains on release, whatever the shape (Soul Tithe's echo, a buff on a hit spell).
        if (spell.HasSelfStatus && Entity != null)
        {
            Statuses?.Apply(StatusEffectDatabase.Get(spell.SelfStatusEffectId), Entity);
        }
    }

    private void DeliverShaped(SpellResource spell, int team, float power, float charge)
    {
        // Signature mechanics (Phase 29.5G) layer on top of the base shape: a zone/totem field
        // replaces the instant delivery with a lingering spawn; blink rides along a Self cast.
        if (spell.ZoneDuration > 0f)
        {
            CastZone(spell, team, power, charge);
            return;
        }

        if (spell.SummonDuration > 0f)
        {
            CastTotem(spell, team, power);
            return;
        }

        switch (spell.Delivery)
        {
            case SpellDelivery.Self:
                CastSelf(spell, power);
                break;
            case SpellDelivery.Area:
                CastArea(spell, team, power, charge);
                break;
            case SpellDelivery.Cone:
                CastCone(spell, team, power, charge);
                break;
            default:
                CastProjectile(spell, team, power, charge);
                break;
        }
    }

    private void CastZone(SpellResource spell, int team, float power, float charge)
    {
        if (Entity?.Body is not Node3D body)
        {
            return;
        }

        var zone = new SpellZone
        {
            Spell = spell,
            Packet = BuildPacket(spell, power, charge),
            Caster = Entity,
            CasterTeam = team,
            Radius = spell.ImpactRadius > 0f ? spell.ImpactRadius : DefaultNovaRadius,
            Duration = spell.ZoneDuration,
            TickInterval = spell.ZoneTickInterval,
            PullStrength = spell.PullStrength,
        };
        GetTree().CurrentScene.AddChild(zone);
        zone.GlobalPosition = body.GlobalPosition + (Vector3.Up * 0.1f);
    }

    private void CastTotem(SpellResource spell, int team, float power)
    {
        if (Entity?.Body is not Node3D body)
        {
            return;
        }

        var totem = new SpellTotem
        {
            Target = _stats,
            HealPerTick = spell.Healing * Empower(spell, power),
            Duration = spell.SummonDuration,
            TickInterval = spell.SummonTickInterval,
            Tint = SpellSchools.Color(spell.School),
            Spell = spell,
            Caster = Entity,
            CasterTeam = team,
            Health = spell.SummonHealth > 0f ? spell.SummonHealth : SpellTotem.DefaultHealth,
        };
        GetTree().CurrentScene.AddChild(totem);
        totem.GlobalPosition = body.GlobalPosition;
    }

    /// <summary>Whether the spell is a corrupted variant (gated above Untainted, Phase 23H) — the
    /// Weave (29.5E) empowers and cheapens these as the world dies.</summary>
    private static bool IsCorrupted(SpellResource spell) => spell.MinCorruptionTier > CorruptionTier.Untainted;

    /// <summary>The spell's mana cost after the region's Weave potency (Phase 29.5E).</summary>
    private static float EffectiveManaCost(SpellResource spell) =>
        spell.ManaCost * Weave.CostMultiplier(IsCorrupted(spell));

    /// <summary>Combined cast power: the charge multiplier × the spell's own rank × the caster's
    /// school mastery (Phase 29.5C) × the region's Weave potency (Phase 29.5E).</summary>
    private float Empower(SpellResource spell, float power) =>
        power
        * SpellMastery.DamageMultiplier(RankOf(spell), spell.DamagePerRank)
        * (_mastery?.PowerMultiplier(spell.School) ?? 1f)
        * Weave.PowerMultiplier(IsCorrupted(spell));

    /// <summary>
    /// The blow a spell deals, stamped as one: <see cref="HitKind.Spell"/> (never parryable), carrying the
    /// charge a held cast reached, the spell's own poise damage when it authors one, and unblockable when
    /// the spell says so. The charge scales the damage through <paramref name="power"/> here; the defence
    /// side only reads the stamp, so nothing counts twice.
    /// </summary>
    private DamagePacket BuildPacket(SpellResource spell, float power, float charge)
    {
        (float amount, bool isCrit) = CombatMath.RollSpell(spell.BaseDamage, _stats);
        float poise = spell.PoiseDamage > 0f ? spell.PoiseDamage : SpellPoiseDamage;
        return new DamagePacket(
            amount * Empower(spell, power), spell.School, Entity, isCrit, poise,
            HitKind.Spell, charge, Unblockable: !spell.Blockable);
    }

    private void CastProjectile(SpellResource spell, int team, float power, float charge)
    {
        (Vector3 origin, Vector3 direction) = Aim();
        SpellProjectile projectile = _projectilePool?.Get() ?? new SpellProjectile { Released = ReturnProjectile };

        // Add to the tree (so its visual children build on first use) then position + arm it.
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = origin;
        projectile.Launch(spell, BuildPacket(spell, power, charge), Entity, team, direction);
    }

    private void CastArea(SpellResource spell, int team, float power, float charge)
    {
        if (Entity?.Body is not Node3D body)
        {
            return;
        }

        Vector3 center = body.GlobalPosition + (Vector3.Up * 1f);
        float radius = spell.ImpactRadius > 0f ? spell.ImpactRadius : DefaultNovaRadius;
        SpellResolver.Detonate(body, spell, BuildPacket(spell, power, charge), Entity, team, center, radius);
    }

    /// <summary>A wedge along the caster's aim (Phase 35C, dragon breath). Direction comes from the
    /// same <see cref="Aim"/> helper a projectile uses, so pitching the aim node — which is how a
    /// hovering dragon breathes downward — needs nothing here.</summary>
    private void CastCone(SpellResource spell, int team, float power, float charge)
    {
        if (Entity?.Body is not Node3D body)
        {
            return;
        }

        (Vector3 origin, Vector3 direction) = Aim();
        float length = spell.ImpactRadius > 0f ? spell.ImpactRadius : DefaultNovaRadius;
        SpellResolver.Sweep(
            body, spell, BuildPacket(spell, power, charge), Entity, team,
            origin, direction, length, spell.ConeAngleDegrees);
    }

    private void CastSelf(SpellResource spell, float power)
    {
        if (spell.BlinkDistance > 0f)
        {
            Blink(spell.BlinkDistance);
        }

        ApplySupport(Entity, spell, power);
    }

    // --- ground, barrier, dash ---------------------------------------------------------------------

    /// <summary>A spell that lands where the caster aims, after a telegraph of exactly its delay.</summary>
    private void CastGround(SpellResource spell, int team, float power, float charge)
    {
        if (Entity?.Body is not Node3D)
        {
            return;
        }

        var ground = new SpellGround
        {
            Spell = spell,
            Packet = BuildPacket(spell, power, charge),
            Caster = Entity,
            CasterTeam = team,
            Delay = spell.GroundDelay,
            Radius = spell.ImpactRadius > 0f ? spell.ImpactRadius : DefaultNovaRadius,
        };
        GetTree().CurrentScene.AddChild(ground);
        ground.GlobalPosition = PlacePoint(spell);
    }

    /// <summary>A standing wall square across the aim, at the aim point clamped to the place range.</summary>
    private void CastBarrier(SpellResource spell, int team, float power, float charge)
    {
        if (Entity?.Body is not Node3D body)
        {
            return;
        }

        Vector3 point = PlacePoint(spell);
        Vector3 across = point - body.GlobalPosition;
        across.Y = 0f;
        if (across.LengthSquared() < 0.01f)
        {
            across = -body.GlobalTransform.Basis.Z;
        }

        var barrier = new SpellBarrier
        {
            Spell = spell,
            Packet = BuildPacket(spell, power, charge),
            Caster = Entity,
            CasterTeam = team,
            Width = spell.BarrierWidth,
            Duration = spell.BarrierDuration,
        };
        GetTree().CurrentScene.AddChild(barrier);
        barrier.GlobalPosition = point;
        barrier.Face(across);
    }

    /// <summary>Moves the caster along the aim, stopping short of a wall, and strikes everything within
    /// <see cref="SpellResource.DashHitRadius"/> of the line once. The last foe struck is stunned.</summary>
    private void CastDash(SpellResource spell, int team, float power, float charge)
    {
        if (Entity?.Body is not CharacterBody3D body)
        {
            return;
        }

        Vector3 direction = HorizontalAim();
        float travel = TravelAlongAim(spell.DashDistance, DashWallMargin);
        Vector3 start = body.GlobalPosition;
        Vector3 end = start + (direction * travel);
        DamagePacket packet = BuildPacket(spell, power, charge);

        // Everything within reach of the line, once each, in the order the dash passes them.
        var struck = new List<(Hurtbox Box, float Along)>();
        var dedupe = new HitDedupe();
        PhysicsDirectSpaceState3D space = body.GetWorld3D().DirectSpaceState;
        float radius = Mathf.Max(0.3f, spell.DashHitRadius);
        int samples = Mathf.Max(1, Mathf.CeilToInt(travel / (radius * 0.75f)));
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = CombatLayers.Hurtbox,
        };
        for (int i = 0; i <= samples; i++)
        {
            float along = travel * i / samples;
            query.Transform = new Transform3D(Basis.Identity, start + (direction * along) + (Vector3.Up * 1f));
            foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, 32))
            {
                if (hit.TryGetValue("collider", out Variant v) && v.AsGodotObject() is Hurtbox box &&
                    SpellResolver.IsHostileTarget(box, Entity, team) &&
                    dedupe.TryHit(box.OwnerEntity, box))
                {
                    struck.Add((box, along));
                }
            }
        }

        // Presentation: a streak of flashes along the line the caster travelled.
        int puffs = Mathf.Max(1, Mathf.CeilToInt(travel / 2f));
        for (int i = 0; i <= puffs; i++)
        {
            SpellResolver.SpawnFlashAt(
                body, start + (direction * (travel * i / puffs)) + (Vector3.Up * 1f), 0.9f, SpellSchools.Color(spell.School));
        }

        body.GlobalPosition = end;
        body.Velocity = Vector3.Zero;

        Hurtbox? last = null;
        struck.Sort((a, b) => a.Along.CompareTo(b.Along));
        foreach ((Hurtbox box, float _) in struck)
        {
            if (SpellResolver.HitOne(body, box, packet, spell, Entity, team) == SpellHitResult.Landed)
            {
                last = box;
            }
        }

        if (last?.OwnerEntity != null)
        {
            last.OwnerEntity.GetComponent<StatusEffectsComponent>()?
                .Apply(StatusEffectDatabase.Get(StunnedId), Entity);
        }
    }

    /// <summary>Where a ground or barrier spell lands: the aim point, clamped to the spell's place range
    /// and dropped onto the ground. The player aims with the camera ray; an actor with no aim node
    /// (an enemy, a companion) places it on the target its action component was told about.</summary>
    private Vector3 PlacePoint(SpellResource spell)
    {
        Node3D? body = Entity?.Body;
        if (body == null)
        {
            return Vector3.Zero;
        }

        Vector3 origin = body.GlobalPosition;
        Vector3 target;
        if (AimNode == null && _actions?.AimPoint is { } told)
        {
            target = told;
        }
        else
        {
            (Vector3 muzzle, Vector3 forward) = Aim();
            float reach = spell.PlaceRange + 6f;
            Vector3 end = muzzle + (forward * reach);
            target = _worldRay?.FirstSolid(body.GetWorld3D().DirectSpaceState, muzzle, end)?.Point ?? end;
        }

        Vector3 flat = target - origin;
        flat.Y = 0f;
        float distance = SpellRules.ClampPlaceDistance(flat.Length(), spell.PlaceRange);
        Vector3 dir = flat.LengthSquared() < 1e-4f ? -body.GlobalTransform.Basis.Z : flat.Normalized();
        Vector3 point = origin + (dir * distance);

        // Down onto the ground from above the point, so a spell aimed at the sky still lands on the floor.
        Vector3 from = point + (Vector3.Up * 4f);
        Vector3 to = point + (Vector3.Down * 8f);
        if (_worldRay?.FirstSolid(body.GetWorld3D().DirectSpaceState, from, to) is { } ground)
        {
            point.Y = ground.Point.Y;
        }
        else
        {
            point.Y = origin.Y;
        }

        return point;
    }

    /// <summary>The aim, flattened: the direction a blink or dash travels.</summary>
    private Vector3 HorizontalAim()
    {
        (_, Vector3 forward) = Aim();
        forward.Y = 0f;
        return forward.LengthSquared() < 1e-4f ? Vector3.Forward : forward.Normalized();
    }

    /// <summary>Metres the caster can travel along its horizontal aim before a wall, up to
    /// <paramref name="wanted"/>, stopping <paramref name="margin"/> short of the wall.</summary>
    private float TravelAlongAim(float wanted, float margin)
    {
        if (Entity?.Body is not Node3D body || _worldRay == null)
        {
            return wanted;
        }

        Vector3 from = body.GlobalPosition + (Vector3.Up * 1f);
        Vector3 to = from + (HorizontalAim() * wanted);
        float hit = _worldRay.FirstSolid(body.GetWorld3D().DirectSpaceState, from, to) is { } solid
            ? from.DistanceTo(solid.Point)
            : -1f;
        return SpellRules.TravelDistance(wanted, hit, margin);
    }

    /// <summary>Teleports the caster up to <paramref name="distance"/> metres along their aim (Phase 29.5G —
    /// Blink), stopping short of world geometry (people are not walls). ponytail: straight horizontal
    /// hop (keeps feet height); no ledge/step handling — fine for a flat-ish dodge.</summary>
    private void Blink(float distance)
    {
        if (Entity?.Body is not CharacterBody3D body)
        {
            return;
        }

        Vector3 direction = HorizontalAim();
        float travel = TravelAlongAim(distance, WallMargin);
        Color colour = SpellSchools.Color(Selected?.School ?? DamageType.Arcane);
        SpellResolver.SpawnFlashAt(body, body.GlobalPosition + (Vector3.Up * 1f), 0.9f, colour);
        body.GlobalPosition += direction * travel;
        body.Velocity = Vector3.Zero;
        SpellResolver.SpawnFlashAt(body, body.GlobalPosition + (Vector3.Up * 1f), 0.9f, colour);
    }

    /// <summary>Applies a Self-delivery spell's heal and/or beneficial status to <paramref name="target"/>
    /// (the caster for a normal Self cast; an ally for an enemy support caster, Phase 29.5F).</summary>
    private void ApplySupport(IEntity? target, SpellResource spell, float power)
    {
        if (target == null)
        {
            return;
        }

        if (spell.Healing > 0f)
        {
            target.GetComponent<StatsComponent>()?.Heal(spell.Healing * Empower(spell, power));
        }

        if (spell.HasStatusEffect)
        {
            target.GetComponent<StatusEffectsComponent>()?
                .Apply(StatusEffectDatabase.Get(spell.StatusEffectId), Entity);
        }
    }

    /// <summary>Selects a known spell by id and casts it. The lever enemy AI uses to choose a spell (the
    /// player cycles + casts); a no-op if the spell isn't known or isn't ready. A retired id resolves to
    /// its replacement. Reuses the full <see cref="TryCast"/> path — no parallel casting logic (29.5F).</summary>
    public bool TryCastById(string spellId)
    {
        string id = SpellAliases.Resolve(spellId);
        int idx = _spells.FindIndex(s => s.Id == id);
        if (idx < 0)
        {
            return false;
        }

        _selected = idx;
        return TryCast();
    }

    /// <summary>Selects a known spell by id and <see cref="BeginCast"/>s it — the lever an AI needs
    /// for a <em>charged or channeled</em> spell, which <see cref="TryCastById"/> cannot start
    /// (Phase 35C, dragon breath). The caller then drives <see cref="UpdateCast"/> and
    /// <see cref="EndCast"/> the way the player's held key does. Returns false if the spell is not
    /// known or a cast is already active.</summary>
    public bool BeginCastById(string spellId)
    {
        if (_activeCast != null)
        {
            return false;
        }

        string id = SpellAliases.Resolve(spellId);
        int idx = _spells.FindIndex(s => s.Id == id);
        if (idx < 0)
        {
            return false;
        }

        _selected = idx;
        BeginCast();
        return true;
    }

    /// <summary>Casts a Self-delivery support spell (heal/ward) onto an <em>ally</em> rather than the
    /// caster — the enemy support caster's "heal/buff allies" (Phase 29.5F). Spends mana + cooldown
    /// through the same gate as any cast.</summary>
    public bool TryCastSupportOn(IEntity ally, SpellResource spell)
    {
        if (!_spells.Contains(spell) || spell.Delivery != SpellDelivery.Self || !CanCast(spell))
        {
            return false;
        }

        _stats!.ModifyCurrent(StatType.Mana, -EffectiveManaCost(spell));
        _cooldowns[spell.Id] = spell.Cooldown;
        ApplySupport(ally, spell, 1f);
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new SpellCastEvent(Entity, spell.Id));
        }

        return true;
    }

    private (Vector3 Origin, Vector3 Direction) Aim()
    {
        Node3D? node = AimNode ?? Entity?.Body;
        if (node == null)
        {
            return (Vector3.Zero, Vector3.Forward);
        }

        Vector3 forward = (-node.GlobalTransform.Basis.Z).Normalized();
        return (node.GlobalPosition + (forward * MuzzleOffset), forward);
    }

    /// <summary>Rebuilds the spellbook from <see cref="KnownSpellIds"/>. A retired id resolves to its
    /// replacement (<see cref="SpellDatabase.Get"/>) and the list is kept free of duplicates, so an old
    /// save that names both the retired id and its replacement knows the spell once.</summary>
    private void RebuildSpells()
    {
        _spells.Clear();
        foreach (string id in KnownSpellIds)
        {
            if (SpellDatabase.Get(id) is { } spell && !_spells.Exists(s => s.Id == spell.Id))
            {
                _spells.Add(spell);
            }
        }

        if (_selected >= _spells.Count)
        {
            _selected = 0;
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var ids = new Godot.Collections.Array();
        foreach (SpellResource spell in _spells)
        {
            ids.Add(spell.Id);
        }

        var ranks = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, int> pair in _ranks)
        {
            ranks[pair.Key] = pair.Value;
        }

        var cooldowns = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, double> pair in _cooldowns)
        {
            cooldowns[pair.Key] = pair.Value;
        }

        return new Godot.Collections.Dictionary
        {
            ["spells"] = ids,
            ["selected"] = _selected,
            ["ranks"] = ranks,
            ["cooldowns"] = cooldowns,
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Live combat state belongs to the timeline being abandoned. Cooldowns are replaced by what the
        // save holds (none, for a save that predates them), and loading while holding a charge, a channel
        // or a wind-up must not leave any of it alive: a stale _activeCast blocks every later BeginCast
        // and keeps IsChanneling true for a cast that no longer exists.
        _cooldowns.Clear();
        _activeCast = null;
        _chargeElapsed = 0f;
        _pending = null;
        _pendingMana = 0f;
        DropChannelSlow();

        if (data.TryGetValue("spells", out Variant spellsVar))
        {
            KnownSpellIds = new Godot.Collections.Array<string>();
            foreach (Variant entry in spellsVar.AsGodotArray())
            {
                // A retired id maps to its replacement rather than being dropped.
                string id = SpellAliases.Resolve(entry.AsString());
                if (!KnownSpellIds.Contains(id))
                {
                    KnownSpellIds.Add(id);
                }
            }

            RebuildSpells();
        }

        _ranks.Clear();
        if (data.TryGetValue("ranks", out Variant ranksVar))
        {
            Godot.Collections.Dictionary ranks = ranksVar.AsGodotDictionary();
            foreach (Variant key in ranks.Keys)
            {
                string id = SpellAliases.Resolve(key.AsString());
                int rank = ranks[key].AsInt32();
                _ranks[id] = _ranks.TryGetValue(id, out int existing) ? Mathf.Max(existing, rank) : rank;
            }
        }

        if (data.TryGetValue("cooldowns", out Variant cooldownsVar))
        {
            Godot.Collections.Dictionary cooldowns = cooldownsVar.AsGodotDictionary();
            foreach (Variant key in cooldowns.Keys)
            {
                string id = SpellAliases.Resolve(key.AsString());
                double remaining = cooldowns[key].AsDouble();
                if (remaining > 0d)
                {
                    _cooldowns[id] = _cooldowns.TryGetValue(id, out double existing) ? Mathf.Max(existing, remaining) : remaining;
                }
            }
        }

        if (data.TryGetValue("selected", out Variant selectedVar))
        {
            _selected = Mathf.Clamp(selectedVar.AsInt32(), 0, Mathf.Max(0, _spells.Count - 1));
        }
    }
}
