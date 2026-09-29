using System.Collections.Generic;
using Embervale.Combat.Actions;
using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Combat impact feedback, and the one place a blow is named. It listens to the raw combat events of
/// a resolution (<see cref="DamageDealtEvent"/>, <see cref="EntityParriedEvent"/>,
/// <see cref="GuardBrokenEvent"/>, <see cref="CriticalHitEvent"/>, <see cref="EntityStaggeredEvent"/>,
/// plus the attacker's last released action and charge for the weight of the blow), gathers them per
/// target, and at the end of the frame — once every event of that resolution has arrived, in whatever
/// order — publishes a single <see cref="HitConfirmedEvent"/> that hit-stop, the screen flash, floating
/// numbers, the mesh lurch and the damage-direction arcs all read. It then spawns the spark and
/// publishes the positional sound for that outcome (<see cref="CombatFx"/>).
///
/// <para>Presentation only: nothing here feeds back into a rule. Owns the effect pool for its
/// lifetime.</para>
/// </summary>
public partial class CombatFeedbackDirector : Node
{
    /// <summary>How long an attacker's last released action or charge still describes its blow.</summary>
    private const ulong KindMemoryMs = 2500;

    private sealed class Pending
    {
        public IEntity Target = null!;
        public IEntity? Source;
        public float Amount;
        public DamageType Type;
        public bool HasDamage;
        public bool Crit;
        public bool Blocked;
        public bool GuardBroken;
        public bool Staggered;
        public bool Parried;
        public HitKind Declared;
        public float SpellWeight = -1f;
    }

    private readonly Dictionary<ulong, Pending> _pending = new();
    private readonly Dictionary<ulong, (ActionKind Kind, ulong At)> _lastAction = new();
    private readonly Dictionary<ulong, (float Charge, ulong At)> _charge = new();
    private readonly HashSet<ulong> _parriedAttackers = new();
    private NodePool<ImpactEffect> _pool = null!;
    private bool _flushQueued;

    public override void _Ready()
    {
        _pool = new NodePool<ImpactEffect>(() => new ImpactEffect { Released = Reclaim }, prewarm: 6);
        EventBus? bus = EventBus.Instance;
        bus?.Subscribe<DamageDealtEvent>(OnDamage);
        bus?.Subscribe<EntityStaggeredEvent>(OnStaggered);
        bus?.Subscribe<EntityParriedEvent>(OnParried);
        bus?.Subscribe<GuardBrokenEvent>(OnGuardBroken);
        bus?.Subscribe<CriticalHitEvent>(OnCritical);
        bus?.Subscribe<ActionReleasedEvent>(OnReleased);
        bus?.Subscribe<ChargeReleasedEvent>(OnCharge);
        bus?.Subscribe<Magic.SpellHitEvent>(OnSpellHit);
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Unsubscribe<DamageDealtEvent>(OnDamage);
        bus?.Unsubscribe<EntityStaggeredEvent>(OnStaggered);
        bus?.Unsubscribe<EntityParriedEvent>(OnParried);
        bus?.Unsubscribe<GuardBrokenEvent>(OnGuardBroken);
        bus?.Unsubscribe<CriticalHitEvent>(OnCritical);
        bus?.Unsubscribe<ActionReleasedEvent>(OnReleased);
        bus?.Unsubscribe<ChargeReleasedEvent>(OnCharge);
        bus?.Unsubscribe<Magic.SpellHitEvent>(OnSpellHit);
        _pending.Clear();
        _pool?.Clear();
    }

    private void Reclaim(ImpactEffect effect) => _pool.Return(effect);

    private Pending Get(IEntity target)
    {
        if (!_pending.TryGetValue(target.RuntimeId, out Pending? pending))
        {
            pending = new Pending { Target = target };
            _pending[target.RuntimeId] = pending;
        }

        // One flush per frame, after every synchronous event of the resolution has been delivered.
        if (!_flushQueued && IsInsideTree())
        {
            _flushQueued = true;
            CallDeferred(MethodName.Flush);
        }

        return pending;
    }

    private void OnDamage(DamageDealtEvent e)
    {
        Pending p = Get(e.Target);
        p.Source ??= e.Source;
        p.Amount += e.Amount;
        p.Type = e.Type;
        p.HasDamage = true;
        p.Crit |= e.IsCrit;
        p.Blocked |= e.IsBlocked;
    }

    private void OnStaggered(EntityStaggeredEvent e) => Get(e.Entity).Staggered = true;

    private void OnParried(EntityParriedEvent e)
    {
        Pending p = Get(e.Defender);
        p.Parried = true;
        p.Source = e.Attacker ?? p.Source;
        if (e.Attacker != null)
        {
            _parriedAttackers.Add(e.Attacker.RuntimeId);
        }
    }

    private void OnGuardBroken(GuardBrokenEvent e)
    {
        Pending p = Get(e.Defender);
        p.GuardBroken = true;
        p.Source ??= e.Attacker;
    }

    private void OnCritical(CriticalHitEvent e)
    {
        Pending p = Get(e.Target);
        p.Crit = true;
        p.Declared = e.Kind;
        p.Source ??= e.Attacker;
    }

    /// <summary>A spell landed: the blow is a spell whatever the caster's last action was (a ground spell
    /// lands long after its cast), and it carries the spell's own <c>ImpactWeight</c> for hit-stop and shake.</summary>
    private void OnSpellHit(Magic.SpellHitEvent e)
    {
        Pending p = Get(e.Target);
        p.Declared = p.Declared == HitKind.Normal ? HitKind.Spell : p.Declared;
        p.SpellWeight = Magic.SpellDatabase.Get(e.SpellId)?.ImpactWeight ?? -1f;
        p.Source ??= e.Caster;
    }

    private void OnReleased(ActionReleasedEvent e) =>
        _lastAction[e.Actor.RuntimeId] = (e.Kind, Time.GetTicksMsec());

    private void OnCharge(ChargeReleasedEvent e) =>
        _charge[e.Attacker.RuntimeId] = (e.Charge, Time.GetTicksMsec());

    /// <summary>Resolves everything gathered this frame into confirmed hits. Public for the probe;
    /// the director calls it deferred.</summary>
    public void Flush()
    {
        _flushQueued = false;
        if (_pending.Count == 0)
        {
            _parriedAttackers.Clear();
            return;
        }

        var batch = new List<Pending>(_pending.Values);
        _pending.Clear();
        ulong now = Time.GetTicksMsec();

        foreach (Pending p in batch)
        {
            // A parried attacker's stagger is already the parry's own beat, not a second event.
            if (!p.HasDamage && p.Staggered && !p.Parried && _parriedAttackers.Contains(p.Target.RuntimeId))
            {
                continue;
            }

            if (p.Target is not Node targetNode || !GodotObject.IsInstanceValid(targetNode))
            {
                continue;
            }

            Confirm(p, now);
        }

        _parriedAttackers.Clear();
        Prune(now);
    }

    private void Confirm(Pending p, ulong now)
    {
        bool resisted = false;
        if (p.HasDamage && !p.Blocked && p.Type != DamageType.True && p.Amount > 0f &&
            p.Target.GetComponent<StatsComponent>() is { } stats)
        {
            float multiplier = CombatMath.ArmorMultiplier(stats.GetValue(CombatMath.ResistanceStat(p.Type)));
            resisted = HitOutcomes.IsResisted(multiplier);
        }

        var facts = new HitFacts(p.HasDamage, p.Crit, p.Blocked, p.GuardBroken, p.Staggered, p.Parried, resisted);
        HitOutcome outcome = HitOutcomes.Resolve(facts);

        ActionKind? lastAction = null;
        float charge = 0f;
        if (p.Source != null)
        {
            ulong id = p.Source.RuntimeId;
            if (_lastAction.TryGetValue(id, out var last) && now - last.At <= KindMemoryMs)
            {
                lastAction = last.Kind;
            }

            if (_charge.TryGetValue(id, out var charged) && now - charged.At <= KindMemoryMs)
            {
                charge = charged.Charge;
            }
        }

        HitKind kind = HitOutcomes.InferKind(p.Declared, lastAction, charge);

        Vector3 point = p.Target.Body.GlobalPosition + (Vector3.Up * 1.1f);
        if (outcome == HitOutcome.Parried && p.Source is { Body: { } attackerBody } &&
            GodotObject.IsInstanceValid(attackerBody))
        {
            point = (point + attackerBody.GlobalPosition + (Vector3.Up * 1.1f)) * 0.5f;
        }

        bool byPlayer = CombatPerspective.IsPlayer(p.Source);
        bool onPlayer = CombatPerspective.IsPlayer(p.Target);
        var hit = new HitConfirmedEvent(
            p.Source, p.Target, p.Amount, p.Type, outcome, kind, p.Staggered, point, byPlayer, onPlayer,
            p.SpellWeight >= 0f ? p.SpellWeight : 0f);
        EventBus.Instance?.Publish(hit);

        // A status tick with no attacker and no weight is not worth a spark or a sound.
        if (p.Source == null && p.Amount < HitStop.MinDamage && outcome == HitOutcome.Hit)
        {
            return;
        }

        SparkPlan spark = CombatFx.Spark(outcome, p.Amount);
        ImpactEffect effect = _pool.Get();
        (GetTree().CurrentScene ?? (Node)GetTree().Root).AddChild(effect);
        effect.GlobalPosition = point;
        effect.Launch(new Color(spark.R, spark.G, spark.B), spark.Scale, spark.Ring);

        CuePlan cue = CombatFx.Plan(outcome, kind);
        EventBus.Instance?.Publish(new SoundCueRequestedEvent(cue.CueId, point, cue.VolumeDb, cue.PitchScale));
    }

    private void Prune(ulong now)
    {
        if (_lastAction.Count > 64)
        {
            var stale = new List<ulong>();
            foreach (var kv in _lastAction)
            {
                if (now - kv.Value.At > KindMemoryMs)
                {
                    stale.Add(kv.Key);
                }
            }

            foreach (ulong id in stale)
            {
                _lastAction.Remove(id);
            }
        }

        if (_charge.Count > 64)
        {
            _charge.Clear();
        }
    }
}
