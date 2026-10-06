using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Stats;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Runs the <see cref="UniqueEffectResource"/>s the wearer's gear carries: each worn item's
/// <see cref="ItemResource.UniqueEffectId"/> and every effect an item-set threshold grants
/// (<see cref="EquipmentComponent.ActiveUniqueEffectIds"/>). The list is rebuilt from scratch on
/// every <see cref="EquipmentChangedEvent"/>, which a load also raises, so nothing here is saved.
///
/// <para><b>Every kind is a reaction to an event that already exists.</b> None of them reaches into
/// the damage pipeline, which means a "deals more damage" effect lands as a second, separate amount
/// right after the hit that triggered it rather than as a bigger first number. That follow-up goes
/// straight to health (the triggering hit was already mitigated) and is announced as its own
/// <see cref="DamageDealtEvent"/>, so damage numbers, hit feedback, aggro and kill credit all see it.
/// A follow-up never triggers another: <c>_dealing</c> guards the re-entry.</para>
///
/// <para>The subscriptions are held for the component's whole life and released in
/// <see cref="OnTeardown"/>; a handler with no active effect returns on its first line.</para>
/// </summary>
[GlobalClass]
public partial class UniqueEffectsComponent : EntityComponent
{
    private readonly List<UniqueEffectResource> _active = new();
    private readonly Dictionary<string, float> _cooldowns = new();
    private readonly List<string> _ready = new();

    private StatsComponent? _stats;
    private CombatComponent? _combat;
    private bool _dealing;
    private float _goldCarry;

    /// <summary>The effects currently in force, one per distinct id.</summary>
    public IReadOnlyList<UniqueEffectResource> Active => _active;

    /// <summary>The total <see cref="UniqueEffectKind.GoldFind"/> fraction in force, for any gold
    /// source that does not arrive as an <see cref="ItemPickedUpEvent"/>.</summary>
    public float GoldFindBonus => Total(UniqueEffectKind.GoldFind);

    public bool Has(UniqueEffectKind kind)
    {
        foreach (UniqueEffectResource effect in _active)
        {
            if (effect.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _combat = Entity.GetComponent<CombatComponent>();

        EventBus? bus = EventBus.Instance;
        bus?.Subscribe<EquipmentChangedEvent>(OnEquipmentChanged);
        bus?.Subscribe<DamageDealtEvent>(OnDamageDealt);
        bus?.Subscribe<EntityDamagedEvent>(OnEntityDamaged);
        bus?.Subscribe<EntityDiedEvent>(OnEntityDied);
        bus?.Subscribe<SpellHitEvent>(OnSpellHit);
        bus?.Subscribe<ActionReleasedEvent>(OnActionReleased);
        bus?.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
        bus?.Subscribe<GameLoadingEvent>(OnGameLoading);
        Rebuild();
    }

    protected override void OnTeardown()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Unsubscribe<EquipmentChangedEvent>(OnEquipmentChanged);
        bus?.Unsubscribe<DamageDealtEvent>(OnDamageDealt);
        bus?.Unsubscribe<EntityDamagedEvent>(OnEntityDamaged);
        bus?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        bus?.Unsubscribe<SpellHitEvent>(OnSpellHit);
        bus?.Unsubscribe<ActionReleasedEvent>(OnActionReleased);
        bus?.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
        bus?.Unsubscribe<GameLoadingEvent>(OnGameLoading);
        _active.Clear();
        _cooldowns.Clear();
    }

    public override void _Process(double delta)
    {
        if (_cooldowns.Count == 0)
        {
            return;
        }

        _ready.Clear();
        foreach (string id in new List<string>(_cooldowns.Keys))
        {
            float left = _cooldowns[id] - (float)delta;
            if (left <= 0f)
            {
                _ready.Add(id);
            }
            else
            {
                _cooldowns[id] = left;
            }
        }

        foreach (string id in _ready)
        {
            _cooldowns.Remove(id);
        }
    }

    private void OnEquipmentChanged(EquipmentChangedEvent e)
    {
        if (ReferenceEquals(e.Owner, Entity))
        {
            Rebuild();
        }
    }

    private void OnGameLoading(GameLoadingEvent _)
    {
        // Cooldowns and the coin fraction belong to the timeline being abandoned. The effect list
        // itself is rebuilt when the equipment restore raises its change event.
        _cooldowns.Clear();
        _goldCarry = 0f;
    }

    private void Rebuild()
    {
        _active.Clear();
        if (Entity?.GetComponent<EquipmentComponent>() is not { } equipment)
        {
            return;
        }

        var seen = new HashSet<string>();
        foreach (string id in equipment.ActiveUniqueEffectIds)
        {
            if (seen.Add(id) && UniqueEffectDatabase.Get(id) is { } effect)
            {
                _active.Add(effect);
            }
        }
    }

    // --- Dealing and taking hits --------------------------------------------------

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_dealing || _active.Count == 0 || Entity is not { } self || e.Amount <= 0f || _stats is not { IsAlive: true })
        {
            return;
        }

        bool dealt = ReferenceEquals(e.Source, self) && !ReferenceEquals(e.Target, self);
        bool taken = ReferenceEquals(e.Target, self) && e.Source != null && !ReferenceEquals(e.Source, self);
        if (!dealt && !taken)
        {
            return;
        }

        // Snapshot: a follow-up can kill, a kill can change what is worn (a drop auto-equips
        // nothing today, but the list is not ours to assume stable across a publish).
        foreach (UniqueEffectResource effect in new List<UniqueEffectResource>(_active))
        {
            if (dealt)
            {
                OnHitLanded(effect, e, self);
            }
            else
            {
                OnHitTaken(effect, e, self);
            }
        }
    }

    private void OnHitLanded(UniqueEffectResource effect, DamageDealtEvent e, IEntity self)
    {
        switch (effect.Kind)
        {
            case UniqueEffectKind.OnHitStatus:
                if (!e.IsBlocked && IsReady(effect) && UniqueEffectRules.Rolls(effect.Chance, GD.Randf())
                    && StatusEffectDatabase.Get(effect.StatusId) is { } status
                    && e.Target.GetComponent<StatusEffectsComponent>() is { } statuses)
                {
                    float duration = effect.DurationSeconds > 0f && status.Duration > 0f
                        ? effect.DurationSeconds / status.Duration
                        : 1f;
                    statuses.Apply(status, self, duration);
                    StartCooldown(effect, effect.CooldownSeconds);
                }

                break;

            case UniqueEffectKind.LowHealthPower:
                if (UniqueEffectRules.Below(_stats!.GetNormalized(StatType.Health), effect.Threshold))
                {
                    FollowUp(e.Target, UniqueEffectRules.BonusDamage(e.Amount, effect.Magnitude), e.Type);
                }

                break;

            case UniqueEffectKind.CritExecute:
                if (e.IsCrit && e.Target.GetComponent<StatsComponent>() is { IsAlive: true } theirs
                    && UniqueEffectRules.Below(
                        UniqueEffectRules.FractionBeforeHit(
                            theirs.GetCurrent(StatType.Health), e.Amount, theirs.GetMax(StatType.Health)),
                        effect.Threshold))
                {
                    FollowUp(e.Target, UniqueEffectRules.BonusDamage(e.Amount, effect.Magnitude), e.Type);
                }

                break;
        }
    }

    private void OnHitTaken(UniqueEffectResource effect, DamageDealtEvent e, IEntity self)
    {
        IEntity attacker = e.Source!;
        switch (effect.Kind)
        {
            case UniqueEffectKind.BlockReflect:
                if (e.IsBlocked)
                {
                    FollowUp(
                        attacker,
                        UniqueEffectRules.Reflected(e.Amount, _combat?.BlockMitigation ?? 0f, effect.Magnitude),
                        e.Type);
                }

                break;

            case UniqueEffectKind.ThornsFlat:
                if (IsInMeleeRange(self, attacker))
                {
                    FollowUp(attacker, effect.Magnitude, DamageType.Physical);
                }

                break;
        }
    }

    /// <summary>ManaShield. Reacts after the blow, so it gives back from mana what the blow took
    /// rather than stopping it: a blow that kills outright is not softened.</summary>
    private void OnEntityDamaged(EntityDamagedEvent e)
    {
        if (_active.Count == 0 || !ReferenceEquals(e.Entity, Entity) || e.RemainingHealth <= 0f || _stats == null)
        {
            return;
        }

        float fraction = Total(UniqueEffectKind.ManaShield);
        float paid = UniqueEffectRules.ManaShield(e.Amount, fraction, _stats.GetCurrent(StatType.Mana));
        if (paid <= 0f)
        {
            return;
        }

        // ModifyCurrent, not Heal: this is damage that never should have landed, not a heal to announce.
        _stats.ModifyCurrent(StatType.Mana, -paid);
        _stats.ModifyCurrent(StatType.Health, paid);
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (_active.Count == 0 || Entity is not { } self || !ReferenceEquals(e.Killer, self)
            || ReferenceEquals(e.Entity, self) || _stats is not { IsAlive: true })
        {
            return;
        }

        float fraction = Total(UniqueEffectKind.OnKillHeal);
        if (fraction > 0f)
        {
            _stats.Heal(UniqueEffectRules.KillHeal(_stats.GetMax(StatType.Health), fraction));
        }
    }

    // --- Spells, dodges, coin -----------------------------------------------------

    /// <summary>SpellEcho. The cast itself cannot be repeated from outside the casting core, so the
    /// echo repeats what the spell did: a second hit on the target it landed on.</summary>
    private void OnSpellHit(SpellHitEvent e)
    {
        if (_dealing || _active.Count == 0 || Entity is not { } self || !ReferenceEquals(e.Caster, self)
            || ReferenceEquals(e.Target, self) || e.Amount <= 0f)
        {
            return;
        }

        foreach (UniqueEffectResource effect in new List<UniqueEffectResource>(_active))
        {
            if (effect.Kind == UniqueEffectKind.SpellEcho && IsReady(effect)
                && UniqueEffectRules.Rolls(effect.Chance, GD.Randf()))
            {
                StartCooldown(effect, effect.CooldownSeconds);
                FollowUp(
                    e.Target,
                    UniqueEffectRules.BonusDamage(e.Amount, effect.Magnitude),
                    SpellDatabase.Get(e.SpellId)?.School ?? DamageType.Arcane);
            }
        }
    }

    /// <summary>
    /// DodgeRefund. A whiffed hit raises no event (i-frames return before any is published), so
    /// the dodge is recognised from the other side: an enemy's melee attack reaching its release
    /// frame within reach while the wearer is rolling through it invulnerable.
    /// </summary>
    private void OnActionReleased(ActionReleasedEvent e)
    {
        if (_active.Count == 0 || Entity is not { } self || ReferenceEquals(e.Actor, self)
            || e.Kind is not (ActionKind.Attack or ActionKind.HeavyAttack)
            || _combat is not { IsInvulnerable: true } || _stats is not { IsAlive: true }
            || self.GetComponent<DodgeComponent>() is not { IsDodging: true }
            || e.Actor.GetComponent<CombatComponent>() is not { } theirs || theirs.Team == _combat.Team
            || !IsInMeleeRange(self, e.Actor))
        {
            return;
        }

        foreach (UniqueEffectResource effect in _active)
        {
            if (effect.Kind == UniqueEffectKind.DodgeRefund && IsReady(effect))
            {
                StartCooldown(effect, Mathf.Max(effect.CooldownSeconds, UniqueEffectRules.DodgeRefundMinGapSeconds));
                _stats.ModifyCurrent(StatType.Stamina, effect.Magnitude);
            }
        }
    }

    private void OnItemPickedUp(ItemPickedUpEvent e)
    {
        if (_active.Count == 0 || !ReferenceEquals(e.Owner, Entity) || e.Item.Id != GameIds.Currency.Gold)
        {
            return;
        }

        float fraction = Total(UniqueEffectKind.GoldFind);
        if (fraction <= 0f)
        {
            return;
        }

        int bonus = UniqueEffectRules.GoldBonus(e.Quantity, fraction, _goldCarry, out _goldCarry);
        if (bonus > 0)
        {
            Entity!.GetComponent<InventoryComponent>()?.AddItem(e.Item, bonus);
        }
    }

    // --- Shared -------------------------------------------------------------------

    /// <summary>The summed magnitude of every active effect of one kind.</summary>
    private float Total(UniqueEffectKind kind)
    {
        float total = 0f;
        foreach (UniqueEffectResource effect in _active)
        {
            if (effect.Kind == kind)
            {
                total += effect.Magnitude;
            }
        }

        return total;
    }

    private bool IsReady(UniqueEffectResource effect) => !_cooldowns.ContainsKey(effect.Id);

    private void StartCooldown(UniqueEffectResource effect, float seconds)
    {
        if (seconds > 0f)
        {
            _cooldowns[effect.Id] = seconds;
        }
    }

    private static bool IsInMeleeRange(IEntity a, IEntity b)
    {
        return a.Body is { } first && b.Body is { } second
            && GodotObject.IsInstanceValid(first) && GodotObject.IsInstanceValid(second)
            && UniqueEffectRules.InMeleeRange(first.GlobalPosition.DistanceSquaredTo(second.GlobalPosition));
    }

    /// <summary>Deals an effect's extra damage to <paramref name="target"/>: straight to health,
    /// credited to the wearer, and announced so feedback and AI treat it as a hit.</summary>
    private void FollowUp(IEntity target, float amount, DamageType type)
    {
        if (amount <= 0f || Entity is not { } self || target.GetComponent<StatsComponent>() is not { IsAlive: true } theirs)
        {
            return;
        }

        _dealing = true;
        try
        {
            theirs.ApplyDamage(amount, self);
            EventBus.Instance?.Publish(new DamageDealtEvent(self, target, amount, type, false, false));
        }
        finally
        {
            _dealing = false;
        }
    }
}
