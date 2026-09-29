using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// The seam <c>tools/magic_status_probe.gd</c> drives the status rules through. A GDScript call cannot
/// pass an <see cref="IEntity"/> or a <see cref="DamagePacket"/> and cannot subscribe to a C# event, so
/// this thin node takes bodies, builds the packet, calls the real static entry points
/// (<see cref="SpellResolver.HitOne"/>, <see cref="StatusEffectsComponent"/>) and tallies the magic events.
/// It holds no rule of its own.
/// </summary>
[GlobalClass]
public partial class MagicStatusProbeSeam : Node
{
    private readonly Dictionary<string, int> _counts = new();

    /// <summary>The id of the last <see cref="SpellComboEvent"/>.</summary>
    public string LastComboId { get; private set; } = string.Empty;

    /// <summary>Damage of the last <see cref="StatusDetonatedEvent"/>.</summary>
    public float LastDetonateDamage { get; private set; }

    public override void _EnterTree()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Subscribe<StatusDispelledEvent>(e => Bump("dispelled:" + e.EffectId));
        bus?.Subscribe<StatusDetonatedEvent>(e =>
        {
            LastDetonateDamage = e.Damage;
            Bump("detonated:" + e.EffectId);
        });
        bus?.Subscribe<StatusResistedEvent>(e => Bump("resisted:" + e.EffectId));
        bus?.Subscribe<SpellComboEvent>(e =>
        {
            LastComboId = e.ComboId;
            Bump("combo:" + e.ComboId);
        });
        bus?.Subscribe<WardBrokenEvent>(e => Bump("wardbroken:" + e.EffectId));
        bus?.Subscribe<StatusEffectAppliedEvent>(e => Bump("applied:" + e.EffectId));
        bus?.Subscribe<StatusEffectRemovedEvent>(e => Bump("removed:" + e.EffectId));
    }

    private void Bump(string key) => _counts[key] = Count(key) + 1;

    /// <summary>Whether the locale catalogue carries <paramref name="key"/> (initialises it first).</summary>
    public bool LocHas(string key)
    {
        Embervale.Localization.Loc.Initialize();
        return Embervale.Localization.Loc.Has(key);
    }

    public bool BusAvailable => EventBus.Instance != null;

    public int Count(string key) => _counts.TryGetValue(key, out int n) ? n : 0;

    public void ResetCounts()
    {
        _counts.Clear();
        LastComboId = string.Empty;
        LastDetonateDamage = 0f;
    }

    public bool Apply(Node3D target, string statusId, Node3D? source) =>
        Status(target)?.Apply(StatusEffectDatabase.Get(statusId), source as IEntity) ?? false;

    /// <summary>The id stripped, or empty when there was nothing to take.</summary>
    public string Dispel(Node3D target, Node3D? source) =>
        Status(target)?.Dispel(source as IEntity) ?? string.Empty;

    public int Cleanse(Node3D target, string statusId, Node3D? source) =>
        Status(target)?.Cleanse(statusId, source as IEntity) ?? 0;

    public int Consume(Node3D target, string statusId) => Status(target)?.Consume(statusId) ?? 0;

    public float ModifyIncoming(Node3D target, float amount, Node3D? source) =>
        Status(target)?.ModifyIncoming(amount, source as IEntity) ?? amount;

    public float WardRemaining(Node3D target) => Status(target)?.WardRemaining ?? 0f;

    public double ImmunityRemaining(Node3D target, int control) =>
        Status(target)?.ImmunityRemaining((StatusControl)control) ?? 0d;

    /// <summary>Runs one spell hit through the real resolver seam: damage, school identity, combo, status.</summary>
    public void Hit(Hurtbox hurtbox, SpellResource spell, Node3D caster, float damage)
    {
        var entity = caster as IEntity;
        int team = entity?.GetComponent<CombatComponent>()?.Team ?? 0;
        var packet = new DamagePacket(damage, spell.School, entity, false, 0f, HitKind.Spell);
        SpellResolver.HitOne(caster, hurtbox, packet, spell, entity, team);
    }

    /// <summary>Kills <paramref name="target"/>, crediting <paramref name="killer"/>.</summary>
    public void Kill(Node3D target, Node3D? killer) =>
        (target as IEntity)?.GetComponent<Embervale.Stats.StatsComponent>()?.ApplyDamage(99999f, killer as IEntity);

    /// <summary>Advances a body's status timers by <paramref name="seconds"/> without waiting for them.</summary>
    public void Step(Node3D body, float seconds)
    {
        StatusEffectsComponent? status = Status(body);
        for (float t = 0f; t < seconds; t += 0.1f)
        {
            status?._Process(0.1);
        }
    }

    private static StatusEffectsComponent? Status(Node3D body) =>
        (body as IEntity)?.GetComponent<StatusEffectsComponent>();
}
