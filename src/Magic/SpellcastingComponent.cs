using System.Collections.Generic;
using Embervale.Combat;
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
/// per-spell cooldowns, and the cast itself (mana spend → deliver). It is the magic
/// analogue of <see cref="MeleeWeaponComponent"/> and is deliberately input-agnostic —
/// the player controller (and later enemy AI) decides <em>when</em> to call
/// <see cref="TryCast"/> / <see cref="Cycle"/>.
///
/// Delivery is resource-driven (<see cref="SpellResource.Delivery"/>): a projectile
/// fired along the caster's aim, an instant burst around the caster, or a self heal/buff.
/// Known spells + the prepared index persist via <see cref="ISaveable"/>; cooldowns are
/// transient.
/// </summary>
[GlobalClass]
public partial class SpellcastingComponent : EntityComponent, ISaveable
{
    private const float SpellPoiseDamage = 12f;
    private const float DefaultNovaRadius = 4f;
    private const float MuzzleOffset = 1.2f;

    /// <summary>Spell ids this entity starts knowing (authored by the factory/scene).</summary>
    [Export]
    public Godot.Collections.Array<string> KnownSpellIds { get; set; } = new();

    /// <summary>Aim source for projectiles/area targeting; the player's camera pivot.
    /// Falls back to the entity body when not injected.</summary>
    public Node3D? AimNode { get; set; }

    private readonly List<SpellResource> _spells = new();
    private readonly Dictionary<string, double> _cooldowns = new();
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
    private double _channelTickTimer;

    /// <summary>True while a charged cast is being held (drives charge-meter UI later).</summary>
    public bool IsCharging => _activeCast is { CastMode: CastMode.Charged };

    /// <summary>True while a channeled cast is sustaining.</summary>
    public bool IsChanneling => _activeCast is { CastMode: CastMode.Channeled };

    // Pooled projectiles: rapid casting reuses bolts instead of churning the scene tree.
    private NodePool<SpellProjectile>? _projectilePool;

    public string SaveId => SaveKey("spells");

    public IReadOnlyList<SpellResource> Spells => _spells;

    public int SelectedIndex => _selected;

    public SpellResource? Selected =>
        _spells.Count == 0 ? null : _spells[Mathf.Clamp(_selected, 0, _spells.Count - 1)];

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
        _progression = Entity.GetComponent<Progression.ProgressionComponent>();
        _mastery = Entity.GetComponent<SchoolMasteryComponent>();
        _projectilePool = new NodePool<SpellProjectile>(
            () => new SpellProjectile { Released = ReturnProjectile }, prewarm: 4);
        RebuildSpells();
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        _projectilePool?.Clear();
        SaveManager.Instance?.Unregister(this);
    }

    private void ReturnProjectile(SpellProjectile projectile) => _projectilePool?.Return(projectile);

    public override void _Process(double delta)
    {
        if (_cooldowns.Count == 0)
        {
            return;
        }

        // Snapshot the keys so removing/updating entries during the tick is safe.
        foreach (string id in new List<string>(_cooldowns.Keys))
        {
            double remaining = _cooldowns[id] - delta;
            if (remaining <= 0d)
            {
                _cooldowns.Remove(id);
            }
            else
            {
                _cooldowns[id] = remaining;
            }
        }
    }

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
        if (_spells.Exists(s => s.Id == spellId) || SpellDatabase.Get(spellId) is not { } spell)
        {
            return;
        }

        if (!MeetsCorruption(spell))
        {
            return;
        }

        _spells.Add(spell);
        if (!KnownSpellIds.Contains(spellId))
        {
            KnownSpellIds.Add(spellId);
        }
    }

    /// <summary>Whether the spell is already in this caster's spellbook.</summary>
    public bool IsKnown(SpellResource spell) => _spells.Exists(s => s.Id == spell.Id);

    /// <summary>The spell's current rank (1 once known, 0 if unknown).</summary>
    public int RankOf(SpellResource spell) =>
        _ranks.TryGetValue(spell.Id, out int rank) ? rank : (IsKnown(spell) ? 1 : 0);

    /// <summary>Can the caster buy (learn) this unknown spell now — corruption met and skill points spare?</summary>
    public bool CanBuy(SpellResource spell) =>
        !IsKnown(spell) && MeetsCorruption(spell) && (_progression?.SkillPoints ?? 0) >= spell.LearnCost;

    /// <summary>Buys an unknown spell with skill points (Phase 29.5C-lite). Returns false if not affordable.</summary>
    public bool Buy(SpellResource spell)
    {
        if (!CanBuy(spell) || _progression?.SpendSkillPoints(spell.LearnCost) != true)
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

    /// <summary>Can the caster rank up this known spell — below max and enough skill points?</summary>
    public bool CanUpgrade(SpellResource spell) =>
        IsKnown(spell) && RankOf(spell) < spell.MaxRank && (_progression?.SkillPoints ?? 0) >= spell.UpgradeCost;

    /// <summary>Spends skill points to raise a known spell's rank, empowering its damage/healing.</summary>
    public bool Upgrade(SpellResource spell)
    {
        if (!CanUpgrade(spell) || _progression?.SpendSkillPoints(spell.UpgradeCost) != true)
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

    public bool CanCast(SpellResource? spell)
    {
        if (spell == null || _stats == null || !_stats.IsAlive)
        {
            return false;
        }

        return CooldownOf(spell) <= 0f && _stats.GetCurrent(StatType.Mana) >= EffectiveManaCost(spell);
    }

    /// <summary>Casts the prepared spell instantly. Returns false if none is ready/affordable. Charged and
    /// channeled spells route through <see cref="BeginCast"/> instead.</summary>
    public bool TryCast()
    {
        SpellResource? spell = Selected;
        if (!CanCast(spell))
        {
            return false;
        }

        _stats!.ModifyCurrent(StatType.Mana, -EffectiveManaCost(spell!));
        _cooldowns[spell.Id] = spell.Cooldown;
        Deliver(spell, 1f);

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new SpellCastEvent(Entity, spell.Id));
        }

        return true;
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

        if (spell.CastMode == CastMode.Charged && CanCast(spell))
        {
            float power = SpellCharge.PowerMultiplier(_chargeElapsed, spell.ChargeTime, spell.MaxChargeMultiplier);
            _stats!.ModifyCurrent(StatType.Mana, -EffectiveManaCost(spell));
            _cooldowns[spell.Id] = spell.Cooldown;
            Deliver(spell, power);
            if (Entity != null)
            {
                EventBus.Instance?.Publish(new SpellCastEvent(Entity, spell.Id));
            }
        }
        else if (spell.CastMode == CastMode.Channeled)
        {
            _cooldowns[spell.Id] = spell.Cooldown;
        }
    }

    /// <summary>Abandons an in-progress charged/channeled cast without firing (e.g. a menu opens or the
    /// game pauses) — no damage, no mana spent on release, no cooldown.</summary>
    public void CancelCast() => _activeCast = null;

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
        Deliver(spell, 1f);
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new SpellCastEvent(Entity, spell.Id));
        }
    }

    private void Deliver(SpellResource spell, float power)
    {
        int team = _combat?.Team ?? 0;
        switch (spell.Delivery)
        {
            case SpellDelivery.Self:
                CastSelf(spell, power);
                break;
            case SpellDelivery.Area:
                CastArea(spell, team, power);
                break;
            default:
                CastProjectile(spell, team, power);
                break;
        }
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

    private DamagePacket BuildPacket(SpellResource spell, float power)
    {
        (float amount, bool isCrit) = CombatMath.RollSpell(spell.BaseDamage, _stats);
        return new DamagePacket(amount * Empower(spell, power), spell.School, Entity, isCrit, SpellPoiseDamage);
    }

    private void CastProjectile(SpellResource spell, int team, float power)
    {
        (Vector3 origin, Vector3 direction) = Aim();
        SpellProjectile projectile = _projectilePool?.Get() ?? new SpellProjectile { Released = ReturnProjectile };

        // Add to the tree (so its visual children build on first use) then position + arm it.
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = origin;
        projectile.Launch(spell, BuildPacket(spell, power), Entity, team, direction);
    }

    private void CastArea(SpellResource spell, int team, float power)
    {
        if (Entity?.Body is not Node3D body)
        {
            return;
        }

        Vector3 center = body.GlobalPosition + (Vector3.Up * 1f);
        float radius = spell.ImpactRadius > 0f ? spell.ImpactRadius : DefaultNovaRadius;
        SpellResolver.Detonate(body, spell, BuildPacket(spell, power), Entity, team, center, radius);
    }

    private void CastSelf(SpellResource spell, float power) => ApplySupport(Entity, spell, power);

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

    /// <summary>Selects a known spell by id and casts it instantly. The lever enemy AI uses to choose a
    /// spell (the player cycles + casts); a no-op if the spell isn't known or isn't ready. Reuses the
    /// full <see cref="TryCast"/> path — no parallel casting logic (Phase 29.5F).</summary>
    public bool TryCastById(string spellId)
    {
        int idx = _spells.FindIndex(s => s.Id == spellId);
        if (idx < 0)
        {
            return false;
        }

        _selected = idx;
        return TryCast();
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

    private void RebuildSpells()
    {
        _spells.Clear();
        foreach (string id in KnownSpellIds)
        {
            if (SpellDatabase.Get(id) is { } spell)
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

        return new Godot.Collections.Dictionary
        {
            ["spells"] = ids,
            ["selected"] = _selected,
            ["ranks"] = ranks,
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (data.TryGetValue("spells", out Variant spellsVar))
        {
            KnownSpellIds = new Godot.Collections.Array<string>();
            foreach (Variant entry in spellsVar.AsGodotArray())
            {
                KnownSpellIds.Add(entry.AsString());
            }

            RebuildSpells();
        }

        _ranks.Clear();
        if (data.TryGetValue("ranks", out Variant ranksVar))
        {
            Godot.Collections.Dictionary ranks = ranksVar.AsGodotDictionary();
            foreach (Variant key in ranks.Keys)
            {
                _ranks[key.AsString()] = ranks[key].AsInt32();
            }
        }

        if (data.TryGetValue("selected", out Variant selectedVar))
        {
            _selected = Mathf.Clamp(selectedVar.AsInt32(), 0, Mathf.Max(0, _spells.Count - 1));
        }
    }
}
