using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic.Vfx;
using Embervale.Stats;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// Holds the timed status effects currently afflicting (or buffing) an entity and
/// ticks them each frame: damage-over-time is applied through the
/// <see cref="StatsComponent"/> (credited to the effect's source so DoT kills still
/// attribute), and stat modifiers (slows, wards) are pushed/pulled as
/// <see cref="StatModifier"/>s sourced to the effect instance. Re-applying an effect
/// refreshes its duration, or adds a stack up to the definition's cap.
///
/// The magic upgrade puts the rules here, all driven by <see cref="StatusEffectResource"/> fields:
/// stack detonation (Kindle), spread on death (Stinging Swarm), a mark amplifying and a ward absorbing
/// incoming damage (<see cref="ModifyIncoming"/>), dispel and cleanse, and diminishing returns on
/// controls so nothing is chain-locked. Enemies and bosses run exactly the same rules.
///
/// Effects are transient combat state — like poise/stagger they are intentionally
/// <em>not</em> persisted; a freshly loaded entity simply starts clean.
/// </summary>
[GlobalClass]
public partial class StatusEffectsComponent : EntityComponent
{
    /// <summary>How many times a status may jump on death before it dies with its bearer.</summary>
    public const int MaxSpreadJumps = 3;

    private const float DetonatePoise = 6f;

    private enum Ending
    {
        Expired,
        Consumed,
        Dispelled,
        Broken,
    }

    private readonly Dictionary<string, StatusEffect> _active = new();
    private readonly List<(StatusControl Mask, double Remaining)> _immunities = new();

    private StatsComponent? _stats;
    private CombatComponent? _combat;

    /// <summary>The tick's snapshot of the active set, reused every frame. Only <see cref="_Process"/>
    /// touches it and a node's tick never re-enters itself, so one buffer is enough; the damage
    /// path keeps its own per-call snapshot because that one can re-enter.</summary>
    private readonly List<StatusEffect> _tickSnapshot = new();

    /// <summary>True while <see cref="_Process"/> is switched off: no status and no control
    /// immunity is running, so there is nothing to count down. Adding either one wakes it.</summary>
    private bool _tickAsleep;

    private void WakeTick()
    {
        if (_tickAsleep)
        {
            _tickAsleep = false;
            SetProcess(true);
        }
    }

    /// <summary>The effects currently active on this entity (read-only, for UI).</summary>
    public Dictionary<string, StatusEffect>.ValueCollection ActiveEffects => _active.Values;

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Subscribe<GameLoadingEvent>(OnGameLoading);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Unsubscribe<GameLoadingEvent>(OnGameLoading);
        ClearAll();
    }

    /// <summary>Applies (or refreshes / stacks) a status effect from its definition. Returns false when
    /// it was refused: the bearer is dead, or is still immune to the control it carries (a
    /// <see cref="StatusResistedEvent"/> is raised for the latter).</summary>
    public bool Apply(StatusEffectResource? definition, IEntity? source) => Apply(definition, source, 1f);

    /// <summary>Applies a charge-scaled lifetime without modifying the shared resource.</summary>
    public bool Apply(StatusEffectResource? definition, IEntity? source, float durationMultiplier) =>
        ApplyWithSpread(definition, source, 0, durationMultiplier);

    private bool ApplyWithSpread(StatusEffectResource? definition, IEntity? source, int spreadGeneration,
        float durationMultiplier)
    {
        if (definition == null || Entity == null || _stats is { IsAlive: false })
        {
            return false;
        }

        StatusControl hard = StatusMath.HardOf(definition.Controls);
        if (_active.TryGetValue(definition.Id, out StatusEffect? existing))
        {
            // A live control is never extended by re-applying it, so it cannot be held on forever.
            if (hard != StatusControl.None)
            {
                return true;
            }

            existing.AddStack(durationMultiplier);
            if (definition.AbsorbAmount > 0f)
            {
                existing.AbsorbCapacity = WardCapacity(definition, source);
                existing.AbsorbRemaining = existing.AbsorbCapacity;
            }

            if (StatusMath.ShouldDetonate(existing.Stacks, definition.DetonateAtStacks))
            {
                Detonate(existing);
            }

            return true;
        }

        if (StatusMath.IsControlRefused(hard, ImmuneControls()))
        {
            EventBus.Instance?.Publish(new StatusResistedEvent(Entity, definition.Id));
            return false;
        }

        var effect = new StatusEffect(definition, source, durationMultiplier) { SpreadGeneration = spreadGeneration };
        if (definition.AbsorbAmount > 0f)
        {
            effect.AbsorbCapacity = WardCapacity(definition, source);
            effect.AbsorbRemaining = effect.AbsorbCapacity;
        }

        _active[definition.Id] = effect;
        WakeTick();
        ApplyModifier(effect);
        EventBus.Instance?.Publish(new StatusEffectAppliedEvent(Entity, definition.Id, source));

        if ((hard & (StatusControl.Root | StatusControl.Stun)) != 0)
        {
            Entity.GetComponent<DodgeComponent>()?.CancelForControl();
        }

        // A stun holds the body the way a stagger does, so actions and casts are refused by the code
        // that already respects a stagger. No punish window: a freeze is not a poise break.
        if ((hard & StatusControl.Stun) != 0)
        {
            _combat?.NotifyStatusStun();
        }

        if (StatusMath.ShouldDetonate(effect.Stacks, definition.DetonateAtStacks))
        {
            Detonate(effect);
        }

        return true;
    }

    public bool Has(string effectId) => _active.ContainsKey(effectId);

    // --- the read API other systems use. Everything that asks "can this actor cast / move" asks here. ---

    /// <summary>Every control the active statuses put on this entity, OR-ed together.</summary>
    public StatusControl Controls
    {
        get
        {
            StatusControl all = StatusControl.None;
            foreach (StatusEffect effect in _active.Values)
            {
                all |= effect.Definition.Controls;
            }

            return all;
        }
    }

    public bool IsSilenced => (Controls & StatusControl.Silence) != 0;

    public bool IsRooted => (Controls & StatusControl.Root) != 0;

    public bool IsStunned => (Controls & StatusControl.Stun) != 0;

    /// <summary>The longest remaining stun owned by an active status. Removing it releases only this
    /// contribution; ordinary combat stagger keeps its own timer.</summary>
    public float StunRemaining
    {
        get
        {
            double remaining = 0d;
            foreach (StatusEffect effect in _active.Values)
            {
                if ((effect.Definition.Controls & StatusControl.Stun) != 0)
                {
                    remaining = System.Math.Max(remaining, effect.Remaining);
                }
            }

            return (float)remaining;
        }
    }

    /// <summary>Current stack count of an effect, 0 when absent.</summary>
    public int StacksOf(string effectId) =>
        _active.TryGetValue(effectId, out StatusEffect? effect) ? effect.Stacks : 0;

    /// <summary>Fixed damage the bearer's wards can still absorb (0 with no ward).</summary>
    public float WardRemaining
    {
        get
        {
            float total = 0f;
            foreach (StatusEffect effect in _active.Values)
            {
                total += effect.AbsorbRemaining;
            }

            return total;
        }
    }

    /// <summary>Seconds of immunity left against a control the bearer just shook off; 0 when none.</summary>
    public double ImmunityRemaining(StatusControl control)
    {
        double best = 0d;
        foreach ((StatusControl mask, double remaining) in _immunities)
        {
            if ((mask & control) != 0 && remaining > best)
            {
                best = remaining;
            }
        }

        return best;
    }

    // --- spell-facing hooks: stable, called by the casting code ---

    /// <summary>Strips an effect outright (combos "consume" the status they trigger off, a spell eats the
    /// stacks it spends). Returns the stacks it had, 0 if absent. No dispel event: nobody stripped it.</summary>
    public int Consume(string effectId)
    {
        if (!_active.TryGetValue(effectId, out StatusEffect? effect))
        {
            return 0;
        }

        int stacks = effect.Stacks;
        Remove(effectId, Ending.Consumed, null);
        return stacks;
    }

    /// <summary>Null Lance: strips the longest-lasting dispellable beneficial status. Returns its id, or
    /// null when there was nothing to take. Raises <see cref="StatusDispelledEvent"/>.</summary>
    public string? Dispel(IEntity? source)
    {
        var candidates = new List<(string Id, bool IsBeneficial, double Remaining)>();
        foreach (StatusEffect effect in _active.Values)
        {
            if (effect.Definition.Dispellable)
            {
                candidates.Add((effect.Definition.Id, effect.Definition.IsBeneficial, effect.Remaining));
            }
        }

        if (StatusMath.PickDispel(candidates) is not { } id)
        {
            return null;
        }

        Remove(id, Ending.Dispelled, source);
        return id;
    }

    /// <summary>Knit Bone: removes one named status (harmful or not) unless it is a deep curse. Returns the
    /// stacks it carried so the caller can heal for each (<see cref="StatusMath.StackBonusMultiplier"/>).
    /// Raises <see cref="StatusDispelledEvent"/>.</summary>
    public int Cleanse(string effectId, IEntity? source)
    {
        if (!_active.TryGetValue(effectId, out StatusEffect? effect) || !effect.Definition.Dispellable)
        {
            return 0;
        }

        int stacks = effect.Stacks;
        Remove(effectId, Ending.Dispelled, source);
        return stacks;
    }

    /// <summary>
    /// Runs incoming damage through the bearer's statuses: marks amplify it, flat reductions trim it and
    /// wards absorb a fixed amount before they break with a flash. Called by <c>CombatComponent</c> just
    /// before health is touched (and by the DoT tick below), so every route to health agrees.
    /// </summary>
    public float ModifyIncoming(float amount, IEntity? source)
    {
        if (amount <= 0f || _active.Count == 0)
        {
            return amount;
        }

        float amplify = 0f;
        float reduce = 0f;
        foreach (StatusEffect effect in _active.Values)
        {
            float mod = effect.Definition.DamageTakenModifier;
            if (mod > 0f)
            {
                amplify += mod;
            }
            else if (mod < 0f && effect.Definition.AbsorbAmount <= 0f)
            {
                reduce -= mod;
            }
        }

        float result = StatusMath.Reduce(StatusMath.Amplify(amount, amplify), reduce);

        // Each invocation owns its snapshot: ward-break events and DoT death can re-enter status code.
        foreach (StatusEffect ward in new List<StatusEffect>(_active.Values))
        {
            if (!_active.TryGetValue(ward.Definition.Id, out StatusEffect? live) || !ReferenceEquals(live, ward))
            {
                continue;
            }

            StatusEffectResource def = ward.Definition;
            if (def.AbsorbAmount <= 0f || def.DamageTakenModifier >= 0f || ward.AbsorbRemaining <= 0f)
            {
                continue;
            }

            float before = ward.AbsorbRemaining;
            (result, ward.AbsorbRemaining) = StatusMath.Absorb(result, -def.DamageTakenModifier, ward.AbsorbRemaining);
            if (ward.AbsorbRemaining <= 0f)
            {
                BreakWard(ward);
            }
            else if (ward.AbsorbRemaining < before && Entity?.Body is { } body && IsInstanceValid(body) &&
                     body.IsInsideTree())
            {
                // The ward took some of it and holds.
                SpellVfx.StatusProc(SpellProcKind.WardHit, def.School, Entity, body.GlobalPosition, 0f);
            }
        }

        return result;
    }

    public override void _Process(double delta)
    {
        TickImmunities(delta);

        if (_active.Count == 0)
        {
            // Nothing afflicts this actor and nothing is counting down: the resting state of nearly
            // every body in the world. Stop being called until a status or an immunity lands.
            if (_immunities.Count == 0)
            {
                _tickAsleep = true;
                SetProcess(false);
            }

            return;
        }

        // A corpse keeps no afflictions: clear everything so modifiers don't linger.
        if (_stats is { IsAlive: false })
        {
            ClearAll();
            return;
        }

        // Snapshot: a tick can break a ward, detonate or spread, all of which edit the live set.
        // Copied into a reused buffer rather than a fresh list, which used to be one allocation per
        // afflicted actor per frame for as long as anything was active on it.
        _tickSnapshot.Clear();
        _tickSnapshot.AddRange(_active.Values);
        for (int i = 0; i < _tickSnapshot.Count; i++)
        {
            StatusEffect effect = _tickSnapshot[i];
            if (!_active.TryGetValue(effect.Definition.Id, out StatusEffect? live) || !ReferenceEquals(live, effect))
            {
                continue;
            }

            Tick(effect, delta);
            if (effect.Remaining <= 0d && _active.TryGetValue(effect.Definition.Id, out live) && ReferenceEquals(live, effect))
            {
                Remove(effect.Definition.Id, Ending.Expired, null);
            }
        }

        _tickSnapshot.Clear();
    }

    private void Tick(StatusEffect effect, double delta)
    {
        double elapsed = System.Math.Min(System.Math.Max(0d, delta), System.Math.Max(0d, effect.Remaining));
        effect.Remaining -= System.Math.Max(0d, delta);

        StatusEffectResource def = effect.Definition;
        if (!def.HasTickEffect)
        {
            return;
        }

        (int ticks, double newTimer) = StatusMath.AdvanceDot(effect.TickTimer, elapsed, def.TickInterval);
        effect.TickTimer = newTimer;
        for (int i = 0; i < ticks; i++)
        {
            if (!IsActive(effect) || _stats is { IsAlive: false })
            {
                break;
            }

            if (def.HasDamageOverTime)
            {
                float hit = ModifyIncoming(def.DamagePerTick * effect.Stacks, effect.Source);
                if (!IsActive(effect))
                {
                    break;
                }
                _stats?.ApplyDamage(hit, effect.Source);
            }

            // Death/load callbacks may remove this effect and immediately revive the bearer.
            // Its abandoned timeline cannot heal or keep damaging the now-live actor.
            if (!IsActive(effect) || _stats is { IsAlive: false })
            {
                break;
            }

            if (def.HasHealOverTime)
            {
                _stats?.Heal(def.HealPerTick);
            }
        }
    }

    private bool IsActive(StatusEffect effect) =>
        _active.TryGetValue(effect.Definition.Id, out StatusEffect? live) && ReferenceEquals(live, effect);

    // --- controls: diminishing returns ---

    private StatusControl ImmuneControls()
    {
        StatusControl all = StatusControl.None;
        foreach ((StatusControl mask, double _) in _immunities)
        {
            all |= mask;
        }

        return all;
    }

    private void TickImmunities(double delta)
    {
        for (int i = _immunities.Count - 1; i >= 0; i--)
        {
            (StatusControl mask, double remaining) = _immunities[i];
            remaining -= delta;
            if (remaining <= 0d)
            {
                _immunities.RemoveAt(i);
            }
            else
            {
                _immunities[i] = (mask, remaining);
            }
        }
    }

    // --- wards ---

    private float WardCapacity(StatusEffectResource def, IEntity? source)
    {
        float power = source?.GetComponent<StatsComponent>()?.GetValue(StatType.SpellPower) ?? 0f;
        return StatusMath.WardCapacity(def.AbsorbAmount, power, def.AbsorbPerSpellPower);
    }

    private void BreakWard(StatusEffect ward)
    {
        if (Entity == null)
        {
            return;
        }

        DamageType school = ward.Definition.School;
        Remove(ward.Definition.Id, Ending.Broken, null);
        EventBus.Instance?.Publish(new WardBrokenEvent(Entity, ward.Definition.Id));

        // The flash a breaking ward makes.
        if (Entity?.Body is { } body && IsInstanceValid(body) && body.IsInsideTree())
        {
            SpellVfx.StatusProc(SpellProcKind.WardBreak, school, Entity, body.GlobalPosition, 0f);
        }
    }

    // --- detonation and death ---

    private void Detonate(StatusEffect effect)
    {
        StatusEffectResource def = effect.Definition;
        if (Entity == null)
        {
            return;
        }

        float damage = StatusMath.DetonateDamage(effect.Stacks, def.DetonateDamagePerStack);
        Vector3 centre = Entity.Body.GlobalPosition;
        IEntity? source = effect.Source;
        StatusEffectResource? ignite = string.IsNullOrEmpty(def.DetonateAppliesStatusId)
            ? null
            : StatusEffectDatabase.Get(def.DetonateAppliesStatusId);

        // Consumed first: a kill below must not find the status still on the corpse.
        Remove(def.Id, Ending.Consumed, null);

        var packet = new DamagePacket(damage, def.School, source, false, DetonatePoise, HitKind.Spell);
        bool killed;
        if (_combat != null)
        {
            killed = _combat.ReceiveDamage(packet).Killed;
        }
        else
        {
            killed = _stats is { IsAlive: true } && damage >= _stats.GetCurrent(StatType.Health);
            _stats?.ApplyDamage(damage, source);
        }

        if (!killed)
        {
            Apply(ignite, source);
        }

        if (def.DetonateRadius > 0f && source != null)
        {
            foreach (Hurtbox hurtbox in HostilesNear(centre, def.DetonateRadius, source, Entity))
            {
                if (!hurtbox.Receive(packet).Killed)
                {
                    hurtbox.OwnerEntity?.GetComponent<StatusEffectsComponent>()?.Apply(ignite, source);
                }
            }
        }

        EventBus.Instance?.Publish(new StatusDetonatedEvent(Entity, def.Id, damage, source));
        SpellVfx.StatusProc(SpellProcKind.Detonation, def.School, Entity, centre, def.DetonateRadius);
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (Entity == null)
        {
            return;
        }

        if (ReferenceEquals(e.Entity, Entity))
        {
            SpreadOnDeath(e.Killer);
            ClearAll();
        }
    }

    /// <summary>Stinging Swarm: when the bearer dies, each spreading status jumps to the nearest living
    /// hostile of its caster within its radius, preferring one it is not already on.</summary>
    private void SpreadOnDeath(IEntity? killer)
    {
        foreach (StatusEffect effect in new List<StatusEffect>(_active.Values))
        {
            StatusEffectResource def = effect.Definition;
            IEntity? source = effect.Source;
            if (def.SpreadOnDeathRadius <= 0f || !StatusMath.CanSpread(effect.SpreadGeneration, MaxSpreadJumps) ||
                source == null || Entity == null || !IsValid(source))
            {
                continue;
            }

            var candidates = new List<(float DistanceSquared, bool AlreadyHas)>();
            var owners = new List<StatusEffectsComponent>();
            Vector3 centre = Entity.Body.GlobalPosition;
            foreach (Hurtbox hurtbox in HostilesNear(centre, def.SpreadOnDeathRadius, source, Entity))
            {
                StatusEffectsComponent? status = hurtbox.OwnerEntity?.GetComponent<StatusEffectsComponent>();
                StatsComponent? stats = hurtbox.OwnerEntity?.GetComponent<StatsComponent>();
                if (status == null || stats is { IsAlive: false })
                {
                    continue;
                }

                owners.Add(status);
                candidates.Add((hurtbox.GlobalPosition.DistanceSquaredTo(centre), status.Has(def.Id)));
            }

            int pick = StatusMath.PickSpreadTarget(candidates);
            if (pick >= 0)
            {
                if (owners[pick].Entity?.Body is { } next && IsInstanceValid(next) && next.IsInsideTree())
                {
                    SpellVfx.Arc(def.School, source, centre + Vector3.Up, next.GlobalPosition + Vector3.Up,
                        SpellArcKind.Spread);
                }

                owners[pick].ApplyWithSpread(def, source, effect.SpreadGeneration + 1, effect.DurationMultiplier);
            }
        }
    }

    private static bool IsValid(IEntity entity) => GodotObject.IsInstanceValid(entity.Body);

    /// <summary>Living hostile actors of <paramref name="source"/> within <paramref name="radius"/>, one
    /// hurtbox each, never <paramref name="exclude"/>.</summary>
    private static List<Hurtbox> HostilesNear(Vector3 centre, float radius, IEntity source, IEntity exclude)
    {
        var found = new List<Hurtbox>();
        if (!IsValid(source) || !exclude.Body.IsInsideTree())
        {
            return found;
        }

        int team = source.GetComponent<CombatComponent>()?.Team ?? 0;
        PhysicsDirectSpaceState3D space = exclude.Body.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, centre + new Vector3(0f, 1f, 0f)),
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = CombatLayers.Hurtbox,
        };

        var struck = new HitDedupe();
        struck.TryHit(exclude, exclude);
        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, 32))
        {
            if (hit.TryGetValue("collider", out Variant colliderVar) &&
                colliderVar.AsGodotObject() is Hurtbox hurtbox &&
                hurtbox.OwnerEntity is { } owner &&
                owner.GetComponent<StatsComponent>() is not { IsAlive: false } &&
                SpellResolver.IsHostileTarget(hurtbox, source, team) &&
                struck.TryHit(owner, hurtbox))
            {
                found.Add(hurtbox);
            }
        }

        return found;
    }

    // --- removal ---

    private void Remove(string effectId, Ending ending, IEntity? by)
    {
        if (!_active.TryGetValue(effectId, out StatusEffect? effect))
        {
            return;
        }

        RemoveModifier(effect);
        _active.Remove(effectId);
        if (Entity == null)
        {
            return;
        }

        StatusEffectResource def = effect.Definition;
        StatusControl hard = StatusMath.HardOf(def.Controls);
        if (hard != StatusControl.None && def.ControlImmunitySeconds > 0f)
        {
            _immunities.Add((hard, def.ControlImmunitySeconds));
            WakeTick();
        }

        if (ending == Ending.Expired && def.ExpiryManaReturn > 0f && effect.AbsorbCapacity > 0f)
        {
            float mana = StatusMath.WardManaReturn(def.ExpiryManaReturn, effect.AbsorbRemaining, effect.AbsorbCapacity);
            IEntity? caster = effect.Source;
            if (mana > 0f && caster != null && IsValid(caster))
            {
                caster.GetComponent<StatsComponent>()?.ModifyCurrent(StatType.Mana, mana);
            }
        }

        EventBus.Instance?.Publish(new StatusEffectRemovedEvent(Entity, effectId));
        if (ending == Ending.Dispelled)
        {
            EventBus.Instance?.Publish(new StatusDispelledEvent(Entity, effectId, by));
        }
    }

    private void ClearAll()
    {
        var removed = new List<StatusEffect>(_active.Values);
        _active.Clear();
        _immunities.Clear();
        foreach (StatusEffect effect in removed)
        {
            RemoveModifier(effect);
            if (Entity != null)
            {
                EventBus.Instance?.Publish(new StatusEffectRemovedEvent(Entity, effect.Definition.Id));
            }
        }
    }

    // Before saveables restore: strip effect-owned modifiers against the old stats, including on a
    // load that later fails. A quickload must not carry any state from the abandoned combat timeline.
    private void OnGameLoading(GameLoadingEvent e) => ClearAll();

    private void ApplyModifier(StatusEffect effect)
    {
        StatusEffectResource def = effect.Definition;
        if (_stats == null || !def.HasStatModifier)
        {
            return;
        }

        _stats.GetStat(def.ModStat).AddModifier(new StatModifier(def.ModValue, def.ModType, effect));
    }

    private void RemoveModifier(StatusEffect effect)
    {
        StatusEffectResource def = effect.Definition;
        if (_stats == null || !def.HasStatModifier)
        {
            return;
        }

        _stats.GetStat(def.ModStat).RemoveModifiersFromSource(effect);
    }
}
